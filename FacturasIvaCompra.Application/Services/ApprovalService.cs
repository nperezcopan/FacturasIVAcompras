using FacturasIvaCompra.Application.Models;
using FacturasIvaCompra.Domain.Interfaces;
using FacturasIvaCompra.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FacturasIvaCompra.Application.Services
{
    /// <summary>
    /// Aplica la aprobación de un lote completo: inserta todas las facturas en SQL dentro
    /// de una única transacción (todo o nada) y, solo si el commit fue exitoso, mueve cada
    /// PDF a la carpeta de procesados. Un fallo al mover un archivo puntual no revierte el
    /// insert ya confirmado ni aborta el resto de los archivos.
    /// </summary>
    public class ApprovalService
    {
        private readonly IFacturaCompraRepository _repository;
        private readonly IFileMover _fileMover;
        private readonly ILogger<ApprovalService> _logger;

        public ApprovalService(IFacturaCompraRepository repository, IFileMover fileMover, ILogger<ApprovalService> logger)
        {
            _repository = repository;
            _fileMover = fileMover;
            _logger = logger;
        }

        public async Task<ApprovalResult> ApproveBatchAsync(IReadOnlyList<InvoicePreviewRow> rows, CancellationToken ct = default)
        {
            var result = new ApprovalResult();

            // Filas con campos críticos sin completar (celdas amarillas en la grilla, p.ej.
            // fecha de comprobante no reconocida): se rechazan antes de tocar SQL. Sin este
            // filtro, un valor default como Fecha_Comprobante_CC = 0001-01-01 hace fallar el
            // INSERT completo con "SqlDateTime overflow" y se pierde todo el lote, no solo esa fila.
            var incompletas = rows.Where(r => r.HasPendingReview).ToHashSet();
            foreach (var row in incompletas)
            {
                var campos = string.Join(", ", row.MissingFields);
                result.IncompleteInvoices.Add($"{row.SourceFileName} — campo(s) sin completar: {campos}");
            }

            // Duplicados dentro del propio lote: se conserva la primera aparición de cada
            // comprobante y se rechazan las siguientes. La clave de unicidad es Tipo + Punto
            // de Venta + Nro. Comprobante + CUIT Emisor — NO alcanza con Nro_Comprobante_CC
            // solo, porque cada proveedor numera sus propios comprobantes desde su propio
            // punto de venta y dos proveedores distintos pueden compartir el mismo número.
            var sinIncompletas = rows.Where(r => !incompletas.Contains(r)).ToList();
            var duplicadosEnLote = sinIncompletas
                .GroupBy(r => ClaveDe(r).Canonico)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.Skip(1))
                .ToHashSet();

            var candidatas = sinIncompletas.Where(r => !duplicadosEnLote.Contains(r)).ToList();

            var existentesEnBd = await _repository.GetExistingComprobantesAsync(candidatas.Select(ClaveDe), ct);
            var duplicadosEnBd = candidatas.Where(r => existentesEnBd.Contains(ClaveDe(r).Canonico)).ToHashSet();

            var validRows = candidatas.Where(r => !duplicadosEnBd.Contains(r)).ToList();

            foreach (var row in rows.Where(r => duplicadosEnLote.Contains(r) || duplicadosEnBd.Contains(r)))
            {
                var motivo = duplicadosEnBd.Contains(row) ? "ya existe en la base de datos" : "repetido dentro del lote";
                result.DuplicateInvoices.Add(
                    $"{row.SourceFileName} (Tipo {row.Tipo_Comprobante_CC}, PV {row.Punto_Venta_CC}, " +
                    $"Nro {row.Nro_Comprobante_CC}, CUIT {row.CUIT_Emisor_CC}) — {motivo}");
            }

            if (validRows.Count == 0)
            {
                result.SqlCommitted = true;
                result.RowsInserted = 0;
                return result;
            }

            var facturas = validRows.Select(r => r.ToFacturaCompra()).ToList();

            // Nro_Proveedor: se resuelve por CUIT contra dbo.PROVEEDORES en vez de dejarlo null.
            var nrosProveedorPorCuit = await _repository.GetNrosProveedorPorCuitAsync(
                facturas.Select(f => f.CUIT_Emisor_CC), ct);

            foreach (var factura in facturas)
            {
                // Fecha_Caja_Banco_CC: no se extrae del PDF (no aparece en ningún comprobante);
                // se fija al primer domingo del mes de la fecha de emisión del comprobante.
                factura.Fecha_Caja_Banco_CC = PrimerDomingoDelMes(factura.Fecha_Comprobante_CC);
                if (nrosProveedorPorCuit.TryGetValue(factura.CUIT_Emisor_CC.Trim(), out var nroProveedor))
                {
                    factura.Nro_Proveedor = nroProveedor;
                }
            }

            try
            {
                await _repository.InsertBatchAsync(facturas, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falló la inserción del lote en SQL. No se moverá ningún archivo.");
                result.SqlCommitted = false;
                result.SqlErrorMessage = ex.Message;
                return result;
            }

            result.SqlCommitted = true;
            result.RowsInserted = facturas.Count;

            foreach (var row in validRows)
            {
                try
                {
                    _fileMover.MoveToProcessed(row.SourceFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "No se pudo mover el archivo {File} a la carpeta de procesados.", row.SourceFilePath);
                    result.FileMoveErrors.Add(row.SourceFilePath);
                }
            }

            return result;
        }

        private static ComprobanteKey ClaveDe(InvoicePreviewRow row) =>
            new(row.Tipo_Comprobante_CC, row.Punto_Venta_CC, row.Nro_Comprobante_CC, row.CUIT_Emisor_CC);

        private static DateTime PrimerDomingoDelMes(DateTime referencia)
        {
            var primerDiaDelMes = new DateTime(referencia.Year, referencia.Month, 1);
            var diasHastaDomingo = ((int)DayOfWeek.Sunday - (int)primerDiaDelMes.DayOfWeek + 7) % 7;
            return primerDiaDelMes.AddDays(diasHastaDomingo);
        }
    }
}
