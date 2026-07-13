using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FacturasIvaCompra.Domain.Interfaces;
using FacturasIvaCompra.Infrastructure.Services;
using FacturasIvaCompra.Infrastructure.Repositories;
using FacturasIvaCompra.Application.Services;
using FacturasIvaCompra.UI.Logging;

using System.Reflection;
using System.Runtime.InteropServices;

namespace FacturasIvaCompra.UI
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Failsafe: Copy and rename the architecture-specific pdfium DLL to pdfium.dll in the base directory
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string sourceDllName = IntPtr.Size == 8 ? "pdfium_x64.dll" : "pdfium_x86.dll";
                string sourcePath = System.IO.Path.Combine(baseDir, sourceDllName);
                string targetPath = System.IO.Path.Combine(baseDir, "pdfium.dll");

                if (System.IO.File.Exists(sourcePath))
                {
                    System.IO.File.Copy(sourcePath, targetPath, true);
                }
                else
                {
                    // Fallback to runtimes folder if root files aren't found
                    string arch = IntPtr.Size == 8 ? "x64" : "x86";
                    string runtimesPath = System.IO.Path.Combine(baseDir, "runtimes", $"win-{arch}", "native", "pdfium.dll");
                    if (System.IO.File.Exists(runtimesPath))
                    {
                        System.IO.File.Copy(runtimesPath, targetPath, true);
                    }
                }
            }
            catch (Exception) { }

            // Register native DLL resolver for PDFiumSharp
            NativeLibrary.SetDllImportResolver(typeof(PDFiumSharp.PdfDocument).Assembly, ResolveDllImport);

            ApplicationConfiguration.Initialize();

            var host = Host.CreateDefaultBuilder(args)
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(context.Configuration);

                    // Servicios de infraestructura
                    services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
                    services.AddSingleton<IPdfRenderer, PdfiumRenderer>();
                    services.AddSingleton<IOcrService, TesseractOcrService>();
                    services.AddSingleton<IInvoiceFieldExtractor, GenericInvoiceFieldExtractor>();
                    services.AddSingleton<IFileMover, FileMoverService>();
                    services.AddSingleton<IFacturaCompraRepository, FacturaCompraSqlRepository>();

                    // Servicios de aplicación y formulario principal
                    services.AddTransient<BatchInvoiceProcessingService>();
                    services.AddTransient<ApprovalService>();
                    services.AddTransient<MainForm>();
                })
                .ConfigureLogging((_, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                    logging.AddProvider(new CustomLoggerProvider());
                })
                .Build();

            using var serviceScope = host.Services.CreateScope();
            var services = serviceScope.ServiceProvider;
            try
            {
                var mainForm = services.GetRequiredService<MainForm>();
                System.Windows.Forms.Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al iniciar la aplicación: {ex.Message}", "Error Crítico",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == "pdfium")
            {
                string arch = IntPtr.Size == 8 ? "x64" : "x86";
                string runtimesPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", $"win-{arch}", "native", "pdfium.dll");
                if (System.IO.File.Exists(runtimesPath))
                {
                    return NativeLibrary.Load(runtimesPath);
                }

                string baseDirDll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pdfium.dll");
                if (System.IO.File.Exists(baseDirDll))
                {
                    return NativeLibrary.Load(baseDirDll);
                }
            }
            return IntPtr.Zero;
        }
    }
}
