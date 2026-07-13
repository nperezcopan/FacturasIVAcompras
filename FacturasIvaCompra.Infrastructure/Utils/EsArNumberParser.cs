using System.Globalization;

namespace FacturasIvaCompra.Infrastructure.Utils
{
    /// <summary>
    /// Parsea montos y fechas en el formato es-AR habitual de comprobantes fiscales
    /// (punto como separador de miles, coma como separador decimal; fechas dd/MM/aaaa).
    /// </summary>
    public static class EsArNumberParser
    {
        public static bool TryParseMonto(string? raw, out decimal value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var normalized = raw.Trim().Replace(".", "").Replace(",", ".");
            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        public static decimal ParseMonto(string raw)
        {
            TryParseMonto(raw, out var value);
            return value;
        }

        public static bool TryParseFecha(string? raw, out DateTime fecha)
        {
            fecha = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var formats = new[] { "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy" };
            return DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.GetCultureInfo("es-AR"),
                DateTimeStyles.None, out fecha);
        }
    }
}
