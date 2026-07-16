namespace FacturasIvaCompra.UI
{
    public partial class MainForm
    {
        private static readonly Color ColorFondo = Color.FromArgb(245, 245, 248);
        private static readonly Color ColorCampoFaltante = Color.FromArgb(255, 244, 179);   // amarillo: crítico no encontrado
        private static readonly Color ColorCampoCorregido = Color.FromArgb(173, 216, 230);  // celeste: autocorregido por validación Total/Neto/IVA, revisar
        private static readonly Color ColorDefaultNegocio = Color.FromArgb(230, 230, 230);  // gris: nunca se intenta extraer, es default
        private static readonly Color ColorCeldaNormal = Color.White;

        /// <summary>
        /// Columnas cuyo valor SIEMPRE es un default de negocio (nunca se intenta extraer del PDF),
        /// para distinguir visualmente "no encontrado por OCR" de "nunca se buscó".
        /// </summary>
        private static readonly HashSet<string> ColumnasDefaultNegocio = new()
        {
            nameof(Application.Models.InvoicePreviewRow.Nro_Despacho_CC),
            nameof(Application.Models.InvoicePreviewRow.Codigo_Vendedor_CC),
            nameof(Application.Models.InvoicePreviewRow.Codigo_Moneda_CC),
            nameof(Application.Models.InvoicePreviewRow.Tipo_Cambio_CC),
            nameof(Application.Models.InvoicePreviewRow.Cantidad_Alicuota_IVA_CC),
            nameof(Application.Models.InvoicePreviewRow.Codigo_Operacion_CC),
            nameof(Application.Models.InvoicePreviewRow.Codigo_Fiscal_CC),
            nameof(Application.Models.InvoicePreviewRow.Otro_Tributo_CC),
            nameof(Application.Models.InvoicePreviewRow.IVA_Comision_CC),
            nameof(Application.Models.InvoicePreviewRow.Ret_Iva_CC),
            nameof(Application.Models.InvoicePreviewRow.Nro_Proveedor),
            nameof(Application.Models.InvoicePreviewRow.Fecha_Caja_Banco_CC),
            nameof(Application.Models.InvoicePreviewRow.Importe_Conceptos_NO_Neto_Gravado_CC),
            nameof(Application.Models.InvoicePreviewRow.Importe_Operacion_Exenta_CC),
            nameof(Application.Models.InvoicePreviewRow.Importe_Perc_Imp_Nacional_CC),
            nameof(Application.Models.InvoicePreviewRow.Importe_Perc_IIBB_CC),
            nameof(Application.Models.InvoicePreviewRow.Importe_Perc_Imp_Mun_CC),
            nameof(Application.Models.InvoicePreviewRow.Importe_Impuesto_Interno_CC),
        };
    }
}
