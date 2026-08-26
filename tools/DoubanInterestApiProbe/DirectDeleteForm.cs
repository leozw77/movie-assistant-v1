using System.Net;
using System.Net.Http.Headers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DoubanInterestApiProbe;

internal sealed class DirectDeleteForm : Form
{
    private const string MovieBase = "https://movie.douban.com";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly TextBox _subjectId = new() { Width = 140, PlaceholderText = "例如 37291780" };
    private readonly Button _checkLogin = new() { Text = "检查登录状态", AutoSize = true };
    private readonly Button _openSubject = new() { Text = "打开影片页确认", AutoSize = true };
    private readonly Button _delete = new() { Text = "直接调用官方 /remove 删除", AutoSize = true };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9F)
    };

    private bool _ready;
    private bool _busy;

    public DirectDeleteForm()
    {
        Text = "豆瓣评价删除 API 探针 v5（官方 /remove）";
        Width = 1380;
        Height = 880;
        MinimumSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;

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

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        bar.Controls.Add(new Label { Text = "Subject ID", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        bar.Controls.Add(_subjectId);
        bar.Controls.Add(_checkLogin);
        bar.Controls.Add(_openSubject);
        bar.Controls.Add(_delete);
        root.Controls.Add(bar, 0, 0);
        root.Controls.Add(_web, 0, 1);
        root.Controls.Add(new Label
        {
            Text = "此版本只验证刚抓到的豆瓣官方真实请求：POST /subject/{id}/remove，表单仅 ck。执行后请去正常豆瓣网页确认状态、星级、短评是否全部消失。",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 3)
        }, 0, 2);
        root.Controls.Add(_log, 0, 3);
        Controls.Add(root);

        Shown += async (_, _) => await InitAsync();
        _checkLogin.Click += async (_, _) => await CheckLoginAsync();
        _openSubject.Click += (_, _) => OpenSubject();
        _delete.Click += async (_, _) => await DeleteAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DoubanInterestApiProbe",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _web.EnsureCoreWebView2Async(env);
            _ready = true;
            _web.CoreWebView2.Navigate(MovieBase + "/");
            Log("WebView2 已初始化，复用前面探针的登录目录。先点“检查登录状态”。");
        }
        catch (Exception ex)
        {
            Log("初始化失败: " + ex);
        }
    }

    private async Task CheckLoginAsync()
    {
        if (!_ready) return;
        var auth = await ReadAuthAsync();
        Log($"Cookie 数量: {auth.Names.Count}; Cookie 名称: {string.Join(", ", auth.Names.OrderBy(x => x))}");
        Log(auth.HasCk ? "检测到 ck。" : "未检测到 ck。请先登录豆瓣。");
        Log(auth.HasDbcl2 ? "检测到 dbcl2，已有登录会话。" : "未检测到 dbcl2。");
    }

    private async Task DeleteAsync()
    {
        if (_busy || !_ready) return;
        var id = ValidateSubjectId();
        if (id is null) return;

        var confirm = MessageBox.Show(
            this,
            $"将直接发送豆瓣官方页面刚抓到的真实删除请求：\n\nPOST {MovieBase}/subject/{id}/remove\nForm: ck=<token>\n\n这会真实修改你的豆瓣账号。继续吗？",
            "确认真实删除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _busy = true;
        _delete.Enabled = false;
        try
        {
            var auth = await ReadAuthAsync();
            if (!auth.HasCk)
            {
                Log("删除取消：没有 ck。请先登录。");
                return;
            }

            var url = $"{MovieBase}/subject/{id}/remove";
            Log("------------------------------------------------------------");
            Log("REQUEST POST " + url);
            Log("POST DATA: ck=<已隐藏>");

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
            request.Headers.Referrer = new Uri($"{MovieBase}/subject/{id}/");
            request.Headers.TryAddWithoutValidation("User-Agent", auth.UserAgent);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ck"] = auth.Ck!
            });
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded; charset=UTF-8");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Log($"RESPONSE {(int)response.StatusCode} {response.ReasonPhrase}");
            if (response.Headers.Location is not null) Log("Location: " + response.Headers.Location);
            Log($"Response body length: {body.Length}");
            if (body.Length <= 1000 && !string.IsNullOrWhiteSpace(body)) Log("Body: " + body.Replace("\r", " ").Replace("\n", " "));
            Log("请现在去正常豆瓣网页确认：状态、星级、短评是否全部消失。");
        }
        catch (Exception ex)
        {
            Log("删除请求异常: " + ex);
        }
        finally
        {
            _busy = false;
            _delete.Enabled = true;
        }
    }

    private async Task<AuthContext> ReadAuthAsync()
    {
        var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync(MovieBase + "/");
        var ck = cookies.FirstOrDefault(c => string.Equals(c.Name, "ck", StringComparison.OrdinalIgnoreCase))?.Value;
        var names = cookies.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var hasDbcl2 = cookies.Any(c => string.Equals(c.Name, "dbcl2", StringComparison.OrdinalIgnoreCase));
        var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        var userAgentJson = await _web.CoreWebView2.ExecuteScriptAsync("navigator.userAgent");
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
            MessageBox.Show(this, "请输入纯数字 Subject ID。", "Subject ID 无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return id;
    }

    private void OpenSubject()
    {
        var id = ValidateSubjectId();
        if (id is not null && _ready) _web.CoreWebView2.Navigate($"{MovieBase}/subject/{id}/");
    }

    private void Log(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(text));
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {text}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private sealed record AuthContext(string? Ck, bool HasDbcl2, IReadOnlyList<string> Names, string CookieHeader, string UserAgent)
    {
        public bool HasCk => !string.IsNullOrWhiteSpace(Ck);
    }
}
