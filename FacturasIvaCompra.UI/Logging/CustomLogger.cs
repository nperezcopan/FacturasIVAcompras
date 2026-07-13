using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace FacturasIvaCompra.UI.Logging
{
    public class CustomLogger : ILogger
    {
        private readonly string _categoryName;
        private static readonly object LockObj = new();
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "app.log");

        public static event Action<string>? OnLog;

        public CustomLogger(string categoryName)
        {
            _categoryName = categoryName;

            var directory = Path.GetDirectoryName(LogFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logLine = $"[{timestamp}] [{logLevel}] {message}";
            if (exception != null)
            {
                logLine += Environment.NewLine + exception.ToString();
            }

            lock (LockObj)
            {
                try
                {
                    File.AppendAllText(LogFilePath, logLine + Environment.NewLine);
                }
                catch
                {
                    // Evitar caídas si el archivo de log está bloqueado
                }
            }

            OnLog?.Invoke(logLine);
        }
    }

    public class CustomLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new CustomLogger(categoryName);
        }

        public void Dispose() { }
    }
}
