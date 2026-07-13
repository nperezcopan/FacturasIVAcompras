using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using FacturasIvaCompra.Domain.Interfaces;

namespace FacturasIvaCompra.Infrastructure.Services
{
    /// <summary>
    /// Extrae texto reconstruyendo el orden de lectura real (arriba a abajo, izquierda a
    /// derecha) a partir de la posición (X/Y) de cada palabra, en vez de usar Page.Text
    /// directamente. Necesario porque en layouts tipo formulario/tabla (p.ej. comprobantes
    /// AFIP/ARCA) Page.Text concatena las palabras en el orden de los operadores del content
    /// stream del PDF, que no coincide con el orden visual y no inserta saltos de línea entre
    /// filas — palabras de filas o columnas distintas terminan pegadas sin separador
    /// ("ORIGINALCASTILLO GERMAN LEONARDO"), lo que rompe cualquier extracción por regex basada
    /// en adyacencia de etiqueta/valor.
    /// </summary>
    public class PdfPigTextExtractor : IPdfTextExtractor
    {
        // Dos palabras se consideran de la misma línea visual si sus posiciones verticales
        // (Bottom) difieren menos que esta fracción de la altura de línea promedio de la
        // página; evita que jitter normal de baseline entre palabras de una misma fila las
        // separe en líneas distintas. En tablas, el valor numérico de una celda puede quedar
        // centrado unos puntos más abajo que la etiqueta/alícuota de su misma fila visual
        // (observado: ~3,2pt de diferencia contra ~4,6pt de altura de línea) — la tolerancia
        // se ajustó a 1.0 (en vez de 0.5) para no partir esa fila en dos líneas.
        private const double ToleranciaLineaFraccionAltura = 1.0;

        public string ExtractTextFromPage(string pdfPath, int pageIndex)
        {
            using (var document = PdfDocument.Open(pdfPath))
            {
                if (pageIndex < 0 || pageIndex >= document.NumberOfPages)
                    return string.Empty;

                // PdfPig uses 1-based index for pages
                var page = document.GetPage(pageIndex + 1);
                return ReconstruirOrdenDeLectura(page);
            }
        }

        public int GetPageCount(string pdfPath)
        {
            using (var document = PdfDocument.Open(pdfPath))
            {
                return document.NumberOfPages;
            }
        }

        private static string ReconstruirOrdenDeLectura(Page page)
        {
            var palabras = page.GetWords()
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .OrderByDescending(w => w.BoundingBox.Bottom)
                .ToList();

            if (palabras.Count == 0) return page.Text ?? string.Empty;

            var alturaTipica = palabras.Average(w => w.BoundingBox.Height);
            var tolerancia = Math.Max(alturaTipica * ToleranciaLineaFraccionAltura, 1.0);

            var lineas = new List<List<Word>>();
            foreach (var palabra in palabras)
            {
                var lineaActual = lineas.Count > 0 ? lineas[^1] : null;
                if (lineaActual != null
                    && Math.Abs(lineaActual[0].BoundingBox.Bottom - palabra.BoundingBox.Bottom) <= tolerancia)
                {
                    lineaActual.Add(palabra);
                }
                else
                {
                    lineas.Add(new List<Word> { palabra });
                }
            }

            var sb = new StringBuilder();
            foreach (var linea in lineas)
            {
                var ordenada = linea.OrderBy(w => w.BoundingBox.Left);
                sb.AppendLine(string.Join(" ", ordenada.Select(w => w.Text)));
            }

            return sb.ToString();
        }
    }
}
