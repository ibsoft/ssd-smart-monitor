using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SsdHealthInspector;

internal sealed class MainForm : Form
{
    private readonly ComboBox _drives = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 410 };
    private readonly Button _scan = new() { Text = "Rescan", AutoSize = true };
    private readonly Button _html = new() { Text = "Save HTML report", AutoSize = true, Enabled = false };
    private readonly Button _json = new() { Text = "Export JSON", AutoSize = true, Enabled = false };
    private readonly Label _state = new() { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Label _details = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 12) };
    private readonly Label _footer = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _empty = new() { Dock = DockStyle.Top, Height = 44, Text = "No ATA SMART attributes available. See Diagnostics.", ForeColor = Color.DimGray };
    private readonly DataGridView _table = new();
    private readonly TextBox _advice = TextPanel();
    private readonly TextBox _diagnostics = TextPanel();
    private List<DiskSnapshot> _disks = new();
    private DiskSnapshot? Selected => _drives.SelectedItem as DiskSnapshot;

    public MainForm()
    {
        Text = "UNIXFOR - SSD Health Inspector — S.M.A.R.T. Report";
        Size = new Size(1020, 850);
        MinimumSize = new Size(820, 650);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        ForeColor = Color.FromArgb(0, 23, 72);
        BackColor = Color.White;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(root);
        root.Controls.Add(new Label
        {
            Text = "UNIXFOR - SSD Health Inspector S.M.A.R.T. Report",
            AutoSize = true,
            ForeColor = Color.Navy,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 18)
        }, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 12) };
        actions.Controls.AddRange(new Control[] { _drives, _scan, _html, _json });
        root.Controls.Add(actions, 0, 1);
        var info = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        info.Controls.Add(_state);
        info.Controls.Add(_details);
        root.Controls.Add(info, 0, 2);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        root.Controls.Add(tabs, 0, 3);
        var report = new TabPage("S.M.A.R.T. Report") { BackColor = Color.White, Padding = new Padding(5) };
        var advice = new TabPage("Findings / Advice");
        var diagnostics = new TabPage("Diagnostics / NVMe");
        tabs.TabPages.AddRange(new[] { report, advice, diagnostics });
        advice.Controls.Add(_advice);
        diagnostics.Controls.Add(_diagnostics);
        _table.Dock = DockStyle.Fill;
        _table.ReadOnly = true;
        _table.AllowUserToAddRows = false;
        _table.AllowUserToDeleteRows = false;
        _table.RowHeadersVisible = false;
        _table.BackgroundColor = Color.White;
        _table.BorderStyle = BorderStyle.None;
        _table.GridColor = Color.SlateGray;
        _table.EnableHeadersVisualStyles = false;
        _table.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 245, 250);
        _table.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
        _table.ColumnHeadersHeight = 30;
        _table.RowTemplate.Height = 27;
        _table.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        foreach (var column in new[] { ("State", 70), ("Num", 50), ("Attribute Name", 260), ("Value", 60), ("Worst", 60), ("Raw (hex)", 150), ("Threshold", 80) })
            _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column.Item1, Width = column.Item2, SortMode = DataGridViewColumnSortMode.NotSortable });
        _table.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _table.Columns[2].MinimumWidth = 180;
        _table.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        _table.Columns[5].DefaultCellStyle.Font = new Font("Consolas", 9F);
        _table.CellPainting += PaintState;
        report.Controls.Add(_table);
        report.Controls.Add(_empty);
        report.Controls.Add(new Label
        {
            Text = "Green: no threshold crossing   •   Amber: review   •   Red: threshold crossed   •   Grey: unknown",
            Dock = DockStyle.Bottom,
            Height = 34,
            ForeColor = Color.DimGray
        });
        root.Controls.Add(_footer, 0, 4);
        _scan.Click += async (_, _) => await ScanAsync();
        _drives.SelectedIndexChanged += (_, _) => ShowDrive();
        _html.Click += (_, _) => Export(true);
        _json.Click += (_, _) => Export(false);
        Shown += async (_, _) => await ScanAsync();
    }

    private static TextBox TextPanel() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = Color.White,
        BorderStyle = BorderStyle.None
    };

    private async Task ScanAsync()
    {
        _scan.Enabled = _html.Enabled = _json.Enabled = _drives.Enabled = false;
        _footer.Text = "Reading drive information and SMART pages…";
        try
        {
            int? previous = Selected?.Index;
            var disks = await DiskDiscovery.ScanAsync();
            _disks = disks;
            _drives.Items.Clear();
            _table.Rows.Clear();
            _state.Text = "No accessible drives";
            _details.Text = _advice.Text = _diagnostics.Text = "";
            _empty.Visible = true;
            foreach (var disk in disks) _drives.Items.Add(disk);
            if (disks.Count > 0)
                _drives.SelectedIndex = Math.Max(0, disks.FindIndex(d => d.Index == previous));
            _footer.Text = $"{disks.Count} physical drive(s) | Read-only SMART commands | Scan completed {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _footer.Text = "Scan failed; any displayed readings are from the previous scan.";
            MessageBox.Show(this, ex.Message, "Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _scan.Enabled = _drives.Enabled = true;
            _html.Enabled = Selected is not null;
            _json.Enabled = _disks.Count > 0;
        }
    }

    private void ShowDrive()
    {
        if (Selected is not { } d) return;
        var decision = HealthEvaluator.Evaluate(d);
        _state.Text = "● State: " + decision.Headline;
        _state.ForeColor = decision.Level switch
        {
            DecisionLevel.Healthy => Color.ForestGreen,
            DecisionLevel.ReplaceNow => Color.Firebrick,
            DecisionLevel.ReplaceSoon or DecisionLevel.Monitor => Color.DarkGoldenrod,
            _ => Color.SlateGray
        };
        _details.Text = SmartReport.Details(d);
        _table.Rows.Clear();
        foreach (var a in d.Attributes)
        {
            string state = SmartReport.State(a);
            int i = _table.Rows.Add(state, a.Id.ToString("000"), SmartReport.Name(d, a), a.Current.ToString("000"),
                a.Worst.ToString("000"), SmartReport.Raw(a), a.Threshold?.ToString("000") ?? "—");
            _table.Rows[i].Cells[0].ToolTipText = state;
            _table.Rows[i].Cells[5].ToolTipText = $"Raw decimal: {a.Raw} (vendor-specific units)";
        }
        _empty.Visible = d.Attributes.Count == 0;
        _advice.Text = decision.Recommendation + "\r\n\r\n" + string.Join("\r\n\r\n", decision.Findings) +
            "\r\n\r\nEvidence: " + decision.Evidence + "\r\n\r\nRaw counters and wear meanings vary by vendor. Green indicators do not guarantee remaining life.";
        _diagnostics.Text = $"Device: {d.DeviceID}\r\nSMART source: {d.SmartSource}\r\nATA attributes: {d.Attributes.Count}\r\n" +
            $"SMART predicts failure: {d.PredictFailure?.ToString() ?? "Unknown"}\r\nTRIM: {d.TrimSupported}\r\n" +
            string.Join("\r\n", d.Diagnostics) + "\r\n" + d.NvmeDiagnostic;
        if (d.Nvme is { } n) _diagnostics.AppendText($"\r\nNVMe critical warning: 0x{n.CriticalWarning:X2}\r\nTemperature: {n.TemperatureC} °C\r\nLifetime used: {n.PercentageUsed}%\r\nAvailable spare: {n.AvailableSpare}%\r\nSpare threshold: {n.SpareThreshold}%\r\nMedia errors: {n.MediaErrors}\r\nError log entries: {n.ErrorLogEntries}\r\nPower-on hours: {n.PowerOnHours}\r\nUnsafe shutdowns: {n.UnsafeShutdowns}");
    }

    private void PaintState(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
        e.PaintBackground(e.ClipBounds, true);
        string state = e.Value?.ToString() ?? "UNKNOWN";
        using var brush = new SolidBrush(ColorTranslator.FromHtml(SmartReport.Colour(state)));
        var rect = new Rectangle(e.CellBounds.Left + 8, e.CellBounds.Top + (e.CellBounds.Height - 12) / 2, 12, 12);
        e.Graphics!.FillEllipse(brush, rect);
        e.Graphics.DrawEllipse(Pens.Gray, rect);
        TextRenderer.DrawText(e.Graphics, state == "UNKNOWN" ? "?" : state == "REVIEW" ? "!" : state == "FAIL" ? "×" : "✓",
            Font, new Rectangle(rect.Right + 3, e.CellBounds.Top, 28, e.CellBounds.Height), ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        e.Handled = true;
    }

    private void InitializeComponent()
    {

    }

    private void Export(bool html)
    {
        if (Selected is not { } disk) return;
        using var dialog = new SaveFileDialog
        {
            Filter = html ? "HTML report (*.html)|*.html" : "JSON report (*.json)|*.json",
            FileName = $"ssd-health-{DateTime.Now:yyyyMMdd-HHmmss}." + (html ? "html" : "json"),
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, html ? SmartReport.Html(disk) : ReportWriter.CreateJson(_disks), new UTF8Encoding(false)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
