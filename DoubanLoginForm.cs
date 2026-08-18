using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed class DoubanLoginForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0), Text = "正在打开豆瓣官方登录页……" };
    private readonly Button _verify = new() { Text = "验证登录", AutoSize = true, Margin = new Padding(8) };
    private readonly WebView2EnvironmentProvider _environments;
    private readonly DoubanWebView2Connector _connector;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2500 };
    private bool _checking;
    private bool _initialized;
    private bool _closing;
    private bool _verified;

    internal DoubanLoginForm(WebView2EnvironmentProvider environments, DoubanWebView2Connector connector)
    {
        _environments = environments;
        _connector = connector;
        Text = "豆瓣官方扫码登录";
        Width = 1040; Height = 780; MinimumSize = new Size(780, 600); StartPosition = FormStartPosition.CenterParent;
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.White };
        _verify.Dock = DockStyle.Right;
        footer.Controls.Add(_status); footer.Controls.Add(_verify);
        Controls.Add(_webView); Controls.Add(footer);
        Shown += async (_, _) => await InitializeAsync();
        _verify.Click += async (_, _) =>
        {
            if (_verified) { Close(); return; }
            await CheckLoginAsync(true);
        };
        FormClosing += (_, _) => { _closing = true; _timer.Stop(); _connector.SetLoginWindowActive(false); };
        FormClosed += (_, _) => { _timer.Dispose(); _webView.Dispose(); };
    }

    private async Task InitializeAsync()
    {
        if (_initialized || _closing || IsDisposed) return;
        try
        {
            _connector.SetLoginWindowActive(true);
            await _connector.WaitForIdleAsync();
            DiagnosticLogger.Write("Douban login WebView2 initialization started.");
            await _webView.EnsureCoreWebView2Async(await _environments.GetDoubanEnvironmentAsync());
            var core = _webView.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) => { if (!DoubanWebView2Connector.IsAllowedDoubanTopLevel(e.Uri)) e.Cancel = true; };
            core.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess) DiagnosticLogger.Write($"Douban login navigation failed; Status={e.WebErrorStatus}; Uri={core.Source}");
            };
            core.ProcessFailed += (_, e) =>
            {
                DiagnosticLogger.Write($"Douban login WebView2 process failed; Kind={e.ProcessFailedKind}");
                if (!_closing && !IsDisposed) _status.Text = "豆瓣登录页面进程异常，请关闭窗口后重试。";
            };
            core.Navigate("https://accounts.douban.com/passport/login?source=movie");
            _initialized = true;
            DiagnosticLogger.Write("Douban login WebView2 navigation started.");
            _status.Text = "请直接使用豆瓣官方页面扫码。本程序不会读取、输出或导出 Cookie。";
            _status.Text = "请直接使用豆瓣官方页面扫码。扫码完成后点击“验证登录”。";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write("Douban login WebView2 initialization failed: " + ex);
            if (!_closing && !IsDisposed)
            {
                _status.Text = "无法启动 WebView2：" + ex.Message;
                _verify.Enabled = false;
            }
        }
    }

    private async Task CheckLoginAsync(bool showFailure)
    {
        if (_checking || _closing || IsDisposed) return;
        _checking = true;
        try
        {
            if (_closing || IsDisposed) return;
            _webView.CoreWebView2?.Navigate("about:blank");
            await Task.Delay(250);
            _connector.SetLoginWindowActive(false);
            var session = await _connector.VerifySessionAsync();
            if (session.IsLoggedIn)
            {
                _timer.Stop();
                _verified = true;
                _status.Text = $"豆瓣登录已验证（Profile {session.ProfileId}），登录状态保存在观影助手专用 Profile。请点击“关闭”继续。";
                _verify.Text = "关闭";
                DiagnosticLogger.Write("Douban login verified; login window remains open for explicit close.");
            }
            else if (showFailure) _status.Text = session.Text + (string.IsNullOrWhiteSpace(session.Error) ? "，请完成官方二维码扫码后重试。" : $"：{session.Error}");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write("Douban login verification failed: " + ex);
            if (showFailure && !_closing && !IsDisposed) _status.Text = "验证失败：" + ex.Message;
        }
        finally { if (!_verified && !_closing) _connector.SetLoginWindowActive(true); _checking = false; }
    }
}
