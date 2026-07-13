using FacturasIvaCompra.Domain.Entities;

namespace FacturasIvaCompra.Domain.Models
{
    /// <summary>
    /// Resultado crudo de IInvoiceFieldExtractor: los campos detectados (con sus defaults
    /// donde no se encontró nada) más el registro de qué campos críticos quedaron sin extraer.
    /// </summary>
    public class InvoiceExtractionResult
    {
        public FacturaCompra Factura { get; set; } = new();
        public HashSet<string> MissingFields { get; } = new();
        public string RawText { get; set; } = string.Empty;
        public string SourceFileName { get; set; } = string.Empty;
    }
}
