using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed partial class DoubanWebView2Connector : IDoubanConnector, IDisposable
{
    private readonly WebView2 _worker;
    private readonly WebView2EnvironmentProvider _environments;
    private readonly string _webViewRole;
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly Store _store = new();
    private DoubanSessionState _session;
    private bool _initialized;
    private bool _eventsBound;
    private readonly CancellationTokenSource _browserLifetime = new();
    private bool _disposed;
    private bool _loginWindowActive;
    private string _loginStateBeforeVerification = "";
    private bool _deleteConfirmationPending;
    private string _deleteSubjectId = "";

    internal DoubanWebView2Connector(WebView2 worker, WebView2EnvironmentProvider environments, string webViewRole = "Worker")
    {
        _worker = worker;
        _environments = environments;
        _webViewRole = string.IsNullOrWhiteSpace(webViewRole) ? "Worker" : webViewRole.Trim();
        _session = _store.LoadDoubanSession();
    }

    public string Name => $"内置 WebView2 豆瓣连接器（{_webViewRole}）";
    internal string WebViewRole => _webViewRole;
    internal event Action<DoubanSessionStatus>? SessionStatusChanged;
    internal event Action<CoreWebView2ProcessFailedKind>? BrowserProcessFailed;
    internal DoubanSessionStatus CurrentSessionStatus => ToStatus();
    internal bool LoginWindowActive => _loginWindowActive;

    internal async Task WaitForIdleAsync()
    {
        await _navigationGate.WaitAsync();
        _navigationGate.Release();
    }

    private async Task<bool> WaitForNavigationGateAsync()
    {
        await _navigationGate.WaitAsync().ConfigureAwait(true);
        return true;
    }

    internal void SetLoginWindowActive(bool active)
    {
        _loginWindowActive = active;
        if (active)
        {
            _loginStateBeforeVerification = _session.LoginState;
            if (!string.Equals(_session.LoginState, "logged-in", StringComparison.OrdinalIgnoreCase))
                SetSessionState("verifying", "登录窗口已打开");
        }
        else if (_session.LoginState == "verifying")
        {
            var restoreState = string.IsNullOrWhiteSpace(_loginStateBeforeVerification) ? "unknown" : _loginStateBeforeVerification;
            SetSessionState(restoreState, "");
            _loginStateBeforeVerification = "";
        }
        else
        {
            _loginStateBeforeVerification = "";
        }
    }

    private DoubanSessionStatus ToStatus()
    {
        var text = _session.LoginState switch
        {
            "logged-in" => "豆瓣：已登录",
            "cookie-saved" => "豆瓣：登录信息已保存",
            "not-logged-in" => "豆瓣：未登录",
            "captcha" => "豆瓣：需要验证",
            "connection-error" => "豆瓣：连接异常",
            "verifying" => "豆瓣：正在验证",
            _ => "豆瓣：待验证"
        };
        return new(_session.LoginState, text, _session.ProfileId, _session.LastVerifiedAt, _session.LastError);
    }

    private void SetSessionState(string state, string error)
    {
        if (_session.LoginState == state && string.Equals(_session.LastError, error, StringComparison.Ordinal)) return;
        _session.LoginState = state;
        _session.LastError = error;
        _store.SaveDoubanSession(_session);
        SessionStatusChanged?.Invoke(ToStatus());
    }

    private void SetLoggedIn(string profileId)
    {
        if (!string.IsNullOrWhiteSpace(profileId)) _session.ProfileId = profileId;
        _session.LoginState = "logged-in";
        _session.LastVerifiedAt = DateTime.Now;
        _session.LastError = "";
        _store.SaveDoubanSession(_session);
        SessionStatusChanged?.Invoke(ToStatus());
    }

    internal async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        await _initializationGate.WaitAsync().ConfigureAwait(true);
        try
        {
        if (_initialized) return;
        var probe = WebView2EnvironmentProvider.ProbeRuntime();
        if (!probe.Available) throw new InvalidOperationException("未检测到 WebView2 Evergreen Runtime。" + probe.Error);
        await _worker.EnsureCoreWebView2Async(await _environments.GetDoubanEnvironmentAsync());
        var core = _worker.CoreWebView2;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        if (!_eventsBound)
        {
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.ScriptDialogOpening += (_, e) =>
        {
            var subjectId = ExtractSubjectId(e.Uri);
            if (string.IsNullOrWhiteSpace(subjectId) && _deleteConfirmationPending)
                subjectId = ExtractSubjectId(_worker.Source?.ToString());
            var deleteDialog = _deleteConfirmationPending && e.Kind == CoreWebView2ScriptDialogKind.Confirm &&
                IsExpectedDeleteDialogContext(e.Uri, subjectId);
            if (deleteDialog) _deleteDialogSeen = true;
            var accepted = deleteDialog && (e.Message.Contains("删除", StringComparison.Ordinal) || e.Message.Contains("移除", StringComparison.Ordinal) || e.Message.Contains("取消", StringComparison.Ordinal));
            if (accepted)
            {
                _deleteDialogAccepted = true;
                e.Accept();
            }
            DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 Douban script dialog; Kind={e.Kind}; SubjectId={subjectId}; DeleteDialog={deleteDialog}; Accepted={accepted}");
        };
        core.NavigationStarting += (_, e) =>
        {
            if (!IsAllowedDoubanTopLevel(e.Uri)) { e.Cancel = true; return; }
            ObserveReviewNavigationStarting(e.Uri);
            ObserveDeleteNavigationStarting(e.Uri);
        };
        core.NavigationCompleted += (_, e) =>
        {
            ObserveReviewNavigationCompleted(e);
            ObserveDeleteNavigationCompleted();
        };
        core.ProcessFailed += (_, e) =>
        {
            var browserExited = e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited;
            DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 Douban process failed; Kind={e.ProcessFailedKind}; BrowserExited={browserExited}; Recovery={(browserExited ? "delegated-to-host" : "not-required")}");
            if (!browserExited) return;
            _initialized = false;
            try { _browserLifetime.Cancel(); } catch { }
            BrowserProcessFailed?.Invoke(e.ProcessFailedKind);
        };
        _eventsBound = true;
        }
        _initialized = true;
        DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 Douban initialized; Runtime={probe.Version}; Profile={_environments.DoubanProfileDirectory}");
        }
        finally { _initializationGate.Release(); }
    }

    internal async Task<DoubanSessionStatus> GetSessionStatusAsync(bool skipWhenBusy = false)
    {
        var acquired = skipWhenBusy
            ? await _navigationGate.WaitAsync(0).ConfigureAwait(true)
            : await WaitForNavigationGateAsync().ConfigureAwait(true);
        if (!acquired)
        {
            DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 session cookie check skipped; Reason=foreground-navigation-busy");
            return ToStatus();
        }
        try
        {
            await EnsureInitializedAsync();
            var cookies = await _worker.CoreWebView2.CookieManager.GetCookiesAsync("https://www.douban.com/");
            // dbcl2 identifies an authenticated Douban account; ck can exist for anonymous visitors.
            var hasSessionCookie = cookies.Any(x => x.Name.Equals("dbcl2", StringComparison.OrdinalIgnoreCase));
            if (!hasSessionCookie) SetSessionState("not-logged-in", "Cookie missing");
            if (hasSessionCookie && _session.LoginState is "unknown" or "not-logged-in" or "connection-error") SetSessionState("cookie-saved", "");
            if (!hasSessionCookie && _session.LoginState != "logged-in") SetSessionState("not-logged-in", "未发现豆瓣会话 Cookie");
            return ToStatus();
        }
        catch (Exception ex)
        {
            SetSessionState("connection-error", ex.Message);
            return ToStatus();
        }
        finally { _navigationGate.Release(); }
    }

    public async Task<bool> IsLoggedInAsync() => (await VerifySessionAsync()).IsLoggedIn;

    private async Task<bool> VerifySessionCoreAsync()
    {
        SetSessionState("verifying", "");
        await NavigateAsync("https://www.douban.com/mine/");
        for (var i = 0; i < 30; i++)
        {
            var value = await EvaluateAsync(BrowserCdpService.DoubanProfileProbeScript);
            if (value.TryGetProperty("loginPage", out var login) && login.GetBoolean()) { SetSessionState("not-logged-in", "豆瓣登录已失效"); return false; }
            if (value.TryGetProperty("ready", out var ready) && ready.GetBoolean() && value.TryGetProperty("accountLoggedIn", out var account) && account.GetBoolean())
            {
                var profileId = String(value, "profileId");
                if (string.IsNullOrWhiteSpace(profileId)) return false;
                SetLoggedIn(profileId);
                return true;
            }
            await Task.Delay(500);
        }
        SetSessionState("connection-error", "豆瓣登录状态未能在规定时间内确认");
        return false;
    }

    internal async Task<DoubanSessionStatus> VerifySessionAsync()
    {
        await _navigationGate.WaitAsync();
        try
        {
            await EnsureInitializedAsync();
            await VerifySessionCoreAsync();
            return ToStatus();
        }
        catch (Exception ex) { SetSessionState("connection-error", ex.Message); return ToStatus(); }
        finally { _navigationGate.Release(); }
    }

    public async Task<DoubanSearchPageResult> SearchAsync(string text, int start)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return new(false, false, [], false, "请输入片名。");
        await _navigationGate.WaitAsync();
        try
        {
            await EnsureInitializedAsync();
            var url = $"https://search.douban.com/movie/subject_search?search_text={Uri.EscapeDataString(text)}&cat=1002&start={Math.Max(0, start)}";
            await NavigateAsync(url);
            JsonElement value = default;
            var found = false;
            for (var i = 0; i < 60; i++)
            {
                value = await EvaluateAsync(BrowserCdpService.DoubanSearchExtractionScript);
                if (value.TryGetProperty("ready", out var ready) && ready.GetBoolean()) { found = true; break; }
                await Task.Delay(500);
            }
            if (!found) throw new InvalidDataException("内置豆瓣搜索页在30秒内没有返回可用结果。");
            var captcha = value.TryGetProperty("captcha", out var captchaValue) && captchaValue.GetBoolean();
            var loggedIn = !value.TryGetProperty("loggedIn", out var loginValue) || loginValue.GetBoolean();
            if (captcha) return new(loggedIn, true, [], false, "豆瓣要求人工验证，请在内置登录窗口完成验证后重试。");
            if (!loggedIn) return new(false, false, [], false, "内置豆瓣 Profile 尚未登录，请先扫码登录。");
            var items = new List<DoubanSearchCandidate>();
            if (value.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array)
                foreach (var item in array.EnumerateArray())
                {
                    var id = String(item, "subjectId");
                    var subjectUrl = String(item, "url");
                    var visibleText = String(item, "visibleText");
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(subjectUrl) && !string.IsNullOrWhiteSpace(visibleText))
                        items.Add(new DoubanSearchCandidate(id, subjectUrl, String(item, "poster"), visibleText, BrowserCdpService.ReadStatusOptions(item)));
                }
            var hasMore = value.TryGetProperty("hasMore", out var more) && more.GetBoolean();
            var error = String(value, "error");
            DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 Douban search result; Text={text}; Start={start}; Count={items.Count}; HasMore={hasMore}");
            return new(true, false, items, hasMore, error);
        }
        finally { _navigationGate.Release(); }
    }

    public async Task<DoubanSubjectMetadataResult> ReadMetadataAsync(string subjectUrl, bool probeStatusCapabilities = true)
    {
        await _navigationGate.WaitAsync();
        try { return await ReadMetadataCoreAsync(subjectUrl, probeStatusCapabilities); }
        finally { _navigationGate.Release(); }
    }

    private async Task<DoubanSubjectMetadataResult> ReadMetadataCoreAsync(string subjectUrl, bool probeStatusCapabilities)
    {
        if (!IsAllowedSubjectUrl(subjectUrl)) throw new InvalidDataException("豆瓣影片地址无效。");
        await EnsureInitializedAsync();
        await EnsureMetadataSubjectPageAsync(subjectUrl);
        var script = BrowserCdpService.DoubanRenderedSubjectScript;
        JsonElement value = default;
        var found = false;
        for (var i = 0; i < 60; i++)
        {
            value = await EvaluateAsync(script);
            var ready = value.TryGetProperty("ready", out var readyValue) && readyValue.GetBoolean();
            var captcha = value.TryGetProperty("captcha", out var captchaValue) && captchaValue.GetBoolean();
            if (ready || captcha) { found = true; break; }
            await Task.Delay(500);
        }
        if (!found) throw new InvalidDataException("内置豆瓣详情页在30秒内没有就绪。");
        var captchaResult = value.TryGetProperty("captcha", out var captchaFinal) && captchaFinal.GetBoolean();
        var loggedIn = !value.TryGetProperty("loggedIn", out var loginFinal) || loginFinal.GetBoolean();
        double? score = null;
        if (value.TryGetProperty("score", out var scoreValue) && scoreValue.TryGetDouble(out var parsedScore) && parsedScore is > 0 and <= 10) score = parsedScore;
        var result = new DoubanSubjectMetadataResult(String(value, "title"), score, String(value, "poster"), captchaResult, loggedIn, captchaResult ? 429 : 200, String(value, "error"))
        {
            Year = String(value, "year"), Genres = String(value, "genres"), Directors = String(value, "directors"), Runtime = String(value, "runtime"), Countries = String(value, "countries"), ImdbId = String(value, "imdbId"),
            Summary = String(value, "summary"), StatusOptions = BrowserCdpService.ReadStatusOptions(value),
            StatusCapabilitiesKnown = false, StatusCapabilitySource = "detail-metadata", ConnectorSource = "webview2"
        };
        if (captchaResult) throw new InvalidOperationException("豆瓣要求验证码，已停止读取并保留本地数据。");
        if (!loggedIn) return result with { Error = "内置豆瓣 Profile 尚未登录。" };

        var subjectId = ExtractSubjectId(subjectUrl);
        if (probeStatusCapabilities)
        {
            var capabilities = await ProbeOfficialStatusCapabilitiesAsync(subjectUrl, subjectId, result.StatusOptions);
            result = result with
            {
                StatusOptions = capabilities.Options.Count > 0 ? capabilities.Options : result.StatusOptions,
                StatusCapabilitiesKnown = capabilities.Known,
                StatusCapabilitySource = capabilities.Source,
                StatusCapabilityError = capabilities.Error
            };
        }
        else
        {
            DiagnosticLogger.Write($"Douban metadata status capability probe skipped; SubjectId={subjectId}; Reason=official-review-read-follows");
        }
        return result;
    }

    private async Task EnsureMetadataSubjectPageAsync(string subjectUrl)
    {
        var subjectId = ExtractSubjectId(subjectUrl);
        var currentUrl = _worker.Source?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(subjectId) &&
            currentUrl.Contains($"/subject/{subjectId}/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var probe = await EvaluateAsync(DoubanWritePostSubmitProbeScript).ConfigureAwait(true);
                var reusable = Boolean(probe, "subjectPage") &&
                               !Boolean(probe, "formOpen") &&
                               !Boolean(probe, "captcha") &&
                               !Boolean(probe, "loginPage") &&
                               string.Equals(String(probe, "readyState"), "complete", StringComparison.OrdinalIgnoreCase);
                if (reusable)
                {
                    DiagnosticLogger.Write($"Douban metadata performance reuse; SubjectId={subjectId}; URL={currentUrl}");
                    return;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Write($"Douban metadata reuse probe failed; SubjectId={subjectId}; Error={ex.Message}");
            }
        }

        DiagnosticLogger.Write($"Douban metadata performance navigate; SubjectId={subjectId}; From={currentUrl}; To={subjectUrl}");
        await NavigateAsync(subjectUrl).ConfigureAwait(true);
    }

    private Task<DoubanWriteResult> SaveDoubanEntryLegacyAsync(string subjectUrl, DoubanEntryWriteRequest request) =>
        SaveDoubanEntryCoreAsync(subjectUrl, request);

    private Task<DoubanWriteResult> DeleteLegacyAsync(string subjectUrl) => DeleteCoreAsync(subjectUrl);

    private async Task<DoubanWriteResult> SaveDoubanEntryCoreAsync(string subjectUrl, DoubanEntryWriteRequest request)
    {
        var normalizedRequest = request with { Comment = (request.Comment ?? "").Trim() };
        var validationError = ValidateEntryRequest(normalizedRequest);
        if (!string.IsNullOrWhiteSpace(validationError))
            return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", "snapshot", validationError);
        if (!IsAllowedSubjectUrl(subjectUrl))
            return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", "snapshot", "豆瓣影片地址无效。");

        await _navigationGate.WaitAsync();
        var stage = "snapshot";
        var submitted = false;
        var settled = false;
        try
        {
            await EnsureInitializedAsync();
            var fixedProfileId = await EnsureFixedProfileCoreAsync("save");
            if (string.IsNullOrWhiteSpace(fixedProfileId))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "无法确认当前豆瓣用户 Profile。");

            await NavigateAsync(subjectUrl);
            var subjectId = ExtractSubjectId(subjectUrl);
            var before = await EvaluateAsync(DoubanWriteSnapshotScript.Replace("__PROFILE_ID__", JsonSerializer.Serialize(fixedProfileId)));
            DiagnosticLogger.Write($"Douban unified save snapshot; Stage={stage}; Href={String(before, "href")}; SubjectId={String(before, "subjectId")}; RequestedStatus={normalizedRequest.Status}; RequestedRating={normalizedRequest.Rating?.ToString() ?? "null"}; RequestedComment={normalizedRequest.SetComment}; FixedProfileId={fixedProfileId}; DetectedProfileId={String(before, "detectedProfileId")}; Controls={(before.TryGetProperty("controls", out var controls) ? controls.ToString() : "")}");

            if (Boolean(before, "captcha"))
            {
                SetSessionState("captcha", "豆瓣要求人工验证");
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "豆瓣要求人工验证，请完成验证后重试。");
            }
            if (Boolean(before, "loginPage") || !Boolean(before, "loggedIn"))
            {
                SetSessionState("not-logged-in", "豆瓣尚未登录");
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "内置豆瓣 Profile 尚未登录，请先扫码登录。");
            }
            if (!Boolean(before, "subjectPage") || !string.Equals(String(before, "subjectId"), subjectId, StringComparison.Ordinal))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "当前页面不是请求的豆瓣影片详情页。");
            var detectedProfileId = String(before, "detectedProfileId");
            if (!string.IsNullOrWhiteSpace(detectedProfileId) && !string.Equals(detectedProfileId, fixedProfileId, StringComparison.Ordinal))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "当前页面豆瓣用户与固定用户快照不一致。");
            if (!StatusAvailable(before, normalizedRequest.Status) && !Boolean(before, "editControlFound"))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "官方表单不提供请求状态。");

            stage = "formProbe";
            var open = await EvaluateAsync(DoubanWriteOpenScriptV2
                .Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId))
                .Replace("__STATUS__", JsonSerializer.Serialize(normalizedRequest.Status)));
            if (!Boolean(open, "clicked"))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, String(open, "error", "官方表单入口缺失。"));

            var formWait = await WaitForOfficialFormAsync(subjectId, requireSelectedInterest: true);
            var form = formWait.Form;
            DiagnosticLogger.Write($"Douban unified save form probe; SubjectId={subjectId}; Stable={formWait.Stable}; Attempts={formWait.Attempts}; StableSamples={formWait.StableSamples}; Form={form}");
            if (Boolean(form, "captcha"))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "豆瓣要求人工验证，请完成验证后重试。");
            if (Boolean(form, "loginPage"))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "内置豆瓣 Profile 登录已失效。");
            if (!Boolean(form, "ready"))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, String(form, "error", "官方编辑表单没有出现。"));
            if (!formWait.Stable)
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "官方编辑表单字段在等待时间内未稳定，未执行提交。请重试。");
            if (!FormOffersStatus(form, normalizedRequest.Status))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "官方表单不提供请求状态。");
            if (normalizedRequest.Rating is not null && (Int(form, "ratingControlCount") ?? 0) <= 0)
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "官方评分控件缺失。");
            if (normalizedRequest.SetComment && (Int(form, "commentControlCount") ?? 0) <= 0)
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, "官方短评控件缺失。");

            stage = "domFill";
            var payload = JsonSerializer.Serialize(new
            {
                subjectId,
                status = normalizedRequest.Status,
                rating = normalizedRequest.Rating,
                setComment = normalizedRequest.SetComment,
                comment = normalizedRequest.Comment
            });
            var submit = await EvaluateAsync(DoubanWriteSubmitScriptV2.Replace("__PAYLOAD__", payload));
            if (!Boolean(submit, "submitted"))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "blocked", stage, String(submit, "error", "官方提交按钮未能触发。"));
            submitted = true;
            stage = "submit";
            DiagnosticLogger.Write($"Douban unified save submitted; RequestedStatus={normalizedRequest.Status}; RequestedRating={normalizedRequest.Rating?.ToString() ?? "null"}; RequestedComment={normalizedRequest.SetComment}; Submit={submit}");

            var settle = await WaitForSubmitSettlementAsync();
            DiagnosticLogger.Write($"Douban unified save settle; Settled={settle.Settled}; State={settle.State}; Attempts={settle.Attempts}; Probe={settle.Probe}");
            if (!settle.Settled)
            {
                var settleError = settle.State switch
                {
                    "captcha" => "官方提交后出现人工验证，写入可能已触发但尚未确认，本地数据未更新。",
                    "login" => "官方提交后登录失效，写入可能已触发但尚未确认，本地数据未更新。",
                    _ => "官方提交后页面在等待时间内未稳定，写入可能已触发但尚未确认，本地数据未更新。"
                };
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "unconfirmed", stage, settleError, settled: false);
            }

            settled = true;
            stage = "readback";
            await NavigateAsync(subjectUrl);
            var afterSnapshot = await EvaluateAsync(DoubanWriteSnapshotScript.Replace("__PROFILE_ID__", JsonSerializer.Serialize(fixedProfileId)));
            if (Boolean(afterSnapshot, "captcha"))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "unconfirmed", stage, "提交后豆瓣要求人工验证，官方回读未确认写入。", settled: true);
            var afterDetectedProfileId = String(afterSnapshot, "detectedProfileId");
            if (!string.IsNullOrWhiteSpace(afterDetectedProfileId) && !string.Equals(afterDetectedProfileId, fixedProfileId, StringComparison.Ordinal))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "unconfirmed", stage, "写入后用户快照不一致，本地数据未更新。", settled: true);

            var readbackOpen = await EvaluateAsync(DoubanWriteOpenScriptV2
                .Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId))
                .Replace("__STATUS__", JsonSerializer.Serialize(normalizedRequest.Status)));
            if (!Boolean(readbackOpen, "clicked"))
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "unconfirmed", stage, "官方详情页回读无法重新打开编辑表单：" + String(readbackOpen, "error", "入口缺失"), settled: true);
            var officialWait = await WaitForOfficialFormAsync(subjectId, requireSelectedInterest: true);
            var official = officialWait.Form;
            DiagnosticLogger.Write($"Douban unified save readback form; SubjectId={subjectId}; Stable={officialWait.Stable}; Attempts={officialWait.Attempts}; StableSamples={officialWait.StableSamples}; Form={official}");
            var officialStatus = String(official, "selectedInterest");
            var officialRating = Boolean(official, "ratingKnown") ? Int(official, "rating") : null;
            var officialReview = Boolean(official, "commentKnown") ? String(official, "comment") : "";
            var officialStatusOptions = ReadOfficialFormStatusOptions(official);
            var verified = officialWait.Stable && OfficialFormMatches(official, normalizedRequest);

            try { await NavigateAsync(subjectUrl); }
            catch (Exception restoreException) { DiagnosticLogger.Write("Douban unified save restore subject failed; Error=" + restoreException.Message); }

            if (!verified)
            {
                var readbackError = officialWait.Stable
                    ? "官方详情页回读未确认全部请求字段，本地数据未更新。"
                    : "官方详情页回读字段在等待时间内未稳定，本地数据未更新。";
                return EntryWriteResult(false, subjectUrl, normalizedRequest, "unconfirmed", stage, readbackError, officialStatus, officialRating, officialReview, settled: true, statusOptions: officialStatusOptions);
            }

            SetLoggedIn(fixedProfileId);
            return EntryWriteResult(true, subjectUrl, normalizedRequest, "confirmed", stage, "", officialStatus, officialRating, officialReview, settled: true, statusOptions: officialStatusOptions);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"Douban unified save FAILED; Stage={stage}; Submitted={submitted}; URL={_worker.Source}; Error={ex}");
            return EntryWriteResult(false, subjectUrl, normalizedRequest, submitted ? "unconfirmed" : "failed", stage,
                submitted ? "官方提交动作可能已经触发，但回读失败，本地数据未更新：" + ex.Message : ex.Message, settled: settled);
        }
        finally { _navigationGate.Release(); }
    }

    private sealed record OfficialFormWaitResult(JsonElement Form, bool Stable, int Attempts, int StableSamples);
    private sealed record SubmitSettlementWaitResult(JsonElement Probe, bool Settled, string State, int Attempts);
    private sealed record StatusCapabilityProbeResult(List<DoubanStatusOption> Options, bool Known, string Source, string Error);

    private async Task<OfficialFormWaitResult> WaitForOfficialFormAsync(string subjectId, bool requireSelectedInterest = false)
    {
        JsonElement form = default;
        var previousSignature = "";
        var stableSamples = 0;
        const int maxAttempts = 24;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await Task.Delay(attempt == 1 ? 100 : 250);
            form = await EvaluateAsync(DoubanWriteFormProbeScript.Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId)));
            if (Boolean(form, "captcha") || Boolean(form, "loginPage"))
                return new(form, false, attempt, 0);
            if (!Boolean(form, "ready") || (requireSelectedInterest && !Boolean(form, "semanticComplete")))
            {
                if (Boolean(form, "ready") && requireSelectedInterest)
                    DiagnosticLogger.Write($"Douban official form initializing; SubjectId={subjectId}; Attempt={attempt}; SelectedInterestCount={Int(form, "selectedInterestCount") ?? 0}; SelectedInterest={String(form, "selectedInterest")}");
                previousSignature = "";
                stableSamples = 0;
                continue;
            }

            var signature = OfficialFormSignature(form);
            stableSamples = signature.Length > 0 && string.Equals(signature, previousSignature, StringComparison.Ordinal) ? stableSamples + 1 : 1;
            previousSignature = signature;
            if (stableSamples >= 2) return new(form, true, attempt, stableSamples);
        }
        return new(form, false, maxAttempts, stableSamples);
    }

    private async Task<SubmitSettlementWaitResult> WaitForSubmitSettlementAsync()
    {
        JsonElement probe = default;
        var previousSignature = "";
        var stableSamples = 0;
        for (var attempt = 1; attempt <= 48; attempt++)
        {
            await Task.Delay(250);
            try
            {
                probe = await EvaluateAsync(DoubanWritePostSubmitProbeScript);
                if (Boolean(probe, "captcha")) return new(probe, false, "captcha", attempt);
                if (Boolean(probe, "loginPage")) return new(probe, false, "login", attempt);

                var candidate = Boolean(probe, "subjectPage") && !Boolean(probe, "formOpen") &&
                    !string.Equals(String(probe, "readyState"), "loading", StringComparison.OrdinalIgnoreCase);
                if (!candidate)
                {
                    previousSignature = "";
                    stableSamples = 0;
                    continue;
                }
                var signature = $"{String(probe, "href")}|{String(probe, "readyState")}|{Boolean(probe, "formOpen")}";
                stableSamples = string.Equals(signature, previousSignature, StringComparison.Ordinal) ? stableSamples + 1 : 1;
                previousSignature = signature;
                if (stableSamples >= 2) return new(probe, true, "settled", attempt);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Write($"Douban unified save settlement probe transient failure; Attempt={attempt}; Error={ex.Message}");
            }
        }
        return new(probe, false, "timeout", 48);
    }

    private async Task<StatusCapabilityProbeResult> ProbeOfficialStatusCapabilitiesAsync(string subjectUrl, string subjectId, List<DoubanStatusOption> metadataOptions)
    {
        var opened = false;
        try
        {
            var open = await EvaluateAsync(DoubanWriteOpenCapabilityScript.Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId)));
            opened = Boolean(open, "clicked");
            if (!opened)
            {
                var error = String(open, "error", "官方编辑表单入口缺失。");
                DiagnosticLogger.Write($"Douban status capability open failed; SubjectId={subjectId}; Open={open}");
                return new(metadataOptions, false, "detail-metadata", error);
            }

            var wait = await WaitForOfficialFormAsync(subjectId);
            DiagnosticLogger.Write($"Douban status capability form; SubjectId={subjectId}; Stable={wait.Stable}; Attempts={wait.Attempts}; StableSamples={wait.StableSamples}; Form={wait.Form}");
            if (Boolean(wait.Form, "captcha")) return new(metadataOptions, false, "official-form", "豆瓣要求人工验证，状态能力未确认。");
            if (Boolean(wait.Form, "loginPage")) return new(metadataOptions, false, "official-form", "豆瓣登录已失效，状态能力未确认。");
            if (!wait.Stable) return new(metadataOptions, false, "official-form", "官方编辑表单字段未稳定，状态能力将在保存前再次确认。");

            var options = ReadOfficialFormStatusOptions(wait.Form);
            if (options.Count == 0) return new(metadataOptions, false, "official-form", "官方编辑表单没有返回可识别的状态选项。");
            return new(options, true, "official-form", "");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"Douban status capability probe failed; SubjectId={subjectId}; Error={ex}");
            return new(metadataOptions, false, "detail-metadata", "状态能力探测失败，将在保存前再次确认：" + ex.Message);
        }
        finally
        {
            if (opened)
            {
                try { await NavigateAsync(subjectUrl); }
                catch (Exception ex) { DiagnosticLogger.Write($"Douban status capability restore failed; SubjectId={subjectId}; Error={ex.Message}"); }
            }
        }
    }

    private static List<DoubanStatusOption> ReadOfficialFormStatusOptions(JsonElement form)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["wish"] = "想看", ["do"] = "在看", ["collect"] = "看过"
        };
        var selected = String(form, "selectedInterest");
        var result = new List<DoubanStatusOption>();
        if (!form.TryGetProperty("interestOptions", out var options) || options.ValueKind != JsonValueKind.Array) return result;
        foreach (var option in options.EnumerateArray())
        {
            var value = String(option, "value");
            if (!labels.TryGetValue(value, out var text) || result.Any(x => x.Text.Equals(text, StringComparison.Ordinal))) continue;
            result.Add(new DoubanStatusOption(text, string.Equals(value, selected, StringComparison.OrdinalIgnoreCase) || Boolean(option, "checked")));
        }
        return result;
    }

    private static string OfficialFormSignature(JsonElement form)
    {
        if (form.ValueKind != JsonValueKind.Object) return "";
        var interests = form.TryGetProperty("interestOptions", out var options) && options.ValueKind == JsonValueKind.Array
            ? string.Join(",", options.EnumerateArray().Select(x => $"{String(x, "type")}:{String(x, "value")}:{Boolean(x, "checked")}"))
            : "";
        return string.Join("|", new[]
        {
            interests,
            String(form, "selectedInterest"),
            Boolean(form, "ratingKnown").ToString(),
            String(form, "ratingRaw"),
            Boolean(form, "commentKnown").ToString(),
            NormalizeText(String(form, "comment")),
            (Int(form, "ratingControlCount") ?? 0).ToString(),
            (Int(form, "commentControlCount") ?? 0).ToString(),
            String(form, "actionPath")
        });
    }

    private static DoubanWriteResult EntryWriteResult(bool success, string subjectUrl, DoubanEntryWriteRequest request, string phase, string stage, string error,
        string officialStatus = "", int? officialRating = null, string officialReview = "", bool settled = false, List<DoubanStatusOption>? statusOptions = null) =>
        new(success, subjectUrl, "save", officialStatus, officialRating, officialReview, false, error, phase, stage,
            request.Status, request.Rating, request.SetComment, request.SetComment ? request.Comment.Length : 0, false, settled, statusOptions);

    internal static string ValidateEntryRequest(DoubanEntryWriteRequest request)
    {
        if (request.Status is not ("wish" or "do" or "collect")) return "豆瓣状态无效。";
        if (request.Rating is < 1 or > 5) return "评分必须为 1 至 5 星。";
        if (request.Status == "wish" && request.Rating is not null) return "想看状态不能同时提交评分。";
        if (request.SetComment && request.Comment.Length > 330) return "短评不能超过 330 字。";
        return "";
    }

    private static bool StatusAvailable(JsonElement snapshot, string status) =>
        snapshot.TryGetProperty("controls", out var controls) && controls.ValueKind == JsonValueKind.Array &&
        controls.EnumerateArray().Any(x => Boolean(x, "found") && string.Equals(String(x, "status"), status, StringComparison.OrdinalIgnoreCase));

    private static bool FormOffersStatus(JsonElement form, string status)
    {
        if (!form.TryGetProperty("interestOptions", out var options) || options.ValueKind != JsonValueKind.Array) return false;
        var values = options.EnumerateArray().Select(x => String(x, "value")).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return values.Any(x => string.Equals(x, status, StringComparison.OrdinalIgnoreCase));
    }

    private static bool OfficialFormMatches(JsonElement form, DoubanEntryWriteRequest request)
    {
        if (!Boolean(form, "ready") || Boolean(form, "captcha") || Boolean(form, "loginPage")) return false;
        if (!string.Equals(String(form, "selectedInterest"), request.Status, StringComparison.OrdinalIgnoreCase)) return false;
        if (request.Rating is not null && (!Boolean(form, "ratingKnown") || Int(form, "rating") != request.Rating)) return false;
        if (request.SetComment && (!Boolean(form, "commentKnown") || !string.Equals(NormalizeText(String(form, "comment")), NormalizeText(request.Comment), StringComparison.Ordinal))) return false;
        return true;
    }

    private static string NormalizeText(string value) =>
        string.Join(" ", value.Replace("\r", " ").Replace("\n", " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool Boolean(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static int? Int(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.TryGetInt32(out var number) ? number : null;

    private async Task<string> EnsureFixedProfileCoreAsync(string action)
    {
        var profile = _session.ProfileId;
        if (!string.IsNullOrWhiteSpace(profile)) return profile;
        DiagnosticLogger.Write($"Douban write profile missing; Action={action}; Attempt=VerifySessionCore");
        try { if (await VerifySessionCoreAsync()) profile = _session.ProfileId; }
        catch (Exception ex) { DiagnosticLogger.Write($"Douban write profile verification failed; Action={action}; Error={ex.Message}"); }
        return profile;
    }

    private async Task<DoubanWriteResult> DeleteCoreAsync(string subjectUrl)
    {
        if (!IsAllowedSubjectUrl(subjectUrl)) throw new InvalidDataException("豆瓣影片地址无效。");
        await _navigationGate.WaitAsync();
        try
        {
            await EnsureInitializedAsync();
            var fixedProfileId = await EnsureFixedProfileCoreAsync("delete");
            if (string.IsNullOrWhiteSpace(fixedProfileId)) return new(false, subjectUrl, "delete", "", null, "", false, "无法确认当前豆瓣用户 Profile。");
            await NavigateAsync(subjectUrl);
            var before = await EvaluateAsync(DoubanWriteSnapshotScript.Replace("__PROFILE_ID__", JsonSerializer.Serialize(fixedProfileId)));
            var profileId = fixedProfileId;
            DiagnosticLogger.Write($"Douban delete snapshot; Action=delete; Href={String(before, "href")}; LoginPage={String(before, "loginPage")}; SnapshotProfileId={String(before, "profileId")}; FixedProfileId={fixedProfileId}");
            if (before.TryGetProperty("captcha", out var cap) && cap.GetBoolean()) return new(false, subjectUrl, "delete", "", null, "", false, "豆瓣要求人工验证，请完成验证后重试。");
            if (!before.TryGetProperty("loggedIn", out var li) || !li.GetBoolean() || string.IsNullOrWhiteSpace(profileId)) return new(false, subjectUrl, "delete", "", null, "", false, "无法固定当前豆瓣用户快照。");
            _deleteSubjectId = ExtractSubjectId(subjectUrl); _deleteConfirmationPending = true;
            try
            {
                var clicked = await EvaluateAsync(DoubanDeleteControlScript);
                var clickedOk = clicked.TryGetProperty("clicked", out var clickedFlag) && clickedFlag.GetBoolean() || clicked.TryGetProperty("success", out var successFlag) && successFlag.GetBoolean();
                if (!clickedOk) return new(false, subjectUrl, "delete", "", null, "", false, String(clicked, "error", "官方删除控件缺失"));
                await Task.Delay(1800);
                await NavigateAsync(subjectUrl);
                var after = await EvaluateAsync(DoubanWriteSnapshotScript.Replace("__PROFILE_ID__", JsonSerializer.Serialize(fixedProfileId)));
                if (String(after, "profileId") != profileId) return new(false, subjectUrl, "delete", "", null, "", false, "删除后用户快照不一致。");
                foreach (var status in new[] { "wish", "do", "collect" })
                {
                    await NavigateAsync($"https://movie.douban.com/people/{profileId}/{status}?start=0");
                    var exists = await EvaluateAsync(DoubanHistoryContainsSubjectScript.Replace("__SUBJECT_ID__", JsonSerializer.Serialize(_deleteSubjectId)));
                    if (!exists.TryGetProperty("ready", out var ready) || !ready.GetBoolean() || (exists.TryGetProperty("contains", out var contains) && contains.GetBoolean()))
                        return new(false, subjectUrl, "delete", "", null, "", false, $"删除后无法确认 {status} 列表状态。");
                }
                SetLoggedIn(profileId);
                return new(true, subjectUrl, "delete", "deleted", null, "", true);
            }
            finally { _deleteConfirmationPending = false; _deleteSubjectId = ""; }
        }
        finally { _navigationGate.Release(); }
    }

    private async Task NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        var navigationStartedAt = DateTimeOffset.UtcNow;
        var navigationTimer = System.Diagnostics.Stopwatch.StartNew();
        DiagnosticLogger.Write($"WebView={_webViewRole}; NavigationStarting; TargetUrl={url}; StartedAt={navigationStartedAt:O}");
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, CoreWebView2NavigationCompletedEventArgs args) => completion.TrySetResult(args);
        _worker.NavigationCompleted += Handler;
        try
        {
            _worker.CoreWebView2.Navigate(url);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _browserLifetime.Token);
            CoreWebView2NavigationCompletedEventArgs completed;
            try
            {
                completed = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), linked.Token);
            }
            catch (OperationCanceledException) when (_browserLifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("WebView2 浏览器进程已退出，正在自动恢复。");
            }
            navigationTimer.Stop();
            DiagnosticLogger.Write($"WebView={_webViewRole}; NavigationCompleted; TargetUrl={url}; CurrentUrl={_worker.Source}; IsSuccess={completed.IsSuccess}; WebErrorStatus={completed.WebErrorStatus}; NavigationElapsedMs={navigationTimer.Elapsed.TotalMilliseconds:F0}");
            if (!completed.IsSuccess && completed.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                throw new InvalidOperationException("豆瓣页面加载失败：" + completed.WebErrorStatus);
        }
        finally { _worker.NavigationCompleted -= Handler; }
    }

    private async Task<JsonElement> EvaluateAsync(string script)
    {
        var wrappedScript =
            "(()=>{try{const value=(" + script +
            ");return JSON.stringify({ok:true,value:value,href:location.href||'',title:document.title||'',readyState:document.readyState||''});}" +
            "catch(error){return JSON.stringify({ok:false,error:String(error),description:error&&error.message?String(error.message):'',stack:error&&error.stack?String(error.stack):'',href:location.href||'',title:document.title||'',readyState:document.readyState||''});}})()";
        var scriptPreview = script[..Math.Min(120, script.Length)];
        string json;
        try
        {
            json = await _worker.CoreWebView2.ExecuteScriptAsync(wrappedScript);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 ExecuteScript FAILED; Expression={scriptPreview}; URL={_worker.Source}; Error={ex}");
            throw;
        }

        using var outer = JsonDocument.Parse(json);
        if (outer.RootElement.ValueKind != JsonValueKind.String)
        {
            DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 ExecuteScript unexpected result; Expression={scriptPreview}; URL={_worker.Source}; Result={outer.RootElement}");
            throw new InvalidOperationException("豆瓣页面脚本返回格式异常。");
        }
        var envelopeJson = outer.RootElement.GetString();
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 ExecuteScript empty result; Expression={scriptPreview}; URL={_worker.Source}");
            throw new InvalidOperationException("豆瓣页面脚本没有返回结果。");
        }
        using var envelopeDocument = JsonDocument.Parse(envelopeJson);
        var envelope = envelopeDocument.RootElement;
        if (envelope.ValueKind != JsonValueKind.Object)
        {
            DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 ExecuteScript invalid envelope; Expression={scriptPreview}; URL={_worker.Source}; Envelope={envelope}; EnvelopeKind={envelope.ValueKind}");
            throw new InvalidOperationException("豆瓣页面脚本返回内容无法解析。");
        }
        var href = envelope.TryGetProperty("href", out var hrefValue) ? hrefValue.GetString() ?? "" : _worker.Source?.ToString() ?? "";
        var title = envelope.TryGetProperty("title", out var titleValue) ? titleValue.GetString() ?? "" : "";
        var readyState = envelope.TryGetProperty("readyState", out var readyStateValue) ? readyStateValue.GetString() ?? "" : "";
        var ok = envelope.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        if (!ok)
        {
            var error = envelope.TryGetProperty("error", out var errorValue) ? errorValue.GetString() ?? "JavaScript 执行失败" : "JavaScript 执行失败";
            var description = envelope.TryGetProperty("description", out var descriptionValue) ? descriptionValue.GetString() ?? "" : "";
            var stack = envelope.TryGetProperty("stack", out var stackValue) ? stackValue.GetString() ?? "" : "";
            DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 SCRIPT ERROR; Expression={scriptPreview}; Error={error}; Description={description}; Href={href}; Title={title}; ReadyState={readyState}; Stack={stack}");
            throw new InvalidOperationException($"豆瓣页面脚本执行失败：{(string.IsNullOrWhiteSpace(description) ? error : description)}；URL={href}");
        }
        if (!envelope.TryGetProperty("value", out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            DiagnosticLogger.Write($"WebView={_webViewRole}; WebView2 ExecuteScript value is null; Expression={scriptPreview}; Href={href}; Title={title}; ReadyState={readyState}");
            throw new InvalidOperationException($"豆瓣页面脚本没有返回有效值；URL={href}");
        }
        return value.Clone();
    }

    internal static bool IsAllowedDoubanTopLevel(string? url)
    {
        if (string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
               (uri.Host.Equals("douban.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".douban.com", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsAllowedSubjectUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase) &&
        System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/subject/\d+/?$");

    // Writes use the rendered official controls/forms; they never call a
    // private endpoint or send an HTTP request from the application.
    internal const string DoubanWriteSnapshotScript = """
(() => {
  const fixedProfileId = __PROFILE_ID__;
  const href = location.href || '';
  const body = document.body?.innerText || '';
  const match = href.match(/^https:\/\/movie\.douban\.com\/subject\/(\d+)\/?/);
  const subjectId = match?.[1] || '';
  const captcha = href.includes('/misc/sorry') || document.title.includes('禁止访问') ||
    !!document.querySelector('input[name="captcha-solution"],.captcha_image,#captcha_image') ||
    /异常请求|请输入验证码|访问过于频繁|像机器人程序|点击证明/.test(body);
  const loginPage = href.includes('accounts.douban.com') ||
    !!document.querySelector('form[action*="accounts/login"],form[action*="/passport/login"]');
  const personal = [...document.querySelectorAll('#interest_sect_level,.interest_sect_level,#interest_sect,[class*="interest"]')]
    .find(x => !x.closest('#interest_sectl,.rating_wrap'));
  const labels = {wish:'想看',do:'在看',collect:'看过'};
  const normalize = value => String(value || '').replace(/\s+/g,'').trim();
  const fallbackControl = status => {
    if (!personal) return null;
    const hit = [...personal.querySelectorAll('a,button,input,label,span')].find(node => normalize(node.value || node.textContent) === labels[status]);
    return hit?.closest('a,button,input') || hit || null;
  };
  const controls = ['wish','do','collect'].map(status => {
    const name = `pbtn-${subjectId}-${status}`;
    const exact = subjectId ? document.querySelector(`[name="${name}"]`) : null;
    const element = exact || fallbackControl(status);
    if (!element) return {status,name,found:false,fallback:false};
    const className = String(element.className || '');
    const selected = element.checked === true || element.getAttribute('aria-pressed') === 'true' ||
      element.getAttribute('aria-selected') === 'true' || /(?:^|\s)(?:selected|active|on|current)(?:\s|$)/i.test(className) ||
      /已想看|已在看|已看过|修改/.test((element.value || element.textContent || '').replace(/\s+/g,''));
    const container = element.closest('.a_stars,.interest_sect_level,#interest_sect_level,#interest_sect,[class*="interest"]');
    return {status,name,found:true,fallback:!exact,selected,tag:element.tagName,className,text:(element.value || element.textContent || '').replace(/\s+/g,'').trim(),containerTag:container?.tagName||'',containerId:container?.id||'',containerClass:String(container?.className||'')};
  });
  const foundControls = controls.filter(x => x.found);
  const selected = foundControls.filter(x => x.selected);
  const editName = `pbtn-${subjectId}`;
  const editControl = subjectId ? personal?.querySelector(`[name="${editName}"]`) : null;
  const deleteCandidates = personal ? [...personal.querySelectorAll('input,a,button')].filter(node =>
    normalize(node.value || node.textContent) === '删除' && /(?:^|\s)a_confirm_link(?:\s|$)/i.test(String(node.className || ''))) : [];
  const deleteControl = deleteCandidates.length === 1 ? deleteCandidates[0] : null;
  const deleteForm = deleteControl?.closest('form');
  const deleteAction = deleteForm?.getAttribute('action') || '';
  let deleteActionPath=''; try { deleteActionPath=deleteAction ? new URL(deleteAction, location.href).pathname : ''; } catch {}
  const personalText = normalize(personal?.innerText || '');
  const currentStatus = /我想看|已想看|想看这部/.test(personalText) ? 'wish' :
    /我在看|已在看|在看这部/.test(personalText) ? 'do' :
    /我看过|已看过|看过这部/.test(personalText) ? 'collect' : '';
  const statusKnown = !!personal && ((foundControls.length >= 2) || (!!editControl && !!currentStatus)) && selected.length <= 1;
  const detectedStatus = selected.length === 1 ? selected[0].status : currentStatus || (statusKnown ? 'none' : '');
  const ratingNode = personal?.querySelector('input[name="rating"]:checked,[class*="allstar"],[data-rating].selected');
  const ratingClass = String(ratingNode?.className || '');
  const rating = Number(ratingNode?.value || ratingNode?.getAttribute?.('data-rating') || (ratingClass.match(/allstar([1-5])0/) || [])[1] || 0) || 0;
  const ratingKnown = !!ratingNode;
  const commentNode = personal?.querySelector('textarea[name="comment"],.comment,.short-comment,[data-comment]');
  const comment = (commentNode?.value || commentNode?.textContent || '').trim();
  const commentKnown = !!commentNode;
  const markedDateSource = personal?.querySelector('time,[datetime],.date,.date-time')?.getAttribute?.('datetime') ||
    personal?.querySelector('time,[datetime],.date,.date-time')?.textContent || personalText;
  const markedDateMatch = String(markedDateSource || '').match(/((?:19|20)\d{2})[-/.年](\d{1,2})[-/.月](\d{1,2})日?/);
  const markedDate = markedDateMatch ? `${markedDateMatch[1]}-${String(markedDateMatch[2]).padStart(2,'0')}-${String(markedDateMatch[3]).padStart(2,'0')}` : '';
  const markedDateKnown = detectedStatus === 'none' || !!markedDate;
  const profileLinks = [...document.querySelectorAll('#db-global-nav a[href*="/people/"],#global-nav a[href*="/people/"],.top-nav-info a[href*="/people/"]')];
  const detectedProfileId = profileLinks.map(x => (x.href.match(/\/people\/(\d+)\//) || [])[1]).find(Boolean) || '';
  const profileId = fixedProfileId || detectedProfileId;
  const personalCandidates = personal ? [...personal.querySelectorAll('a,button,input,label,span')].map(x => ({tag:x.tagName,name:x.getAttribute('name')||'',id:x.id||'',text:x.getAttribute('name')==='ck'?'[redacted]':normalize(x.value||x.textContent||''),className:String(x.className||'')})).filter(x => x.text || x.name || x.id).slice(0,30) : [];
  return {ready:true,href,title:document.title,readyState:document.readyState,subjectPage:!!match,subjectId,captcha,loginPage,
    loggedIn:!loginPage&&!captcha&&!!profileId,profileId,detectedProfileId,status:detectedStatus,review:comment,officialTitle:document.querySelector('h1 span[property="v:itemreviewed"],h1')?.textContent?.trim()||'',markedDate,markedDateKnown,
    detectedStatus,statusKnown,currentStatus,editControlFound:!!editControl,editControlName:editName,
    deleteControlFound:!!deleteControl,deleteControlCount:deleteCandidates.length,deleteControlTag:deleteControl?.tagName||'',deleteControlType:deleteControl?.type||'',deleteControlClass:String(deleteControl?.className||''),deleteFormActionPath:deleteActionPath,deleteFormMethod:deleteForm?.getAttribute('method')||'',
    availableStatuses:foundControls.map(x=>x.status),rating,ratingKnown,comment,commentKnown,personalRegionFound:!!personal,
    controls,statusControls:controls,personalCandidates};
})()
""";
    internal const string DoubanWriteOpenCapabilityScript = """
(() => {
  const subjectId=__SUBJECT_ID__;
  const personal=[...document.querySelectorAll('#interest_sect_level,.interest_sect_level,#interest_sect,[class*="interest"]')]
    .find(x=>!x.closest('#interest_sectl,.rating_wrap'));
  if(!personal)return {clicked:false,error:'官方个人状态区域缺失'};
  const normalize=value=>String(value||'').replace(/\s+/g,'').trim();
  const generic=personal.querySelector(`[name="pbtn-${subjectId}"]`);
  const statuses=['wish','do','collect'];
  const named=statuses.map(status=>personal.querySelector(`[name="pbtn-${subjectId}-${status}"]`)).filter(Boolean);
  const isSelected=node=>node.checked===true||node.getAttribute('aria-pressed')==='true'||node.getAttribute('aria-selected')==='true'||
    /(?:^|\s)(?:selected|active|on|current)(?:\s|$)/i.test(String(node.className||''))||/已想看|已在看|已看过|修改/.test(normalize(node.value||node.textContent));
  const selected=named.find(isSelected);
  const fallback=[...personal.querySelectorAll('a,button,input')].find(node=>/^(想看|在看|看过|修改)$/.test(normalize(node.value||node.textContent)));
  const button=generic||selected||named[0]||fallback;
  if(!button)return {clicked:false,error:'官方编辑表单入口缺失',genericFound:!!generic,namedCount:named.length};
  if(!['A','BUTTON','INPUT'].includes(button.tagName))return {clicked:false,error:'官方编辑入口类型不受支持',tag:button.tagName};
  button.click();
  return {clicked:true,generic:button===generic,selected:button===selected,tag:button.tagName,name:button.getAttribute('name')||'',text:normalize(button.value||button.textContent)};
})()
""";
    internal const string DoubanWriteOpenScriptV2 = """
(() => {
  const subjectId = __SUBJECT_ID__; const status = __STATUS__;
  const expected = `pbtn-${subjectId}-${status}`;
  const labels = {wish:'想看',do:'在看',collect:'看过'};
  const personal = [...document.querySelectorAll('#interest_sect_level,.interest_sect_level,#interest_sect,[class*="interest"]')]
    .find(x => !x.closest('#interest_sectl,.rating_wrap'));
  const exact = document.querySelector(`[name="${expected}"]`);
  const generic = subjectId ? personal?.querySelector(`[name="pbtn-${subjectId}"]`) : null;
  const hit = !exact && personal ? [...personal.querySelectorAll('a,button,input,label,span')]
    .find(node => String(node.value || node.textContent || '').replace(/\s+/g,'').trim() === labels[status]) : null;
  const button = exact || generic || hit?.closest('a,button,input') || hit;
  if (!button) return {clicked:false,error:'官方表单不提供请求状态',expected,fallbackSearched:!!personal};
  if (!['A','BUTTON','INPUT'].includes(button.tagName)) return {clicked:false,error:'官方状态控件类型不受支持',tag:button.tagName};
  button.click();
  return {clicked:true,generic:!!generic&&!exact,tag:button.tagName,name:button.getAttribute('name')||'',text:(button.value||button.textContent||'').replace(/\s+/g,'').trim()};
})()
""";
    internal const string DoubanWriteFormProbeScript = """
(() => {
  const subjectId=__SUBJECT_ID__; const href=location.href||''; const body=document.body?.innerText||'';
  const captcha=href.includes('/misc/sorry')||document.title.includes('禁止访问')||
    !!document.querySelector('input[name="captcha-solution"],.captcha_image,#captcha_image')||
    /异常请求|请输入验证码|访问过于频繁|像机器人程序|点击证明/.test(body);
  const loginPage=href.includes('accounts.douban.com')||!!document.querySelector('form[action*="accounts/login"],form[action*="/passport/login"]');
  const containerOf=form=>{const c=form.closest('.interest_sect_level,#interest_sect_level,#interest_sect,[class*="interest"]');return c&&!c.closest('#interest_sectl,.rating_wrap')?c:null;};
  const forms=[...document.querySelectorAll('form')].filter(form=>{
    const style=getComputedStyle(form); if(style.display==='none'||style.visibility==='hidden')return false;
    const c=containerOf(form); if(!c)return false;
    const action=form.getAttribute('action')||''; if(action&&subjectId&&!action.includes(subjectId))return false;
    return !!form.querySelector('textarea[name="comment"],input[name="rating"],input[type="submit"],button[type="submit"]') &&
      !/accounts\/login|passport\/login/.test(action);
  });
  const form=forms[0];
  const fields=form?[...form.querySelectorAll('input,textarea,button')].map(x=>({tag:x.tagName,type:x.type||'',name:x.name||'',checked:x.checked===true,maxLength:x.maxLength||0})):[];
  const ratingControlCount=form?[...form.querySelectorAll('img[id^="star"],[id^="star"],[data-rating],[role="radio"],input[type="radio"][name="rating"]')].filter(x=>getComputedStyle(x).display!=='none'&&getComputedStyle(x).visibility!=='hidden').length:0;
  const commentControlCount=form?[...form.querySelectorAll('textarea[name="comment"],textarea')].length:0;
  const interestOptions=form?[...form.querySelectorAll('input[name="interest"]')].map(x=>({type:x.type||'',value:x.value||'',checked:x.checked===true})):[];
  const checkedInterests=interestOptions.filter(x=>(x.type==='radio'||x.type==='checkbox')&&x.checked);
  const hiddenInterest=interestOptions.length===1&&interestOptions[0].type==='hidden'&&/^(wish|do|collect)$/.test(interestOptions[0].value)?interestOptions[0]:null;
  const selectedInterest=checkedInterests[0]?.value || hiddenInterest?.value || '';
  const selectedInterestCount=checkedInterests.length+(hiddenInterest?1:0);
  const semanticComplete=selectedInterestCount===1&&/^(wish|do|collect)$/.test(selectedInterest);
  const ratingHidden=form?.querySelector('input[name="rating"]');
  const ratingRaw=String(ratingHidden?.value||'').trim();
  const ratingNumber=Number(ratingRaw||0);
  const ratingNormalized=Number.isFinite(ratingNumber)&&ratingNumber>0 ? (ratingNumber>=10&&ratingNumber%10===0?ratingNumber/10:ratingNumber) : 0;
  const rating=ratingNormalized>=1&&ratingNormalized<=5?ratingNormalized:null;
  const ratingKnown=!!ratingHidden;
  const clean=value=>String(value||'').replace(/\r\n/g,'\n').trim();
  const clearRatingControlCount=form?[...form.querySelectorAll('img[id^="star"],a,button,input,[role="button"],[data-rating],[data-value],[role="radio"]')].filter(node=>{
    const style=getComputedStyle(node); if(style.display==='none'||style.visibility==='hidden'||node.disabled)return false;
    const text=clean(node.value||node.textContent||node.title||node.getAttribute('aria-label'));
    const raw=String(node.getAttribute('data-rating')||node.getAttribute('data-value')||node.value||'');
    const explicitZeroId=/star[_-]?0$/i.test(String(node.id||''));
    return explicitZeroId||/^(0|none|null)$/i.test(raw)||/取消评分|清除评分|删除评分|不评分|无评分/.test(text);
  }).length:0;
  const clearRatingViaWishSupported=!!form?.querySelector('input[name="interest"][value="wish"]')&&
    !!form?.querySelector('input[name="rating"]')&&interestOptions.some(x=>x.value==='wish');
  const commentNode=form?.querySelector('textarea[name="comment"],textarea');
  const comment=String(commentNode?.value||'').trim();
  const commentKnown=!!commentNode;
  const action=form?.getAttribute('action')||'';
  let actionPath=''; try{actionPath=action?new URL(action,location.href).pathname:'';}catch{}
  const error=captcha?'豆瓣要求人工验证':loginPage?'豆瓣登录已失效':form?'':'官方编辑表单没有出现';
  return {ready:!!form,captcha,loginPage,error,actionPath,fields,ratingControlCount,clearRatingControlCount,clearRatingViaWishSupported,commentControlCount,interestOptions,selectedInterest,selectedInterestCount,semanticComplete,rating,ratingRaw,ratingKnown,comment,commentKnown,personalContainerFound:!!form,formCount:forms.length};
})()
""";
    internal const string DoubanWriteSubmitScriptV2 = """
(() => {
  const payload=__PAYLOAD__; const subjectId=String(payload.subjectId||'');
  const containerOf=form=>{const c=form.closest('.interest_sect_level,#interest_sect_level,#interest_sect,[class*="interest"]');return c&&!c.closest('#interest_sectl,.rating_wrap')?c:null;};
  const forms=[...document.querySelectorAll('form')].filter(form=>{
    const style=getComputedStyle(form); if(style.display==='none'||style.visibility==='hidden')return false;
    const c=containerOf(form); if(!c)return false;
    const action=form.getAttribute('action')||''; if(action&&subjectId&&!action.includes(subjectId))return false;
    return !!form.querySelector('textarea[name="comment"],input[name="rating"],input[type="submit"],button[type="submit"]') &&
      !/accounts\/login|passport\/login/.test(action);
  });
  const form=forms[0];
  if(!form)return {submitted:false,error:'官方编辑表单缺失'};
  let statusFallback=false;
  const action=form.getAttribute('action')||'';
  if(action){try{const u=new URL(action,location.href);if(u.origin!==location.origin)return {submitted:false,error:'拒绝跨域官方表单'};if(subjectId&&!u.pathname.includes(subjectId))return {submitted:false,error:'官方表单影片ID不匹配'};}catch{return {submitted:false,error:'官方表单地址无效'};}}
  const interests=[...form.querySelectorAll('input[name="interest"]')];
  const beforeInterest=interests.find(x=>x.checked===true)?.value || (interests.length===1&&interests[0].type==='hidden'?interests[0].value:'');
  if(interests.length){
    let target=interests.find(x=>String(x.value||'').toLowerCase()===String(payload.status||'').toLowerCase());
    const availableStatuses=interests.map(x=>String(x.value||'')).filter(Boolean);
    if(!target && interests.length===1 && interests[0].type==='hidden' &&
       String(interests[0].value||'').toLowerCase()===String(payload.status||'').toLowerCase()) target=interests[0];
    if(!target)return {submitted:false,error:'官方表单不提供请求状态',availableStatuses};
    if(target.type==='radio'||target.type==='checkbox'){
      target.click(); target.dispatchEvent(new Event('input',{bubbles:true})); target.dispatchEvent(new Event('change',{bubbles:true}));
    }
    if(String(target.value||'').toLowerCase()!==String(payload.status||'').toLowerCase() || ((target.type==='radio'||target.type==='checkbox')&&!target.checked))
      return {submitted:false,error:'官方状态控件未更新',availableStatuses};
  }
  const selectedBeforeSubmit=interests.find(x=>x.checked===true)?.value || (interests.length===1&&interests[0].type==='hidden'?interests[0].value:'');
  if(String(selectedBeforeSubmit||'').toLowerCase()!==String(payload.status||'').toLowerCase())
    return {submitted:false,error:'提交前状态未选中',requestedStatus:payload.status,beforeInterest,selectedBeforeSubmit};
  if(payload.rating!==null){
    const hidden=form.querySelector('input[name="rating"]');
    const scope=containerOf(form)||form;
    const ratingOf=node=>{
      const text=String(node.getAttribute?.('data-rating')||node.getAttribute?.('data-value')||node.getAttribute?.('value')||node.getAttribute?.('title')||node.id||node.textContent||'');
      const cls=String(node.className||'');
      const match=text.match(/(?:^|\D)([1-5])(?:\D|$)/)||cls.match(/(?:stars?|rating|allstar)[_-]?([1-5])(?:0|\b)/i);
      const n=Number(match?.[1]||0); return n>=1&&n<=5?n:0;
    };
    const candidates=[...scope.querySelectorAll('a,button,label,span,img[id^="star"],input[type="radio"],[role="radio"],[data-rating],[data-value],[id^="star"],[class*="star"],[class*="rating"]')]
      .filter(node=>node!==hidden&&getComputedStyle(node).display!=='none'&&getComputedStyle(node).visibility!=='hidden')
      .map(node=>({node,value:ratingOf(node)})).filter(x=>x.value>0);
    const rating=candidates.find(x=>x.value===Number(payload.rating));
    if(!rating)return {submitted:false,error:'官方评分控件缺失',candidateCount:candidates.length,candidates:candidates.map(x=>({tag:x.node.tagName,className:String(x.node.className||''),value:x.value}))};
    rating.node.click();
    rating.node.dispatchEvent(new Event('input',{bubbles:true})); rating.node.dispatchEvent(new Event('change',{bubbles:true}));
    const raw=String(hidden?.value||''); const parsed=Number(raw||0); const confirmed=parsed===Number(payload.rating)||parsed===Number(payload.rating)*10;
    if(!confirmed)return {submitted:false,error:'官方评分值未更新',candidateValue:rating.value,hiddenValuePresent:!!hidden,hiddenValueIsNumeric:Number.isFinite(parsed)};
  }
  if(payload.setComment){
    const comment=form.querySelector('textarea[name="comment"],textarea');
    if(!comment)return {submitted:false,error:'官方短评控件缺失'};
    const max=Number(comment.maxLength||0); if(max>0&&payload.comment.length>max)return {submitted:false,error:`comment exceeds maxlength ${max}`};
    comment.value=payload.comment;
    comment.dispatchEvent(new Event('input',{bubbles:true})); comment.dispatchEvent(new Event('change',{bubbles:true}));
  }
  let submit=form.querySelector('button[type="submit"],input[type="submit"],button.submit,.submit input');
  if(!submit){
    const replacement=[...document.querySelectorAll('form')].find(candidate=>{
      const style=getComputedStyle(candidate); if(style.display==='none'||style.visibility==='hidden')return false;
      const c=containerOf(candidate); if(!c)return false;
      const candidateAction=candidate.getAttribute('action')||'';
      return (!candidateAction||!subjectId||candidateAction.includes(subjectId)) && !!candidate.querySelector('textarea[name="comment"],input[name="rating"],input[type="submit"],button[type="submit"]');
    });
    submit=replacement?.querySelector('button[type="submit"],input[type="submit"],button.submit,.submit input')||null;
  }
  if(!submit)return {submitted:false,error:'官方提交按钮缺失',formConnected:!!form.isConnected,
    visibleForms:[...document.querySelectorAll('form')].map(candidate=>({action:candidate.getAttribute('action')||'',submitCount:candidate.querySelectorAll('button[type="submit"],input[type="submit"],button.submit,.submit input').length})).slice(0,5)};
  submit.click();
  return {submitted:true,statusFallback,actionPath:action?new URL(action,location.href).pathname:'',requestedStatus:payload.status,beforeInterest,selectedBeforeSubmit,statusChanged:String(beforeInterest||'').toLowerCase()!==String(payload.status||'').toLowerCase(),ratingChanged:payload.rating!==null,commentChanged:payload.setComment};
})()
""";
    internal const string DoubanWritePostSubmitProbeScript = """
(() => {
  const href=location.href||''; const body=document.body?.innerText||'';
  const captcha=href.includes('/misc/sorry')||document.title.includes('禁止访问')||
    !!document.querySelector('input[name="captcha-solution"],.captcha_image,#captcha_image')||
    /异常请求|请输入验证码|访问过于频繁|像机器人程序|点击证明/.test(body);
  const loginPage=href.includes('accounts.douban.com')||
    !!document.querySelector('form[action*="accounts/login"],form[action*="/passport/login"]');
  const subjectPage=/^https:\/\/movie\.douban\.com\/subject\/\d+\/?/.test(href);
  const formOpen=[...document.querySelectorAll('form')].some(form=>{
    const style=getComputedStyle(form);
    const container=form.closest('.interest_sect_level,#interest_sect_level,#interest_sect,[class*="interest"]');
    return style.display!=='none'&&style.visibility!=='hidden'&&!!container&&!container.closest('#interest_sectl,.rating_wrap')&&
      !!form.querySelector('input[name="interest"],input[name="rating"],textarea[name="comment"]');
  });
  return {href,readyState:document.readyState,subjectPage,formOpen,captcha,loginPage};
})()
""";
    internal const string DoubanHistoryContainsSubjectScript = """
(()=>{const id=__SUBJECT_ID__;const href=location.href||'';const body=document.body?.innerText||'';const captcha=href.includes('/misc/sorry')||/验证码|访问过于频繁|像机器人程序/.test(body);const loginPage=href.includes('accounts.douban.com')||!!document.querySelector('form[action*="accounts/login"],form[action*="/passport/login"]');const links=[...document.querySelectorAll('a[href*="/subject/"]')];const contains=links.some(x=>(x.href.match(/\/subject\/(\d+)/)||[])[1]===id);return {ready:!captcha&&!loginPage,contains,captcha,loginPage};})()
""";
    internal const string DoubanDeleteControlScript = """
(()=>{const personal=[...document.querySelectorAll('#interest_sect_level,.interest_sect_level,#interest_sect,[class*="interest"]')].find(x=>!x.closest('#interest_sectl,.rating_wrap'));const norm=v=>String(v||'').replace(/\s+/g,'').trim();const candidates=personal?[...personal.querySelectorAll('input,a,button')].filter(x=>norm(x.value||x.textContent)==='删除'&&/(^|\s)a_confirm_link(\s|$)/i.test(String(x.className||''))):[];if(candidates.length!==1)return {clicked:false,error:candidates.length===0?'官方删除控件缺失':'官方删除控件不唯一',candidateCount:candidates.length};const node=candidates[0];node.click();return {clicked:true,candidateCount:1};})()
""";
    private static string String(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)) return "";
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
    }
    internal static string ReadDiagnosticValue(JsonElement value, string name) => String(value, name);
    private static string String(JsonElement value, string name, string fallback) { var text = String(value, name); return string.IsNullOrWhiteSpace(text) ? fallback : text; }
    private static string ExtractSubjectId(string? url) => System.Text.RegularExpressions.Regex.Match(url ?? "", @"/subject/(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _browserLifetime.Cancel(); } catch { }
        _browserLifetime.Dispose();
        _navigationGate.Dispose();
        _initializationGate.Dispose();
    }
}
