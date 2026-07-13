using System;
using System.IO;
using System.Net.Http;
using Tesseract;
using FacturasIvaCompra.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FacturasIvaCompra.Infrastructure.Services
{
    public class TesseractOcrService : IOcrService
    {
        private readonly ILogger<TesseractOcrService> _logger;
        private static readonly HttpClient HttpClient = new();

        public TesseractOcrService(ILogger<TesseractOcrService> logger)
        {
            _logger = logger;
        }

        public string PerformOcr(byte[] imageBytes, string language)
        {
            var tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

            if (!Directory.Exists(tessdataPath))
            {
                Directory.CreateDirectory(tessdataPath);
            }

            var traineddataFile = Path.Combine(tessdataPath, $"{language}.traineddata");
            if (!File.Exists(traineddataFile))
            {
                _logger.LogInformation("Archivo de datos de Tesseract '{Lang}.traineddata' no encontrado en '{Path}'. Descargándolo automáticamente...", language, traineddataFile);
                DownloadTessdata(language, traineddataFile);
            }

            using (var engine = new TesseractEngine(tessdataPath, language, EngineMode.Default))
            {
                using (var img = Pix.LoadFromMemory(imageBytes))
                {
                    using (var page = engine.Process(img))
                    {
                        var text = page.GetText();
                        _logger.LogDebug("Texto obtenido por OCR (Confianza: {Confidence:P2}): {TextLength} caracteres", page.GetMeanConfidence(), text?.Length ?? 0);
                        return text ?? string.Empty;
                    }
                }
            }
        }

        private void DownloadTessdata(string language, string outputPath)
        {
            var url = $"https://github.com/tesseract-ocr/tessdata/raw/main/{language}.traineddata";
            try
            {
                var response = HttpClient.GetAsync(url).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    response.Content.CopyToAsync(fileStream).GetAwaiter().GetResult();
                }
                _logger.LogInformation("Archivo de datos '{Lang}.traineddata' descargado y guardado correctamente.", language);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al descargar el archivo de datos de OCR desde {Url}.", url);
                throw new InvalidOperationException($"No se pudo descargar el archivo de datos de Tesseract para el idioma '{language}'. Por favor, colóquelo manualmente en '{outputPath}'.", ex);
            }
        }
    }
}
