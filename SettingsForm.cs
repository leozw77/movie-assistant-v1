namespace QbPotDoubanAi;

public sealed class SettingsForm : Form
{
    private readonly TextBox _key = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _model = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _threshold = new() { Minimum = 70, Maximum = 100 };
    private readonly NumericUpDown _minutes = new() { Minimum = 1, Maximum = 180 };
    private readonly ComboBox _browser = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _directory = new() { Dock = DockStyle.Fill };
    public AppSettings Result { get; private set; }
    public SettingsForm(AppSettings settings)
    {
        Result = settings; Text = "观影助手设置"; Width = 680; Height = 380; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        _browser.Items.AddRange(["Chrome", "Edge"]); _browser.SelectedItem = settings.PreferredBrowser == "Edge" ? "Edge" : "Chrome";
        _directory.Text = settings.VideoDirectory;
        _key.Text = settings.DeepSeekApiKey; _model.Text = settings.Model; _threshold.Value = (decimal)Math.Clamp(settings.CompletionThreshold * 100, 70, 100); _minutes.Value = Math.Clamp(settings.MinimumWatchMinutes, 1, 180);
        var test = new Button { Text = "测试 API", AutoSize = true }; test.Click += async (_, _) => { Apply(); test.Enabled = false; try { await new DeepSeekService().TestAsync(Result); MessageBox.Show(this, "DeepSeek 连接成功。"); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "测试失败"); } finally { test.Enabled = true; } };
        var browse = new Button { Text = "选择…", AutoSize = true }; browse.Click += (_, _) => { using var d = new FolderBrowserDialog { InitialDirectory = _directory.Text }; if (d.ShowDialog(this) == DialogResult.OK) _directory.Text = d.SelectedPath; };
        var save = new Button { Text = "保存", AutoSize = true, DialogResult = DialogResult.OK }; save.Click += (_, _) => Apply(); var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 3, RowCount = 8 }; table.ColumnStyles.Add(new(SizeType.AutoSize)); table.ColumnStyles.Add(new(SizeType.Percent, 100)); table.ColumnStyles.Add(new(SizeType.AutoSize));
        AddRow(table, 0, "观影浏览器", _browser, new Label { Text = "不可用时自动回退", AutoSize = true }); AddRow(table, 1, "PotPlayer影视目录", _directory, browse); AddRow(table, 2, "DeepSeek API Key", _key, test); AddRow(table, 3, "模型", _model, new Label { Text = "默认 deepseek-v4-flash", AutoSize = true }); AddRow(table, 4, "完成阈值", _threshold, new Label { Text = "%", AutoSize = true }); AddRow(table, 5, "最少观看", _minutes, new Label { Text = "分钟", AutoSize = true });
        var note = new Label { Text = "PotPlayer沿用稳定版监控；独立浏览器不接触日常浏览器资料。", AutoSize = true, ForeColor = Color.DimGray }; table.Controls.Add(note, 0, 6); table.SetColumnSpan(note, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true }; buttons.Controls.Add(save); buttons.Controls.Add(cancel); table.Controls.Add(buttons, 0, 7); table.SetColumnSpan(buttons, 3); Controls.Add(table); AcceptButton = save; CancelButton = cancel;
    }
    private static void AddRow(TableLayoutPanel t, int row, string label, Control middle, Control right) { t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); t.Controls.Add(middle, 1, row); t.Controls.Add(right, 2, row); }
    private void Apply() => Result = new AppSettings { PreferredBrowser = _browser.SelectedItem?.ToString() ?? "Chrome", VideoDirectory = _directory.Text.Trim(), DeepSeekApiKey = _key.Text.Trim(), Model = _model.Text.Trim(), CompletionThreshold = (double)_threshold.Value / 100, MinimumWatchMinutes = (int)_minutes.Value };
}

