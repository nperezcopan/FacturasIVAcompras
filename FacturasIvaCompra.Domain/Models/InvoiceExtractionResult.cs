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

        /// <summary>
        /// Campos cuyo valor extraído no cerraba con la validación cruzada Total/Neto/IVA y se
        /// recalculó automáticamente (ver GenericInvoiceFieldExtractor). Se muestran en la
        /// grilla con un color distinto al de MissingFields para que el usuario los revise
        /// antes de aprobar, aunque no bloquean la aprobación como sí lo hacen los faltantes.
        /// </summary>
        public HashSet<string> CorrectedFields { get; } = new();
        public string RawText { get; set; } = string.Empty;
        public string SourceFileName { get; set; } = string.Empty;

        /// <summary>
        /// Mes/año detectado en el concepto de la factura cuando dice "COMISIONES &lt;mes&gt;
        /// &lt;año&gt;" (día siempre 1). Null si el concepto no menciona comisiones de un mes
        /// específico. Usado por ApprovalService para calcular Fecha_Caja_Banco_CC respetando
        /// el mes de la comisión en vez del mes de emisión del comprobante — decisión de
        /// negocio explícita.
        /// </summary>
        public DateTime? MesComisionDetectado { get; set; }
    }
}
