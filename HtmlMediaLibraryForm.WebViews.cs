using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed partial class HtmlMediaLibraryForm
{
    private async Task EnsureDoubanPlusViewAsync()
    {
        if (_doubanPlusView.CoreWebView2 is not null) return;
        var probe = WebView2EnvironmentProvider.ProbeRuntime();
        if (!probe.Available) throw new InvalidOperationException("未检测到 WebView2 Evergreen Runtime。" + probe.Error);
        await _doubanPlusView.EnsureCoreWebView2Async(await _environments.GetDoubanEnvironmentAsync()).ConfigureAwait(true);
        var core = _doubanPlusView.CoreWebView2!;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping(
            WatchlistPosterHost,
            _watchlist.PostersDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.WebMessageReceived += OnDoubanPlusWebMessageReceived;
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                OnDoubanBrowserProcessFailed(e.ProcessFailedKind);
        };
        await DoubanPlusWebView2Script.InstallShellAsync(core).ConfigureAwait(true);
        await WatchlistWebView2Script.InstallAsync(core).ConfigureAwait(true);
        core.NavigateToString(DoubanPlusWebView2Script.GetShellDocument());
        DiagnosticLogger.Write($"WebView=DoubanShell; Initialized=True; VisibleListWebViewIsShell=True; Runtime={probe.Version}");
    }

    private async Task EnsureLegacyDoubanPlusViewAsync()
    {
        if (_doubanPlusView.CoreWebView2 is not null) return;
        var probe = WebView2EnvironmentProvider.ProbeRuntime();
        if (!probe.Available) throw new InvalidOperationException("未检测到 WebView2 Evergreen Runtime。" + probe.Error);
        await _doubanPlusView.EnsureCoreWebView2Async(await _environments.GetDoubanEnvironmentAsync()).ConfigureAwait(true);
        var core = _doubanPlusView.CoreWebView2!;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        DiagnosticLogger.Write($"WebView=DoubanPlus; WatchlistPosterMappingStart; Directory={_watchlist.PostersDirectory}");
        core.SetVirtualHostNameToFolderMapping(
            WatchlistPosterHost,
            _watchlist.PostersDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
        DiagnosticLogger.Write($"WebView=DoubanPlus; WatchlistPosterMappingReady; Host={WatchlistPosterHost}");
        core.WebMessageReceived += OnDoubanPlusWebMessageReceived;
        core.NavigationStarting += (_, e) =>
        {
            var allowed = DoubanWebView2Connector.IsAllowedDoubanTopLevel(e.Uri);
            DiagnosticLogger.Write($"WebView=DoubanPlus; NavigationStarting; Allowed={allowed}; TargetUrl={e.Uri}");
            if (!allowed) e.Cancel = true;
            else
            {
                var pendingHistoryReturnUrl = _pendingDoubanHistoryReturnUrl;
                if (!string.IsNullOrWhiteSpace(pendingHistoryReturnUrl))
                {
                    _pendingDoubanHistoryReturnUrl = "";
                    if (!AreEquivalentDoubanNavigationUrls(e.Uri, pendingHistoryReturnUrl))
                    {
                        e.Cancel = true;
                        DiagnosticLogger.Write($"WebView=DoubanPlus; HistoryReturnTargetMismatch=True; ExpectedUrl={pendingHistoryReturnUrl}; ActualUrl={e.Uri}; Fallback=Navigate");
                        try
                        {
                            BeginInvoke((Action)(() => NavigateDoubanPlusToUrl(pendingHistoryReturnUrl, "history-target-mismatch-fallback")));
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLogger.Write($"WebView=DoubanPlus; HistoryReturnFallbackScheduleFailed=True; ExpectedUrl={pendingHistoryReturnUrl}; Error={ex.Message}");
                        }
                        return;
                    }

                    DiagnosticLogger.Write($"WebView=DoubanPlus; HistoryReturnTargetMatched=True; Url={e.Uri}");
                }

                var previousDoubanUrl = _activeDoubanPlusNavigationUrl;
                var previousDoubanViewVisible = _doubanPlusView.Visible;
                var isListToListNavigation = IsDoubanPlusListPageUrl(previousDoubanUrl) && IsDoubanPlusListPageUrl(e.Uri);
                if (IsDoubanSubjectPageUrl(e.Uri) && IsDoubanPlusListPageUrl(previousDoubanUrl))
                    _activeDoubanReturnUrl = previousDoubanUrl;
                var isRecoveryNavigation = AreEquivalentDoubanNavigationUrls(_doubanPlusRecoveryUrl, e.Uri);
                if (isRecoveryNavigation)
                    _doubanPlusRecoveryUrl = "";
                else
                    _doubanPlusRecoveryAttempts = 0;
                _pendingDoubanPlusNavigationId = e.NavigationId;
                _doubanPlusShownNavigationId = 0;
                _activeDoubanPlusNavigationUrl = e.Uri;
                if (IsAllowedDoubanPersonalUrl(e.Uri)) _activeDoubanPersonalPageUrl = e.Uri;
                _returnToLibraryButton.Visible = false;
                if (!string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
                {
                    // Keep the old document from exposing the native Douban page while the
                    // new document is still mounting the Douban Plus root.
                    _doubanPlusView.Visible = false;
                    ShowDoubanNavigationOverlay("正在切换豆瓣页面…");
                }
                DiagnosticLogger.Write($"WebView=DoubanPlus; PreviousDocumentHidden={!previousDoubanViewVisible}; NavigationOverlay={!string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase) && !isListToListNavigation}; ListToListNavigation={isListToListNavigation}; NavigationId={e.NavigationId}; PreviousUrl={previousDoubanUrl}; ReturnUrl={_activeDoubanReturnUrl}; TargetUrl={e.Uri}");
            }
        };
        core.DOMContentLoaded += async (_, e) =>
        {
            if (e.NavigationId != _pendingDoubanPlusNavigationId ||
                !IsDoubanPlusListPageUrl(_activeDoubanPlusNavigationUrl)) return;

            var stable = await WaitForStableDoubanPlusRootAsync(e.NavigationId).ConfigureAwait(true);
            var mounted = stable || await ProbeDoubanPlusPageAsync().ConfigureAwait(true);
            if (!stable || e.NavigationId != _pendingDoubanPlusNavigationId) return;

            _doubanPlusShownNavigationId = e.NavigationId;
            _doubanPlusRecoveryAttempts = 0;
            HideDoubanNavigationOverlay();
            _doubanPlusView.Visible = true;
            _doubanPlusView.BringToFront();
            BringDoubanAccountBarToFront();
            _returnToLibraryButton.Text = DoubanReturnButtonText();
            _returnToLibraryButton.Visible = CanShowDoubanPlusReturnButton();
            if (_returnToLibraryButton.Visible) _returnToLibraryButton.BringToFront();
            DiagnosticLogger.Write($"WebView=DoubanPlus; CurrentDocumentShown=True; StableRender=True; NavigationId={e.NavigationId}; RootMounted=True; Phase=DOMContentLoaded; TargetUrl={_activeDoubanPlusNavigationUrl}");
        };
        core.NavigationCompleted += async (_, e) =>
        {
            DiagnosticLogger.Write($"WebView=DoubanPlus; NavigationCompleted; IsSuccess={e.IsSuccess}; WebErrorStatus={e.WebErrorStatus}; TargetUrl={_doubanPlusView.Source}");
            if (e.NavigationId != _pendingDoubanPlusNavigationId) return;
            if (e.NavigationId == _doubanPlusShownNavigationId)
            {
                DiagnosticLogger.Write($"WebView=DoubanPlus; NavigationCompletedAfterEarlyShow=True; NavigationId={e.NavigationId}; TargetUrl={_activeDoubanPlusNavigationUrl}");
                return;
            }
            var stable = e.IsSuccess && await WaitForStableDoubanPlusRootAsync(e.NavigationId).ConfigureAwait(true);
            var mounted = stable || await ProbeDoubanPlusPageAsync().ConfigureAwait(true);
            if (stable && e.NavigationId == _pendingDoubanPlusNavigationId && !string.IsNullOrWhiteSpace(_activeDoubanPlusNavigationUrl))
            {
                _doubanPlusRecoveryAttempts = 0;
                HideDoubanNavigationOverlay();
                _doubanPlusView.Visible = true;
                _doubanPlusView.BringToFront();
                BringDoubanAccountBarToFront();
                _returnToLibraryButton.Text = DoubanReturnButtonText();
                _returnToLibraryButton.Visible = CanShowDoubanPlusReturnButton();
                if (_returnToLibraryButton.Visible) _returnToLibraryButton.BringToFront();
                DiagnosticLogger.Write($"WebView=DoubanPlus; CurrentDocumentShown=True; StableRender=True; NavigationId={e.NavigationId}; RootMounted=True; SubjectId={_activeDetailSubjectId}");
            }
            else if (!string.IsNullOrWhiteSpace(_activeDoubanPlusNavigationUrl))
            {
                var targetUrl = _activeDoubanPlusNavigationUrl;
                if (_doubanPlusRecoveryAttempts < 1)
                {
                    _doubanPlusRecoveryAttempts++;
                    _doubanPlusRecoveryUrl = targetUrl;
                    ShowDoubanNavigationOverlay(IsAllowedDoubanExploreOrTvUrl(targetUrl) ? "探索页面正在恢复，请稍候…" : "页面内容正在重新加载…");
                    DiagnosticLogger.Write($"WebView=DoubanPlus; ContentRecoveryScheduled=True; NavigationId={e.NavigationId}; Attempt={_doubanPlusRecoveryAttempts}; Url={targetUrl}; Stable={stable}; Mounted={mounted}; ExploreWait={IsAllowedDoubanExploreOrTvUrl(targetUrl)}");
                    _ = RetryDoubanPlusNavigationAsync(targetUrl, e.NavigationId);
                }
                else
                {
                    ShowDoubanNavigationOverlay(IsAllowedDoubanExploreOrTvUrl(targetUrl) ? "探索页面恢复失败，请右键刷新页面。" : "豆瓣页面内容加载失败，请重新搜索。");
                    DiagnosticLogger.Write($"WebView=DoubanPlus; CurrentDocumentShown=False; StableRender=False; ContentRecoveryExhausted=True; NavigationId={e.NavigationId}; Attempt={_doubanPlusRecoveryAttempts}; RootMounted={mounted}; SubjectId={_activeDetailSubjectId}");
                }
            }
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (DoubanWebView2Connector.IsAllowedDoubanTopLevel(e.Uri)) core.Navigate(e.Uri);
        };
        core.ProcessFailed += (_, e) =>
        {
            var browserExited = e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited;
            DiagnosticLogger.Write($"WebView=DoubanPlus; WebView2 process failed; Kind={e.ProcessFailedKind}; BrowserExited={browserExited}; Recovery={(browserExited ? "delegated-to-host" : "not-required")}");
            if (browserExited) OnDoubanBrowserProcessFailed(e.ProcessFailedKind);
        };
        await DoubanPlusWebView2Script.InstallAsync(core).ConfigureAwait(true);
        await WatchlistWebView2Script.InstallAsync(core).ConfigureAwait(true);
        DiagnosticLogger.Write($"WebView=DoubanPlus; WebView2 initialized; Runtime={probe.Version}; Profile={_environments.DoubanProfileDirectory}; ScriptVersion={DoubanPlusWebView2Script.Version}; SourceCommit={DoubanPlusWebView2Script.SourceCommit}");
    }

    private async Task EnsureDoubanSourceViewAsync()
    {
        if (_doubanSourceView.CoreWebView2 is not null) return;
        var probe = WebView2EnvironmentProvider.ProbeRuntime();
        if (!probe.Available) throw new InvalidOperationException("未检测到 WebView2 Evergreen Runtime。" + probe.Error);
        await _doubanSourceView.EnsureCoreWebView2Async(await _environments.GetDoubanEnvironmentAsync()).ConfigureAwait(true);
        var core = _doubanSourceView.CoreWebView2!;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.NavigationStarting += (_, e) =>
        {
            var allowed = IsAllowedDoubanSourceUrl(e.Uri);
            _doubanSourceNavigationCompleted = false;
            Interlocked.Increment(ref _doubanSourceNavigationVersion);
            if (!allowed) e.Cancel = true;
            DiagnosticLogger.Write($"WebView=DoubanSource; NavigationStarting; Allowed={allowed}; TargetUrl={e.Uri}");
        };
        core.NavigationCompleted += async (_, e) =>
        {
            DiagnosticLogger.Write($"WebView=DoubanSource; NavigationCompleted; IsSuccess={e.IsSuccess}; WebErrorStatus={e.WebErrorStatus}; TargetUrl={core.Source}");
            if (_frodoPersonalActive)
            {
                _doubanSourceNavigationCompleted = false;
                DiagnosticLogger.Write($"WebView=DoubanSource; NavigationCompleted ignored; Reason=FrodoPersonalActive; TargetUrl={core.Source}");
                return;
            }
            _doubanSourceNavigationCompleted = e.IsSuccess && IsAllowedDoubanSourceUrl(core.Source);
            if (_doubanSourceNavigationCompleted)
                await RequestDoubanSourceReadAsync("navigation-completed").ConfigureAwait(true);
        };
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                OnDoubanBrowserProcessFailed(e.ProcessFailedKind);
        };
        await DoubanPlusWebView2Script.InstallSourceBridgeAsync(core).ConfigureAwait(true);
        DiagnosticLogger.Write($"WebView=DoubanSource; Initialized=True; Visible=False; Profile={_environments.DoubanProfileDirectory}");
    }

    private async Task EnsureDoubanLoginViewAsync()
    {
        if (_doubanLoginView.CoreWebView2 is not null) return;
        var probe = WebView2EnvironmentProvider.ProbeRuntime();
        if (!probe.Available) throw new InvalidOperationException("未检测到 WebView2 Evergreen Runtime。" + probe.Error);
        await _doubanLoginView.EnsureCoreWebView2Async(await _environments.GetDoubanEnvironmentAsync()).ConfigureAwait(true);
        var core = _doubanLoginView.CoreWebView2!;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.NavigationStarting += (_, e) =>
        {
            var allowed = DoubanWebView2Connector.IsAllowedDoubanTopLevel(e.Uri);
            DiagnosticLogger.Write($"WebView=DoubanLoginInline; NavigationStarting; Allowed={allowed}; TargetUrl={e.Uri}");
            if (!allowed) e.Cancel = true;
        };
        core.NavigationCompleted += (_, e) =>
        {
            _doubanLoginStatus.Text = e.IsSuccess
                ? "请使用豆瓣 App 扫码，完成后点击“验证登录”。"
                : "扫码页面加载失败，请点击“返回”后重新打开。";
            DiagnosticLogger.Write($"WebView=DoubanLoginInline; NavigationCompleted; IsSuccess={e.IsSuccess}; WebErrorStatus={e.WebErrorStatus}; TargetUrl={_doubanLoginView.Source}");
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (DoubanWebView2Connector.IsAllowedDoubanTopLevel(e.Uri)) core.Navigate(e.Uri);
        };
        core.ProcessFailed += (_, e) =>
        {
            DiagnosticLogger.Write($"WebView=DoubanLoginInline; WebView2 process failed; Kind={e.ProcessFailedKind}");
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                _doubanLoginStatus.Text = "登录页面所在浏览器进程已退出，请返回后重新打开。";
        };
    }

    private async Task EnsureDoubanSubjectViewAsync()
    {
        if (_doubanSubjectView.CoreWebView2 is not null) return;
        var probe = WebView2EnvironmentProvider.ProbeRuntime();
        if (!probe.Available) throw new InvalidOperationException("未检测到 WebView2 Evergreen Runtime。" + probe.Error);
        await _doubanSubjectView.EnsureCoreWebView2Async(await _environments.GetDoubanEnvironmentAsync()).ConfigureAwait(true);
        var core = _doubanSubjectView.CoreWebView2!;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping(
            WatchlistPosterHost,
            _watchlist.PostersDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.WebMessageReceived += OnDoubanSubjectWebMessageReceived;
        core.NavigationStarting += (_, e) =>
        {
            var allowed = DoubanWebView2Connector.IsAllowedDoubanTopLevel(e.Uri);
            DiagnosticLogger.Write($"WebView=DoubanSubject; NavigationStarting; Allowed={allowed}; TargetUrl={e.Uri}");
            if (!allowed)
            {
                e.Cancel = true;
                return;
            }

            _pendingDoubanSubjectNavigationId = e.NavigationId;
            _doubanSubjectShownNavigationId = 0;
            _activeDoubanSubjectNavigationUrl = e.Uri;
            _doubanSubjectRecoveryAttempts = 0;
            _doubanSubjectRecoveryUrl = "";
            _returnToLibraryButton.Visible = false;
            if (!string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
                ShowDoubanNavigationOverlay("正在打开影片详情…");
        };
        core.NavigationCompleted += async (_, e) =>
        {
            DiagnosticLogger.Write($"WebView=DoubanSubject; NavigationCompleted; IsSuccess={e.IsSuccess}; WebErrorStatus={e.WebErrorStatus}; TargetUrl={_activeDoubanSubjectNavigationUrl}");
            if (e.NavigationId != _pendingDoubanSubjectNavigationId) return;
            if (e.NavigationId == _doubanSubjectShownNavigationId) return;

            var stable = e.IsSuccess && await HasStableDoubanSubjectRootAsync().ConfigureAwait(true);
            if (stable && e.NavigationId == _pendingDoubanSubjectNavigationId)
            {
                _doubanSubjectShownNavigationId = e.NavigationId;
                _doubanSubjectRecoveryAttempts = 0;
                HideDoubanNavigationOverlay();
                _doubanPlusView.Visible = false;
                _doubanSubjectView.Visible = true;
                _doubanSubjectView.BringToFront();
                _returnToLibraryButton.Text = "返回";
                _returnToLibraryButton.Visible = true;
                _returnToLibraryButton.BringToFront();
                DiagnosticLogger.Write($"WebView=DoubanSubject; CurrentDocumentShown=True; StableRender=True; NavigationId={e.NavigationId}; SubjectId={_activeDetailSubjectId}");
                return;
            }

            if (_doubanSubjectRecoveryAttempts < 1 && IsDoubanPlusEnhancedPageUrl(_activeDoubanSubjectNavigationUrl))
            {
                _doubanSubjectRecoveryAttempts++;
                _doubanSubjectRecoveryUrl = _activeDoubanSubjectNavigationUrl;
                ShowDoubanNavigationOverlay("豆瓣页面正在恢复，请稍候…");
                _ = RetryDoubanSubjectNavigationAsync(_doubanSubjectRecoveryUrl, e.NavigationId);
            }
            else
            {
                ShowDoubanNavigationOverlay("豆瓣页面加载失败，请右键刷新或返回列表。");
                DiagnosticLogger.Write($"WebView=DoubanSubject; CurrentDocumentShown=False; StableRender=False; ContentRecoveryExhausted=True; NavigationId={e.NavigationId}");
            }
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (DoubanWebView2Connector.IsAllowedDoubanTopLevel(e.Uri)) core.Navigate(e.Uri);
        };
        core.ProcessFailed += (_, e) =>
        {
            var browserExited = e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited;
            DiagnosticLogger.Write($"WebView=DoubanSubject; WebView2 process failed; Kind={e.ProcessFailedKind}; BrowserExited={browserExited}; Recovery={(browserExited ? "delegated-to-host" : "not-required")}");
            if (browserExited) OnDoubanBrowserProcessFailed(e.ProcessFailedKind);
        };
        await DoubanPlusWebView2Script.InstallAsync(core).ConfigureAwait(true);
        await WatchlistWebView2Script.InstallAsync(core).ConfigureAwait(true);
        DiagnosticLogger.Write($"WebView=DoubanSubject; WebView2 initialized; Runtime={probe.Version}; Profile={_environments.DoubanProfileDirectory}; ScriptVersion={DoubanPlusWebView2Script.Version}; SharedEnvironment=True");
    }

    private async Task<bool> HasStableDoubanSubjectRootAsync()
    {
        try
        {
            var result = await _doubanSubjectView.CoreWebView2.ExecuteScriptAsync("""
(() => {
  const root = document.querySelector("#atv-douban-root");
  const wrapper = document.querySelector("#wrapper");
  const probe = window.__qbDoubanPlusProbe || {};
  const rect = root?.getBoundingClientRect();
  const style = root ? getComputedStyle(root) : null;
  const isSubject = location.hostname === "movie.douban.com" && /^\/subject\/\d+\/?$/u.test(location.pathname);
  const isCelebrities = location.hostname === "movie.douban.com" && /^\/subject\/\d+\/celebrities\/?$/u.test(location.pathname);
  const isPersonage = location.hostname === "www.douban.com" && /^\/personage\/\d+\/?$/u.test(location.pathname);
  const contentReady = isSubject
    ? root?.querySelector(".atv-hero-title")
    : isCelebrities
      ? root?.querySelector(".atv-celebrities-hero h1, .atv-credit-groups")
      : isPersonage
        ? root?.querySelector(".atv-personage-hero, .atv-personage")
        : root?.querySelector(".atv-hero-title");
  const ready = Boolean(root && document.body && probe.importResolved === true && probe.importRejected !== true &&
    document.body.classList.contains("atv-enhanced") && (!wrapper || getComputedStyle(wrapper).display === "none") &&
    style && style.display !== "none" && style.visibility !== "hidden" && Number(style.opacity) >= 0.98 &&
    rect && rect.width > 0 && rect.height > 0 && contentReady);
  return { ready, href: location.href, isCelebrities, isPersonage, isSubject, importResolved: probe.importResolved === true };
})()
""").ConfigureAwait(true);
            using var document = JsonDocument.Parse(result);
            return document.RootElement.TryGetProperty("ready", out var ready) && ready.GetBoolean();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView=DoubanSubject; StableRenderProbeFailed={ex.Message}");
            return false;
        }
    }

    private async Task RetryDoubanSubjectNavigationAsync(string url, ulong previousNavigationId)
    {
        try
        {
            await Task.Delay(300).ConfigureAwait(true);
            if (_closing || _pendingDoubanSubjectNavigationId != previousNavigationId ||
                !AreEquivalentDoubanNavigationUrls(_activeDoubanSubjectNavigationUrl, url)) return;
            _doubanSubjectView.CoreWebView2?.Navigate(url);
            DiagnosticLogger.Write($"WebView=DoubanSubject; ContentRecoveryRetry=True; Url={url}");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView=DoubanSubject; ContentRecoveryFailed=True; Url={url}; Error={ex.Message}");
        }
    }

    private async void OnDoubanPlusWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) =>
        await HandleDoubanPlusWebMessageReceivedAsync(_doubanPlusView, e).ConfigureAwait(true);

    private async void OnDoubanSourceWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) =>
        await HandleDoubanSourceWebMessageReceivedAsync(e).ConfigureAwait(true);

    private async void OnDoubanSubjectWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) =>
        await HandleDoubanPlusWebMessageReceivedAsync(_doubanSubjectView, e).ConfigureAwait(true);

    private async Task HandleDoubanSourceWebMessageReceivedAsync(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (!IsAllowedDoubanSourceUrl(e.Source)) return;
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var type = ReadString(root, "type");
            if (type == "doubanSourceReady")
            {
                await RequestDoubanSourceReadAsync("source-ready").ConfigureAwait(true);
                return;
            }
            if (type != "doubanSourceResult") return;

            var sourceUrl = ReadString(root, "url");
            if (!IsAllowedDoubanSourceUrl(sourceUrl)) throw new InvalidDataException("Source 返回的豆瓣列表地址无效。");
            // readPage returns the same JSON synchronously to its C# caller. The
            // bridge also posts it for diagnostics; the host-owned request is
            // the single forwarding path, otherwise one navigation can render
            // and materialize posters twice.
            DiagnosticLogger.Write($"Unified Shell Source result event ignored; Url={sourceUrl}; RequestId={ReadString(root, "requestId")}; Reason=host-owned-read");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"Unified Shell Source message failed; Error={ex}");
            _pendingShellDataJson = JsonSerializer.Serialize(new { type = "doubanShellData", error = ex.Message, items = Array.Empty<object>() });
            PostPendingShellDataIfReady();
        }
    }

    private async Task RequestDoubanSourceReadAsync(string reason)
    {
        await _doubanSourceReadGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_frodoPersonalActive || _doubanSourceView.CoreWebView2 is null || !_doubanSourceNavigationCompleted ||
                !IsAllowedDoubanSourceUrl(_activeDoubanSourceNavigationUrl))
            {
                DiagnosticLogger.Write($"Unified Shell Source read skipped; Reason={reason}; NavigationCompleted={_doubanSourceNavigationCompleted}; Source={_doubanSourceView.CoreWebView2?.Source ?? "<none>"}");
                return;
            }

            var navigationVersion = Volatile.Read(ref _doubanSourceNavigationVersion);
            if (navigationVersion == _doubanSourceReadScheduledVersion)
            {
                DiagnosticLogger.Write($"Unified Shell Source read coalesced; Reason={reason}; NavigationVersion={navigationVersion}; Url={_activeDoubanSourceNavigationUrl}");
                return;
            }
            _doubanSourceReadScheduledVersion = navigationVersion;

            var sourceUrl = _activeDoubanSourceNavigationUrl;
            var generation = Interlocked.Increment(ref _doubanSourceGeneration);
            var mode = DoubanSourceModeForUrl(sourceUrl);
            var forwardedRequestId = _pendingDoubanSourceReadRequestId;
            var forwardedOperation = _pendingDoubanSourceReadOperation;
            var requestId = string.IsNullOrWhiteSpace(forwardedRequestId) ? $"{mode}-{generation}" : forwardedRequestId;
            var request = JsonSerializer.Serialize(new { requestId, mode, generation });
            var core = _doubanSourceView.CoreWebView2;
            var personalSource = IsAllowedDoubanPersonalUrl(sourceUrl);
            var bridgeName = personalSource ? "QbDoubanPersonalSourceBridge" : "QbDoubanSourceBridge";
            var bridgeProbe = await core.ExecuteScriptAsync($"({{ present: typeof window.{bridgeName} === 'object' && typeof window.{bridgeName}.readPage === 'function', href: location.href, readyState: document.readyState }})").ConfigureAwait(true);
            if (bridgeProbe.Contains("\"present\":false", StringComparison.Ordinal))
            {
                DiagnosticLogger.Write($"Unified Shell Source bridge missing; Reason={reason}; Probe={bridgeProbe}; Action=ExecuteScriptFallback");
                var bridgeScript = personalSource
                    ? DoubanPlusWebView2Script.GetPersonalSourceBridgeScript()
                    : DoubanPlusWebView2Script.GetSourceBridgeScript();
                await core.ExecuteScriptAsync(bridgeScript).ConfigureAwait(true);
                bridgeProbe = await core.ExecuteScriptAsync($"({{ present: typeof window.{bridgeName} === 'object' && typeof window.{bridgeName}.readPage === 'function', href: location.href, readyState: document.readyState }})").ConfigureAwait(true);
            }

            var script = $"window.{bridgeName} && window.{bridgeName}.readPage({request});";
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            var attempts = 0;
            var timedOut = false;
            JsonElement resultRoot = default;
            string readResult = "";
            while (true)
            {
                attempts++;
                readResult = await core.ExecuteScriptAsync(script).ConfigureAwait(true);
                using (var resultDocument = JsonDocument.Parse(readResult))
                {
                    resultRoot = resultDocument.RootElement.Clone();
                }
                if (resultRoot.ValueKind == JsonValueKind.String)
                {
                    using var nestedDocument = JsonDocument.Parse(resultRoot.GetString() ?? "{}");
                    resultRoot = nestedDocument.RootElement.Clone();
                }

                var itemCount = resultRoot.ValueKind == JsonValueKind.Object &&
                                resultRoot.TryGetProperty("items", out var items) &&
                                items.ValueKind == JsonValueKind.Array
                    ? items.GetArrayLength()
                    : 0;
                var pageReady = resultRoot.ValueKind == JsonValueKind.Object && ReadBool(resultRoot, "pageReady");
                var searchSource = IsDoubanSearchPageUrl(sourceUrl);
                if (itemCount > 0 || ((personalSource || searchSource) && pageReady)) break;

                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt);
                if (elapsed >= DoubanSourceDomWaitTimeout)
                {
                    timedOut = true;
                    break;
                }

                await Task.Delay(DoubanSourceDomPollInterval).ConfigureAwait(true);
            }

            if (navigationVersion != Volatile.Read(ref _doubanSourceNavigationVersion) ||
                !AreEquivalentDoubanNavigationUrls(sourceUrl, _activeDoubanSourceNavigationUrl))
            {
                DiagnosticLogger.Write($"Unified Shell Source read dropped stale result; Reason={reason}; NavigationVersion={navigationVersion}; CurrentVersion={_doubanSourceNavigationVersion}; Url={sourceUrl}; CurrentUrl={_activeDoubanSourceNavigationUrl}");
                return;
            }

            await ForwardDoubanSourceResultToShellAsync(resultRoot, forwardedOperation).ConfigureAwait(true);
            if (string.Equals(_pendingDoubanSourceReadRequestId, forwardedRequestId, StringComparison.Ordinal))
            {
                _pendingDoubanSourceReadRequestId = "";
                _pendingDoubanSourceReadOperation = "";
            }
            var waitDuration = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt);
            DiagnosticLogger.Write($"Unified Shell Source read completed; Reason={reason}; Generation={generation}; Attempts={attempts}; WaitMs={waitDuration.TotalMilliseconds:0}; TimedOut={timedOut}; Url={sourceUrl}; BridgeProbe={bridgeProbe}; ReadResult={readResult}");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"Unified Shell Source read failed; Reason={reason}; Error={ex}");
            PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "source-read" });
            PostShellMessage(new { type = "doubanShellFilterError", error = ex.Message });
        }
        finally
        {
            _doubanSourceReadGate.Release();
        }
    }

}

