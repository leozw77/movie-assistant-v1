namespace QbPotDoubanAi;

public sealed class BrowserStatusForm : Form
{
    private readonly TrayContext _owner;
    private readonly Label _connection = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold), MaximumSize = new Size(620, 0) };
    private readonly Label _media = new() { AutoSize = true, MaximumSize = new Size(620, 0) };
    private readonly Label _privacy = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(620, 0), Text = "独立配置目录；CDP 仅连接 127.0.0.1 动态端口。程序只读取当前爱奇艺页面的公开片名和 HTML5 video 播放状态，不读取 Cookie、密码或浏览历史。" };
    public BrowserStatusForm(TrayContext owner)
    {
        _owner = owner; Text = "观影浏览器连接状态"; ClientSize = new Size(680, 340); StartPosition = FormStartPosition.CenterScreen;
        var launch = new Button { Text = "启动观影浏览器", AutoSize = true }; launch.Click += async (_, _) => await _owner.LaunchBrowserAsync();
        var test = new Button { Text = "测试当前爱奇艺电影", AutoSize = true }; test.Click += async (_, _) => await _owner.TestCurrentIqiyiAsync();
        var close = new Button { Text = "关闭窗口", AutoSize = true }; close.Click += (_, _) => Hide();
        var buttons = new FlowLayoutPanel { AutoSize = true }; buttons.Controls.Add(launch); buttons.Controls.Add(test); buttons.Controls.Add(close);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22), AutoScroll = true };
        panel.Controls.Add(_connection); panel.Controls.Add(_media); panel.Controls.Add(_privacy); panel.Controls.Add(buttons); Controls.Add(panel);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        RefreshFromOwner();
    }
    public void RefreshFromOwner()
    {
        if (IsDisposed) return; _connection.Text = _owner.Status;
        var s = _owner.Session; var m = _owner.Current; var r = _owner.CurrentRecord;
        _media.Text = s is null ? "浏览器：未启动" : $"浏览器：{s.BrowserName}\n端口：127.0.0.1:{s.Port}\n配置：{s.ProfileDirectory}\n\n" +
            (m is null ? "影片：尚未发现受支持页面" : $"影片：{m.Title}\n年份：{m.Year?.ToString() ?? "未知"}\n类型：{(string.IsNullOrWhiteSpace(m.Genre) ? "未知" : m.Genre)}\n时长：{TimeSpan.FromSeconds(m.Duration):hh\\:mm\\:ss}\n当前：{TimeSpan.FromSeconds(m.CurrentTime):hh\\:mm\\:ss}\n状态：{(m.Paused ? "暂停" : "播放")}\n累计真实观看：{TimeSpan.FromSeconds(r?.WatchedSeconds ?? 0):hh\\:mm\\:ss}");
    }
}

