using System.Net;
using System.Net.Http.Headers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DoubanInterestApiProbe;

internal sealed class MainForm : Form
{
    private const string MovieBase = "https://movie.douban.com";

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly TextBox _subjectId = new() { Dock = DockStyle.Fill, PlaceholderText = "例如 1295644" };
    private readonly ComboBox _status = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _submitRating = new() { Text = "提交评分", AutoSize = true };
    private readonly NumericUpDown _rating = new() { Minimum = 1, Maximum = 5, Value = 5, Width = 70 };
    private readonly CheckBox _submitComment = new() { Text = "提交短评（勾选后空文本也会提交）", AutoSize = true };
    private readonly TextBox _comment = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9F)
    };

    private readonly Button _checkLogin = new() { Text = "检查登录状态", AutoSize = true };
    private readonly Button _openHome = new() { Text = "打开豆瓣", AutoSize = true };
    private readonly Button _openSubject = new() { Text = "打开影片页确认", AutoSize = true };
    private readonly Button _write = new() { Text = "写入状态 / 评分 / 短评", AutoSize = true };
    private readonly Button _remove = new() { Text = "取消标记", AutoSize = true };
    private readonly Button _clearLog = new() { Text = "清空日志", AutoSize = true };

    private bool _webReady;
    private bool _busy;

    public MainForm()
    {
        Text = "豆瓣评价 API 写入探针";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1450;
        Height = 900;
        MinimumSize = new Size(1100, 720);

        _status.Items.Add(new StatusItem("想看", "wish"));
        _status.Items.Add(new StatusItem("在看", "do"));
        _status.Items.Add(new StatusItem("看过", "collect"));
        _status.SelectedIndex = 2;

        Controls.Add(BuildLayout());

        Shown += async (_, _) => await InitializeWebViewAsync();
        _checkLogin.Click += async (_, _) => await CheckLoginAsync();
        _openHome.Click += (_, _) => Navigate(MovieBase + "/");
        _openSubject.Click += (_, _) => OpenSubject();
        _write.Click += async (_, _) => await WriteInterestAsync();
        _remove.Click += async (_, _) => await RemoveInterestAsync();
        _clearLog.Click += (_, _) => _log.Clear();
    }

    private Control BuildLayout()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 900,
            FixedPanel = FixedPanel.Panel2
        };
        split.Panel1.Controls.Add(_webView);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            Padding = new Padding(12)
        };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(right, new Label
        {
            Text = "直接 HTTP 写入测试（不会打开豆瓣评价状态窗口）",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        });
        AddRow(right, new Label
        {
            Text = "左侧先登录豆瓣。程序只读取当前 WebView2 会话 Cookie，不显示/保存 Cookie 值。",
            AutoSize = true,
            MaximumSize = new Size(450, 0)
        });
        AddRow(right, MakeButtonRow(_openHome, _checkLogin));
        AddRow(right, new Label { Text = "豆瓣 Subject ID", AutoSize = true });
        AddRow(right, _subjectId);
        AddRow(right, new Label { Text = "状态", AutoSize = true });
        AddRow(right, _status);

        var ratingRow = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        ratingRow.Controls.Add(_submitRating);
        ratingRow.Controls.Add(new Label { Text = "星级", AutoSize = true, Margin = new Padding(16, 6, 2, 0) });
        ratingRow.Controls.Add(_rating);
        AddRow(right, ratingRow);

        AddRow(right, _submitComment);
        AddRow(right, _comment, new RowStyle(SizeType.Absolute, 110));
        AddRow(right, MakeButtonRow(_write, _remove));
        AddRow(right, MakeButtonRow(_openSubject, _clearLog));
        AddRow(right, new Label
        {
            Text = "诊断日志（HTTP 200 只表示请求返回，不代表最终写入成功；请以豆瓣网页人工确认为准）",
            AutoSize = true,
            MaximumSize = new Size(450, 0)
        });
        AddRow(right, _log, new RowStyle(SizeType.Percent, 100));

        split.Panel2.Controls.Add(right);
        return split;
    }

    private static void AddRow(TableLayoutPanel table, Control control, RowStyle? style = null)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(style ?? new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control, 0, row);
    }

    private static FlowLayoutPanel MakeButtonRow(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        panel.Controls.AddRange(buttons);
        return panel;
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
            Log("WebView2 已初始化。请在左侧完成豆瓣登录，然后点“检查登录状态”。");
        }
        catch (Exception ex)
        {
            Log("WebView2 初始化失败: " + ex.Message);
            MessageBox.Show(this, ex.ToString(), "WebView2 初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CheckLoginAsync()
    {
        if (!EnsureWebReady()) return;
        try
        {
            var auth = await ReadAuthContextAsync();
            var names = string.Join(", ", auth.CookieNames.OrderBy(x => x));
            Log($"Cookie 数量: {auth.CookieNames.Count}; Cookie 名称: {names}");
            Log(auth.HasCk
                ? "检测到 ck：具备调用写入接口所需的 CSRF token。"
                : "未检测到 ck：请先在左侧登录/访问豆瓣电影页面。所有写入按钮会拒绝执行。");
            Log(auth.HasDbcl2
                ? "检测到 dbcl2：看起来已有豆瓣登录会话。"
                : "未检测到 dbcl2：可能尚未登录，或当前会话 Cookie 结构不同。");
        }
        catch (Exception ex)
        {
            Log("检查登录失败: " + ex.Message);
        }
    }

    private async Task WriteInterestAsync()
    {
        if (!BeginOperation()) return;
        try
        {
            var id = ValidateSubjectId();
            if (id is null) return;

            var auth = await ReadAuthContextAsync();
            if (!auth.HasCk)
            {
                MessageBox.Show(this, "没有检测到 ck Cookie。请先在左侧登录豆瓣，然后再试。", "未登录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("写入已取消：缺少 ck。没有发送任何 POST 请求。");
                return;
            }

            var status = (StatusItem)_status.SelectedItem!;
            var fields = new Dictionary<string, string>
            {
                ["ck"] = auth.Ck!,
                ["interest"] = status.Value
            };
            if (_submitRating.Checked)
                fields["rating"] = ((int)_rating.Value).ToString();
            if (_submitComment.Checked)
                fields["comment"] = _comment.Text;

            Log("------------------------------------------------------------");
            Log($"准备写入 Subject={id}; interest={status.Value}; rating={(_submitRating.Checked ? _rating.Value.ToString() : "<省略>")}; comment={(_submitComment.Checked ? $"提交，长度 {_comment.Text.Length}" : "<省略>")}");
            Log($"POST {MovieBase}/j/subject/{id}/interest");

            var result = await PostFormAsync($"{MovieBase}/j/subject/{id}/interest", id, auth, fields);
            LogHttpResult(result);
            Log("请点击“打开影片页确认”，或在外部豆瓣网页版人工确认最终状态、评分和短评。");
        }
        catch (Exception ex)
        {
            Log("写入异常: " + ex);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RemoveInterestAsync()
    {
        if (!BeginOperation()) return;
        try
        {
            var id = ValidateSubjectId();
            if (id is null) return;

            var confirm = MessageBox.Show(
                this,
                $"确认通过接口取消 Subject {id} 的想看/在看/看过标记？\n\n这是实际写操作。",
                "确认取消标记",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                Log("取消标记操作被用户取消。没有发送 POST 请求。");
                return;
            }

            var auth = await ReadAuthContextAsync();
            if (!auth.HasCk)
            {
                MessageBox.Show(this, "没有检测到 ck Cookie。请先登录豆瓣。", "未登录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Log("------------------------------------------------------------");
            Log($"POST {MovieBase}/j/subject/{id}/removeinterest");
            var result = await PostFormAsync(
                $"{MovieBase}/j/subject/{id}/removeinterest",
                id,
                auth,
                new Dictionary<string, string> { ["ck"] = auth.Ck! });
            LogHttpResult(result);
            Log("请到豆瓣网页人工确认是否真的取消。");
        }
        catch (Exception ex)
        {
            Log("取消标记异常: " + ex);
        }
        finally
        {
            EndOperation();
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
        var location = response.Headers.Location?.ToString();
        return new HttpResult((int)response.StatusCode, response.ReasonPhrase ?? string.Empty, location, body);
    }

    private async Task<AuthContext> ReadAuthContextAsync()
    {
        if (!EnsureWebReady()) throw new InvalidOperationException("WebView2 尚未初始化。");

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
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string>(json);
        }
        catch
        {
            return null;
        }
    }

    private string? ValidateSubjectId()
    {
        var id = _subjectId.Text.Trim();
        if (id.Length == 0 || id.Any(ch => !char.IsDigit(ch)))
        {
            MessageBox.Show(this, "请输入纯数字豆瓣 Subject ID，例如 1295644。", "Subject ID 无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return id;
    }

    private void OpenSubject()
    {
        var id = ValidateSubjectId();
        if (id is not null) Navigate($"{MovieBase}/subject/{id}/");
    }

    private void Navigate(string url)
    {
        if (EnsureWebReady()) _webView.CoreWebView2.Navigate(url);
    }

    private bool EnsureWebReady()
    {
        if (_webReady && _webView.CoreWebView2 is not null) return true;
        MessageBox.Show(this, "WebView2 还没有初始化完成。", "请稍后重试", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private bool BeginOperation()
    {
        if (_busy)
        {
            MessageBox.Show(this, "已有写入请求正在执行。", "操作进行中", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        if (!EnsureWebReady()) return false;
        _busy = true;
        _write.Enabled = false;
        _remove.Enabled = false;
        return true;
    }

    private void EndOperation()
    {
        _busy = false;
        _write.Enabled = true;
        _remove.Enabled = true;
    }

    private void LogHttpResult(HttpResult result)
    {
        Log($"HTTP {result.StatusCode} {result.ReasonPhrase}");
        if (!string.IsNullOrWhiteSpace(result.Location))
            Log("Redirect Location: " + result.Location);
        Log("Raw response:");
        Log(string.IsNullOrWhiteSpace(result.Body) ? "<空响应>" : result.Body);
        if (result.StatusCode is >= 200 and < 300)
            Log("请求已返回 2xx，但探针不会据此宣告写入成功。请以豆瓣网页人工确认。");
        else if (result.StatusCode is >= 300 and < 400)
            Log("收到重定向。若跳到登录页，通常说明当前会话未被服务器接受。");
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

    private sealed record AuthContext(
        string? Ck,
        bool HasDbcl2,
        IReadOnlyList<string> CookieNames,
        string CookieHeader,
        string UserAgent)
    {
        public bool HasCk => !string.IsNullOrWhiteSpace(Ck);
    }

    private sealed record HttpResult(int StatusCode, string ReasonPhrase, string? Location, string Body);

    private sealed record StatusItem(string Text, string Value)
    {
        public override string ToString() => $"{Text} ({Value})";
    }
}
