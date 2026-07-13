namespace FacturasIvaCompra.Domain.Interfaces
{
    public interface IPdfTextExtractor
    {
        string ExtractTextFromPage(string pdfPath, int pageIndex);

        int GetPageCount(string pdfPath);
    }
}
