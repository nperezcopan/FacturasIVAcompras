namespace FacturasIvaCompra.Domain.Entities
{
    /// <summary>
    /// Mapea las columnas de negocio de dbo.AFIP_Citi_Compra (servidor "07").
    /// No incluye Cod_CC (autoincremental en SQL). migrado_pdf lo fija el repositorio en 1.
    ///
    /// Varias columnas que a priori parecen numéricas son en realidad `char` de ancho fijo en
    /// el esquema real (confirmado con sp_help), siguiendo la convención del régimen AFIP RG
    /// 3685 "CITI Compras": códigos y números se guardan como texto con ceros a la izquierda
    /// ajustado al ancho de columna, no como int/decimal. Se tipan aquí como string para
    /// preservar exactamente ese formato (ver GenericInvoiceFieldExtractor para el padding).
    /// </summary>
    public class FacturaCompra
    {
        public int Mes_CC { get; set; }
        public int Anio_CC { get; set; }
        public DateTime Fecha_Comprobante_CC { get; set; }

        /// <summary>char(3), código AFIP zero-padded (Factura A="001", B="006", C="011", etc.).</summary>
        public string Tipo_Comprobante_CC { get; set; } = string.Empty;

        /// <summary>char(5). Zero-padded a 4 dígitos (p.ej. "0004").</summary>
        public string Punto_Venta_CC { get; set; } = string.Empty;

        /// <summary>char(20). Zero-padded a 8 dígitos (p.ej. "00000321").</summary>
        public string Nro_Comprobante_CC { get; set; } = string.Empty;

        /// <summary>char(52). Despacho de importación: no aplica a compras de servicios/bienes locales.</summary>
        public string? Nro_Despacho_CC { get; set; } = null;

        /// <summary>char(2). Código AFIP de documento del vendedor: "80" = CUIT (siempre, ya que Nro_Vendedor_CC es el CUIT).</summary>
        public string Codigo_Vendedor_CC { get; set; } = "80";

        public string Nro_Vendedor_CC { get; set; } = string.Empty;
        public string Nombre_Vendedor_CC { get; set; } = string.Empty;
        public decimal Importe_Total_Operacion_CC { get; set; }
        public decimal Importe_Conceptos_NO_Neto_Gravado_CC { get; set; } = 0;
        public decimal Importe_Operacion_Exenta_CC { get; set; } = 0;
        public decimal Importe_Perc_IVA_CC { get; set; } = 0;
        public decimal Importe_Perc_Imp_Nacional_CC { get; set; } = 0;
        public decimal Importe_Perc_IIBB_CC { get; set; } = 0;
        public decimal Importe_Perc_Imp_Mun_CC { get; set; } = 0;
        public decimal Importe_Impuesto_Interno_CC { get; set; } = 0;
        public string Codigo_Moneda_CC { get; set; } = "PES";

        /// <summary>char(10). Tipo de cambio AFIP: 4 enteros + 6 decimales sin separador ("0001000000" = 1,000000 para PES).</summary>
        public string Tipo_Cambio_CC { get; set; } = "0001000000";

        /// <summary>char(1). Cantidad de alícuotas de IVA distintas en el comprobante.</summary>
        public string Cantidad_Alicuota_IVA_CC { get; set; } = "1";

        /// <summary>char(1). Código de operación especial AFIP (exportación, etc.); en blanco si no aplica.</summary>
        public string? Codigo_Operacion_CC { get; set; } = null;

        public decimal Codigo_Fiscal_CC { get; set; } = 0;

        /// <summary>char(15). Otros tributos; sin equivalente directo en el PDF.</summary>
        public string? Otro_Tributo_CC { get; set; } = null;

        public string CUIT_Emisor_CC { get; set; } = string.Empty;
        public string Denominacion_Emisor_CC { get; set; } = string.Empty;
        public decimal IVA_Comision_CC { get; set; } = 0;
        public decimal Porc_Iva_CC { get; set; }
        public decimal Neto_CC { get; set; }
        public decimal Ret_Iva_CC { get; set; } = 0;
        public int? Nro_Proveedor { get; set; } = null;
        public DateTime? Fecha_Caja_Banco_CC { get; set; } = null;
    }
}
