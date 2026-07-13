namespace FacturasIvaCompra.Application.Models
{
    public class ApprovalResult
    {
        public bool SqlCommitted { get; set; }
        public int RowsInserted { get; set; }
        public List<string> FileMoveErrors { get; set; } = new();
        public string? SqlErrorMessage { get; set; }

        /// <summary>Descripciones legibles de las facturas rechazadas por Nro_Comprobante_CC duplicado (no se grabaron ni se movió su PDF).</summary>
        public List<string> DuplicateInvoices { get; set; } = new();

        /// <summary>Descripciones legibles de las facturas rechazadas por tener campos críticos sin completar (no se grabaron ni se movió su PDF).</summary>
        public List<string> IncompleteInvoices { get; set; } = new();
    }
}
