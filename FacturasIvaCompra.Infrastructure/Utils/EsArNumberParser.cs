using System.Globalization;
using System.Text.RegularExpressions;

namespace FacturasIvaCompra.Infrastructure.Utils
{
    /// <summary>
    /// Parsea montos y fechas de comprobantes fiscales. La mayoría usa el formato es-AR
    /// (punto de miles, coma decimal; fechas dd/MM/aaaa), pero algunos proveedores (p.ej.
    /// STOP LOSS Bureau de Reaseguros) imprimen los montos al revés (coma de miles, punto
    /// decimal) — TryParseMonto detecta cuál es el separador decimal por posición en vez de
    /// asumir siempre es-AR.
    /// </summary>
    public static class EsArNumberParser
    {
        private static readonly Regex EspaciosRegex = new(@"\s+", RegexOptions.Compiled);

        public static bool TryParseMonto(string? raw, out decimal value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            // El texto puede llegar con espacios sueltos pegados a los separadores (PdfPig
            // fragmentando por fuentes de paso ancho, u OCR perdiendo el kerning): "6, 242. 55".
            var trimmed = EspaciosRegex.Replace(raw.Trim(), "");
            if (trimmed.Length == 0) return false;

            var ultimaComa = trimmed.LastIndexOf(',');
            var ultimoPunto = trimmed.LastIndexOf('.');

            string normalized;
            if (ultimaComa > ultimoPunto)
            {
                // es-AR: "1.234,56" — la coma es el separador decimal.
                normalized = trimmed.Replace(".", "").Replace(",", ".");
            }
            else if (ultimoPunto > ultimaComa)
            {
                // en-US: "1,234.56" — el punto es el separador decimal.
                normalized = trimmed.Replace(",", "");
            }
            else
            {
                normalized = trimmed;
            }

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
