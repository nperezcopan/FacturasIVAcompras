namespace FacturasIvaCompra.Domain.Models
{
    /// <summary>
    /// Clave natural de un comprobante fiscal AFIP: Tipo + Punto de Venta + Nro. Comprobante +
    /// CUIT del emisor. Nro_Comprobante_CC por sí solo NO es único: cada proveedor numera sus
    /// propios comprobantes desde su propio punto de venta, así que dos proveedores distintos
    /// pueden compartir legítimamente el mismo número.
    /// </summary>
    public record struct ComprobanteKey(string TipoComprobante, string PuntoVenta, string NroComprobante, string CuitEmisor)
    {
        /// <summary>Representación canónica (recortada, sin distinción de mayúsculas) usada para comparar/agrupar.</summary>
        public string Canonico =>
            $"{TipoComprobante.Trim()}|{PuntoVenta.Trim()}|{NroComprobante.Trim()}|{CuitEmisor.Trim()}".ToUpperInvariant();
    }
}
