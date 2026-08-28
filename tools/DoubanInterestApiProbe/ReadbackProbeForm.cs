using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DoubanInterestApiProbe;

internal sealed class ReadbackProbeForm : Form
{
    private const string MovieBase = "https://movie.douban.com";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly TextBox _subjectId = new() { Width = 130, PlaceholderText = "例如 1295644" };
    private readonly ComboBox _status = new() { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _includeRating = new() { Text = "写入评分", AutoSize = true, Checked = true };
    private readonly NumericUpDown _rating = new() { Minimum = 1, Maximum = 5, Value = 5, Width = 58 };
    private readonly CheckBox _includeComment = new() { Text = "写入短评", AutoSize = true, Checked = true };
    private readonly TextBox _comment = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _showFieldSamples = new() { Text = "显示字段样本", AutoSize = true, Checked = true };
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
    private readonly Button _read = new() { Text = "读取双端点", AutoSize = true };
    private readonly Button _writeAndRead = new() { Text = "写入后回读", AutoSize = true };
    private readonly Button _deleteAndRead = new() { Text = "删除后回读", AutoSize = true };
    private readonly Button _openSubject = new() { Text = "打开影片页", AutoSize = true };
    private readonly Button _openV2 = new() { Text = "打开 v2 写入窗口", AutoSize = true };
    private readonly Button _openV5 = new() { Text = "打开 v5 删除窗口", AutoSize = true };
    private readonly Button _clearLog = new() { Text = "清空日志", AutoSize = true };

    private bool _ready;
    private bool _busy;

    public ReadbackProbeForm()
    {
        Text = "豆瓣评价精确回读探针 v6";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1480;
        Height = 920;
        MinimumSize = new Size(1050, 720);

        _status.Items.Add(new StatusItem("想看", "wish"));
        _status.Items.Add(new StatusItem("在看", "do"));
        _status.Items.Add(new StatusItem("看过", "collect"));
        _status.SelectedIndex = 2;

        Controls.Add(BuildLayout());

        Shown += async (_, _) => await InitializeAsync();
        _checkLogin.Click += async (_, _) => await CheckLoginAsync();
        _read.Click += async (_, _) => await ReadBackAsync();
        _writeAndRead.Click += async (_, _) => await WriteAndReadAsync();
        _deleteAndRead.Click += async (_, _) => await DeleteAndReadAsync();
        _openSubject.Click += (_, _) => OpenSubject();
        _openV2.Click += (_, _) => new MainForm().Show(this);
        _openV5.Click += (_, _) => new DirectDeleteForm().Show(this);
        _clearLog.Click += (_, _) => _log.Clear();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(2, 2, 2, 4)
        };
        toolbar.Controls.Add(new Label { Text = "Subject ID", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        toolbar.Controls.Add(_subjectId);
        toolbar.Controls.Add(new Label { Text = "写入状态", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        toolbar.Controls.Add(_status);
        toolbar.Controls.Add(_includeRating);
        toolbar.Controls.Add(new Label { Text = "星级", AutoSize = true, Margin = new Padding(3, 7, 2, 0) });
        toolbar.Controls.Add(_rating);
        toolbar.Controls.Add(_includeComment);
        toolbar.Controls.Add(_read);
        toolbar.Controls.Add(_writeAndRead);
        toolbar.Controls.Add(_deleteAndRead);
        toolbar.Controls.Add(_checkLogin);
        toolbar.Controls.Add(_openSubject);
        toolbar.Controls.Add(_openV2);
        toolbar.Controls.Add(_openV5);
        toolbar.Controls.Add(_showFieldSamples);
        toolbar.Controls.Add(_clearLog);
        root.Controls.Add(toolbar, 0, 0);

        var commentBox = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 2, 0, 6)
        };
        commentBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        commentBox.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        commentBox.Controls.Add(new Label
        {
            Text = "短评内容（“写入后回读”时使用；Probe 默认不会自动写入）",
            AutoSize = true
        }, 0, 0);
        commentBox.Controls.Add(_comment, 0, 1);
        root.Controls.Add(commentBox, 0, 1);

        root.Controls.Add(_web, 0, 2);
        root.Controls.Add(new Label
        {
            Text = "读取双端点：GET /j/subject/{id}/interest + GET /subject/{id}/。回读只记录证据，不把 HTTP 2xx 直接当作写入成功。写入/删除按钮都会二次确认。",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 3)
        }, 0, 3);
        root.Controls.Add(_log, 0, 4);
        return root;
    }

    private async Task InitializeAsync()
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
            Log("v6 回读探针已初始化。请在中间页面登录豆瓣，再点“检查登录状态”。");
        }
        catch (Exception ex)
        {
            Log("WebView2 初始化失败: " + ex);
            MessageBox.Show(this, ex.ToString(), "初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CheckLoginAsync()
    {
        if (!EnsureReady()) return;
        try
        {
            var auth = await ReadAuthAsync();
            Log($"Cookie 数量: {auth.CookieNames.Count}; Cookie 名称: {string.Join(", ", auth.CookieNames.OrderBy(x => x))}");
            Log(auth.HasCk ? "检测到 ck。" : "未检测到 ck，请先登录豆瓣电影页面。");
            Log(auth.HasDbcl2 ? "检测到 dbcl2，已有登录会话。" : "未检测到 dbcl2，登录态仍需以服务器响应为准。");
        }
        catch (Exception ex)
        {
            Log("检查登录失败: " + ex);
        }
    }

    private async Task ReadBackAsync()
    {
        if (!BeginOperation()) return;
        try
        {
            var id = ValidateSubjectId();
            if (id is null) return;
            var auth = await ReadAuthAsync();
            if (!auth.HasCk)
            {
                Log("读取取消：没有 ck。没有发送请求。");
                return;
            }

            Log("------------------------------------------------------------");
            Log($"开始单条精确回读 Subject={id}");
            await ReadBackCoreAsync(id, auth);
        }
        catch (Exception ex)
        {
            Log("回读异常: " + ex);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task WriteAndReadAsync()
    {
        if (!BeginOperation()) return;
        try
        {
            var id = ValidateSubjectId();
            if (id is null) return;
            var status = (StatusItem)_status.SelectedItem!;
            var confirm = MessageBox.Show(
                this,
                $"将真实写入 Subject {id}：\n\n状态：{status.Text}\n评分：{(_includeRating.Checked ? _rating.Value.ToString() : "不提交")}\n短评：{(_includeComment.Checked ? _comment.Text : "不提交")}\n\n写入后会自动调用两个 GET 端点回读，但仍只以逐字段结果作为证据。继续吗？",
                "确认真实写入",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                Log("写入后回读已取消，没有发送 POST。");
                return;
            }

            var auth = await ReadAuthAsync();
            if (!auth.HasCk)
            {
                Log("写入取消：没有 ck。没有发送 POST。");
                return;
            }

            var fields = new Dictionary<string, string>
            {
                ["ck"] = auth.Ck!,
                ["foldcollect"] = "F",
                ["interest"] = status.Value,
                ["rating"] = _includeRating.Checked ? ((int)_rating.Value).ToString() : string.Empty,
                ["comment"] = _includeComment.Checked ? _comment.Text : string.Empty
            };
            Log("------------------------------------------------------------");
            Log($"POST {MovieBase}/j/subject/{id}/interest");
            var writeResult = await SendAsync(
                HttpMethod.Post,
                $"{MovieBase}/j/subject/{id}/interest",
                id,
                auth,
                fields);
            LogHttpSummary("写入响应", writeResult);
            if (writeResult.StatusCode is < 200 or >= 300)
            {
                Log("写入没有返回 2xx，停止自动回读。请检查登录态和原始响应。");
                return;
            }

            var snapshot = await ReadBackCoreAsync(id, auth);
            LogExpectedComparison(snapshot, status.Value, _includeRating.Checked ? (int)_rating.Value : null, _includeComment.Checked ? _comment.Text : null);
        }
        catch (Exception ex)
        {
            Log("写入后回读异常: " + ex);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task DeleteAndReadAsync()
    {
        if (!BeginOperation()) return;
        try
        {
            var id = ValidateSubjectId();
            if (id is null) return;
            var confirm = MessageBox.Show(
                this,
                $"将真实删除 Subject {id} 的豆瓣状态、评分和短评：\n\nPOST {MovieBase}/subject/{id}/remove\n\n删除后自动回读确认。继续吗？",
                "确认真实删除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                Log("删除后回读已取消，没有发送 POST。");
                return;
            }

            var auth = await ReadAuthAsync();
            if (!auth.HasCk)
            {
                Log("删除取消：没有 ck。没有发送 POST。");
                return;
            }

            Log("------------------------------------------------------------");
            Log($"POST {MovieBase}/subject/{id}/remove");
            var deleteResult = await SendAsync(
                HttpMethod.Post,
                $"{MovieBase}/subject/{id}/remove",
                id,
                auth,
                new Dictionary<string, string> { ["ck"] = auth.Ck! });
            LogHttpSummary("删除响应", deleteResult);
            var snapshot = await ReadBackCoreAsync(id, auth);
            var directNone = snapshot.Json.Status == "none";
            var pageNone = snapshot.SubjectHtml.Status == "none";
            var ratingEmpty = IsEmptyOrZero(snapshot.ApiHtml.RatingRaw) && IsEmptyOrZero(snapshot.SubjectHtml.RatingRaw);
            var commentEmpty = string.IsNullOrEmpty(snapshot.SubjectHtml.Comment);
            var dateEmpty = string.IsNullOrEmpty(snapshot.SubjectHtml.Date);
            var complete = directNone && pageNone && ratingEmpty && commentEmpty && dateEmpty;
            Log($"删除回读判定：JSON status={snapshot.Json.Status}; subject HTML status={snapshot.SubjectHtml.Status}; ratingEmpty={ratingEmpty}; commentEmpty={commentEmpty}; dateEmpty={dateEmpty}; 完整不存在确认={(complete ? "通过" : "未通过/字段不足")}");
        }
        catch (Exception ex)
        {
            Log("删除后回读异常: " + ex);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task<ReadbackSnapshot> ReadBackCoreAsync(string id, AuthContext auth)
    {
        var jsonResult = await SendAsync(
            HttpMethod.Get,
            $"{MovieBase}/j/subject/{id}/interest",
            id,
            auth,
            null);
        var subjectResult = await SendAsync(
            HttpMethod.Get,
            $"{MovieBase}/subject/{id}/",
            id,
            auth,
            null);

        LogHttpSummary("兴趣 JSON 回读", jsonResult);
        LogHttpSummary("影片 HTML 回读", subjectResult);

        var json = ParseInterestJson(jsonResult.Body);
        var apiHtml = string.IsNullOrEmpty(json.Html)
            ? HtmlObservation.Empty("JSON html 字段缺失或为空")
            : await ParseHtmlAsync(json.Html);
        var subjectHtml = await ParseHtmlAsync(subjectResult.Body);
        var snapshot = new ReadbackSnapshot(jsonResult, subjectResult, json, apiHtml, subjectHtml);
        LogSnapshot(snapshot);
        return snapshot;
    }

    private async Task<HttpResult> SendAsync(
        HttpMethod method,
        string url,
        string subjectId,
        AuthContext auth,
        Dictionary<string, string>? fields)
    {
        using var handler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Cookie", auth.CookieHeader);
        request.Headers.TryAddWithoutValidation("Origin", MovieBase);
        request.Headers.Referrer = new Uri($"{MovieBase}/subject/{subjectId}/");
        request.Headers.TryAddWithoutValidation("User-Agent", auth.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.01));
        if (fields is not null)
        {
            request.Content = new FormUrlEncodedContent(fields);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded; charset=UTF-8");
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return new HttpResult(
            (int)response.StatusCode,
            response.ReasonPhrase ?? string.Empty,
            response.Headers.Location?.ToString(),
            body);
    }

    private static InterestJsonObservation ParseInterestJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return InterestJsonObservation.FromError("空响应");

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return InterestJsonObservation.FromError($"顶层类型为 {document.RootElement.ValueKind}");

            var fields = document.RootElement.EnumerateObject()
                .Select(property => new JsonFieldObservation(
                    property.Name,
                    property.Value.ValueKind.ToString(),
                    DescribeJsonValue(property.Value)))
                .ToList();
            var root = document.RootElement;
            var status = ReadStringField(root, "interest_status");
            var html = root.TryGetProperty("html", out var htmlElement)
                && htmlElement.ValueKind == JsonValueKind.String
                ? htmlElement.GetString()
                : null;
            var comment = ReadFirstStringField(root, "comment", "short_comment", "review");
            var date = ReadFirstStringField(root, "marked_date", "mark_date", "date");
            return new InterestJsonObservation(
                true,
                status.Exists ? NormalizeStatus(status.Value) : "unknown",
                status.Exists ? status.Value : "<missing>",
                html,
                comment.Exists ? comment.Value : "<missing>",
                date.Exists ? date.Value : "<missing>",
                fields,
                null);
        }
        catch (Exception ex)
        {
            return InterestJsonObservation.FromError("JSON 解析失败: " + ex.Message);
        }
    }

    private async Task<HtmlObservation> ParseHtmlAsync(string html)
    {
        if (!EnsureReady()) return HtmlObservation.Empty("WebView2 未准备好");

        var source = JsonSerializer.Serialize(html);
        var script = $$"""
            (() => {
              const source = {{source}};
              const doc = new DOMParser().parseFromString(source, "text/html");
              const text = (el) => (el?.textContent || "").trim();
              const directText = (el) => [...(el?.childNodes || [])]
                .filter((node) => node.nodeType === Node.TEXT_NODE)
                .map((node) => node.textContent || "")
                .join("").trim();
              const statusOf = (value) => {
                const s = String(value || "").trim();
                if (s.includes("已看过") || /^我看过(?:这部电影|这部电视剧)/.test(s)) return "collect";
                if (s.includes("已想看") || /^我想看(?:这部电影|这部电视剧)/.test(s)) return "wish";
                if (s.includes("已在看") || /^(?:我在看|我正在看)(?:这部电影|这部电视剧)?/.test(s)) return "do";
                if (s === "看过") return "collect";
                if (s === "想看") return "wish";
                if (/^(?:正在?)?在看$/.test(s)) return "do";
                return null;
              };
              const isCommentPrompt = (value) => {
                const s = String(value || "").replace(/\s+/g, "");
                return s.includes("给个评价吧") || s.includes("评价吧?(可选)") || s.includes("短评(可选)");
              };
              const root = doc.querySelector("#interest_sect_level, #interest_sectl");
              const scanRoot = root || doc;
              const candidates = [];
              let status = "none";
              for (const el of scanRoot.querySelectorAll("span, div, a")) {
                const s = statusOf(text(el));
                if (s) {
                  candidates.push({ status: s, text: text(el).slice(0, 120), className: String(el.className || "") });
                  if (status === "none") status = s;
                }
              }
              const checkedInterest = scanRoot.querySelector("input[name='interest']:checked");
              const checkedInterestStatus = String(checkedInterest?.value || "");
              if (checkedInterestStatus === "wish" || checkedInterestStatus === "do" || checkedInterestStatus === "collect") {
                status = checkedInterestStatus;
                candidates.unshift({ status: checkedInterestStatus, text: "checked input[name=interest]", className: String(checkedInterest.className || "") });
              }
              const rating = scanRoot.querySelector("#n_rating, input[name='rating']");
              const date = scanRoot.querySelector(".collection_date");
              const textareas = [...scanRoot.querySelectorAll("textarea")];
              const commentControl = scanRoot.querySelector("textarea[name='comment'], #comment, textarea");
              const controlCommentRaw = String(commentControl?.value || commentControl?.textContent || "").trim();
              const controlPlaceholder = String(commentControl?.getAttribute("placeholder") || "").trim();
              const controlComment = controlCommentRaw && controlCommentRaw !== controlPlaceholder && !isCommentPrompt(controlCommentRaw)
                ? controlCommentRaw
                : "";
              let comment = "";
              const commentElements = [...scanRoot.querySelectorAll(".j.a_stars span")];
              for (let index = commentElements.length - 1; index >= 0; index -= 1) {
                const element = commentElements[index];
                const className = String(element.className || "");
                if (/\bmr10\b|\bcolor_gray\b|\bcollection_date\b|\bpl\b|^rating$/.test(className)) continue;
                const value = directText(element);
                if (value && !statusOf(value) && !isCommentPrompt(value)) { comment = value; break; }
              }
              if (controlComment) comment = controlComment;
              let tags = "";
              for (const element of scanRoot.querySelectorAll(".color_gray")) {
                const match = /^标签\s*[:：]\s*(.+)$/.exec(text(element));
                if (match) { tags = match[1]; break; }
              }
              const inputs = [...scanRoot.querySelectorAll("input")];
              const inputSummary = inputs.slice(0, 40).map((element) => {
                const key = [element.id, element.name, element.type].filter(Boolean).join("/");
                const rawValue = String(element.value || "");
                const safeValue = /comment|review|ck|token/i.test(key) ? "<len " + rawValue.length + ">" : rawValue.slice(0, 80);
                return key + "=value:" + safeValue + ",checked:" + String(!!element.checked);
              }).join(" | ");
              const keywordSummary = [...scanRoot.querySelectorAll("span, div, a, button, label")]
                .map((element) => text(element))
                .filter((value) => /看过|想看|在看|评分|短评/.test(value) && value.length <= 180)
                .slice(0, 20);
              const commentSummary = commentElements.slice(-12).map((element) => {
                const value = directText(element);
                return element.tagName.toLowerCase() + "." + String(element.className || "") + ":len=" + value.length;
              }).join(" | ");
              const textareaSummary = textareas.slice(0, 12).map((element) => {
                const key = [element.id, element.name].filter(Boolean).join("/");
                const value = String(element.value || element.textContent || "").trim();
                const placeholder = String(element.getAttribute("placeholder") || "").trim();
                return key + ":valueLen=" + value.length + ",placeholderLen=" + placeholder.length;
              }).join(" | ");
              const dateCandidates = [...scanRoot.querySelectorAll("[class*='date'], [id*='date'], [class*='collection'], [id*='collection']")];
              const dateCandidateSummary = dateCandidates.slice(0, 20).map((element) => {
                return element.tagName.toLowerCase() + "." + String(element.className || "") + ":textLen=" + text(element).length;
              }).join(" | ");
              return JSON.stringify({
                comment,
                commentCandidateCount: commentElements.length,
                commentSummary,
                controlComment,
                date: text(date),
                dateCandidateCount: dateCandidates.length,
                dateCandidateSummary,
                hasInterestRoot: !!root,
                htmlLength: source.length,
                inputCount: inputs.length,
                inputSummary,
                keywordSummary,
                ratingRaw: rating?.value ?? "",
                rootSelector: root ? "#" + root.id : "document",
                rootTextLength: text(scanRoot).length,
                status,
                statusCandidates: candidates.slice(0, 12),
                tags,
                checkedInterestStatus,
                textareaCount: textareas.length,
                textareaSummary,
                title: doc.title || ""
              });
            })()
            """;

        var result = await _web.CoreWebView2.ExecuteScriptAsync(script);
        var decoded = DecodeJsonString(result);
        if (string.IsNullOrWhiteSpace(decoded)) return HtmlObservation.Empty("DOMParser 无结果");
        try
        {
            return JsonSerializer.Deserialize<HtmlObservation>(decoded, JsonOptions)
                ?? HtmlObservation.Empty("DOMParser 结果为空");
        }
        catch (Exception ex)
        {
            return HtmlObservation.Empty("DOMParser 结果解析失败: " + ex.Message);
        }
    }

    private void LogSnapshot(ReadbackSnapshot snapshot)
    {
        if (!snapshot.Json.Parsed && !string.IsNullOrWhiteSpace(snapshot.Json.ParseError))
            Log($"[JSON] 解析失败：{snapshot.Json.ParseError}");
        Log($"[JSON] interest_status 原始值: {snapshot.Json.RawStatus}; 规范状态: {snapshot.Json.Status}; html: {(snapshot.Json.Html is null ? "缺失" : $"存在，长度 {snapshot.Json.Html.Length}")}");
        Log($"[JSON] comment 字段: {snapshot.Json.RawComment}; date 字段: {snapshot.Json.RawDate}");
        Log($"[JSON html] status={snapshot.ApiHtml.Status}; rating={DisplayValue(snapshot.ApiHtml.RatingRaw)}; comment={DisplayValue(snapshot.ApiHtml.Comment)}; date={DisplayValue(snapshot.ApiHtml.Date)}; root={snapshot.ApiHtml.HasInterestRoot}");
        Log($"[subject HTML] status={snapshot.SubjectHtml.Status}; rating={DisplayValue(snapshot.SubjectHtml.RatingRaw)}; comment={DisplayValue(snapshot.SubjectHtml.Comment)}; date={DisplayValue(snapshot.SubjectHtml.Date)}; root={snapshot.SubjectHtml.HasInterestRoot}");
        Log($"[JSON html DOM] selector={snapshot.ApiHtml.RootSelector}; rootTextLength={snapshot.ApiHtml.RootTextLength}; inputs={snapshot.ApiHtml.InputCount}; dates={snapshot.ApiHtml.CollectionDateCount}; commentCandidates={snapshot.ApiHtml.CommentCandidateCount}");
        Log($"[subject HTML DOM] selector={snapshot.SubjectHtml.RootSelector}; rootTextLength={snapshot.SubjectHtml.RootTextLength}; inputs={snapshot.SubjectHtml.InputCount}; dates={snapshot.SubjectHtml.CollectionDateCount}; commentCandidates={snapshot.SubjectHtml.CommentCandidateCount}");
        Log($"[JSON html form] checkedInterest={snapshot.ApiHtml.CheckedInterestStatus}; textareaCount={snapshot.ApiHtml.TextareaCount}; commentControl={DisplayValue(snapshot.ApiHtml.ControlComment)}; dateCandidates={snapshot.ApiHtml.DateCandidateCount}");
        Log($"[subject HTML form] checkedInterest={snapshot.SubjectHtml.CheckedInterestStatus}; textareaCount={snapshot.SubjectHtml.TextareaCount}; commentControl={DisplayValue(snapshot.SubjectHtml.ControlComment)}; dateCandidates={snapshot.SubjectHtml.DateCandidateCount}");
        if (_showFieldSamples.Checked)
        {
            Log("[JSON] 顶层字段：" + (snapshot.Json.Fields.Count == 0
                ? "<无>"
                : string.Join(" | ", snapshot.Json.Fields.Select(field => $"{field.Name}:{field.Kind}={field.Preview}"))));
            Log("[JSON html] 状态候选：" + FormatCandidates(snapshot.ApiHtml.StatusCandidates));
            Log("[subject HTML] 状态候选：" + FormatCandidates(snapshot.SubjectHtml.StatusCandidates));
            Log("[JSON html] 关键词文本：" + FormatTextSamples(snapshot.ApiHtml.KeywordSummary));
            Log("[subject HTML] 关键词文本：" + FormatTextSamples(snapshot.SubjectHtml.KeywordSummary));
            Log("[JSON html] input 摘要：" + DisplayValue(snapshot.ApiHtml.InputSummary));
            Log("[subject HTML] input 摘要：" + DisplayValue(snapshot.SubjectHtml.InputSummary));
            Log("[subject HTML] 短评候选：" + DisplayValue(snapshot.SubjectHtml.CommentSummary));
            Log("[JSON html] textarea 摘要：" + DisplayValue(snapshot.ApiHtml.TextareaSummary));
            Log("[subject HTML] textarea 摘要：" + DisplayValue(snapshot.SubjectHtml.TextareaSummary));
            Log("[JSON html] 日期节点：" + DisplayValue(snapshot.ApiHtml.DateCandidateSummary));
            Log("[subject HTML] 日期节点：" + DisplayValue(snapshot.SubjectHtml.DateCandidateSummary));
        }
        Log("字段结论：当前 Probe 同时保留缺失、空值和解析值；只有字段实际存在且解析成功，才可以纳入精确匹配。");
    }

    private void LogExpectedComparison(ReadbackSnapshot snapshot, string expectedStatus, int? expectedRating, string? expectedComment)
    {
        var statusMatch = string.Equals(snapshot.Json.Status, expectedStatus, StringComparison.Ordinal)
            && string.Equals(snapshot.ApiHtml.Status, expectedStatus, StringComparison.Ordinal);
        var ratingMatch = !expectedRating.HasValue
            || string.Equals(snapshot.ApiHtml.RatingRaw, expectedRating.Value.ToString(), StringComparison.Ordinal);
        var commentMatch = expectedComment is null || string.Equals(snapshot.ApiHtml.Comment, expectedComment.Trim(), StringComparison.Ordinal);
        Log($"写入后匹配（权威源为兴趣 JSON + JSON 内嵌表单；subject HTML 仅作诊断）：状态 {(statusMatch ? "一致" : "不一致/未知")}；评分 {(ratingMatch ? "一致或未断言" : "不一致/未知")}；短评 {(commentMatch ? "一致或未断言" : "不一致/未知")}");
    }

    private static string FormatCandidates(IReadOnlyList<StatusCandidate> candidates) => candidates.Count == 0
        ? "<无>"
        : string.Join(" | ", candidates.Select(candidate => $"{candidate.Status}:{candidate.Text}"));

    private static string FormatTextSamples(IReadOnlyList<string> values) => values.Count == 0
        ? "<无>"
        : string.Join(" | ", values.Select(DisplayValue));

    private static string DisplayValue(string value) => string.IsNullOrEmpty(value) ? "<空>" : value.Length > 160 ? value[..160] + "…" : value;

    private static bool IsEmptyOrZero(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == "0";

    private static string DescribeJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => "null",
            JsonValueKind.String => $"\"{DisplayValue(value.GetString() ?? string.Empty)}\"",
            JsonValueKind.Array => $"array[{value.GetArrayLength()}]",
            JsonValueKind.Object => "object",
            _ => value.ToString()
        };
    }

    private static (bool Exists, string Value) ReadStringField(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return (false, "");
        return value.ValueKind == JsonValueKind.String
            ? (true, value.GetString() ?? "")
            : (true, value.ToString());
    }

    private static (bool Exists, string Value) ReadFirstStringField(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadStringField(root, name);
            if (value.Exists) return value;
        }
        return (false, "");
    }

    private static string NormalizeStatus(string value) => value.Trim() switch
    {
        "" => "none",
        "wish" => "wish",
        "do" => "do",
        "collect" => "collect",
        _ => "unknown"
    };

    private async Task<AuthContext> ReadAuthAsync()
    {
        if (!EnsureReady()) throw new InvalidOperationException("WebView2 尚未初始化");
        var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync(MovieBase + "/");
        var ck = cookies.FirstOrDefault(cookie => string.Equals(cookie.Name, "ck", StringComparison.OrdinalIgnoreCase))?.Value;
        var names = cookies.Select(cookie => cookie.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var hasDbcl2 = cookies.Any(cookie => string.Equals(cookie.Name, "dbcl2", StringComparison.OrdinalIgnoreCase));
        var cookieHeader = string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
        var userAgentJson = await _web.CoreWebView2.ExecuteScriptAsync("navigator.userAgent");
        var userAgent = DecodeJsonString(userAgentJson) ?? "Mozilla/5.0";
        return new AuthContext(ck, hasDbcl2, names, cookieHeader, userAgent);
    }

    private void LogHttpSummary(string title, HttpResult result)
    {
        Log($"{title}: HTTP {result.StatusCode} {result.ReasonPhrase}; bodyLength={result.Body.Length}");
        if (!string.IsNullOrWhiteSpace(result.Location)) Log($"{title} Location: {result.Location}");
    }

    private static string? DecodeJsonString(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
        try { return JsonSerializer.Deserialize<string>(json); }
        catch { return null; }
    }

    private void OpenSubject()
    {
        var id = ValidateSubjectId();
        if (id is not null && EnsureReady()) _web.CoreWebView2.Navigate($"{MovieBase}/subject/{id}/");
    }

    private string? ValidateSubjectId()
    {
        var id = _subjectId.Text.Trim();
        if (id.Length == 0 || id.Any(character => !char.IsDigit(character)))
        {
            MessageBox.Show(this, "请输入纯数字 Subject ID。", "Subject ID 无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return id;
    }

    private bool EnsureReady()
    {
        if (_ready && _web.CoreWebView2 is not null) return true;
        MessageBox.Show(this, "WebView2 还没有初始化完成。", "请稍后重试", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private bool BeginOperation()
    {
        if (_busy)
        {
            MessageBox.Show(this, "已有操作正在执行。", "操作进行中", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        if (!EnsureReady()) return false;
        _busy = true;
        _read.Enabled = false;
        _writeAndRead.Enabled = false;
        _deleteAndRead.Enabled = false;
        return true;
    }

    private void EndOperation()
    {
        _busy = false;
        _read.Enabled = true;
        _writeAndRead.Enabled = true;
        _deleteAndRead.Enabled = true;
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

    private sealed record AuthContext(string? Ck, bool HasDbcl2, IReadOnlyList<string> CookieNames, string CookieHeader, string UserAgent)
    {
        public bool HasCk => !string.IsNullOrWhiteSpace(Ck);
    }

    private sealed record HttpResult(int StatusCode, string ReasonPhrase, string? Location, string Body);

    private sealed record StatusItem(string Text, string Value)
    {
        public override string ToString() => $"{Text} ({Value})";
    }

    private sealed record JsonFieldObservation(string Name, string Kind, string Preview);

    private sealed record InterestJsonObservation(
        bool Parsed,
        string Status,
        string RawStatus,
        string? Html,
        string RawComment,
        string RawDate,
        IReadOnlyList<JsonFieldObservation> Fields,
        string? ParseError)
    {
        public static InterestJsonObservation FromError(string message) => new(false, "unknown", "<unavailable>", null, "<unavailable>", "<unavailable>", Array.Empty<JsonFieldObservation>(), message);
    }

    private sealed record StatusCandidate(string Status, string Text, string ClassName);

    private sealed record HtmlObservation(
        string Comment,
        string Date,
        bool HasInterestRoot,
        int HtmlLength,
        int InputCount,
        string InputSummary,
        IReadOnlyList<string> KeywordSummary,
        string RatingRaw,
        int CollectionDateCount,
        int CommentCandidateCount,
        string CommentSummary,
        string ControlComment,
        int DateCandidateCount,
        string DateCandidateSummary,
        string RootSelector,
        int RootTextLength,
        string Status,
        IReadOnlyList<StatusCandidate> StatusCandidates,
        string Tags,
        string Title,
        string CheckedInterestStatus,
        int TextareaCount,
        string TextareaSummary)
    {
        public static HtmlObservation Empty(string reason) => new("", "", false, 0, 0, "", Array.Empty<string>(), "", 0, 0, "", "", 0, "", "", 0, "unknown", Array.Empty<StatusCandidate>(), "", reason, "", 0, "");
    }

    private sealed record ReadbackSnapshot(
        HttpResult JsonResponse,
        HttpResult SubjectResponse,
        InterestJsonObservation Json,
        HtmlObservation ApiHtml,
        HtmlObservation SubjectHtml);
}
