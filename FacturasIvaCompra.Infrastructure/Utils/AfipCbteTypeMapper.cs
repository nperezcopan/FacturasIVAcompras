namespace FacturasIvaCompra.Infrastructure.Utils
{
    /// <summary>
    /// Mapeo estándar AFIP de tipo de comprobante (letra o código impreso) al código
    /// numérico usado en Tipo_Comprobante_CC.
    /// </summary>
    public static class AfipCbteTypeMapper
    {
        // Códigos AFIP oficiales para los comprobantes de compra más habituales.
        private static readonly Dictionary<int, int> CodigoAfipAValor = new()
        {
            [1]  = 1,  // Factura A
            [6]  = 6,  // Factura B
            [11] = 11, // Factura C
            [2]  = 2,  // Nota de Débito A
            [7]  = 7,  // Nota de Débito B
            [12] = 12, // Nota de Débito C
            [3]  = 3,  // Nota de Crédito A
            [8]  = 8,  // Nota de Crédito B
            [13] = 13, // Nota de Crédito C
            [4]  = 4,  // Recibo A
            [9]  = 9,  // Recibo B
            [15] = 15, // Recibo C
        };

        private static readonly Dictionary<(string TipoDocumento, char Letra), int> PorLetra = new()
        {
            [("FACTURA", 'A')] = 1,
            [("FACTURA", 'B')] = 6,
            [("FACTURA", 'C')] = 11,
            [("NOTA DE DEBITO", 'A')] = 2,
            [("NOTA DE DEBITO", 'B')] = 7,
            [("NOTA DE DEBITO", 'C')] = 12,
            [("NOTA DE CREDITO", 'A')] = 3,
            [("NOTA DE CREDITO", 'B')] = 8,
            [("NOTA DE CREDITO", 'C')] = 13,
        };

        /// <summary>Si el código impreso ("Cód. N° 01") ya es un código AFIP válido, se usa directo.</summary>
        public static bool TryMapCodigo(int codigoImpreso, out int tipoComprobanteCC)
        {
            return CodigoAfipAValor.TryGetValue(codigoImpreso, out tipoComprobanteCC);
        }

        /// <summary>Fallback: tipo de documento detectado por texto ("FACTURA"/"NOTA DE CRÉDITO"/"NOTA DE DÉBITO") + letra A/B/C.</summary>
        public static bool TryMapPorLetra(string tipoDocumento, char letra, out int tipoComprobanteCC)
        {
            var key = (NormalizarTipoDocumento(tipoDocumento), char.ToUpperInvariant(letra));
            return PorLetra.TryGetValue(key, out tipoComprobanteCC);
        }

        private static string NormalizarTipoDocumento(string tipoDocumento)
        {
            return tipoDocumento
                .ToUpperInvariant()
                .Replace("É", "E")
                .Replace("Í", "I")
                .Trim();
        }
    }
}
