using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DoubanInterestApiProbe;

internal sealed class CaptureForm : Form
{
    private const string MovieBase = "https://movie.douban.com";

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly TextBox _subjectId = new() { Width = 120, PlaceholderText = "例如 35811064" };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9F)
    };
    private readonly Button _openSubject = new() { Text = "打开影片页", AutoSize = true };
    private readonly Button _checkLogin = new() { Text = "检查登录状态", AutoSize = true };
    private readonly Button _clearLog = new() { Text = "清空日志", AutoSize = true };

    private readonly Dictionary<string, string> _trackedRequests = new();
    private bool _webReady;

    public CaptureForm()
    {
        Text = "豆瓣官方删除请求抓包探针 v4";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1450;
        Height = 900;
        MinimumSize = new Size(1000, 700);

        Controls.Add(BuildLayout());
        Shown += async (_, _) => await InitializeWebViewAsync();
        _openSubject.Click += (_, _) => OpenSubject();
        _checkLogin.Click += async (_, _) => await CheckLoginAsync();
        _clearLog.Click += (_, _) => _log.Clear();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 240));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        toolbar.Controls.Add(new Label { Text = "Subject ID", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        toolbar.Controls.Add(_subjectId);
        toolbar.Controls.Add(_openSubject);
        toolbar.Controls.Add(_checkLogin);
        toolbar.Controls.Add(_clearLog);
        root.Controls.Add(toolbar, 0, 0);

        root.Controls.Add(new Label
        {
            Text = "操作方法：打开一部已经有想看/在看/看过状态的影片 → 用豆瓣原生界面点修改/删除 → 底部会自动记录真实请求。程序不主动提交任何删除请求。",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 6)
        }, 0, 1);

        root.Controls.Add(_webView, 0, 2);
        root.Controls.Add(_log, 0, 3);
        return root;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DoubanInterestApiProbe",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);
            _webReady = true;

            await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

            var requestReceiver = _webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
            requestReceiver.DevToolsProtocolEventReceived += (_, e) => HandleRequestEvent(e.ParameterObjectAsJson);

            var responseReceiver = _webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.responseReceived");
            responseReceiver.DevToolsProtocolEventReceived += (_, e) => HandleResponseEvent(e.ParameterObjectAsJson);

            _webView.CoreWebView2.Navigate(MovieBase + "/");
            Log("抓包已启动。复用 v2/v3 的 WebView2 登录目录。先点“检查登录状态”。");
            Log("只会显示豆瓣电影域名中与 interest / remove / delete / collect / wish / do 有关的请求，以及所有 POST 到 /j/subject/ 的请求。ck 会自动隐藏。");
        }
        catch (Exception ex)
        {
            Log("初始化失败: " + ex);
            MessageBox.Show(this, ex.ToString(), "初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void HandleRequestEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("request", out var request)) return;

            var url = request.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? string.Empty : string.Empty;
            var method = request.TryGetProperty("method", out var methodEl) ? methodEl.GetString() ?? string.Empty : string.Empty;
            if (!ShouldCapture(url, method)) return;

            var requestId = root.TryGetProperty("requestId", out var ridEl) ? ridEl.GetString() ?? string.Empty : string.Empty;
            if (!string.IsNullOrWhiteSpace(requestId)) _trackedRequests[requestId] = url;

            var postData = request.TryGetProperty("postData", out var pdEl) ? pdEl.GetString() : null;
            Log("------------------------------------------------------------");
            Log($"REQUEST {method} {Redact(url)}");
            if (!string.IsNullOrWhiteSpace(postData))
                Log("POST DATA: " + Redact(postData));
        }
        catch (Exception ex)
        {
            Log("解析 requestWillBeSent 失败: " + ex.Message);
        }
    }

    private void HandleResponseEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var requestId = root.TryGetProperty("requestId", out var ridEl) ? ridEl.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(requestId) || !_trackedRequests.TryGetValue(requestId, out var trackedUrl)) return;

            if (!root.TryGetProperty("response", out var response)) return;
            var status = response.TryGetProperty("status", out var statusEl) ? statusEl.GetDouble() : 0;
            var statusText = response.TryGetProperty("statusText", out var stEl) ? stEl.GetString() ?? string.Empty : string.Empty;
            Log($"RESPONSE {(int)status} {statusText} <- {Redact(trackedUrl)}");
        }
        catch (Exception ex)
        {
            Log("解析 responseReceived 失败: " + ex.Message);
        }
    }

    private bool ShouldCapture(string url, string method)
    {
        if (!url.Contains("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return false;

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            url.Contains("/j/subject/", StringComparison.OrdinalIgnoreCase))
            return true;

        var keywords = new[] { "interest", "remove", "delete", "collect", "wish" };
        return keywords.Any(k => url.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private async Task CheckLoginAsync()
    {
        if (!EnsureWebReady()) return;
        try
        {
            var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync(MovieBase + "/");
            var names = cookies.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            Log($"Cookie 数量: {names.Count}; Cookie 名称: {string.Join(", ", names)}");
            Log(names.Contains("ck", StringComparer.OrdinalIgnoreCase) ? "检测到 ck。" : "未检测到 ck。");
            Log(names.Contains("dbcl2", StringComparer.OrdinalIgnoreCase) ? "检测到 dbcl2，已有登录会话。" : "未检测到 dbcl2。");
        }
        catch (Exception ex)
        {
            Log("检查登录失败: " + ex.Message);
        }
    }

    private void OpenSubject()
    {
        if (!EnsureWebReady()) return;
        var id = _subjectId.Text.Trim();
        if (!Regex.IsMatch(id, "^\\d+$"))
        {
            MessageBox.Show(this, "请输入纯数字 Subject ID。", "ID 无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _webView.CoreWebView2.Navigate($"{MovieBase}/subject/{id}/");
    }

    private bool EnsureWebReady()
    {
        if (_webReady && _webView.CoreWebView2 is not null) return true;
        MessageBox.Show(this, "WebView2 尚未初始化完成。", "请稍后", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var value = Regex.Replace(text, @"(?i)(ck=)[^&\s]+", "$1<已隐藏>");
        value = Regex.Replace(value, @"(?i)(dbcl2=)[^&;\s]+", "$1<已隐藏>");
        return value;
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}
