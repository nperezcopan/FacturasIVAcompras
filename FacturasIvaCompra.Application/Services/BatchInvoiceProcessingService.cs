using System.Diagnostics;
using System.Text;
using FacturasIvaCompra.Application.Models;
using FacturasIvaCompra.Domain.Entities;
using FacturasIvaCompra.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FacturasIvaCompra.Application.Services
{
    /// <summary>
    /// Orquesta el procesamiento de un lote: escanea la carpeta configurada, extrae texto
    /// (nativo u OCR) de cada PDF y aplica la extracción genérica de campos, produciendo
    /// filas pendientes de revisión en el grid de previsualización. No graba nada en SQL
    /// ni mueve archivos — eso ocurre solo al aprobar el lote (ver ApprovalService).
    /// </summary>
    public class BatchInvoiceProcessingService
    {
        // Reemplazo de "CONCEPTO"/"VAR" (PDFAnalizer) por señales típicas de una factura.
        private static readonly string[] SenalesFactura = { "CUIT", "FACTURA", "TOTAL" };

        private static readonly string[] CamposCriticos =
        {
            nameof(FacturaCompra.Fecha_Comprobante_CC),
            nameof(FacturaCompra.Tipo_Comprobante_CC),
            nameof(FacturaCompra.Punto_Venta_CC),
            nameof(FacturaCompra.Nro_Comprobante_CC),
            nameof(FacturaCompra.CUIT_Emisor_CC),
            nameof(FacturaCompra.Denominacion_Emisor_CC),
            nameof(FacturaCompra.Importe_Total_Operacion_CC),
            nameof(FacturaCompra.Neto_CC),
            nameof(FacturaCompra.Porc_Iva_CC),
        };

        private readonly IPdfTextExtractor _textExtractor;
        private readonly IPdfRenderer _renderer;
        private readonly IOcrService _ocrService;
        private readonly IInvoiceFieldExtractor _fieldExtractor;
        private readonly IFacturaCompraRepository _repository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BatchInvoiceProcessingService> _logger;

        public BatchInvoiceProcessingService(
            IPdfTextExtractor textExtractor,
            IPdfRenderer renderer,
            IOcrService ocrService,
            IInvoiceFieldExtractor fieldExtractor,
            IFacturaCompraRepository repository,
            IConfiguration configuration,
            ILogger<BatchInvoiceProcessingService> logger)
        {
            _textExtractor = textExtractor;
            _renderer = renderer;
            _ocrService = ocrService;
            _fieldExtractor = fieldExtractor;
            _repository = repository;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<InvoicePreviewRow>> ProcessFolderAsync(
            IProgress<ProcessingProgress> progress,
            CancellationToken ct = default)
        {
            var sourceFolder = _configuration["FolderSettings:SourceFolder"]
                ?? throw new InvalidOperationException("Falta configurar FolderSettings:SourceFolder en appsettings.json.");

            if (!Directory.Exists(sourceFolder))
            {
                _logger.LogWarning("La carpeta origen '{Folder}' no existe.", sourceFolder);
                progress.Report(new ProcessingProgress(0, 0, $"La carpeta origen no existe: {sourceFolder}", isFinished: true));
                return new List<InvoicePreviewRow>();
            }

            var pdfFiles = Directory.GetFiles(sourceFolder, "*.pdf");
            var results = new List<InvoicePreviewRow>();
            var stopwatch = Stopwatch.StartNew();
            var errors = 0;

            _logger.LogInformation("Iniciando procesamiento de {Count} PDF(s) en {Folder}.", pdfFiles.Length, sourceFolder);

            for (var i = 0; i < pdfFiles.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var pdfPath = pdfFiles[i];
                var fileNumber = i + 1;

                try
                {
                    var text = await ExtractTextAsync(pdfPath, ct);
                    var extraction = _fieldExtractor.Extract(text, Path.GetFileName(pdfPath));
                    results.Add(InvoicePreviewRow.FromExtraction(extraction.Factura, pdfPath, extraction.MissingFields, extraction.CorrectedFields, extraction.MesComisionDetectado));
                    _logger.LogInformation("{File}: procesado, {Missing} campo(s) críticos pendientes de revisión.",
                        Path.GetFileName(pdfPath), extraction.MissingFields.Count);
                }
                catch (Exception ex)
                {
                    errors++;
                    _logger.LogError(ex, "Error no controlado procesando {File}.", pdfPath);
                    results.Add(InvoicePreviewRow.FromExtraction(new FacturaCompra(), pdfPath, new HashSet<string>(CamposCriticos), new HashSet<string>()));
                }

                progress.Report(new ProcessingProgress(
                    currentPage: fileNumber,
                    totalPages: pdfFiles.Length,
                    message: $"{fileNumber}/{pdfFiles.Length} — {Path.GetFileName(pdfPath)}",
                    isFinished: false,
                    totalProcessed: results.Count,
                    totalErrors: errors,
                    elapsedTotal: stopwatch.Elapsed));
            }

            // Nro_Proveedor: se resuelve por CUIT contra dbo.PROVEEDORES ya en la previsualización,
            // para que la grilla no muestre el campo en null antes de aprobar el lote.
            var nrosProveedorPorCuit = await _repository.GetNrosProveedorPorCuitAsync(
                results.Select(r => r.CUIT_Emisor_CC), ct);
            foreach (var row in results)
            {
                if (nrosProveedorPorCuit.TryGetValue(row.CUIT_Emisor_CC.Trim(), out var nroProveedor))
                {
                    row.Nro_Proveedor = nroProveedor;
                }
            }

            stopwatch.Stop();
            progress.Report(new ProcessingProgress(
                currentPage: pdfFiles.Length,
                totalPages: pdfFiles.Length,
                message: "Procesamiento completado.",
                isFinished: true,
                totalProcessed: results.Count,
                totalErrors: errors,
                elapsedTotal: stopwatch.Elapsed));

            return results;
        }

        private async Task<string> ExtractTextAsync(string pdfPath, CancellationToken ct)
        {
            var pageCount = _textExtractor.GetPageCount(pdfPath);
            var nativeBuilder = new StringBuilder();

            for (var i = 0; i < pageCount; i++)
            {
                try
                {
                    nativeBuilder.AppendLine(_textExtractor.ExtractTextFromPage(pdfPath, i));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error al extraer texto nativo de la página {Page} de {Pdf}. Se recurrirá a OCR.", i + 1, pdfPath);
                }
            }

            var nativeText = nativeBuilder.ToString();
            var sinTextoEmbebido = string.IsNullOrWhiteSpace(nativeText);

            // Proveedores con membrete de imagen (p.ej. STOP LOSS Bureau de Reaseguros): el CUIT
            // y la razón social del emisor están en el gráfico de fondo, no en texto
            // seleccionable — el texto nativo nunca va a contener la etiqueta "CUIT" en ese
            // caso, aunque el resto del comprobante sí tenga texto embebido normal. Se
            // complementa (no se reemplaza) con OCR para recuperar esos datos.
            var sinSenalDeCuit = !sinTextoEmbebido && !nativeText.Contains("CUIT", StringComparison.OrdinalIgnoreCase);

            if (!sinTextoEmbebido && !sinSenalDeCuit)
                return nativeText;

            _logger.LogInformation(
                sinTextoEmbebido
                    ? "{Pdf}: sin texto embebido. Ejecutando OCR ({Pages} página(s))."
                    : "{Pdf}: texto embebido sin señal de CUIT (posible membrete de imagen). Complementando con OCR ({Pages} página(s)).",
                pdfPath, pageCount);

            var dpi = int.TryParse(_configuration["OcrSettings:OcrResolutionDpi"], out var configuredDpi) ? configuredDpi : 300;
            var language = _configuration["OcrSettings:OcrLanguage"] ?? "spa";
            var ocrBuilder = new StringBuilder();

            for (var i = 0; i < pageCount; i++)
            {
                string pageText;
                if (sinSenalDeCuit)
                {
                    // El membrete con el CUIT/razón social del emisor suele estar en la franja
                    // superior de la página, fuera de la región parcial (0.45 → 1.0) usada para
                    // OCR rápido — se va directo a página completa en vez de arriesgarse a que
                    // la pasada parcial encuentre otra señal (p.ej. "TOTAL") y se salte la
                    // página completa sin haber cubierto el membrete.
                    var fullImageBytes = await Task.Run(
                        () => _renderer.RenderPageToImage(pdfPath, i, dpi), ct);
                    pageText = await Task.Run(
                        () => _ocrService.PerformOcr(fullImageBytes, language), ct);
                }
                else
                {
                    // Pasada 1: región parcial (0.45 → 1.0), más rápida.
                    var partialImageBytes = await Task.Run(
                        () => _renderer.RenderPageRegionToImage(pdfPath, i, dpi, 0.45, 1.0), ct);
                    pageText = await Task.Run(
                        () => _ocrService.PerformOcr(partialImageBytes, language), ct);

                    var tieneSenal = SenalesFactura.Any(s => pageText.Contains(s, StringComparison.OrdinalIgnoreCase));
                    if (!tieneSenal)
                    {
                        // Pasada 2: página completa, si la parcial no encontró indicios de factura.
                        var fullImageBytes = await Task.Run(
                            () => _renderer.RenderPageToImage(pdfPath, i, dpi), ct);
                        pageText = await Task.Run(
                            () => _ocrService.PerformOcr(fullImageBytes, language), ct);
                    }
                }

                ocrBuilder.AppendLine(pageText);
            }

            var ocrText = ocrBuilder.ToString();
            return sinTextoEmbebido ? ocrText : nativeText + Environment.NewLine + ocrText;
        }
    }
}
