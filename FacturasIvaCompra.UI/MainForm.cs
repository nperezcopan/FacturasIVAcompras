using System.ComponentModel;
using FacturasIvaCompra.Application.Models;
using FacturasIvaCompra.Application.Services;
using FacturasIvaCompra.UI.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FacturasIvaCompra.UI
{
    public partial class MainForm : Form
    {
        private readonly BatchInvoiceProcessingService _processingService;
        private readonly ApprovalService _approvalService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MainForm> _logger;

        private BindingList<InvoicePreviewRow> _previewRows = new();

        public MainForm(
            BatchInvoiceProcessingService processingService,
            ApprovalService approvalService,
            IConfiguration configuration,
            ILogger<MainForm> logger)
        {
            _processingService = processingService;
            _approvalService = approvalService;
            _configuration = configuration;
            _logger = logger;

            InitializeComponent();

            CustomLogger.OnLog += LogToConsole;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            lblCarpetaOrigen.Text = $"Carpeta origen: {_configuration["FolderSettings:SourceFolder"]}";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CustomLogger.OnLog -= LogToConsole;
            base.OnFormClosed(e);
        }

        private async void BtnProcesarCarpeta_Click(object? sender, EventArgs e)
        {
            btnProcesarCarpeta.Enabled = false;
            rtbLogs.Clear();
            pbProgreso.Value = 0;
            lblEstado.Text = "Procesando...";

            var progress = new Progress<ProcessingProgress>(OnProgressReported);

            try
            {
                var rows = await _processingService.ProcessFolderAsync(progress);

                if (rows.Count == 0)
                {
                    MessageBox.Show(this, "No se encontraron PDFs para procesar en la carpeta configurada.",
                        "Sin resultados", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _previewRows = new BindingList<InvoicePreviewRow>(rows);
                dgvPreview.DataSource = _previewRows;
                ActualizarResumen();
                MostrarPanelPreview();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar la carpeta.");
                MessageBox.Show(this, $"Error al procesar la carpeta:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnProcesarCarpeta.Enabled = true;
            }
        }

        private void OnProgressReported(ProcessingProgress report)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<ProcessingProgress>(OnProgressReported), report);
                return;
            }

            lblEstado.Text = $"{report.Message}   |   Procesados: {report.TotalProcessed}   Errores: {report.TotalErrors}   Tiempo: {report.ElapsedTotal:mm\\:ss}";

            if (report.TotalPages > 0)
            {
                pbProgreso.Maximum = report.TotalPages;
                pbProgreso.Value = Math.Min(report.CurrentPage, report.TotalPages);
            }
        }

        private void LogToConsole(string line)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(LogToConsole), line);
                return;
            }

            rtbLogs.AppendText(line + Environment.NewLine);
        }

        private void ActualizarResumen()
        {
            var pendientes = _previewRows.Count(r => r.HasPendingReview);
            lblResumen.Text = $"{pendientes} de {_previewRows.Count} factura(s) con campos pendientes de revisión.";
        }

        private void DgvPreview_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            var columnName = dgvPreview.Columns[e.ColumnIndex].Name;
            if (ColumnasDefaultNegocio.Contains(columnName))
            {
                e.CellStyle!.BackColor = ColorDefaultNegocio;
                return;
            }

            if (e.RowIndex >= _previewRows.Count) return;
            var row = _previewRows[e.RowIndex];
            e.CellStyle!.BackColor = row.MissingFields.Contains(columnName) ? ColorCampoFaltante : ColorCeldaNormal;
        }

        private void DgvPreview_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _previewRows.Count) return;

            var columnName = dgvPreview.Columns[e.ColumnIndex].Name;
            var row = _previewRows[e.RowIndex];
            if (row.MissingFields.Remove(columnName))
            {
                dgvPreview.InvalidateCell(e.ColumnIndex, e.RowIndex);
                ActualizarResumen();
            }
        }

        private async void BtnAprobarLote_Click(object? sender, EventArgs e)
        {
            var confirm = MessageBox.Show(this,
                $"¿Confirma aprobar el lote de {_previewRows.Count} factura(s)? " +
                "Se insertarán en la base de datos y los PDFs se moverán a la carpeta de procesados.",
                "Aprobar lote", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            btnAprobarLote.Enabled = false;
            btnRechazarLote.Enabled = false;
            Cursor = Cursors.WaitCursor;

            try
            {
                var result = await _approvalService.ApproveBatchAsync(_previewRows.ToList());

                if (!result.SqlCommitted)
                {
                    MessageBox.Show(this,
                        $"No se pudo grabar el lote en la base de datos. Ningún PDF fue movido.\n\nDetalle: {result.SqlErrorMessage}",
                        "Error al aprobar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var tieneObservaciones = result.DuplicateInvoices.Count > 0 || result.IncompleteInvoices.Count > 0 || result.FileMoveErrors.Count > 0;
                var secciones = new List<string>
                {
                    result.RowsInserted > 0
                        ? $"Se insertaron {result.RowsInserted} factura(s) en la base de datos."
                        : "No se insertó ninguna factura nueva."
                };

                if (result.IncompleteInvoices.Count > 0)
                {
                    var lista = string.Join(Environment.NewLine, result.IncompleteInvoices);
                    secciones.Add(
                        $"{result.IncompleteInvoices.Count} factura(s) fueron rechazadas por tener campos críticos sin completar " +
                        $"y NO se grabaron ni se movió su PDF:\n{lista}\n\n" +
                        "Vuelva a procesar la carpeta para corregirlas en la grilla y reintentar.");
                }

                if (result.DuplicateInvoices.Count > 0)
                {
                    var lista = string.Join(Environment.NewLine, result.DuplicateInvoices);
                    secciones.Add(
                        $"{result.DuplicateInvoices.Count} factura(s) fueron rechazadas por Nro. de Comprobante duplicado " +
                        $"y NO se grabaron ni se movió su PDF:\n{lista}");
                }

                if (result.FileMoveErrors.Count > 0)
                {
                    var lista = string.Join(Environment.NewLine, result.FileMoveErrors);
                    secciones.Add(
                        $"{result.FileMoveErrors.Count} archivo(s) no se pudieron mover automáticamente y quedaron en la carpeta origen:\n{lista}\n\n" +
                        "Muévalos manualmente a la carpeta de procesados.");
                }

                MessageBox.Show(this, string.Join("\n\n", secciones),
                    tieneObservaciones ? "Lote aprobado con observaciones" : "Lote aprobado",
                    MessageBoxButtons.OK, tieneObservaciones ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

                _previewRows = new BindingList<InvoicePreviewRow>();
                dgvPreview.DataSource = _previewRows;
                MostrarPanelProcesamiento();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al aprobar el lote.");
                MessageBox.Show(this, $"Error inesperado al aprobar el lote:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnAprobarLote.Enabled = true;
                btnRechazarLote.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        private void BtnRechazarLote_Click(object? sender, EventArgs e)
        {
            var confirm = MessageBox.Show(this,
                "¿Confirma rechazar el lote? Ningún dato se grabará y los PDFs permanecerán en la carpeta de origen.",
                "Rechazar lote", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            _previewRows = new BindingList<InvoicePreviewRow>();
            dgvPreview.DataSource = _previewRows;
            MostrarPanelProcesamiento();
        }
    }
}
