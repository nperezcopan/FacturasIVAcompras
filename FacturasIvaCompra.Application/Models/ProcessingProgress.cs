namespace FacturasIvaCompra.Application.Models
{
    /// <summary>
    /// Transporta el estado de progreso actual del procesamiento hacia la UI.
    /// Incluye estadísticas acumuladas actualizadas en cada archivo.
    /// </summary>
    public class ProcessingProgress
    {
        public int TotalPages { get; }
        public int CurrentPage { get; }
        public string Message { get; }
        public bool IsFinished { get; }

        // Estadísticas acumuladas
        public int TotalProcessed { get; }
        public int TotalCopied { get; }
        public int TotalFolderNotFound { get; }
        public int TotalErrors { get; }
        public TimeSpan ElapsedTotal { get; }

        public ProcessingProgress(
            int currentPage,
            int totalPages,
            string message,
            bool isFinished = false,
            int totalProcessed = 0,
            int totalCopied = 0,
            int totalFolderNotFound = 0,
            int totalErrors = 0,
            TimeSpan elapsedTotal = default)
        {
            CurrentPage = currentPage;
            TotalPages = totalPages;
            Message = message;
            IsFinished = isFinished;
            TotalProcessed = totalProcessed;
            TotalCopied = totalCopied;
            TotalFolderNotFound = totalFolderNotFound;
            TotalErrors = totalErrors;
            ElapsedTotal = elapsedTotal;
        }
    }
}
