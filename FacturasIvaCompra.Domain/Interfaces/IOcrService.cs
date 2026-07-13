namespace FacturasIvaCompra.Domain.Interfaces
{
    public interface IOcrService
    {
        string PerformOcr(byte[] imageBytes, string language);
    }
}
