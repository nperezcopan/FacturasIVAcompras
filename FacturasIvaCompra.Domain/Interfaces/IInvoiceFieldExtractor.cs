using FacturasIvaCompra.Domain.Models;

namespace FacturasIvaCompra.Domain.Interfaces
{
    /// <summary>
    /// Extrae los campos fiscales de una factura de compra a partir de su texto
    /// (nativo u OCR), de forma genérica por heurísticas/regex, sin depender de un
    /// template por proveedor.
    /// </summary>
    public interface IInvoiceFieldExtractor
    {
        InvoiceExtractionResult Extract(string text, string sourceFileName);
    }
}
