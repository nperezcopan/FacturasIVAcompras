namespace FacturasIvaCompra.UI
{
    public partial class MainForm
    {
        // Panel de procesamiento
        private Panel pnlProcesamiento = null!;
        private Label lblCarpetaOrigen = null!;
        private Button btnProcesarCarpeta = null!;
        private ProgressBar pbProgreso = null!;
        private Label lblEstado = null!;
        private RichTextBox rtbLogs = null!;

        // Panel de previsualización
        private Panel pnlPreview = null!;
        private Label lblResumen = null!;
        private DataGridView dgvPreview = null!;
        private Button btnAprobarLote = null!;
        private Button btnRechazarLote = null!;

        private static readonly (string Prop, string Header, int Width, bool ReadOnly)[] ColumnasGrid =
        {
            (nameof(Application.Models.InvoicePreviewRow.SourceFileName), "Archivo", 160, true),
            (nameof(Application.Models.InvoicePreviewRow.Fecha_Comprobante_CC), "Fecha", 90, false),
            (nameof(Application.Models.InvoicePreviewRow.Tipo_Comprobante_CC), "Tipo Cbte.", 70, false),
            (nameof(Application.Models.InvoicePreviewRow.Punto_Venta_CC), "Pto. Venta", 70, false),
            (nameof(Application.Models.InvoicePreviewRow.Nro_Comprobante_CC), "Nro. Comprobante", 110, false),
            (nameof(Application.Models.InvoicePreviewRow.CUIT_Emisor_CC), "CUIT Emisor", 110, false),
            (nameof(Application.Models.InvoicePreviewRow.Denominacion_Emisor_CC), "Denominación Emisor", 200, false),
            (nameof(Application.Models.InvoicePreviewRow.Importe_Total_Operacion_CC), "Total", 100, false),
            (nameof(Application.Models.InvoicePreviewRow.Neto_CC), "Neto", 100, false),
            (nameof(Application.Models.InvoicePreviewRow.Porc_Iva_CC), "% IVA", 60, false),
            (nameof(Application.Models.InvoicePreviewRow.Importe_Perc_IVA_CC), "Perc. IVA", 90, false),
            (nameof(Application.Models.InvoicePreviewRow.Nro_Proveedor), "Nro. Proveedor", 100, false),
            (nameof(Application.Models.InvoicePreviewRow.Mes_CC), "Mes", 50, false),
            (nameof(Application.Models.InvoicePreviewRow.Anio_CC), "Año", 60, false),
            (nameof(Application.Models.InvoicePreviewRow.Nro_Vendedor_CC), "Nro. Vendedor", 110, false),
            (nameof(Application.Models.InvoicePreviewRow.Nombre_Vendedor_CC), "Nombre Vendedor", 180, false),
            (nameof(Application.Models.InvoicePreviewRow.Codigo_Vendedor_CC), "Cód. Vendedor", 90, false),
            (nameof(Application.Models.InvoicePreviewRow.Nro_Despacho_CC), "Nro. Despacho", 100, false),
            (nameof(Application.Models.InvoicePreviewRow.Codigo_Moneda_CC), "Moneda", 70, false),
            (nameof(Application.Models.InvoicePreviewRow.Tipo_Cambio_CC), "Tipo Cambio", 90, false),
            (nameof(Application.Models.InvoicePreviewRow.Cantidad_Alicuota_IVA_CC), "Cant. Alíc. IVA", 100, false),
            (nameof(Application.Models.InvoicePreviewRow.Codigo_Operacion_CC), "Cód. Operación", 100, false),
            (nameof(Application.Models.InvoicePreviewRow.Codigo_Fiscal_CC), "Cód. Fiscal", 90, false),
            (nameof(Application.Models.InvoicePreviewRow.Otro_Tributo_CC), "Otro Tributo", 90, false),
            (nameof(Application.Models.InvoicePreviewRow.IVA_Comision_CC), "IVA Comisión", 90, false),
            (nameof(Application.Models.InvoicePreviewRow.Ret_Iva_CC), "Ret. IVA", 80, false),
            (nameof(Application.Models.InvoicePreviewRow.Fecha_Caja_Banco_CC), "Fecha Caja/Banco", 110, false),
            (nameof(Application.Models.InvoicePreviewRow.Importe_Conceptos_NO_Neto_Gravado_CC), "No Neto Gravado", 120, false),
            (nameof(Application.Models.InvoicePreviewRow.Importe_Operacion_Exenta_CC), "Operación Exenta", 120, false),
            (nameof(Application.Models.InvoicePreviewRow.Importe_Perc_Imp_Nacional_CC), "Perc. Imp. Nacional", 120, false),
            (nameof(Application.Models.InvoicePreviewRow.Importe_Perc_IIBB_CC), "Perc. IIBB", 90, false),
            (nameof(Application.Models.InvoicePreviewRow.Importe_Perc_Imp_Mun_CC), "Perc. Imp. Mun.", 100, false),
            (nameof(Application.Models.InvoicePreviewRow.Importe_Impuesto_Interno_CC), "Imp. Interno", 90, false),
        };

        private void InitializeComponent()
        {
            Text = "Facturas IVA Compra";
            Width = 1200;
            Height = 750;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ColorFondo;

            BuildPanelProcesamiento();
            BuildPanelPreview();

            Controls.Add(pnlPreview);
            Controls.Add(pnlProcesamiento);

            MostrarPanelProcesamiento();
        }

        private void BuildPanelProcesamiento()
        {
            pnlProcesamiento = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };

            lblCarpetaOrigen = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 26,
                Font = new Font(Font, FontStyle.Bold),
                Text = "Carpeta origen: (sin configurar)"
            };

            btnProcesarCarpeta = new Button
            {
                Text = "Procesar carpeta",
                Dock = DockStyle.Top,
                Height = 36,
                Margin = new Padding(0, 8, 0, 8)
            };
            btnProcesarCarpeta.Click += BtnProcesarCarpeta_Click;

            pbProgreso = new ProgressBar { Dock = DockStyle.Top, Height = 22, Margin = new Padding(0, 8, 0, 0) };

            lblEstado = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 24,
                Text = string.Empty
            };

            rtbLogs = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font(FontFamily.GenericMonospace, 8.5f),
                BackColor = Color.White
            };

            var panelSuperior = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                RowCount = 3,
                ColumnCount = 1,
                Height = 100,
                AutoSize = true
            };
            panelSuperior.Controls.Add(lblCarpetaOrigen);
            panelSuperior.Controls.Add(btnProcesarCarpeta);
            panelSuperior.Controls.Add(pbProgreso);

            pnlProcesamiento.Controls.Add(rtbLogs);
            pnlProcesamiento.Controls.Add(lblEstado);
            pnlProcesamiento.Controls.Add(panelSuperior);
        }

        private void BuildPanelPreview()
        {
            pnlPreview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };

            lblResumen = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 26,
                Font = new Font(Font, FontStyle.Bold)
            };

            dgvPreview = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };

            foreach (var (prop, header, width, readOnly) in ColumnasGrid)
            {
                dgvPreview.Columns.Add(new DataGridViewTextBoxColumn
                {
                    DataPropertyName = prop,
                    Name = prop,
                    HeaderText = header,
                    Width = width,
                    ReadOnly = readOnly
                });
            }

            dgvPreview.CellFormatting += DgvPreview_CellFormatting;
            dgvPreview.CellEndEdit += DgvPreview_CellEndEdit;

            var panelBotones = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                FlowDirection = FlowDirection.RightToLeft
            };

            btnRechazarLote = new Button { Text = "Rechazar lote", Width = 140, Height = 34 };
            btnRechazarLote.Click += BtnRechazarLote_Click;

            btnAprobarLote = new Button { Text = "Aprobar lote", Width = 140, Height = 34 };
            btnAprobarLote.Click += BtnAprobarLote_Click;

            panelBotones.Controls.Add(btnRechazarLote);
            panelBotones.Controls.Add(btnAprobarLote);

            pnlPreview.Controls.Add(dgvPreview);
            pnlPreview.Controls.Add(panelBotones);
            pnlPreview.Controls.Add(lblResumen);
        }

        private void MostrarPanelProcesamiento()
        {
            pnlPreview.Visible = false;
            pnlProcesamiento.Visible = true;
        }

        private void MostrarPanelPreview()
        {
            pnlProcesamiento.Visible = false;
            pnlPreview.Visible = true;
        }
    }
}
