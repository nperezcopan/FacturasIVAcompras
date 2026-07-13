namespace FacturasIvaCompra.Domain.Interfaces
{
    /// <summary>
    /// Mueve un PDF ya aprobado desde la carpeta de origen a la carpeta de procesados.
    /// </summary>
    public interface IFileMover
    {
        /// <returns>Ruta final del archivo movido (puede diferir si hubo colisión de nombre).</returns>
        string MoveToProcessed(string sourceFilePath);
    }
}
