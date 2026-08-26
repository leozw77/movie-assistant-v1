using System.Net;
using System.Net.Http.Headers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DoubanInterestApiProbe;

internal sealed class DeleteProbeForm : Form
{
    private const string MovieBase = "https://movie.douban.com";

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly TextBox _subjectId = new() { Width = 140, PlaceholderText = "例如 36554071" };
    private readonly Button _checkLogin = new() { Text = "检查登录状态", AutoSize = true };
    private readonly Button _delete = new() { Text = "用 interest=none 删除", AutoSize = true };
    private readonly Button _openSubject = new() { Text = "打开影片页确认", AutoSize = true };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9F)
    };

    private bool _webReady;
    private bool _busy;

    public DeleteProbeForm()
    {
        Text = "豆瓣评价 API 删除探针 v3（interest=none）";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1350;
        Height = 850;
        MinimumSize = new Size(950, 650);

        Controls.Add(BuildLayout());
        Shown += async (_, _) => await InitializeWebViewAsync();
        _checkLogin.Click += async (_, _) => await CheckLoginAsync();
        _delete.Click += async (_, _) => await DeleteAsync();
        _openSubject.Click += (_, _) => OpenSubject();
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
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        toolbar.Controls.Add(new Label
        {
            Text = "Subject ID",
            AutoSize = true,
            Margin = new Padding(0, 8, 4, 0),
            Font = new Font(Font, FontStyle.Bold)
        });
        toolbar.Controls.Add(_subjectId);
        toolbar.Controls.Add(_delete);
        toolbar.Controls.Add(_checkLogin);
        toolbar.Controls.Add(_openSubject);
        toolbar.Controls.Add(new Label
        {
            Text = "本版只验证删除：POST /interest，interest=none，ck=当前登录 token",
            AutoSize = true,
            Margin = new Padding(14, 8, 0, 0)
        });

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_webView, 0, 1);
        root.Controls.Add(new Label
        {
            Text = "日志：如果返回 HTTP 200 + {\"r\":0}，再到豆瓣网页人工确认条目是否已从想看/在看/看过中移除。",
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 3)
        }, 0, 2);
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
            _webView.CoreWebView2.Navigate(MovieBase + "/");
            Log("WebView2 已初始化。此版本复用 v2 的登录目录；先点“检查登录状态”。");
        }
        catch (Exception ex)
        {
            Log("WebView2 初始化失败: " + ex);
        }
    }

    private async Task CheckLoginAsync()
    {
        if (!EnsureWebReady()) return;
        try
        {
            var auth = await ReadAuthContextAsync();
            Log($"Cookie 数量: {auth.CookieNames.Count}; Cookie 名称: {string.Join(", ", auth.CookieNames.OrderBy(x => x))}");
            Log(auth.HasCk ? "检测到 ck。" : "未检测到 ck，请先登录豆瓣。");
            Log(auth.HasDbcl2 ? "检测到 dbcl2，已有登录会话。" : "未检测到 dbcl2。");
        }
        catch (Exception ex)
        {
            Log("检查登录失败: " + ex.Message);
        }
    }

    private async Task DeleteAsync()
    {
        if (_busy || !EnsureWebReady()) return;
        var id = ValidateSubjectId();
        if (id is null) return;

        var confirm = MessageBox.Show(
            this,
            $"确认删除 Subject {id} 的想看/在看/看过标记？\n\n将直接发送 interest=none。",
            "确认实际写操作",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _busy = true;
        _delete.Enabled = false;
        try
        {
            var auth = await ReadAuthContextAsync();
            if (!auth.HasCk)
            {
                Log("删除取消：缺少 ck。");
                return;
            }

            var url = $"{MovieBase}/j/subject/{id}/interest";
            Log("------------------------------------------------------------");
            Log($"POST {url}");
            Log("Form: interest=none; ck=<已隐藏>");

            var result = await PostFormAsync(
                url,
                id,
                auth,
                new Dictionary<string, string>
                {
                    ["ck"] = auth.Ck!,
                    ["interest"] = "none"
                });

            Log($"HTTP {result.StatusCode} {result.ReasonPhrase}");
            Log("Raw response:");
            Log(string.IsNullOrWhiteSpace(result.Body) ? "<空响应>" : result.Body);
            Log("请点击“打开影片页确认”，并在正常豆瓣网页确认条目确实已取消标记。");
        }
        catch (Exception ex)
        {
            Log("删除异常: " + ex);
        }
        finally
        {
            _busy = false;
            _delete.Enabled = true;
        }
    }

    private async Task<HttpResult> PostFormAsync(
        string url,
        string subjectId,
        AuthContext auth,
        Dictionary<string, string> fields)
    {
        using var handler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.TryAddWithoutValidation("Cookie", auth.CookieHeader);
        request.Headers.TryAddWithoutValidation("Origin", MovieBase);
        request.Headers.Referrer = new Uri($"{MovieBase}/subject/{subjectId}/");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("User-Agent", auth.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/javascript"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.01));
        request.Content = new FormUrlEncodedContent(fields);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded; charset=UTF-8");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return new HttpResult((int)response.StatusCode, response.ReasonPhrase ?? string.Empty, body);
    }

    private async Task<AuthContext> ReadAuthContextAsync()
    {
        var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync(MovieBase + "/");
        var ck = cookies.FirstOrDefault(c => string.Equals(c.Name, "ck", StringComparison.OrdinalIgnoreCase))?.Value;
        var names = cookies.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var hasDbcl2 = cookies.Any(c => string.Equals(c.Name, "dbcl2", StringComparison.OrdinalIgnoreCase));
        var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        var userAgentJson = await _webView.CoreWebView2.ExecuteScriptAsync("navigator.userAgent");
        var userAgent = DecodeJsonString(userAgentJson) ?? "Mozilla/5.0";
        return new AuthContext(ck, hasDbcl2, names, cookieHeader, userAgent);
    }

    private static string? DecodeJsonString(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<string>(json); }
        catch { return null; }
    }

    private string? ValidateSubjectId()
    {
        var id = _subjectId.Text.Trim();
        if (id.Length == 0 || id.Any(ch => !char.IsDigit(ch)))
        {
            MessageBox.Show(this, "请输入纯数字豆瓣 Subject ID。", "Subject ID 无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return id;
    }

    private void OpenSubject()
    {
        var id = ValidateSubjectId();
        if (id is not null && EnsureWebReady())
            _webView.CoreWebView2.Navigate($"{MovieBase}/subject/{id}/");
    }

    private bool EnsureWebReady()
    {
        if (_webReady && _webView.CoreWebView2 is not null) return true;
        MessageBox.Show(this, "WebView2 还没有初始化完成。", "请稍后重试", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void Log(string message)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private sealed record AuthContext(
        string? Ck,
        bool HasDbcl2,
        IReadOnlyList<string> CookieNames,
        string CookieHeader,
        string UserAgent)
    {
        public bool HasCk => !string.IsNullOrWhiteSpace(Ck);
    }

    private sealed record HttpResult(int StatusCode, string ReasonPhrase, string Body);
}
