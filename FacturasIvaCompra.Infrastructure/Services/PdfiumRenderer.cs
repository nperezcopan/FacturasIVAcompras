using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using PDFiumSharp;
using PDFiumSharp.Types;
using FacturasIvaCompra.Domain.Interfaces;

namespace FacturasIvaCompra.Infrastructure.Services
{
    public class PdfiumRenderer : IPdfRenderer
    {
        public byte[] RenderPageToImage(string pdfPath, int pageIndex, int dpi)
            => RenderPageRegionToImage(pdfPath, pageIndex, dpi, 0.0, 1.0);

        public byte[] RenderPageRegionToImage(string pdfPath, int pageIndex, int dpi,
            double startFraction = 0.0, double endFraction = 1.0)
        {
            // Clamp fractions
            startFraction = Math.Clamp(startFraction, 0.0, 1.0);
            endFraction   = Math.Clamp(endFraction,   0.0, 1.0);
            if (endFraction <= startFraction) endFraction = 1.0;

            using var doc = new PdfDocument(pdfPath);
            if (pageIndex < 0 || pageIndex >= doc.Pages.Count)
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Página fuera de rango.");

            var page = doc.Pages[pageIndex];

            double scale = dpi / 72.0;
            int fullWidth  = (int)(page.Width  * scale);
            int fullHeight = (int)(page.Height * scale);

            using var pdfiumBitmap = new PDFiumBitmap(fullWidth, fullHeight, false);
            // Sin este relleno, PDFium deja en negro cualquier zona de la página que no tenga
            // un rectángulo blanco explícito de fondo (p.ej. plantillas ARCA con celdas de
            // tabla sin fill propio). Eso tapa el texto ahí y el OCR nunca lo detecta, aunque
            // el PDF sí lo tenga (visto en la celda "Producto/Servicio" con el concepto
            // "COMISIONES...").
            pdfiumBitmap.Fill(new PDFiumColor(255, 255, 255, 255));
            page.Render(pdfiumBitmap);

            using var fullBitmap = new Bitmap(
                fullWidth, fullHeight,
                pdfiumBitmap.Stride,
                PixelFormat.Format32bppRgb,
                pdfiumBitmap.Scan0);

            // Crop to the requested vertical region
            int cropY      = (int)(fullHeight * startFraction);
            int cropHeight = (int)(fullHeight * (endFraction - startFraction));
            cropHeight     = Math.Max(1, Math.Min(cropHeight, fullHeight - cropY));

            var cropRect    = new Rectangle(0, cropY, fullWidth, cropHeight);
            using var croppedBmp = fullBitmap.Clone(cropRect, fullBitmap.PixelFormat);

            using var ms = new MemoryStream();
            croppedBmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }
}
