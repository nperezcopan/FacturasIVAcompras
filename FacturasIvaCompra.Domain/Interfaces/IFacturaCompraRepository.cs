using FacturasIvaCompra.Domain.Entities;
using FacturasIvaCompra.Domain.Models;

namespace FacturasIvaCompra.Domain.Interfaces
{
    /// <summary>
    /// Inserta un lote completo de facturas en dbo.AFIP_Citi_Compra dentro de una
    /// única transacción (todo o nada).
    /// </summary>
    public interface IFacturaCompraRepository
    {
        Task InsertBatchAsync(IReadOnlyList<FacturaCompra> facturas, CancellationToken ct = default);

        /// <summary>
        /// De las claves de comprobante recibidas (Tipo + Punto de Venta + Nro + CUIT Emisor),
        /// devuelve las representaciones canónicas (<see cref="ComprobanteKey.Canonico"/>) de
        /// las que ya existen en dbo.AFIP_Citi_Compra (comparando sin espacios de relleno del char).
        /// </summary>
        Task<HashSet<string>> GetExistingComprobantesAsync(IEnumerable<ComprobanteKey> claves, CancellationToken ct = default);

        /// <summary>
        /// Busca en dbo.PROVEEDORES el Nro_Proveedor correspondiente a cada CUIT recibido
        /// (comparando sin espacios de relleno). Los CUIT sin proveedor asociado no aparecen
        /// en el diccionario devuelto.
        /// </summary>
        Task<Dictionary<string, int>> GetNrosProveedorPorCuitAsync(IEnumerable<string> cuits, CancellationToken ct = default);
    }
}
