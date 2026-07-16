using FacturasIvaCompra.Domain.Entities;

namespace FacturasIvaCompra.Application.Models
{
    /// <summary>
    /// Fila editable del grid de previsualización. Duplica las propiedades de FacturaCompra
    /// (en vez de envolverla) para permitir binding directo de DataGridView por
    /// DataPropertyName, sin indirección tipo "Factura.Campo".
    /// </summary>
    public class InvoicePreviewRow
    {
        public string SourceFilePath { get; set; } = string.Empty;
        public string SourceFileName => Path.GetFileName(SourceFilePath);

        public int Mes_CC { get; set; }
        public int Anio_CC { get; set; }
        public DateTime Fecha_Comprobante_CC { get; set; }
        public string Tipo_Comprobante_CC { get; set; } = string.Empty;
        public string Punto_Venta_CC { get; set; } = string.Empty;
        public string Nro_Comprobante_CC { get; set; } = string.Empty;
        public string? Nro_Despacho_CC { get; set; }
        public string Codigo_Vendedor_CC { get; set; } = "80";
        public string Nro_Vendedor_CC { get; set; } = string.Empty;
        public string Nombre_Vendedor_CC { get; set; } = string.Empty;
        public decimal Importe_Total_Operacion_CC { get; set; }
        public decimal Importe_Conceptos_NO_Neto_Gravado_CC { get; set; }
        public decimal Importe_Operacion_Exenta_CC { get; set; }
        public decimal Importe_Perc_IVA_CC { get; set; }
        public decimal Importe_Perc_Imp_Nacional_CC { get; set; }
        public decimal Importe_Perc_IIBB_CC { get; set; }
        public decimal Importe_Perc_Imp_Mun_CC { get; set; }
        public decimal Importe_Impuesto_Interno_CC { get; set; }
        public string Codigo_Moneda_CC { get; set; } = "PES";
        public string Tipo_Cambio_CC { get; set; } = "0001000000";
        public string Cantidad_Alicuota_IVA_CC { get; set; } = "1";
        public string? Codigo_Operacion_CC { get; set; }
        public decimal Codigo_Fiscal_CC { get; set; }
        public string? Otro_Tributo_CC { get; set; }
        public string CUIT_Emisor_CC { get; set; } = string.Empty;
        public string Denominacion_Emisor_CC { get; set; } = string.Empty;
        public decimal IVA_Comision_CC { get; set; }
        public decimal Porc_Iva_CC { get; set; }
        public decimal Neto_CC { get; set; }
        public decimal Ret_Iva_CC { get; set; }
        public int? Nro_Proveedor { get; set; }
        public DateTime? Fecha_Caja_Banco_CC { get; set; }

        /// <summary>Nombres de propiedad cuyo valor no pudo extraerse; usado solo por el grid, no se persiste.</summary>
        public HashSet<string> MissingFields { get; set; } = new();

        /// <summary>Nombres de propiedad autocorregidos por validación cruzada Total/Neto/IVA; usado solo por el grid, no se persiste.</summary>
        public HashSet<string> CorrectedFields { get; set; } = new();
        public bool HasPendingReview => MissingFields.Count > 0;

