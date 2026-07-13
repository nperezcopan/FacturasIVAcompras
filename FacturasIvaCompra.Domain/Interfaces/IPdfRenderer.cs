namespace FacturasIvaCompra.Domain.Interfaces
{
    public interface IPdfRenderer
    {
        byte[] RenderPageToImage(string pdfPath, int pageIndex, int dpi);

        byte[] RenderPageRegionToImage(string pdfPath, int pageIndex, int dpi,
            double startFraction = 0.0, double endFraction = 1.0);
    }
}
