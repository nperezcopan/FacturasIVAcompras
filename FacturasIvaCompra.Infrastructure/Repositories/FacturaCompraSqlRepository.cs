using FacturasIvaCompra.Domain.Entities;
using FacturasIvaCompra.Domain.Interfaces;
using FacturasIvaCompra.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FacturasIvaCompra.Infrastructure.Repositories
{
    /// <summary>
    /// Inserta el lote completo de facturas en dbo.AFIP_Citi_Compra (servidor "07") dentro de
    /// una única transacción: todo o nada. Cod_CC no se incluye (columna IDENTITY autogenerada
    /// en SQL — a confirmar contra el esquema real antes del primer uso en producción).
    /// </summary>
    public class FacturaCompraSqlRepository : IFacturaCompraRepository
    {
        private const string InsertSql = """
            INSERT INTO dbo.AFIP_Citi_Compra
            (Mes_CC, Anio_CC, Fecha_Comprobante_CC, Tipo_Comprobante_CC, Punto_Venta_CC,
             Nro_Comprobante_CC, Nro_Despacho_CC, Codigo_Vendedor_CC, Nro_Vendedor_CC, Nombre_Vendedor_CC,
             Importe_Total_Operacion_CC, Importe_Conceptos_NO_Neto_Gravado_CC, Importe_Operacion_Exenta_CC,
             Importe_Perc_IVA_CC, Importe_Perc_Imp_Nacional_CC, Importe_Perc_IIBB_CC, Importe_Perc_Imp_Mun_CC,
             Importe_Impuesto_Interno_CC, Codigo_Moneda_CC, Tipo_Cambio_CC, Cantidad_Alicuota_IVA_CC,
             Codigo_Operacion_CC, Codigo_Fiscal_CC, Otro_Tributo_CC, CUIT_Emisor_CC, Denominacion_Emisor_CC,
             IVA_Comision_CC, Porc_Iva_CC, Neto_CC, Ret_Iva_CC, Nro_Proveedor, Fecha_Caja_Banco_CC, migrado_pdf)
            VALUES
            (@Mes_CC, @Anio_CC, @Fecha_Comprobante_CC, @Tipo_Comprobante_CC, @Punto_Venta_CC,
             @Nro_Comprobante_CC, @Nro_Despacho_CC, @Codigo_Vendedor_CC, @Nro_Vendedor_CC, @Nombre_Vendedor_CC,
             @Importe_Total_Operacion_CC, @Importe_Conceptos_NO_Neto_Gravado_CC, @Importe_Operacion_Exenta_CC,
             @Importe_Perc_IVA_CC, @Importe_Perc_Imp_Nacional_CC, @Importe_Perc_IIBB_CC, @Importe_Perc_Imp_Mun_CC,
             @Importe_Impuesto_Interno_CC, @Codigo_Moneda_CC, @Tipo_Cambio_CC, @Cantidad_Alicuota_IVA_CC,
             @Codigo_Operacion_CC, @Codigo_Fiscal_CC, @Otro_Tributo_CC, @CUIT_Emisor_CC, @Denominacion_Emisor_CC,
             @IVA_Comision_CC, @Porc_Iva_CC, @Neto_CC, @Ret_Iva_CC, @Nro_Proveedor, @Fecha_Caja_Banco_CC, 1);
            """;

        private readonly IConfiguration _configuration;
        private readonly ILogger<FacturaCompraSqlRepository> _logger;

        public FacturaCompraSqlRepository(IConfiguration configuration, ILogger<FacturaCompraSqlRepository> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task InsertBatchAsync(IReadOnlyList<FacturaCompra> facturas, CancellationToken ct = default)
        {
            var connectionString = _configuration["ConnectionStrings:AfipCitiCompra"]
                ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:AfipCitiCompra en appsettings.json.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = InsertSql;

                foreach (var factura in facturas)
                {
                    command.Parameters.Clear();
                    AddParameters(command, factura);
                    await command.ExecuteNonQueryAsync(ct);
                }

                await transaction.CommitAsync(ct);
                _logger.LogInformation("Lote de {Count} factura(s) insertado correctamente en AFIP_Citi_Compra.", facturas.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al insertar el lote en AFIP_Citi_Compra. Se revierte la transacción completa.");
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        private void AddParameters(SqlCommand command, FacturaCompra f)
        {
            command.Parameters.AddWithValue("@Mes_CC", f.Mes_CC);
            command.Parameters.AddWithValue("@Anio_CC", f.Anio_CC);
            command.Parameters.AddWithValue("@Fecha_Comprobante_CC", f.Fecha_Comprobante_CC);
            command.Parameters.AddWithValue("@Tipo_Comprobante_CC", Truncate(f, nameof(f.Tipo_Comprobante_CC), f.Tipo_Comprobante_CC, 3));
            command.Parameters.AddWithValue("@Punto_Venta_CC", Truncate(f, nameof(f.Punto_Venta_CC), f.Punto_Venta_CC, 5));
            command.Parameters.AddWithValue("@Nro_Comprobante_CC", Truncate(f, nameof(f.Nro_Comprobante_CC), f.Nro_Comprobante_CC, 20));
            command.Parameters.AddWithValue("@Nro_Despacho_CC", (object?)Truncate(f, nameof(f.Nro_Despacho_CC), f.Nro_Despacho_CC, 52) ?? DBNull.Value);
            command.Parameters.AddWithValue("@Codigo_Vendedor_CC", Truncate(f, nameof(f.Codigo_Vendedor_CC), f.Codigo_Vendedor_CC, 2));
            command.Parameters.AddWithValue("@Nro_Vendedor_CC", Truncate(f, nameof(f.Nro_Vendedor_CC), f.Nro_Vendedor_CC, 20));
            command.Parameters.AddWithValue("@Nombre_Vendedor_CC", Truncate(f, nameof(f.Nombre_Vendedor_CC), f.Nombre_Vendedor_CC, 30));
            command.Parameters.AddWithValue("@Importe_Total_Operacion_CC", f.Importe_Total_Operacion_CC);
            command.Parameters.AddWithValue("@Importe_Conceptos_NO_Neto_Gravado_CC", f.Importe_Conceptos_NO_Neto_Gravado_CC);
            command.Parameters.AddWithValue("@Importe_Operacion_Exenta_CC", f.Importe_Operacion_Exenta_CC);
            command.Parameters.AddWithValue("@Importe_Perc_IVA_CC", f.Importe_Perc_IVA_CC);
            command.Parameters.AddWithValue("@Importe_Perc_Imp_Nacional_CC", f.Importe_Perc_Imp_Nacional_CC);
            command.Parameters.AddWithValue("@Importe_Perc_IIBB_CC", f.Importe_Perc_IIBB_CC);
            command.Parameters.AddWithValue("@Importe_Perc_Imp_Mun_CC", f.Importe_Perc_Imp_Mun_CC);
            command.Parameters.AddWithValue("@Importe_Impuesto_Interno_CC", f.Importe_Impuesto_Interno_CC);
            command.Parameters.AddWithValue("@Codigo_Moneda_CC", Truncate(f, nameof(f.Codigo_Moneda_CC), f.Codigo_Moneda_CC, 3));
            command.Parameters.AddWithValue("@Tipo_Cambio_CC", Truncate(f, nameof(f.Tipo_Cambio_CC), f.Tipo_Cambio_CC, 10));
            command.Parameters.AddWithValue("@Cantidad_Alicuota_IVA_CC", Truncate(f, nameof(f.Cantidad_Alicuota_IVA_CC), f.Cantidad_Alicuota_IVA_CC, 1));
            command.Parameters.AddWithValue("@Codigo_Operacion_CC", (object?)Truncate(f, nameof(f.Codigo_Operacion_CC), f.Codigo_Operacion_CC, 1) ?? DBNull.Value);
            command.Parameters.AddWithValue("@Codigo_Fiscal_CC", f.Codigo_Fiscal_CC);
            command.Parameters.AddWithValue("@Otro_Tributo_CC", (object?)Truncate(f, nameof(f.Otro_Tributo_CC), f.Otro_Tributo_CC, 15) ?? DBNull.Value);
            command.Parameters.AddWithValue("@CUIT_Emisor_CC", Truncate(f, nameof(f.CUIT_Emisor_CC), f.CUIT_Emisor_CC, 11));
            command.Parameters.AddWithValue("@Denominacion_Emisor_CC", Truncate(f, nameof(f.Denominacion_Emisor_CC), f.Denominacion_Emisor_CC, 30));
            command.Parameters.AddWithValue("@IVA_Comision_CC", f.IVA_Comision_CC);
            command.Parameters.AddWithValue("@Porc_Iva_CC", f.Porc_Iva_CC);
            command.Parameters.AddWithValue("@Neto_CC", f.Neto_CC);
            command.Parameters.AddWithValue("@Ret_Iva_CC", f.Ret_Iva_CC);
            command.Parameters.AddWithValue("@Nro_Proveedor", (object?)f.Nro_Proveedor ?? DBNull.Value);
            command.Parameters.AddWithValue("@Fecha_Caja_Banco_CC", (object?)f.Fecha_Caja_Banco_CC ?? DBNull.Value);
        }

        /// <summary>
        /// Consulta por lote la existencia de cada clave (Tipo + Punto de Venta + Nro +
        /// CUIT Emisor) contra dbo.AFIP_Citi_Compra. Como SQL Server no soporta comparar tuplas
        /// directamente, arma un WHERE con una condición OR por clave distinta (parametrizada,
        /// sin riesgo de inyección) — asumible para el tamaño de lote habitual de este flujo
        /// (decenas de comprobantes, no miles).
        /// </summary>
        public async Task<HashSet<string>> GetExistingComprobantesAsync(IEnumerable<ComprobanteKey> claves, CancellationToken ct = default)
        {
            var distintas = claves
                .Select(c => new ComprobanteKey(c.TipoComprobante.Trim(), c.PuntoVenta.Trim(), c.NroComprobante.Trim(), c.CuitEmisor.Trim()))
                .Distinct()
                .ToList();

            var existentes = new HashSet<string>();
            if (distintas.Count == 0) return existentes;

            var connectionString = _configuration["ConnectionStrings:AfipCitiCompra"]
                ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:AfipCitiCompra en appsettings.json.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            var condiciones = new List<string>();
            for (var i = 0; i < distintas.Count; i++)
            {
                condiciones.Add(
                    $"(RTRIM(Tipo_Comprobante_CC) = @t{i} AND RTRIM(Punto_Venta_CC) = @p{i} " +
                    $"AND RTRIM(Nro_Comprobante_CC) = @n{i} AND RTRIM(CUIT_Emisor_CC) = @c{i})");
                command.Parameters.AddWithValue($"@t{i}", distintas[i].TipoComprobante);
                command.Parameters.AddWithValue($"@p{i}", distintas[i].PuntoVenta);
                command.Parameters.AddWithValue($"@n{i}", distintas[i].NroComprobante);
                command.Parameters.AddWithValue($"@c{i}", distintas[i].CuitEmisor);
            }

            command.CommandText = $"""
                SELECT DISTINCT RTRIM(Tipo_Comprobante_CC), RTRIM(Punto_Venta_CC), RTRIM(Nro_Comprobante_CC), RTRIM(CUIT_Emisor_CC)
                FROM dbo.AFIP_Citi_Compra
                WHERE {string.Join(" OR ", condiciones)};
                """;

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var clave = new ComprobanteKey(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                existentes.Add(clave.Canonico);
            }

            return existentes;
        }

        /// <summary>
        /// Resuelve Nro_Proveedor por CUIT contra dbo.PROVEEDORES (columnas CUIT / NRO_PROVEEDOR).
        /// dbo.PROVEEDORES.CUIT guarda el CUIT con un cero de relleno adelante (char(12): "0" +
        /// los 11 dígitos reales, confirmado contra el CUIT propio de la empresa: "30500051929"
        /// vive como "030500051929"), mientras que CUIT_Emisor_CC son los 11 dígitos "limpios" —
        /// por eso se compara contra los últimos 11 caracteres, no el campo completo.
        /// Los CUIT sin proveedor asociado simplemente no aparecen en el diccionario devuelto —
        /// FacturaCompra.Nro_Proveedor queda en null para esos casos, igual que hoy.
        /// </summary>
        public async Task<Dictionary<string, int>> GetNrosProveedorPorCuitAsync(IEnumerable<string> cuits, CancellationToken ct = default)
        {
            var distintos = cuits.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct().ToList();
            var resultado = new Dictionary<string, int>();
            if (distintos.Count == 0) return resultado;

            var connectionString = _configuration["ConnectionStrings:AfipCitiCompra"]
                ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:AfipCitiCompra en appsettings.json.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            var nombresParametros = distintos.Select((_, i) => $"@c{i}").ToList();
            command.CommandText = $"""
                SELECT RIGHT(RTRIM(CUIT), 11), NRO_PROVEEDOR
                FROM dbo.PROVEEDORES
                WHERE RIGHT(RTRIM(CUIT), 11) IN ({string.Join(", ", nombresParametros)});
                """;
            for (var i = 0; i < distintos.Count; i++)
            {
                command.Parameters.AddWithValue(nombresParametros[i], distintos[i]);
            }

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                resultado[reader.GetString(0)] = reader.GetInt32(1);
            }

            return resultado;
        }

        /// <summary>
        /// Última línea de defensa antes del INSERT: recorta al ancho real de la columna
        /// `char` en SQL Server (ver sp_help de dbo.AFIP_Citi_Compra). Sin esto, un valor más
        /// largo que la columna (extraído del PDF o tipeado a mano en la grilla) hace fallar
        /// todo el lote con "String or binary data would be truncated".
        /// </summary>
        private string? Truncate(FacturaCompra f, string campo, string? valor, int maxLength)
        {
            if (valor is null || valor.Length <= maxLength) return valor;

            _logger.LogWarning(
                "{Campo} de la factura {Comprobante} mide {Actual} caracteres y se recortó a {Max} para entrar en la columna: \"{Valor}\" → \"{Truncado}\".",
                campo, f.Nro_Comprobante_CC, valor.Length, maxLength, valor, valor[..maxLength]);
            return valor[..maxLength];
        }
    }
}