        public static InvoicePreviewRow FromExtraction(
            FacturaCompra factura, string sourceFilePath, HashSet<string> missingFields, HashSet<string> correctedFields)
        {
            return new InvoicePreviewRow
            {
                SourceFilePath = sourceFilePath,
                MissingFields = new HashSet<string>(missingFields),
                CorrectedFields = new HashSet<string>(correctedFields),
                Mes_CC = factura.Mes_CC,
                Anio_CC = factura.Anio_CC,
                Fecha_Comprobante_CC = factura.Fecha_Comprobante_CC,
                Tipo_Comprobante_CC = factura.Tipo_Comprobante_CC,
                Punto_Venta_CC = factura.Punto_Venta_CC,
                Nro_Comprobante_CC = factura.Nro_Comprobante_CC,
                Nro_Despacho_CC = factura.Nro_Despacho_CC,
                Codigo_Vendedor_CC = factura.Codigo_Vendedor_CC,
                Nro_Vendedor_CC = factura.Nro_Vendedor_CC,
                Nombre_Vendedor_CC = factura.Nombre_Vendedor_CC,
                Importe_Total_Operacion_CC = factura.Importe_Total_Operacion_CC,
                Importe_Conceptos_NO_Neto_Gravado_CC = factura.Importe_Conceptos_NO_Neto_Gravado_CC,
                Importe_Operacion_Exenta_CC = factura.Importe_Operacion_Exenta_CC,
                Importe_Perc_IVA_CC = factura.Importe_Perc_IVA_CC,
                Importe_Perc_Imp_Nacional_CC = factura.Importe_Perc_Imp_Nacional_CC,
                Importe_Perc_IIBB_CC = factura.Importe_Perc_IIBB_CC,
                Importe_Perc_Imp_Mun_CC = factura.Importe_Perc_Imp_Mun_CC,
                Importe_Impuesto_Interno_CC = factura.Importe_Impuesto_Interno_CC,
                Codigo_Moneda_CC = factura.Codigo_Moneda_CC,
                Tipo_Cambio_CC = factura.Tipo_Cambio_CC,
                Cantidad_Alicuota_IVA_CC = factura.Cantidad_Alicuota_IVA_CC,
                Codigo_Operacion_CC = factura.Codigo_Operacion_CC,
                Codigo_Fiscal_CC = factura.Codigo_Fiscal_CC,
                Otro_Tributo_CC = factura.Otro_Tributo_CC,
                CUIT_Emisor_CC = factura.CUIT_Emisor_CC,
                Denominacion_Emisor_CC = factura.Denominacion_Emisor_CC,
                IVA_Comision_CC = factura.IVA_Comision_CC,
                Porc_Iva_CC = factura.Porc_Iva_CC,
                Neto_CC = factura.Neto_CC,
                Ret_Iva_CC = factura.Ret_Iva_CC,
                Nro_Proveedor = factura.Nro_Proveedor,
                Fecha_Caja_Banco_CC = factura.Fecha_Caja_Banco_CC,
            };
        }

        public FacturaCompra ToFacturaCompra()
        {
            return new FacturaCompra
            {
                Mes_CC = Mes_CC,
                Anio_CC = Anio_CC,
                Fecha_Comprobante_CC = Fecha_Comprobante_CC,
                Tipo_Comprobante_CC = Tipo_Comprobante_CC,
                Punto_Venta_CC = Punto_Venta_CC,
                Nro_Comprobante_CC = Nro_Comprobante_CC,
                Nro_Despacho_CC = Nro_Despacho_CC,
                Codigo_Vendedor_CC = Codigo_Vendedor_CC,
                Nro_Vendedor_CC = Nro_Vendedor_CC,
                Nombre_Vendedor_CC = Nombre_Vendedor_CC,
                Importe_Total_Operacion_CC = Importe_Total_Operacion_CC,
                Importe_Conceptos_NO_Neto_Gravado_CC = Importe_Conceptos_NO_Neto_Gravado_CC,
                Importe_Operacion_Exenta_CC = Importe_Operacion_Exenta_CC,
                Importe_Perc_IVA_CC = Importe_Perc_IVA_CC,
                Importe_Perc_Imp_Nacional_CC = Importe_Perc_Imp_Nacional_CC,
                Importe_Perc_IIBB_CC = Importe_Perc_IIBB_CC,
                Importe_Perc_Imp_Mun_CC = Importe_Perc_Imp_Mun_CC,
                Importe_Impuesto_Interno_CC = Importe_Impuesto_Interno_CC,
                Codigo_Moneda_CC = Codigo_Moneda_CC,
                Tipo_Cambio_CC = Tipo_Cambio_CC,
                Cantidad_Alicuota_IVA_CC = Cantidad_Alicuota_IVA_CC,
                Codigo_Operacion_CC = Codigo_Operacion_CC,
                Codigo_Fiscal_CC = Codigo_Fiscal_CC,
                Otro_Tributo_CC = Otro_Tributo_CC,
                CUIT_Emisor_CC = CUIT_Emisor_CC,
                Denominacion_Emisor_CC = Denominacion_Emisor_CC,
                IVA_Comision_CC = IVA_Comision_CC,
                Porc_Iva_CC = Porc_Iva_CC,
                Neto_CC = Neto_CC,
                Ret_Iva_CC = Ret_Iva_CC,
                Nro_Proveedor = Nro_Proveedor,
                Fecha_Caja_Banco_CC = Fecha_Caja_Banco_CC,
            };
        }
    }
}
