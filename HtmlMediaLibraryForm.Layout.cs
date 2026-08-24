using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed partial class HtmlMediaLibraryForm
{
    private static WebView2 CreateDetailWebView() =>
        new() { Size = new Size(1, 1), Location = new Point(-20, -20), TabStop = false };

    private static WebView2 CreateWorkerWebView() =>
        new() { Size = new Size(1024, 768), Location = new Point(-5000, -5000), TabStop = false };

    private static WebView2 CreateDoubanPlusView() =>
        new() { Dock = DockStyle.Fill, Visible = false, TabStop = false };

    private static WebView2 CreateDoubanSourceView() =>
        new() { Size = new Size(1, 1), Location = new Point(-10000, -10000), Visible = false, TabStop = false };

    private static WebView2 CreateDoubanSubjectView() =>
        new() { Dock = DockStyle.Fill, Visible = false, TabStop = false };

    private static WebView2 CreateDoubanLoginView() =>
        new() { Dock = DockStyle.Fill, TabStop = true };

    private static Panel CreateDoubanAccountBar() => new()
    {
        Size = new Size(300, 40),
        Location = new Point(860, 8),
        Anchor = AnchorStyles.Top | AnchorStyles.Right,
        BackColor = Color.FromArgb(23, 26, 31),
        BorderStyle = BorderStyle.FixedSingle,
        Visible = false,
        TabStop = false
    };

    private static Label CreateDoubanAccountStatus() => new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        Padding = new Padding(10, 0, 4, 0),
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(174, 182, 194),
        BackColor = Color.Transparent,
        Text = "豆瓣：待验证"
    };

    private static Button CreateDoubanLoginButton() => new()
    {
        Dock = DockStyle.Right,
        Width = 92,
        Text = "扫码登录",
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(240, 179, 79),
        ForeColor = Color.FromArgb(16, 20, 24),
        UseVisualStyleBackColor = false,
        Cursor = Cursors.Hand,
        TabStop = true
    };

    private static Panel CreateDoubanLoginPanel() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(11, 14, 18),
        Visible = false,
        TabStop = true
    };

    private static Label CreateDoubanLoginStatus() => new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        Padding = new Padding(4, 0, 12, 0),
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(174, 182, 194),
        Text = "请使用豆瓣 App 扫码，完成后点击“验证登录”。"
    };

    private static Button CreateDoubanLoginVerifyButton() => new()
    {
        Dock = DockStyle.Right,
        Width = 116,
        Text = "验证登录",
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(240, 179, 79),
        ForeColor = Color.FromArgb(16, 20, 24),
        UseVisualStyleBackColor = false,
        Cursor = Cursors.Hand
    };

    private static Button CreateDoubanLoginCancelButton() => new()
    {
        Dock = DockStyle.Right,
        Width = 82,
        Text = "返回",
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(37, 44, 52),
        ForeColor = Color.White,
        UseVisualStyleBackColor = false,
        Cursor = Cursors.Hand
    };

    private static Panel CreateDoubanNavigationOverlay()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(11, 14, 18),
            Visible = false,
            TabStop = true
        };
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = "正在切换豆瓣页面…",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(174, 182, 194),
            BackColor = Color.FromArgb(11, 14, 18),
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular),
            Padding = new Padding(24)
        };
        panel.Controls.Add(label);
        panel.Tag = label;
        return panel;
    }

    private void ConfigureDoubanNavigationContextMenu()
    {
        var refresh = new ToolStripMenuItem("刷新页面");
        refresh.Click += (_, _) => RefreshDoubanPlusPage();
        var home = new ToolStripMenuItem("返回首页");
        home.Click += async (_, _) => await NavigateDoubanHomeAsync().ConfigureAwait(true);
        _doubanNavigationContextMenu.Items.Add(refresh);
        _doubanNavigationContextMenu.Items.Add(home);
        _doubanNavigationOverlay.ContextMenuStrip = _doubanNavigationContextMenu;
        if (_doubanNavigationOverlay.Tag is Control child) child.ContextMenuStrip = _doubanNavigationContextMenu;
    }

    private void AttachDoubanViewMouseHandlers()
    {
        _doubanPlusView.MouseUp += OnDoubanViewMouseUp;
        _doubanSubjectView.MouseUp += OnDoubanViewMouseUp;
    }

    private void OnDoubanViewMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || sender is not Control view) return;
        _doubanNavigationContextMenu.Show(view, e.Location);
    }

    private void RefreshDoubanPlusPage()
    {
        if (_closing) return;
        _pendingDoubanHistoryReturnUrl = "";
        if (_doubanSubjectView.Visible)
        {
            DiagnosticLogger.Write($"WebView=DoubanSubject; OverlayRefreshRequested=True; Url={_doubanSubjectView.Source}");
            _doubanSubjectView.CoreWebView2?.Reload();
            return;
        }

        if (_doubanPlusView.Visible && string.Equals(_activeShellViewKind, "watchlist", StringComparison.Ordinal))
        {
            DiagnosticLogger.Write("WebView=DoubanShell; OverlayRefreshRequested=True; ViewKind=watchlist; Action=ReloadLocalWatchlist");
            PostShellMessage(new { type = "doubanShellWatchlistRefresh" });
            return;
        }

        if (_doubanPlusView.Visible && _frodoPersonalActive && IsAllowedDoubanPersonalUrl(_activeDoubanSourceNavigationUrl))
        {
            _ = RefreshFrodoPersonalAsync("overlay-refresh");
            return;
        }

        if (_doubanPlusView.Visible && _doubanSourceView.CoreWebView2 is not null &&
            _doubanSourceNavigationCompleted && IsAllowedDoubanSourceUrl(_activeDoubanSourceNavigationUrl))
        {
            _doubanSourceReadScheduledVersion = -1;
            DiagnosticLogger.Write($"WebView=DoubanSource; OverlayRefreshRequested=True; Url={_activeDoubanSourceNavigationUrl}; Action=ReloadRealSource");
            _doubanSourceView.CoreWebView2.Reload();
            return;
        }

        DiagnosticLogger.Write($"WebView=DoubanShell; OverlayRefreshRequested=True; Url={_doubanPlusView.Source}; Action=ReloadShellFallback");
        _doubanPlusView.CoreWebView2?.Reload();
    }

    private async Task NavigateDoubanHomeAsync()
    {
        if (_closing) return;
        var homeUrl = _activeDoubanPersonalPageUrl;
        if (IsAllowedDoubanPersonalUrl(homeUrl))
        {
            var profileMatch = System.Text.RegularExpressions.Regex.Match(homeUrl, @"^https://movie\.douban\.com/people/(\d+)/(?:collect|wish|do)/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (profileMatch.Success) homeUrl = $"https://movie.douban.com/people/{profileMatch.Groups[1].Value}/collect";
        }
        if (!IsAllowedDoubanPersonalUrl(homeUrl))
        {
            var session = await _workerConnector.GetSessionStatusAsync().ConfigureAwait(true);
            if (session.IsLoggedIn && System.Text.RegularExpressions.Regex.IsMatch(session.ProfileId ?? "", "^\\d+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                homeUrl = $"https://movie.douban.com/people/{session.ProfileId}/collect";
        }
        if (!IsAllowedDoubanPersonalUrl(homeUrl))
        {
            DiagnosticLogger.Write("WebView=DoubanPlus; OverlayHomeRequested=True; Result=NoAuthenticatedPersonalPage");
            return;
        }
        _activeDoubanPersonalPageUrl = homeUrl;
        _activeDoubanReturnUrl = "";
        _pendingDoubanHistoryReturnUrl = "";
        NavigateDoubanPlusToUrl(homeUrl, "overlay-home");
        DiagnosticLogger.Write($"WebView=DoubanPlus; OverlayHomeRequested=True; Url={homeUrl}");
    }

    private void ShowDoubanNavigationOverlay(string message)
    {
        if (_doubanNavigationOverlay.Tag is Label label) label.Text = message;
        _doubanNavigationOverlay.Visible = true;
        _doubanNavigationOverlay.BringToFront();
        if (!_doubanLoginPanel.Visible && !_closing)
        {
            _doubanAccountBar.Visible = true;
            _doubanAccountBar.BringToFront();
        }
    }

    private void HideDoubanNavigationOverlay()
    {
        _doubanNavigationOverlay.Visible = false;
        if (!_doubanLoginPanel.Visible && !_closing)
        {
            _doubanAccountBar.Visible = true;
            _doubanAccountBar.BringToFront();
        }
    }

    private static TaskCompletionSource<bool> CompletedReadySignal()
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult(true);
        return signal;
    }

    private void CreateDoubanConnectors()
    {
        _detailConnector = new DoubanWebView2Connector(_detailView, _environments, "Detail");
        _workerConnector = new DoubanWebView2Connector(_workerView, _environments, "Worker");
        _connector = new DoubanConnectorRouter(_workerConnector, _cdp, _preferredBrowser);
        _detailConnector.BrowserProcessFailed += OnDoubanBrowserProcessFailed;
        _workerConnector.BrowserProcessFailed += OnDoubanBrowserProcessFailed;
        _workerConnector.SessionStatusChanged += OnDoubanSessionStatusChanged;
    }

    private void DetachDoubanConnectorEvents()
    {
        _detailConnector.BrowserProcessFailed -= OnDoubanBrowserProcessFailed;
        _workerConnector.BrowserProcessFailed -= OnDoubanBrowserProcessFailed;
        _workerConnector.SessionStatusChanged -= OnDoubanSessionStatusChanged;
    }

    private void OnDoubanSessionStatusChanged(DoubanSessionStatus status)
    {
        if (_closing || IsDisposed) return;
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => OnDoubanSessionStatusChanged(status)));
                return;
            }
            _doubanAccountStatus.Text = status.Text;
            if (_doubanLoginPanel.Visible && !status.IsLoggedIn)
                _doubanLoginStatus.Text = status.State == "connection-error" ? status.Text + "，请稍后重试。" : status.Text;
        }
        catch (InvalidOperationException) { }
    }

    private async Task WaitForDoubanRecoveryAsync()
    {
        if (!_doubanRecovering) return;
        var signal = _doubanReadySignal;
        try
        {
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("豆瓣 WebView2 正在恢复，请稍后重试。");
        }
    }

    private void OnDoubanBrowserProcessFailed(CoreWebView2ProcessFailedKind kind)
    {
        if (_closing || kind != CoreWebView2ProcessFailedKind.BrowserProcessExited) return;
        if (Interlocked.Exchange(ref _doubanRecoveryScheduled, 1) != 0)
        {
            DiagnosticLogger.Write($"BuildFix12 R8 recovery coalesced; Kind={kind}; Generation={_doubanBrowserGeneration}");
            return;
        }

        try
        {
            BeginInvoke((Action)(async () => await RecoverDoubanWebViewsAsync(kind).ConfigureAwait(true)));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _doubanRecoveryScheduled, 0);
            DiagnosticLogger.Write($"BuildFix12 R8 recovery scheduling failed; Kind={kind}; Error={ex}");
        }
    }

    private async Task RecoverDoubanWebViewsAsync(CoreWebView2ProcessFailedKind kind)
    {
        if (_closing) return;
        var startedAt = DateTimeOffset.UtcNow;
        var generation = Interlocked.Increment(ref _doubanBrowserGeneration);
        _doubanRecovering = true;
        _doubanReadySignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workerQueue.PauseForBrowserRecovery("browser-process-exited");
        ShowDoubanNavigationOverlay("豆瓣页面正在恢复…");
        DiagnosticLogger.Write($"BuildFix12 R8 dual WebView recovery START; Kind={kind}; Generation={generation}; ActiveSubjectId={_activeDetailSubjectId}; ActiveRequestId={_activeDetailRequestId}");

        var oldDetailView = _detailView;
        var oldWorkerView = _workerView;
        var oldDoubanPlusView = _doubanPlusView;
        var oldDoubanSourceView = _doubanSourceView;
        var oldDoubanSubjectView = _doubanSubjectView;
        var oldDetailConnector = _detailConnector;
        var oldWorkerConnector = _workerConnector;
        var wasDoubanPlusVisible = oldDoubanPlusView.Visible;
        var activeDoubanPlusNavigationUrl = _activeDoubanPlusNavigationUrl;
        var activeDoubanSourceNavigationUrl = _activeDoubanSourceNavigationUrl;
        var wasDoubanSubjectVisible = oldDoubanSubjectView.Visible;
        var activeDoubanSubjectNavigationUrl = _activeDoubanSubjectNavigationUrl;

        try
        {
            DetachDoubanConnectorEvents();
            oldDetailConnector.Dispose();
            oldWorkerConnector.Dispose();
            Controls.Remove(oldDetailView);
            Controls.Remove(oldWorkerView);
            Controls.Remove(oldDoubanPlusView);
            Controls.Remove(oldDoubanSourceView);
            Controls.Remove(oldDoubanSubjectView);
            oldDetailView.Dispose();
            oldWorkerView.Dispose();
            oldDoubanPlusView.Dispose();
            oldDoubanSourceView.Dispose();
            oldDoubanSubjectView.Dispose();

            await Task.Delay(250).ConfigureAwait(true);

            _detailView = CreateDetailWebView();
            _workerView = CreateWorkerWebView();
            _doubanPlusView = CreateDoubanPlusView();
            _doubanSourceView = CreateDoubanSourceView();
            _doubanSubjectView = CreateDoubanSubjectView();
            AttachDoubanViewMouseHandlers();
            Controls.Add(_doubanPlusView);
            Controls.Add(_doubanSourceView);
            Controls.Add(_doubanSubjectView);
            Controls.Add(_detailView);
            Controls.Add(_workerView);
            CreateDoubanConnectors();
            ShowDoubanNavigationOverlay("豆瓣页面正在恢复…");

            await Task.WhenAll(
                _detailConnector.EnsureInitializedAsync(),
                _workerConnector.EnsureInitializedAsync(),
                EnsureDoubanPlusViewAsync(),
                EnsureDoubanSourceViewAsync(),
                EnsureDoubanSubjectViewAsync()).ConfigureAwait(true);
            _detailView.CoreWebView2.Navigate("about:blank");
            _workerView.CoreWebView2.Navigate("about:blank");
            if (wasDoubanPlusVisible && DoubanWebView2Connector.IsAllowedDoubanTopLevel(activeDoubanPlusNavigationUrl))
            {
                _doubanPlusView.Visible = true;
                _doubanPlusView.BringToFront();
                DiagnosticLogger.Write($"Unified Shell recovery restored visible Shell; FormerListUrl={activeDoubanPlusNavigationUrl}");
            }
            if (!_frodoPersonalActive && DoubanWebView2Connector.IsAllowedDoubanTopLevel(activeDoubanSourceNavigationUrl))
            {
                _activeDoubanSourceNavigationUrl = activeDoubanSourceNavigationUrl;
                _doubanSourceView.CoreWebView2.Navigate(activeDoubanSourceNavigationUrl);
            }
            else if (_frodoPersonalActive)
            {
                DiagnosticLogger.Write($"Frodo personal source preserved across WebView2 recovery; Url={_activeDoubanSourceNavigationUrl}");
            }
            if (wasDoubanSubjectVisible && DoubanWebView2Connector.IsAllowedSubjectUrl(activeDoubanSubjectNavigationUrl))
            {
                _doubanSubjectView.Visible = true;
                _doubanSubjectView.BringToFront();
                _doubanSubjectView.CoreWebView2.Navigate(activeDoubanSubjectNavigationUrl);
            }

            // Cookie/profile state lives in the shared user-data folder. A lightweight cookie check
            // confirms the recreated controller is attached without misreporting a process crash as logout.
            var session = await _workerConnector.GetSessionStatusAsync().ConfigureAwait(true);
            DiagnosticLogger.Write($"BuildFix12 R8 dual WebView recovery READY; Generation={generation}; SessionState={session.State}; ProfileId={session.ProfileId}; ElapsedMs={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:F0}");

            _doubanRecovering = false;
            _doubanReadySignal.TrySetResult(true);
            _workerQueue.ResumeAfterBrowserRecovery("dual-webview-recreated");
            HideDoubanNavigationOverlay();
        }
        catch (Exception ex)
        {
            _doubanRecovering = false;
            _doubanReadySignal.TrySetException(ex);
            _workerQueue.ResumeAfterBrowserRecovery("recovery-failed-release-queue");
            HideDoubanNavigationOverlay();
            DiagnosticLogger.Write($"BuildFix12 R8 dual WebView recovery FAILED; Generation={generation}; ElapsedMs={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:F0}; Error={ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _doubanRecoveryScheduled, 0);
        }
    }

}
