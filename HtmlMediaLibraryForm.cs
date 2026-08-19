using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed class HtmlMediaLibraryForm : Form
{
    private const string WatchlistPosterHost = "qb-watchlist.local";
    private static readonly TimeSpan DoubanSourceDomWaitTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DoubanSourceDomPollInterval = TimeSpan.FromMilliseconds(150);
    private static readonly HttpClient DoubanPlusHttpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(25)
    };
    private const int DoubanPosterCacheLimit = 64;
    private const int DoubanPosterCacheItemLimit = 512 * 1024;
    private static readonly object DoubanPosterCacheGate = new();
    private static readonly Dictionary<string, string> DoubanPosterDataUriCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly PersistentState _software;
    private readonly DoubanHistoryState _history;
    private readonly AppSettings _settings;
    private readonly DoubanSearchCacheState _searchCache;
    private readonly Store _store;
    private readonly LocalWatchlistStore _watchlist;
    private readonly BrowserCdpService _cdp;
    private readonly string _preferredBrowser;
    private readonly DoubanPlusOpenTarget? _initialOpenTarget;
    private readonly Panel _doubanNavigationOverlay = CreateDoubanNavigationOverlay();
    private readonly Panel _doubanAccountBar = CreateDoubanAccountBar();
    private readonly Label _doubanAccountStatus = CreateDoubanAccountStatus();
    private readonly Button _doubanLoginButton = CreateDoubanLoginButton();
    private readonly Panel _doubanLoginPanel = CreateDoubanLoginPanel();
    private readonly Label _doubanLoginStatus = CreateDoubanLoginStatus();
    private readonly Button _doubanLoginVerifyButton = CreateDoubanLoginVerifyButton();
    private readonly Button _doubanLoginCancelButton = CreateDoubanLoginCancelButton();
    private readonly ContextMenuStrip _doubanNavigationContextMenu = new();
    private WebView2 _detailView = CreateDetailWebView();
    private WebView2 _workerView = CreateWorkerWebView();
    private WebView2 _doubanPlusView = CreateDoubanPlusView();
    private WebView2 _doubanSourceView = CreateDoubanSourceView();
    private WebView2 _doubanSubjectView = CreateDoubanSubjectView();
    private WebView2 _doubanLoginView = CreateDoubanLoginView();
    private readonly Button _returnToLibraryButton = new()
    {
        AutoSize = false,
        Size = new Size(118, 36),
        Location = new Point(16, 16),
        Text = "返回",
        Visible = false,
        TabStop = true,
        Anchor = AnchorStyles.Top | AnchorStyles.Left,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(35, 35, 35),
        ForeColor = Color.White,
        UseVisualStyleBackColor = false,
        Cursor = Cursors.Hand
    };
    private readonly WebView2EnvironmentProvider _environments;
    private DoubanWebView2Connector _detailConnector = null!;
    private DoubanWebView2Connector _workerConnector = null!;
    private DoubanConnectorRouter _connector = null!;
    private readonly WorkerJobQueue _workerQueue;
    private readonly FrodoPersonalProvider _frodoPersonalProvider;
    private readonly FrodoPersonalIndexService _frodoPersonalIndex;
    private readonly FrodoPersonalQuerySession _frodoPersonalQuery = new();
    private readonly object _frodoPersonalIndexBuildGate = new();
    private readonly HashSet<string> _frodoPersonalIndexBuilds = new(StringComparer.Ordinal);
    private bool _frodoPersonalActive;
    private bool _initialized;
    private bool _closing;
    private bool _doubanRecovering;
    private int _doubanRecoveryScheduled;
    private int _doubanBrowserGeneration;
    private TaskCompletionSource<bool> _doubanReadySignal = CompletedReadySignal();
    private string _activeDetailSubjectId = "";
    private string _activeDetailRequestId = "";
    private string _activeDoubanPlusSubjectUrl = "";
    private string _activeDoubanPlusNavigationUrl = "";
    private string _activeDoubanSubjectNavigationUrl = "";
    private string _activeDoubanPersonalPageUrl = "";
    private string _activeDoubanReturnUrl = "";
    private string _pendingDoubanHistoryReturnUrl = "";
    private ulong _pendingDoubanPlusNavigationId;
    private ulong _doubanPlusShownNavigationId;
    private string _doubanPlusRecoveryUrl = "";
    private int _doubanPlusRecoveryAttempts;
    private ulong _pendingDoubanSubjectNavigationId;
    private ulong _doubanSubjectShownNavigationId;
    private string _doubanSubjectRecoveryUrl = "";
    private int _doubanSubjectRecoveryAttempts;
    private int _doubanSourceGeneration;
    private int _doubanSourceNavigationVersion;
    private int _doubanSourceNavigationAttempt;
    private int _doubanSourceReadScheduledVersion = -1;
    private readonly SemaphoreSlim _doubanSourceReadGate = new(1, 1);
    private string _activeDoubanSourceNavigationUrl = "";
    private string _pendingDoubanSourceReadRequestId = "";
    private string _pendingDoubanSourceReadOperation = "";
    private string _pendingShellDataJson = "";
    private bool _shellDocumentReady;
    private string _activeShellViewKind = "explore";
    private bool _doubanSourceNavigationCompleted;

    internal HtmlMediaLibraryForm(
        PersistentState software,
        DoubanHistoryState history,
        AppSettings settings,
        BrowserCdpService cdp,
        string preferredBrowser,
        DoubanPlusOpenTarget? initialOpenTarget = null,
        string? dataDirectory = null)
    {
        _store = new Store(dataDirectory);
        _watchlist = new LocalWatchlistStore(dataDirectory);
        _software = software;
        _history = history;
        _settings = settings;
        _cdp = cdp;
        _preferredBrowser = preferredBrowser;
        _initialOpenTarget = initialOpenTarget;
        _searchCache = new();
        _environments = new WebView2EnvironmentProvider(_store.DataDirectory);
        var frodoOptions = FrodoOptions.CreateDefault();
        _frodoPersonalIndex = new FrodoPersonalIndexService(frodoOptions, _store.DataDirectory);
        _frodoPersonalProvider = new FrodoPersonalProvider(frodoOptions, _frodoPersonalIndex);
        CreateDoubanConnectors();
        _workerQueue = new WorkerJobQueue(this);

        Text = "Douban Plus";
        AutoScaleMode = AutoScaleMode.Dpi;
        Width = 1180; Height = 800; MinimumSize = new Size(860, 620); StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        _returnToLibraryButton.Text = "返回";
        _returnToLibraryButton.Click += (_, _) => ReturnToLibrary();
        _returnToLibraryButton.FlatAppearance.BorderSize = 0;
        _returnToLibraryButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
        _returnToLibraryButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(75, 75, 75);
        _doubanAccountBar.Controls.Add(_doubanAccountStatus);
        _doubanAccountBar.Controls.Add(_doubanLoginButton);
        var loginFooter = new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(12, 10, 12, 10), BackColor = Color.FromArgb(23, 26, 31) };
        loginFooter.Controls.Add(_doubanLoginStatus);
        loginFooter.Controls.Add(_doubanLoginCancelButton);
        loginFooter.Controls.Add(_doubanLoginVerifyButton);
        _doubanLoginPanel.Controls.Add(_doubanLoginView);
        _doubanLoginPanel.Controls.Add(loginFooter);
        _doubanLoginButton.Click += (_, _) => ShowDoubanLogin();
        _doubanLoginVerifyButton.Click += async (_, _) => await VerifyInlineDoubanLoginAsync().ConfigureAwait(true);
        _doubanLoginCancelButton.Click += (_, _) => HideInlineDoubanLogin();
        ConfigureDoubanNavigationContextMenu();
        Controls.Add(_doubanPlusView);
        Controls.Add(_doubanSourceView);
        Controls.Add(_doubanSubjectView);
        Controls.Add(_returnToLibraryButton);
        Controls.Add(_detailView);
        Controls.Add(_workerView);
        Controls.Add(_doubanNavigationOverlay);
        Controls.Add(_doubanAccountBar);
        Controls.Add(_doubanLoginPanel);
        Shown += async (_, _) => await InitializeAsync();
        FormClosed += (_, _) =>
        {
            _closing = true;
            _workerQueue.Dispose();
            DetachDoubanConnectorEvents();
            _detailConnector.Dispose();
            _workerConnector.Dispose();
            _detailView.Dispose();
            _workerView.Dispose();
            _doubanPlusView.Dispose();
            _doubanSourceView.Dispose();
            _doubanSubjectView.Dispose();
            _doubanLoginView.Dispose();
        };
    }

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

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        try
        {
            // Douban Plus is the only visible page. The former local MediaLibrary HTML shell is gone.
            await Task.WhenAll(
                _detailConnector.EnsureInitializedAsync(),
                _workerConnector.EnsureInitializedAsync(),
                EnsureDoubanPlusViewAsync(),
                EnsureDoubanSourceViewAsync(),
                EnsureDoubanSubjectViewAsync());
            _detailView.CoreWebView2.Navigate("about:blank");
            _workerView.CoreWebView2.Navigate("about:blank");
            DiagnosticLogger.Write($"Unified Shell stage 1 ready; DetailProfile={_environments.DoubanProfileDirectory}; WorkerProfile={_environments.DoubanProfileDirectory}; SourceProfile={_environments.DoubanProfileDirectory}; SharedEnvironment=True; DoubanPlus={DoubanPlusWebView2Script.Version}; Commit={DoubanPlusWebView2Script.SourceCommit}");
            var session = await _workerConnector.GetSessionStatusAsync().ConfigureAwait(true);
            _doubanAccountStatus.Text = session.Text;
            await NavigateInitialDoubanPageAsync(session).ConfigureAwait(true);
            _initialized = true;
        }
        catch (Exception ex)
        {
            var probe = WebView2EnvironmentProvider.ProbeRuntime();
            var message = "无法启动 Douban Plus。\n\n" + ex.Message + "\n\n请安装 Microsoft Edge WebView2 Evergreen Runtime。\n\n是否打开微软官方安装页面？";
            DiagnosticLogger.Write($"Douban Plus initialization failed; Runtime={probe.Version}; Error={ex}");
            if (MessageBox.Show(this, message, "缺少 WebView2 Runtime", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/consumer/") { UseShellExecute = true });
            Close();
        }
    }

    private async Task<object?> DispatchAsync(string operation, JsonElement payload, string bridgeRequestId)
    {
        if (operation is not ("init" or "sessionStatus" or "login" or "detailCached"))
            await WaitForDoubanRecoveryAsync().ConfigureAwait(true);

        switch (operation)
        {
            case "init": return await BuildInitialStateAsync();
            case "openDoubanPlusDetail": return await OpenDoubanPlusDetailAsync(payload, bridgeRequestId);
            case "openDoubanPersonalPage": return await OpenDoubanPersonalPageAsync(payload);
            case "sessionStatus":
                if (_doubanRecovering)
                    return SessionStatusDto(new DoubanSessionStatus("recovering", "豆瓣：浏览器正在恢复", _workerConnector.CurrentSessionStatus.ProfileId, _workerConnector.CurrentSessionStatus.VerifiedAt, "WebView2 BrowserProcessExited"));
                return SessionStatusDto(await _workerConnector.GetSessionStatusAsync(skipWhenBusy: true));
            case "login":
            {
                ShowDoubanLogin();
                return new { opened = true };
            }
            case "search":
            {
                var query = RequiredString(payload, "query", 120);
                var page = ReadInt(payload, "page", 0, 200);
                var jobId = WorkerJobQueue.NewJobId(WorkerJobType.Search);
                var descriptor = new WorkerJobDescriptor(jobId, WorkerJobType.Search, WorkerJobQueue.PriorityFor(WorkerJobType.Search), "", bridgeRequestId,
                    $"search:{page}", DateTimeOffset.UtcNow);
                var result = await _workerQueue.EnqueueAsync(descriptor,
                    _ => _connector.SearchAsync(query, DoubanSearchPaging.StartForPage(page)));
                return new { loggedIn = result.LoggedIn, captcha = result.Captcha, hasMore = result.HasMore, error = result.Error, page, items = result.Items.Select(SearchCandidateDto).ToList(), jobId };
            }
            case "detailCached":
            {
                var subjectId = RequiredDigits(payload, "subjectId");
                var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
                var requestId = ReadDetailRequestId(payload, bridgeRequestId);
                _activeDetailSubjectId = subjectId;
                _activeDetailRequestId = requestId;
                var record = FindOrCreateRecord(subjectId, subjectUrl);
                DiagnosticLogger.Write($"HTML detail cache returned; SubjectId={subjectId}; RequestId={requestId}; HasMetadata={record.DetailMetadataFetched}; Status={record.Status}; Rating={record.Rating?.ToString() ?? "null"}");
                return DetailDto(record, "cache");
            }
            case "detailMetadata":
            {
                var subjectId = RequiredDigits(payload, "subjectId");
                var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
                var requestId = ReadDetailRequestId(payload, bridgeRequestId);
                var record = FindOrCreateRecord(subjectId, subjectUrl);
                var now = DateTime.Now;
                if (!DetailCachePolicy.NeedsMetadataRefresh(record, now))
                {
                    DiagnosticLogger.Write($"HTML detail metadata cache hit; WebView=Detail; SubjectId={subjectId}; RequestId={requestId}; BasicFetchedAt={record.FullDetailsFetchedAt:O}");
                    return DetailDto(record, "cache-fresh");
                }
                var startedAt = DateTime.UtcNow;
                var metadata = await _detailConnector.ReadMetadataAsync(subjectUrl, probeStatusCapabilities: false);
                if (metadata.Captcha) throw new InvalidOperationException("豆瓣要求验证码，已保留本地数据。");
                if (!metadata.LoggedIn) throw new InvalidOperationException("内置豆瓣 Profile 尚未登录，请先扫码登录。");
                if (!metadata.IsSuccess) throw new InvalidDataException(string.IsNullOrWhiteSpace(metadata.Error) ? "豆瓣没有返回有效详情。" : metadata.Error);
                ApplyMetadata(record, metadata);
                DiagnosticLogger.Write($"HTML detail metadata refreshed; WebView=Detail; SubjectId={subjectId}; RequestId={requestId}; ElapsedMs={(DateTime.UtcNow - startedAt).TotalMilliseconds:F0}; Source={metadata.ConnectorSource}");
                return DetailDto(record, metadata.ConnectorSource);
            }
            case "detailReview":
            {
                var subjectId = RequiredDigits(payload, "subjectId");
                var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
                var requestId = ReadDetailRequestId(payload, bridgeRequestId);
                var record = FindOrCreateRecord(subjectId, subjectUrl);
                var startedAt = DateTime.UtcNow;
                var jobId = WorkerJobQueue.NewJobId(WorkerJobType.OfficialReviewRead);
                var descriptor = new WorkerJobDescriptor(jobId, WorkerJobType.OfficialReviewRead, WorkerJobQueue.PriorityFor(WorkerJobType.OfficialReviewRead), subjectId, requestId,
                    subjectUrl, DateTimeOffset.UtcNow);
                var officialReview = await _workerQueue.EnqueueAsync(descriptor,
                    _ => _connector.ReadOfficialReviewAsync(subjectUrl));
                var officialLoaded = ApplyAuthoritativeReview(record, officialReview);
                if (!officialLoaded)
                {
                    DiagnosticLogger.Write($"HTML detail official review unavailable; WebView=Worker; JobId={jobId}; SubjectId={subjectId}; RequestId={requestId}; Source={officialReview.Source}; Error={officialReview.Error}");
                }
                DiagnosticLogger.Write($"HTML detail review refreshed; WebView=Worker; JobId={jobId}; SubjectId={subjectId}; RequestId={requestId}; ElapsedMs={(DateTime.UtcNow - startedAt).TotalMilliseconds:F0}; OfficialLoaded={officialLoaded}; Source={officialReview.Source}; Error={officialReview.Error ?? "<none>"}");
                return new
                {
                    detail = DetailDto(record, officialLoaded ? "official-review" : "cache"),
                    officialLoaded,
                    error = officialLoaded ? "" : (officialReview.Error ?? "豆瓣官方评价未读取完整。"),
                    jobId,
                    requestId
                };
            }
            case "detail":
            {
                // Compatibility path for older local assets. New assets use cache-first staged requests.
                var subjectId = RequiredDigits(payload, "subjectId");
                var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
                var record = FindOrCreateRecord(subjectId, subjectUrl);
                var metadata = await _detailConnector.ReadMetadataAsync(subjectUrl, probeStatusCapabilities: false);
                if (metadata.Captcha) throw new InvalidOperationException("豆瓣要求验证码，已保留本地数据。");
                if (!metadata.LoggedIn) throw new InvalidOperationException("内置豆瓣 Profile 尚未登录，请先扫码登录。");
                if (!metadata.IsSuccess) throw new InvalidDataException(string.IsNullOrWhiteSpace(metadata.Error) ? "豆瓣没有返回有效详情。" : metadata.Error);
                ApplyMetadata(record, metadata);
                var officialReview = await _connector.ReadOfficialReviewAsync(subjectUrl);
                ApplyAuthoritativeReview(record, officialReview);
                return DetailDto(record, metadata.ConnectorSource + "+official-review");
            }
            case "saveDoubanEntry":
            {
                var record = GetWritableRecord(payload, allowTombstoneRecreate: true);
                var requestId = ReadDetailRequestId(payload, bridgeRequestId);
                var uiTitle = ReadString(payload, "uiTitle").Trim();
                var beforeStatus = record.Status;
                var beforeRating = record.Rating;
                var beforeMarkedDate = record.MarkedDate;
                var cachedTitle = record.Title;
                var saveTimer = System.Diagnostics.Stopwatch.StartNew();
                var status = RequiredLibraryStatus(payload, "status");
                var ratingAction = ReadReviewFieldAction(payload, "ratingAction");
                var rating = ReadNullableInt(payload, "rating", 1, 5);
                var commentAction = ReadReviewFieldAction(payload, "commentAction");
                var comment = commentAction == ReviewFieldAction.Set ? ReadString(payload, "comment").Trim() : null;
                if (commentAction == ReviewFieldAction.Set && (comment?.Length ?? 0) > 330)
                    throw new InvalidDataException("短评不能超过 330 字。");

                var request = new DoubanEntryWriteRequestV2(status, ratingAction, rating, commentAction, comment);
                var jobId = WorkerJobQueue.NewJobId(WorkerJobType.ReviewSave);
                var descriptor = new WorkerJobDescriptor(jobId, WorkerJobType.ReviewSave, WorkerJobQueue.PriorityFor(WorkerJobType.ReviewSave), record.SubjectId, requestId,
                    record.SubjectUrl, DateTimeOffset.UtcNow, NonCancelableOnceStarted: true);
                var connectorResult = await _workerQueue.EnqueueAsync(descriptor,
                    _ => _connector.SaveDoubanEntryAsync(record.SubjectUrl, request));
                var authoritative = SelectAuthoritativeSnapshot(connectorResult);
                var localUpdated = authoritative is not null && ApplyAuthoritativeReview(record, authoritative);
                var result = connectorResult with { LocalUpdated = connectorResult.LocalUpdated || localUpdated };
                if (result.OfficialConfirmed && authoritative is not null)
                    await SyncFrodoPersonalAfterConfirmedWriteAsync(record, beforeStatus, "save").ConfigureAwait(true);
                saveTimer.Stop();
                var subjectUrlSubjectId = DoubanSubjectIdentity.ExtractSubjectId(record.SubjectUrl);
                ReviewTransactionLogger.Write(new
                {
                    UiTitle = uiTitle, CachedTitle = cachedTitle, OfficialTitle = result.Official?.OfficialTitle ?? result.Before?.OfficialTitle ?? "",
                    SubjectId = record.SubjectId, SubjectUrlSubjectId = subjectUrlSubjectId,
                    BeforeStatus = beforeStatus, BeforeRating = beforeRating, BeforeMarkedDate = beforeMarkedDate,
                    TargetStatus = result.Target?.Status ?? status, TargetRating = result.Target?.Rating,
                    result.Submitted, result.NoChange, result.Changed, result.SubmitEventObserved,
                    OfficialStatus = result.Official?.Status ?? result.Before?.Status, OfficialRating = result.Official?.Rating ?? result.Before?.Rating,
                    OfficialMarkedDate = result.Official?.MarkedDate ?? result.Before?.MarkedDate, result.OfficialConfirmed, result.LocalUpdated,
                    DurationMs = Math.Round(saveTimer.Elapsed.TotalMilliseconds), result.Phase, result.Stage, Error = result.Error ?? "",
                    WebView = "Worker", JobId = jobId, RequestId = requestId
                });
                DiagnosticLogger.Write(
                    $"HTML review v2 final result; WebView=Worker; JobId={jobId}; RequestId={requestId}; UiTitle={uiTitle}; CachedTitle={cachedTitle}; OfficialTitle={result.Official?.OfficialTitle ?? result.Before?.OfficialTitle ?? ""}; SubjectId={record.SubjectId}; SubjectUrlSubjectId={subjectUrlSubjectId}; " +
                    $"BeforeStatus={beforeStatus}; BeforeRating={beforeRating?.ToString() ?? "null"}; BeforeMarkedDate={beforeMarkedDate}; TargetStatus={result.Target?.Status ?? status}; TargetRating={result.Target?.Rating?.ToString() ?? "null"}; " +
                    $"Submitted={result.Submitted}; NoChange={result.NoChange}; Changed={result.Changed}; SubmitEventObserved={result.SubmitEventObserved}; OfficialStatus={result.Official?.Status ?? result.Before?.Status}; " +
                    $"OfficialRating={(result.Official?.Rating ?? result.Before?.Rating)?.ToString() ?? "null"}; OfficialMarkedDate={result.Official?.MarkedDate ?? result.Before?.MarkedDate}; " +
                    $"Settled={result.Settled}; OfficialConfirmed={result.OfficialConfirmed}; LocalUpdated={result.LocalUpdated}; DurationMs={saveTimer.Elapsed.TotalMilliseconds:F0}; " +
                    $"Error={(string.IsNullOrWhiteSpace(result.Error) ? "<none>" : result.Error)}");
                return WriteEnvelopeV2(record, result, "webview2-worker-write-v2", jobId, requestId);
            }
            case "deleteEntry":
            {
                var subjectId = RequiredDigits(payload, "subjectId");
                var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
                var requestId = ReadDetailRequestId(payload, bridgeRequestId);
                var uiTitle = ReadString(payload, "uiTitle").Trim();
                var record = FindOrCreateRecord(subjectId, subjectUrl);
                DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "HTML delete preflight", record);
                record.SubjectUrl = subjectUrl;
                var cachedTitle = record.Title;
                var beforeStatus = record.Status;
                var beforeRating = record.Rating;
                var beforeMarkedDate = record.MarkedDate;
                var timer = System.Diagnostics.Stopwatch.StartNew();

                var jobId = WorkerJobQueue.NewJobId(WorkerJobType.ReviewDelete);
                var descriptor = new WorkerJobDescriptor(
                    jobId,
                    WorkerJobType.ReviewDelete,
                    WorkerJobQueue.PriorityFor(WorkerJobType.ReviewDelete),
                    subjectId,
                    requestId,
                    subjectUrl,
                    DateTimeOffset.UtcNow,
                    NonCancelableOnceStarted: true);

                var connectorResult = await _workerQueue.EnqueueAsync(
                    descriptor,
                    _ => _connector.DeleteDoubanEntryAsync(subjectUrl));

                var localUpdated = false;
                if (connectorResult.OfficialConfirmed && connectorResult.Official is { ExistsKnown: true, Exists: false })
                {
                    localUpdated = ApplyConfirmedDeletion(record,
                        connectorResult.NoChange ? "豆瓣官方已无评价，删除状态同步" : "豆瓣官方删除确认");
                }
                var result = connectorResult with { LocalUpdated = connectorResult.LocalUpdated || localUpdated };
                if (result.OfficialConfirmed && result.Official is { ExistsKnown: true, Exists: false })
                    await SyncFrodoPersonalAfterConfirmedDeleteAsync(subjectId, beforeStatus, "delete").ConfigureAwait(true);
                timer.Stop();

                ReviewTransactionLogger.Write(new
                {
                    UiTitle = uiTitle,
                    CachedTitle = cachedTitle,
                    OfficialTitle = result.Official?.OfficialTitle ?? result.Before?.OfficialTitle ?? "",
                    SubjectId = subjectId,
                    SubjectUrlSubjectId = DoubanSubjectIdentity.ExtractSubjectId(subjectUrl),
                    BeforeStatus = result.Before?.Status ?? beforeStatus,
                    BeforeRating = result.Before?.Rating ?? beforeRating,
                    BeforeMarkedDate = result.Before?.MarkedDate ?? beforeMarkedDate,
                    TargetStatus = "delete",
                    TargetRating = (int?)null,
                    Submitted = result.Submitted,
                    NoChange = result.NoChange,
                    Changed = result.Submitted,
                    SubmitEventObserved = result.Submitted,
                    OfficialStatus = result.Official?.ExistsKnown == true && !result.Official.Exists ? "deleted" : (result.Official?.Status ?? ""),
                    OfficialRating = result.Official?.Rating,
                    OfficialMarkedDate = result.Official?.MarkedDate ?? "",
                    OfficialConfirmed = result.OfficialConfirmed,
                    LocalUpdated = result.LocalUpdated,
                    DurationMs = Math.Round(timer.Elapsed.TotalMilliseconds),
                    Phase = result.Phase,
                    Stage = result.Stage,
                    DeleteRoute = result.Route,
                    Error = result.Error ?? "",
                    WebView = "Worker",
                    JobId = jobId,
                    RequestId = requestId,
                    ListChecks = result.ListChecks
                });

                DiagnosticLogger.Write(
                    $"HTML delete v2 final result; WebView=Worker; JobId={jobId}; RequestId={requestId}; UiTitle={uiTitle}; CachedTitle={cachedTitle}; SubjectId={subjectId}; " +
                    $"BeforeStatus={result.Before?.Status ?? beforeStatus}; DeleteRoute={result.Route}; Submitted={result.Submitted}; NoChange={result.NoChange}; Settled={result.Settled}; " +
                    $"OfficialExists={(result.Official?.ExistsKnown == true ? result.Official.Exists.ToString() : "unknown")}; OfficialConfirmed={result.OfficialConfirmed}; " +
                    $"LocalUpdated={result.LocalUpdated}; DurationMs={timer.Elapsed.TotalMilliseconds:F0}; Error={(string.IsNullOrWhiteSpace(result.Error) ? "<none>" : result.Error)}");

                return DeleteEnvelope(record, result, "webview2-worker-delete-v2", jobId, requestId);
            }
            case "ptSearch":
            {
                var imdbId = RequiredString(payload, "imdbId", 20);
                if (!BrowserCdpService.IsValidImdbId(imdbId)) throw new InvalidDataException("IMDb编号无效。");
                await _cdp.EnsureBackgroundAsync(_preferredBrowser);
                await _cdp.OpenPtDepilerSearchAsync(imdbId);
                return new { opened = true };
            }
            default: throw new InvalidDataException("不允许的操作。");
        }
    }

    private async Task<object> BuildInitialStateAsync()
    {
        var session = await _workerConnector.GetSessionStatusAsync();
        object? openDetail = null;
        var openDetailError = "";
        if (_initialOpenTarget is not null)
        {
            try { openDetail = await ResolveInitialOpenDetailAsync(_initialOpenTarget); }
            catch (Exception ex) { openDetailError = ex.Message; DiagnosticLogger.Write($"HTML initial target resolution failed; SearchTitle={_initialOpenTarget.SearchTitle}; Error={ex.Message}"); }
        }
        return new
        {
            version = AppInfo.Version,
            runtime = WebView2EnvironmentProvider.ProbeRuntime(),
            login = SessionStatusDto(session),
            profileDirectory = _environments.DoubanProfileDirectory,
            tabs = new
            {
                collect = _history.Items.Values.Where(x => !x.Tombstoned && x.Status == "collect").OrderByDescending(x => x.MarkedDate).Select(CardDto).ToList(),
                wish = _history.Items.Values.Where(x => !x.Tombstoned && x.Status == "wish").OrderByDescending(x => x.MarkedDate).Select(CardDto).ToList(),
                watching = _history.Items.Values.Where(x => !x.Tombstoned && x.Status == "do").OrderByDescending(x => x.MarkedDate).Select(CardDto).ToList(),
                software = _software.Videos.Values.OrderByDescending(x => x.WatchedSeconds).Select(x => new { key = x.Key, title = x.Title, subtitle = "软件真实观看记录", meta = $"真实观看 {TimeSpan.FromSeconds(x.WatchedSeconds):hh\\:mm\\:ss} · 最高进度 {x.HighestRatio:P0}", posterUrl = "", subjectId = "", subjectUrl = "" }).ToList()
            },
            openDetail,
            openDetailError
        };
    }

    private async Task NavigateInitialDoubanPageAsync(DoubanSessionStatus session)
    {
        if (_initialOpenTarget is not null)
        {
            try
            {
                var resolved = await ResolveInitialOpenDetailAsync(_initialOpenTarget).ConfigureAwait(true);
                var item = JsonSerializer.SerializeToElement(resolved).GetProperty("item");
                var subjectId = item.GetProperty("subjectId").GetString() ?? "";
                var subjectUrl = item.GetProperty("subjectUrl").GetString() ?? "";
                var payload = JsonSerializer.SerializeToElement(new { subjectId, subjectUrl });
                await OpenDoubanPlusDetailAsync(payload, "initial-target").ConfigureAwait(true);
                return;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Write($"Douban Plus initial target resolution failed; SearchTitle={_initialOpenTarget.SearchTitle}; Error={ex.Message}");
            }
        }

        _frodoPersonalActive = false;
        _frodoPersonalProvider.Reset();
        const string url = "https://movie.douban.com/explore";
        _activeDoubanPlusNavigationUrl = url;
        _activeDoubanSourceNavigationUrl = url;
        _doubanSourceNavigationCompleted = false;
        _activeDoubanPersonalPageUrl = "";
        _doubanPlusView.Visible = true;
        _doubanPlusView.BringToFront();
        _doubanAccountBar.Visible = false;
        _returnToLibraryButton.Visible = false;
        _doubanSourceView.CoreWebView2!.Navigate(url);
        DiagnosticLogger.Write($"Unified Shell initial page opened; Shell=visible; Source=hidden; Url={url}; LoggedIn={session.IsLoggedIn}; SourceGeneration={_doubanSourceGeneration}");
    }

    private async Task<object> ResolveInitialOpenDetailAsync(DoubanPlusOpenTarget target)
    {
        var aliases = target.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)).Select(alias => alias.Trim())
            .Append(target.SearchTitle.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        var candidates = new Dictionary<string, DoubanSearchCandidate>(StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            var jobId = WorkerJobQueue.NewJobId(WorkerJobType.Search);
            var descriptor = new WorkerJobDescriptor(jobId, WorkerJobType.Search, WorkerJobQueue.PriorityFor(WorkerJobType.Search), "", "", $"initial-ai:{alias}", DateTimeOffset.UtcNow);
            var result = await _workerQueue.EnqueueAsync(descriptor, _ => _connector.SearchAsync(alias, 0));
            if (result.Captcha) throw new InvalidOperationException("豆瓣要求验证，请完成验证后重试。");
            if (!result.LoggedIn) throw new InvalidOperationException("豆瓣尚未登录，请先扫码登录。");
            foreach (var item in result.Items)
                if (!candidates.ContainsKey(item.SubjectId)) candidates[item.SubjectId] = item;
        }

        var input = new MovieTitleParts(target.SearchTitle, target.SearchTitle, "", "", target.Year, aliases);
        var ranked = candidates.Values.Select(item =>
        {
            var years = MovieTitle.YearsFromText(item.VisibleText);
            int? candidateYear = years.Count == 0 ? null : years[0];
            var evaluation = RecognitionMatcher.Evaluate(input, item.VisibleText, null, candidateYear);
            return new { item, evaluation };
        }).Where(candidate => candidate.evaluation.IsStrongMatch)
            .OrderByDescending(candidate => candidate.evaluation.Score)
            .ThenBy(candidate => candidate.item.SubjectId, StringComparer.Ordinal)
            .ToList();
        if (ranked.Count == 0) throw new InvalidOperationException("未找到与 PotPlayer 影片可靠匹配的豆瓣条目，请先在豆瓣页面搜索并确认。");
        if (ranked.Count > 1 && ranked[0].evaluation.Score - ranked[1].evaluation.Score < 10)
            throw new InvalidOperationException("豆瓣搜索返回多个相近影片，已阻止自动选择，请先在豆瓣页面搜索并确认。");

        var selected = ranked[0].item;
        return new
        {
            item = new
            {
                subjectId = selected.SubjectId,
                subjectUrl = selected.SubjectUrl,
                posterUrl = selected.PosterUrl,
                title = selected.VisibleText.Split('\n')[0].Trim(),
                statusOptions = (selected.StatusOptions ?? []).Select(StatusDto).ToList()
            },
            preferredStatus = target.PreferredStatus,
            source = target.SourceDescription
        };
    }

    private static object SessionStatusDto(DoubanSessionStatus status) => new { state = status.State, text = status.Text, profileId = status.ProfileId, verifiedAt = status.VerifiedAt, error = status.Error, loggedIn = status.IsLoggedIn };

    internal void ShowDoubanLogin()
    {
        if (_closing) return;
        _doubanAccountBar.Visible = false;
        _doubanLoginPanel.Visible = true;
        _doubanLoginPanel.BringToFront();
        _doubanLoginStatus.Text = "正在打开豆瓣扫码登录…";
        _doubanLoginVerifyButton.Enabled = false;
        _ = OpenInlineDoubanLoginAsync();
    }

    private async Task OpenInlineDoubanLoginAsync()
    {
        try
        {
            await EnsureDoubanLoginViewAsync().ConfigureAwait(true);
            if (_closing || !_doubanLoginPanel.Visible) return;
            _workerConnector.SetLoginWindowActive(true);
            _doubanLoginVerifyButton.Enabled = true;
            _doubanLoginStatus.Text = "请使用豆瓣 App 扫码，完成后点击“验证登录”。";
            _doubanLoginView.CoreWebView2!.Navigate("https://accounts.douban.com/passport/login?source=movie");
            DiagnosticLogger.Write("WebView=DoubanLoginInline; LoginPanelOpened=True; PopupForm=False");
        }
        catch (Exception ex)
        {
            _doubanLoginVerifyButton.Enabled = false;
            _doubanLoginStatus.Text = "扫码页面打开失败：" + ex.Message;
            DiagnosticLogger.Write($"WebView=DoubanLoginInline; LoginPanelOpenFailed=True; Error={ex}");
        }
    }

    private async Task VerifyInlineDoubanLoginAsync()
    {
        if (_closing || !_doubanLoginPanel.Visible) return;
        _doubanLoginVerifyButton.Enabled = false;
        _doubanLoginStatus.Text = "正在验证豆瓣登录状态，请稍候…";
        try
        {
            var status = await _workerConnector.VerifySessionAsync().ConfigureAwait(true);
            if (!status.IsLoggedIn)
            {
                _doubanLoginStatus.Text = string.IsNullOrWhiteSpace(status.Error)
                    ? "尚未确认登录，请扫码后再试。"
                    : status.Text + "，请扫码后再试。";
                _doubanLoginVerifyButton.Enabled = true;
                return;
            }

            _doubanLoginStatus.Text = "豆瓣登录已确认，正在返回主界面…";
            HideInlineDoubanLogin();
            await NavigateInitialDoubanPageAsync(status).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _doubanLoginStatus.Text = "登录验证失败：" + ex.Message;
            _doubanLoginVerifyButton.Enabled = true;
            DiagnosticLogger.Write($"WebView=DoubanLoginInline; LoginVerifyFailed=True; Error={ex}");
        }
    }

    private void HideInlineDoubanLogin()
    {
        _doubanLoginPanel.Visible = false;
        _doubanLoginVerifyButton.Enabled = true;
        _doubanLoginStatus.Text = "请使用豆瓣 App 扫码，完成后点击“验证登录”。";
        _workerConnector.SetLoginWindowActive(false);
        if (!_closing)
        {
            _doubanAccountBar.Visible = true;
            _doubanAccountBar.BringToFront();
            DiagnosticLogger.Write("WebView=DoubanLoginInline; LoginPanelClosed=True; PopupForm=False");
        }
    }

    private void BringDoubanAccountBarToFront()
    {
        if (_closing || _doubanLoginPanel.Visible) return;
        _doubanAccountBar.Visible = true;
        _doubanAccountBar.BringToFront();
    }

    private object CardDto(DoubanHistoryRecord record) => new
    {
        subjectId = record.SubjectId, subjectUrl = record.SubjectUrl, title = string.IsNullOrWhiteSpace(record.Title) ? $"豆瓣条目 {record.SubjectId}" : record.Title, posterUrl = record.PosterUrl,
        subtitle = record.Status switch { "collect" => "豆瓣 · 看过", "wish" => "豆瓣 · 想看", "do" => "豆瓣 · 在看", _ => "豆瓣" },
        meta = string.Join(" · ", new[] { record.MarkedDate, record.Tags, record.Comment }.Where(x => !string.IsNullOrWhiteSpace(x))),
        score = record.DoubanScore, myRating = record.Rating, statusOptions = record.DoubanStatusOptions.Select(StatusDto).ToList()
    };

    private object SearchCandidateDto(DoubanSearchCandidate item) => new { subjectId = item.SubjectId, subjectUrl = item.SubjectUrl, posterUrl = item.PosterUrl, visibleText = item.VisibleText, statusOptions = (item.StatusOptions ?? []).Select(StatusDto).ToList() };

    private object DetailDto(DoubanHistoryRecord record, string connectorSource) => new
    {
        subjectId = record.SubjectId, subjectUrl = record.SubjectUrl, title = string.IsNullOrWhiteSpace(record.Title) ? $"豆瓣条目 {record.SubjectId}" : record.Title,
        year = record.Year, genres = record.Genres, directors = record.Directors, imdbId = record.ImdbId, runtime = record.Runtime, countries = record.Countries,
        summary = record.Summary, doubanScore = record.DoubanScore, rating = record.Rating, posterUrl = record.PosterUrl, markedDate = record.MarkedDate, tags = record.Tags, comment = record.Comment, tombstoned = record.Tombstoned,
        statusOptions = record.DoubanStatusOptions.Select(StatusDto).ToList(),
        statusCapabilitiesKnown = record.DoubanStatusCapabilitiesKnown,
        statusCapabilitySource = record.DoubanStatusCapabilitySource,
        statusCapabilityError = record.DoubanStatusCapabilityError,
        connectorSource
    };

    private object WriteEnvelopeV2(DoubanHistoryRecord record, ReviewWriteResultV2 result, string connectorSource, string jobId = "", string requestId = "")
    {
        var phase = result.Phase switch
        {
            ReviewWritePhase.Confirmed => "confirmed",
            ReviewWritePhase.NoChange => "no-change",
            ReviewWritePhase.Blocked => "blocked",
            ReviewWritePhase.Uncertain => "unconfirmed",
            _ => "failed"
        };
        var official = SelectAuthoritativeSnapshot(result) ?? result.Official ?? result.Before;
        return new
        {
            detail = DetailDto(record, connectorSource),
            write = new
            {
                phase,
                stage = result.Stage,
                operation = "save",
                requested = new
                {
                    status = result.Requested.Status,
                    ratingAction = FieldActionText(result.Requested.RatingAction),
                    rating = result.Requested.Rating,
                    commentAction = FieldActionText(result.Requested.CommentAction),
                    comment = result.Requested.Comment
                },
                official = new
                {
                    existsKnown = official?.ExistsKnown == true,
                    exists = official?.ExistsKnown == true ? official.Exists : (bool?)null,
                    statusKnown = official?.StatusKnown == true,
                    status = official?.Status,
                    ratingKnown = official?.RatingKnown == true,
                    rating = official?.Rating,
                    commentKnown = official?.CommentKnown == true,
                    comment = official?.Comment,
                    markedDateKnown = official?.MarkedDateKnown == true,
                    markedDate = official?.MarkedDate,
                    title = official?.OfficialTitle ?? "",
                    subjectId = official?.OfficialSubjectId ?? "",
                    source = official?.Source ?? ""
                },
                settled = result.Settled,
                submitted = result.Submitted,
                noChange = result.NoChange,
                changed = result.Changed,
                submitEventObserved = result.SubmitEventObserved,
                officialConfirmed = result.OfficialConfirmed,
                localUpdated = result.LocalUpdated,
                cacheUpdate = result.OfficialConfirmed
                    ? (result.LocalUpdated ? "completed" : "deferred")
                    : (result.LocalUpdated ? "synchronized" : "not-confirmed"),
                webView = "Worker",
                jobId,
                requestId,
                error = result.Error ?? ""
            }
        };
    }

    private static OfficialReviewSnapshot? SelectAuthoritativeSnapshot(ReviewWriteResultV2 result)
    {
        if (IsCompleteOfficialSnapshot(result.Official)) return result.Official;
        if (IsCompleteOfficialSnapshot(result.Before)) return result.Before;
        return null;
    }

    private static bool IsCompleteOfficialSnapshot(OfficialReviewSnapshot? snapshot) =>
        snapshot is not null && snapshot.ExistsKnown &&
        (!snapshot.Exists || (snapshot.StatusKnown && snapshot.RatingKnown && snapshot.CommentKnown));

    private bool ApplyAuthoritativeReview(DoubanHistoryRecord record, OfficialReviewSnapshot official)
    {
        if (!IsCompleteOfficialSnapshot(official)) return false;
        if (!string.IsNullOrWhiteSpace(official.OfficialSubjectId) && !string.Equals(official.OfficialSubjectId, record.SubjectId, StringComparison.Ordinal))
            throw new InvalidDataException("豆瓣官方页面影片 ID 与本地记录不一致，已阻止缓存更新。");

        if (!official.Exists)
        {
            var wasHistoryRecord = _history.Items.ContainsKey(record.SubjectId);
            record.Rating = null;
            record.Comment = "";
            record.Tags = "";
            record.MarkedDate = "";
            record.DoubanStatusOptions = [];
            record.DoubanStatusCapabilitiesKnown = official.CapabilitiesKnown;
            record.DoubanStatusCapabilitySource = official.Source;
            record.DoubanStatusCapabilityError = official.Error ?? "";

            if (wasHistoryRecord || record.Tombstoned)
            {
                record.Status = "deleted";
                record.Tombstoned = true;
                record.TombstonedAt ??= DateTime.Now;
                if (string.IsNullOrWhiteSpace(record.TombstoneReason)) record.TombstoneReason = "豆瓣官方已无评价同步";
                _history.Items[record.SubjectId] = record;
                _searchCache.Items.Remove(record.SubjectId);
            }
            else
            {
                record.Status = "search";
                _searchCache.Items[record.SubjectId] = record;
            }
            DiagnosticLogger.Write($"HTML authoritative cache overwrite; SubjectId={record.SubjectId}; Exists=false; WasHistory={wasHistoryRecord}; Tombstoned={record.Tombstoned}; Source={official.Source}");
            return true;
        }

        var status = ReviewTargetResolver.NormalizeStatus(official.Status);
        if (status is not ("wish" or "do" or "collect")) return false;

        var previousMarkedDate = record.MarkedDate;
        if (official.MarkedDateKnown && !string.IsNullOrWhiteSpace(official.MarkedDate))
            record.MarkedDate = official.MarkedDate.Trim();
        record.Status = status;
        record.Rating = official.Rating;
        record.Comment = ReviewTargetResolver.NormalizeComment(official.Comment);
        record.Tombstoned = false;
        record.TombstonedAt = null;
        record.TombstoneReason = "";
        record.DoubanStatusCapabilitiesKnown = official.CapabilitiesKnown;
        record.DoubanStatusCapabilitySource = official.Source;
        record.DoubanStatusCapabilityError = official.Error ?? "";

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["wish"] = "想看", ["do"] = "在看", ["collect"] = "看过"
        };
        var supported = official.SupportedStatuses
            .Select(ReviewTargetResolver.NormalizeStatus)
            .Where(labels.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!supported.Contains(status, StringComparer.OrdinalIgnoreCase)) supported.Add(status);
        record.DoubanStatusOptions = supported
            .Select(value => new DoubanStatusOption(labels[value], string.Equals(value, status, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        PromoteToHistory(record);
        DiagnosticLogger.Write($"HTML authoritative cache overwrite; SubjectId={record.SubjectId}; Exists=true; Status={status}; Rating={official.Rating?.ToString() ?? "null"}; PreviousMarkedDate={previousMarkedDate}; OfficialMarkedDate={official.MarkedDate}; MarkedDateKnown={official.MarkedDateKnown}; StoredMarkedDate={record.MarkedDate}; CommentLength={record.Comment.Length}; Source={official.Source}");
        return true;
    }

    private object DeleteEnvelope(DoubanHistoryRecord record, DeleteEntryResult result, string connectorSource, string jobId, string requestId)
    {
        var phase = result.Phase switch
        {
            DeleteWritePhase.Confirmed => "confirmed",
            DeleteWritePhase.NoChange => "no-change",
            DeleteWritePhase.Blocked => "blocked",
            DeleteWritePhase.Uncertain => "unconfirmed",
            _ => "failed"
        };
        var official = result.Official ?? result.Before;
        return new
        {
            detail = DetailDto(record, connectorSource),
            write = new
            {
                phase,
                stage = result.Stage,
                operation = "delete",
                deleteRoute = result.Route,
                requested = new { delete = true },
                official = new
                {
                    existsKnown = official?.ExistsKnown == true,
                    exists = official?.ExistsKnown == true ? official.Exists : (bool?)null,
                    statusKnown = official?.StatusKnown == true,
                    status = official?.Status,
                    ratingKnown = official?.RatingKnown == true,
                    rating = official?.Rating,
                    commentKnown = official?.CommentKnown == true,
                    comment = official?.Comment,
                    markedDateKnown = official?.MarkedDateKnown == true,
                    markedDate = official?.MarkedDate,
                    title = official?.OfficialTitle ?? "",
                    subjectId = official?.OfficialSubjectId ?? "",
                    source = official?.Source ?? ""
                },
                settled = result.Settled,
                submitted = result.Submitted,
                noChange = result.NoChange,
                changed = result.Submitted,
                submitEventObserved = result.Submitted,
                officialConfirmed = result.OfficialConfirmed,
                localUpdated = result.LocalUpdated,
                cacheUpdate = result.LocalUpdated ? "completed" : (result.OfficialConfirmed ? "deferred" : "not-confirmed"),
                listChecks = result.ListChecks.Select(check => new
                {
                    status = check.Status,
                    ready = check.Ready,
                    contains = check.Contains,
                    pagesScanned = check.PagesScanned,
                    hasMore = check.HasMore,
                    scope = check.Scope,
                    error = check.Error
                }).ToList(),
                webView = "Worker",
                jobId,
                requestId,
                error = result.Error ?? ""
            }
        };
    }

    private bool ApplyConfirmedDeletion(DoubanHistoryRecord record, string reason)
    {
        record.Status = "deleted";
        record.Rating = null;
        record.Comment = "";
        record.Tags = "";
        record.MarkedDate = "";
        record.DoubanStatusOptions = [];
        record.DoubanStatusCapabilitiesKnown = false;
        record.DoubanStatusCapabilitySource = "delete-readback";
        record.DoubanStatusCapabilityError = "";
        record.Tombstoned = true;
        record.TombstonedAt = DateTime.Now;
        record.TombstoneReason = reason;
        _history.Items[record.SubjectId] = record;
        _searchCache.Items.Remove(record.SubjectId);
        DiagnosticLogger.Write($"HTML delete tombstone applied; SubjectId={record.SubjectId}; TombstonedAt={record.TombstonedAt:O}; Reason={reason}; DetailMetadataPreserved={record.DetailMetadataFetched}");
        return true;
    }

    private static string FieldActionText(ReviewFieldAction action) => action switch
    {
        ReviewFieldAction.Keep => "keep",
        ReviewFieldAction.Set => "set",
        ReviewFieldAction.Clear => "clear",
        _ => ""
    };

    private object WriteEnvelope(DoubanHistoryRecord record, DoubanWriteResult result, string connectorSource, bool localUpdated)
    {
        var phase = !string.IsNullOrWhiteSpace(result.Phase)
            ? result.Phase
            : result.Success ? "confirmed" : (result.Error.Contains("回读", StringComparison.Ordinal) || result.Error.Contains("快照", StringComparison.Ordinal) ? "unconfirmed" : "failed");
        var requestedStatus = string.IsNullOrWhiteSpace(result.RequestedStatus)
            ? result.Action == "status" ? result.Status : ""
            : result.RequestedStatus;
        var requestedRating = result.RequestedRating ?? (result.Action == "rating" ? result.Rating : null);
        var requestedCommentLength = result.RequestedComment
            ? result.RequestedCommentLength
            : result.Action == "review" ? result.Review.Length : (int?)null;
        return new
        {
            detail = DetailDto(record, connectorSource),
            write = new
            {
                phase,
                stage = string.IsNullOrWhiteSpace(result.Stage) ? (result.Success ? "readback" : "") : result.Stage,
                operation = result.Action,
                requested = new { status = requestedStatus, rating = requestedRating, commentLength = requestedCommentLength },
                official = new { status = result.Status, rating = result.Rating, comment = result.Review },
                settled = result.Settled,
                localUpdated = localUpdated || result.LocalUpdated,
                error = result.Error
            }
        };
    }

    private static void ApplyConfirmedDoubanEntry(DoubanHistoryRecord record, DoubanWriteResult result, DoubanEntryWriteRequest request)
    {
        var status = result.Status is "wish" or "do" or "collect" ? result.Status : request.Status;
        record.Status = status;
        record.Tombstoned = false;
        record.TombstonedAt = null;
        record.TombstoneReason = "";

        record.Rating = status == "wish" ? null : result.Rating;
        if (request.SetComment) record.Comment = result.Review;
        else if (!string.IsNullOrWhiteSpace(result.Review)) record.Comment = result.Review;

        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["wish"] = "想看",
            ["do"] = "在看",
            ["collect"] = "看过"
        };
        var options = (result.StatusOptions is { Count: > 0 } ? result.StatusOptions : record.DoubanStatusOptions)
            .Where(option => labels.Values.Contains(option.Text, StringComparer.Ordinal))
            .ToList();
        if (!options.Any(option => string.Equals(option.Text, labels[status], StringComparison.Ordinal)))
            options.Add(new DoubanStatusOption(labels[status], false));
        record.DoubanStatusOptions = options
            .Select(option => option with { Selected = string.Equals(option.Text, labels[status], StringComparison.Ordinal) })
            .ToList();
        if (result.StatusOptions is { Count: > 0 })
        {
            record.DoubanStatusCapabilitiesKnown = true;
            record.DoubanStatusCapabilitySource = "confirmed-write-form";
            record.DoubanStatusCapabilityError = "";
        }
    }

    private static object StatusDto(DoubanStatusOption status) => new { text = status.Text, selected = status.Selected };

    private DoubanHistoryRecord FindOrCreateRecord(string subjectId, string subjectUrl)
    {
        DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "HTML FindOrCreate request");
        if (_history.Items.TryGetValue(subjectId, out var historyRecord))
        {
            DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "HTML FindOrCreate history", historyRecord);
            return historyRecord;
        }
        if (!_searchCache.Items.TryGetValue(subjectId, out var record))
        {
            record = new DoubanHistoryRecord { SubjectId = subjectId, SubjectUrl = subjectUrl, Status = "search", ImportedAt = DateTime.Now };
            _searchCache.Items[subjectId] = record;
        }
        DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "HTML FindOrCreate search-cache", record);
        record.SubjectId = subjectId;
        record.SubjectUrl = subjectUrl;
        return record;
    }

    private static void ApplyMetadata(DoubanHistoryRecord record, DoubanSubjectMetadataResult metadata)
    {
        record.Title = metadata.Title.Trim();
        if (metadata.Score is not null) record.DoubanScore = metadata.Score;
        Copy(metadata.Poster, value => record.PosterUrl = value); Copy(metadata.Year, value => record.Year = value); Copy(metadata.Genres, value => record.Genres = value);
        Copy(metadata.Directors, value => record.Directors = value); Copy(metadata.Runtime, value => record.Runtime = value);
        Copy(metadata.Countries, value => record.Countries = value); Copy(metadata.ImdbId, value => record.ImdbId = value); Copy(metadata.Summary, value => record.Summary = value);
        if (metadata.StatusCapabilitiesKnown)
        {
            if (metadata.StatusOptions.Count > 0) record.DoubanStatusOptions = metadata.StatusOptions;
            record.DoubanStatusCapabilitiesKnown = true;
            record.DoubanStatusCapabilitySource = metadata.StatusCapabilitySource;
            record.DoubanStatusCapabilityError = metadata.StatusCapabilityError;
        }
        else if (record.DoubanStatusOptions.Count == 0 && metadata.StatusOptions.Count > 0)
        {
            // Detail-page chips are useful as a display fallback, but must not downgrade a previously
            // confirmed official-form capability snapshot.
            record.DoubanStatusOptions = metadata.StatusOptions;
        }
        record.DetailMetadataFetched = DoubanMediaParser.HasCompleteDetailMetadata(record);
        record.FullDetailsFetchedAt = DateTime.Now;
        record.FullDetailsLastError = "";
    }

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

            if (_doubanSubjectRecoveryAttempts < 1 && IsDoubanSubjectPageUrl(_activeDoubanSubjectNavigationUrl))
            {
                _doubanSubjectRecoveryAttempts++;
                _doubanSubjectRecoveryUrl = _activeDoubanSubjectNavigationUrl;
                ShowDoubanNavigationOverlay("影片详情正在恢复，请稍候…");
                _ = RetryDoubanSubjectNavigationAsync(_doubanSubjectRecoveryUrl, e.NavigationId);
            }
            else
            {
                ShowDoubanNavigationOverlay("影片详情加载失败，请右键刷新或返回列表。");
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
  const ready = Boolean(root && document.body && probe.importResolved === true && probe.importRejected !== true &&
    document.body.classList.contains("atv-enhanced") && (!wrapper || getComputedStyle(wrapper).display === "none") &&
    style && style.display !== "none" && style.visibility !== "hidden" && Number(style.opacity) >= 0.98 &&
    rect && rect.width > 0 && rect.height > 0 && root.querySelector(".atv-hero-title"));
  return { ready, href: location.href, importResolved: probe.importResolved === true };
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

    private async Task ForwardDoubanSourceResultToShellAsync(JsonElement root, string operation = "")
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Source DOM 读取结果不是 JSON 对象。");
        var sourceUrl = ReadString(root, "url");
        if (!IsAllowedDoubanSourceUrl(sourceUrl)) throw new InvalidDataException("Source 返回的豆瓣列表地址无效。");
        var items = root.TryGetProperty("items", out var itemsValue) && itemsValue.ValueKind == JsonValueKind.Array
            ? itemsValue.Clone()
            : JsonSerializer.SerializeToElement(Array.Empty<object>());
        // Keep poster URLs as read from the real Douban DOM. The unified Shell
        // loads them directly; list forwarding must not download every
        // cumulative poster again on each paging operation.
        var error = ReadString(root, "error");
        var shellMessage = JsonSerializer.Serialize(new
        {
            type = "doubanShellData",
            requestId = ReadString(root, "requestId"),
            mode = ReadString(root, "mode"),
            generation = root.TryGetProperty("generation", out var generation) && generation.TryGetInt32(out var value) ? value : 0,
            contentType = ReadContentType(root, sourceUrl),
            personalStatus = ReadString(root, "personalStatus"),
            query = ReadString(root, "query"),
            searchPageUrl = IsDoubanSearchPageUrl(sourceUrl) ? sourceUrl : "",
            items,
            filters = root.TryGetProperty("filters", out var filtersValue) ? filtersValue.Clone() : JsonSerializer.SerializeToElement(new { }),
            paging = root.TryGetProperty("paging", out var pagingValue) ? pagingValue.Clone() : JsonSerializer.SerializeToElement(new { hasMore = false }),
            dom = root.TryGetProperty("dom", out var shellDomValue) ? shellDomValue.Clone() : JsonSerializer.SerializeToElement(new { }),
            searchPageLinks = root.TryGetProperty("searchPageLinks", out var searchPageLinksValue) ? searchPageLinksValue.Clone() : JsonSerializer.SerializeToElement(Array.Empty<object>()),
            operation,
            error
        });
        _pendingShellDataJson = shellMessage;
        PostPendingShellDataIfReady();
        var dom = root.TryGetProperty("dom", out var domValue) ? domValue.ToString() : "{}";
        DiagnosticLogger.Write($"Unified Shell Source DOM JSON forwarded; Url={sourceUrl}; Items={items.GetArrayLength()}; Error={error}; Dom={dom}; Generation={_doubanSourceGeneration}");
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task<JsonElement> ExecuteDoubanSourceBridgeAsync(string method, object? payload = null)
    {
        if (_doubanSourceView.CoreWebView2 is null) throw new InvalidOperationException("豆瓣 Source WebView 尚未准备好。");
        var request = JsonSerializer.Serialize(payload ?? new { });
        var bridgeName = IsAllowedDoubanPersonalUrl(_activeDoubanSourceNavigationUrl) ? "QbDoubanPersonalSourceBridge" : "QbDoubanSourceBridge";
        var script = $"(() => {{ try {{ return window.{bridgeName} && typeof window.{bridgeName}.{method} === 'function' ? window.{bridgeName}.{method}({request}) : {{ ok: false, error: 'Source bridge 方法不存在。' }}; }} catch (error) {{ return {{ ok: false, error: String(error?.message || error) }}; }} }})()";
        var raw = await _doubanSourceView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
        using var document = JsonDocument.Parse(raw);
        var result = document.RootElement.Clone();
        if (result.ValueKind == JsonValueKind.String)
        {
            using var nested = JsonDocument.Parse(result.GetString() ?? "{}");
            result = nested.RootElement.Clone();
        }
        return result;
    }

    private async Task<JsonElement> ReadDoubanSourcePageAsync(string requestId, int generation)
    {
        var result = await ExecuteDoubanSourceBridgeAsync("readPage", new
        {
            requestId,
            mode = DoubanSourceModeForUrl(_activeDoubanSourceNavigationUrl),
            generation
        }).ConfigureAwait(true);
        return result;
    }

    private static JsonElement ReadSourceFilters(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("filters", out var filters) && filters.ValueKind == JsonValueKind.Object
            ? filters.Clone()
            : JsonSerializer.SerializeToElement(new { });

    private static JsonElement ReadSourceGroupOptions(JsonElement filters, string title)
    {
        if (filters.ValueKind != JsonValueKind.Object || !filters.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return JsonSerializer.SerializeToElement(Array.Empty<object>());
        foreach (var group in groups.EnumerateArray())
        {
            if (ReadString(group, "title").Equals(title, StringComparison.Ordinal) &&
                group.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                return options.Clone();
        }
        return JsonSerializer.SerializeToElement(Array.Empty<object>());
    }

    private async Task HandleDoubanShellContentTypeAsync(JsonElement root)
    {
        DeactivateFrodoPersonal("content-type");
        var contentType = ReadString(root, "contentType").Trim();
        if (contentType is not ("movie" or "tv"))
            throw new InvalidDataException("豆瓣探索内容类型无效。");

        var requestId = ReadString(root, "requestId");
        var targetUrl = contentType == "tv" ? "https://movie.douban.com/tv/" : "https://movie.douban.com/explore";
        var mode = DoubanExploreModeForUrl(targetUrl);
        PostShellMessage(new { type = "doubanShellContentTypeState", busy = true, contentType, operation = "content-type" });

        if (AreEquivalentDoubanNavigationUrls(_activeDoubanSourceNavigationUrl, targetUrl) && _doubanSourceNavigationCompleted)
        {
            var generation = Interlocked.Increment(ref _doubanSourceGeneration);
            var page = await ReadDoubanSourcePageAsync(string.IsNullOrWhiteSpace(requestId) ? $"{mode}-{generation}" : requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, "content-type-noop").ConfigureAwait(true);
            DiagnosticLogger.Write($"Unified Shell content type no-op; ContentType={contentType}; Url={targetUrl}; Generation={generation}");
            return;
        }

        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _doubanSourceNavigationCompleted = false;
        _activeDoubanPersonalPageUrl = "";
        var navigationAttempt = Interlocked.Increment(ref _doubanSourceNavigationAttempt);
        _doubanSourceView.CoreWebView2!.Navigate(targetUrl);
        _ = MonitorDoubanSourceContentTypeNavigationAsync(contentType, targetUrl, navigationAttempt);
        DiagnosticLogger.Write($"Unified Shell content type navigation; ContentType={contentType}; Mode={mode}; Url={targetUrl}");
    }

    private async Task HandleDoubanShellSearchAsync(JsonElement root)
    {
        DeactivateFrodoPersonal("search");
        var query = ReadString(root, "query").Trim();
        var requestId = ReadString(root, "requestId");
        if (query.Length is 0 or > 160) throw new InvalidDataException("搜索关键词不能为空，且不能超过 160 个字符。");
        var targetUrl = $"https://search.douban.com/movie/subject_search?search_text={Uri.EscapeDataString(query)}";
        _pendingDoubanSourceReadRequestId = "";
        _pendingDoubanSourceReadOperation = "";
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "search" });

        if (AreEquivalentDoubanNavigationUrls(_activeDoubanSourceNavigationUrl, targetUrl) && _doubanSourceNavigationCompleted)
        {
            var generation = Interlocked.Increment(ref _doubanSourceGeneration);
            var page = await ReadDoubanSourcePageAsync(string.IsNullOrWhiteSpace(requestId) ? $"search-{generation}" : requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, "search-noop").ConfigureAwait(true);
            DiagnosticLogger.Write($"Unified Shell search no-op; Query={query}; Url={targetUrl}; Generation={generation}");
            return;
        }

        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _activeDoubanPersonalPageUrl = "";
        _doubanSourceNavigationCompleted = false;
        Interlocked.Increment(ref _doubanSourceNavigationAttempt);
        _doubanSourceView.CoreWebView2!.Navigate(targetUrl);
        DiagnosticLogger.Write($"Unified Shell native Douban search navigation; Query={query}; Url={targetUrl}");
    }

    private async Task HandleDoubanShellSearchPageAsync(JsonElement root)
    {
        DeactivateFrodoPersonal("search-page");
        var targetUrl = ReadString(root, "url").Trim();
        var requestId = ReadString(root, "requestId");
        var append = ReadBool(root, "append");
        if (!IsDoubanSearchPageUrl(targetUrl)) throw new InvalidDataException("豆瓣搜索分页地址无效。");

        _pendingDoubanSourceReadRequestId = append ? requestId : "";
        _pendingDoubanSourceReadOperation = append ? "load-more" : "";
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "search-page" });
        if (AreEquivalentDoubanNavigationUrls(_activeDoubanSourceNavigationUrl, targetUrl) && _doubanSourceNavigationCompleted)
        {
            var generation = Interlocked.Increment(ref _doubanSourceGeneration);
            var page = await ReadDoubanSourcePageAsync(string.IsNullOrWhiteSpace(requestId) ? $"search-page-{generation}" : requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, append ? "load-more" : "search-page-noop").ConfigureAwait(true);
            _pendingDoubanSourceReadRequestId = "";
            _pendingDoubanSourceReadOperation = "";
            return;
        }

        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _activeDoubanPersonalPageUrl = "";
        _doubanSourceNavigationCompleted = false;
        Interlocked.Increment(ref _doubanSourceNavigationAttempt);
        _doubanSourceView.CoreWebView2!.Navigate(targetUrl);
        DiagnosticLogger.Write($"Unified Shell native Douban search page navigation; Url={targetUrl}");
    }

    private async Task HandleDoubanShellPersonalStatusAsync(JsonElement root)
    {
        var status = ReadString(root, "status").Trim();
        if (status is not ("collect" or "wish" or "do"))
            throw new InvalidDataException("豆瓣个人影片状态无效。");

        var requestId = ReadString(root, "requestId");
        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        var candidateProfileId = "";
        if (FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var activeProfileId, out _))
            candidateProfileId = activeProfileId;
        if (string.IsNullOrWhiteSpace(candidateProfileId) || !candidateProfileId.All(char.IsDigit))
            candidateProfileId = _frodoPersonalIndex.CurrentProfileId;

        // Local-first invariant: if a usable Store exists, no WebView/session/network
        // check is allowed to delay a status switch. Disk cache loading is only needed
        // when this profile is not already resident in memory.
        if (!string.IsNullOrWhiteSpace(candidateProfileId) && candidateProfileId.All(char.IsDigit))
        {
            if (!_frodoPersonalIndex.CurrentProfileId.Equals(candidateProfileId, StringComparison.Ordinal))
                await _frodoPersonalIndex.LoadCacheAsync(candidateProfileId).ConfigureAwait(true);

            var localTargetUrl = $"https://movie.douban.com/people/{candidateProfileId}/{status}";
            _activeDoubanPlusNavigationUrl = localTargetUrl;
            _activeDoubanSourceNavigationUrl = localTargetUrl;
            _activeDoubanPersonalPageUrl = localTargetUrl;
            _doubanSourceNavigationCompleted = false;
            _frodoPersonalActive = true;
            _frodoPersonalQuery.Reset();
            var localRequestId = string.IsNullOrWhiteSpace(requestId) ? $"personal-{status}-{generation}" : requestId;
            PostShellMessage(new { type = "doubanShellPersonalState", busy = true, personalStatus = status, operation = "personal-status" });

            if (await TryRenderFrodoPersonalStoreAsync(candidateProfileId, status, localRequestId, generation, "personal-status-local").ConfigureAwait(true))
            {
                _ = SyncFrodoPersonalStoreInBackgroundAsync(candidateProfileId, status, "personal-status");
                DiagnosticLogger.Write($"Unified Shell personal status loaded; Source=FrodoLocalStore; ProfileId={candidateProfileId}; Status={status}; Url={localTargetUrl}; Generation={generation}");
                return;
            }
        }

        // A genuinely missing status needs the authenticated profile bootstrap path.
        await WaitForDoubanRecoveryAsync().ConfigureAwait(true);
        var session = await _workerConnector.VerifySessionAsync().ConfigureAwait(true);
        var profileId = session.ProfileId?.Trim() ?? "";
        if (!session.IsLoggedIn || !System.Text.RegularExpressions.Regex.IsMatch(profileId, "^\\d+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidOperationException("豆瓣尚未登录，请先点击“豆瓣登录”。");

        var targetUrl = $"https://movie.douban.com/people/{profileId}/{status}";
        var resolvedRequestId = string.IsNullOrWhiteSpace(requestId) ? $"personal-{status}-{generation}" : requestId;
        PostShellMessage(new { type = "doubanShellPersonalState", busy = true, personalStatus = status, operation = "personal-status" });
        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _activeDoubanPersonalPageUrl = targetUrl;
        _doubanSourceNavigationCompleted = false;
        _frodoPersonalActive = true;
        _frodoPersonalQuery.Reset();

        await _frodoPersonalIndex.LoadCacheAsync(profileId).ConfigureAwait(true);
        if (await TryRenderFrodoPersonalStoreAsync(profileId, status, resolvedRequestId, generation, "personal-status-local-cache").ConfigureAwait(true))
        {
            _ = SyncFrodoPersonalStoreInBackgroundAsync(profileId, status, "personal-status-cache");
            DiagnosticLogger.Write($"Unified Shell personal status loaded; Source=FrodoLocalStore; ProfileId={profileId}; Status={status}; Url={targetUrl}; Generation={generation}");
            return;
        }

        // First install / genuinely missing status only: show the first Frodo page
        // immediately, then bootstrap the complete Store in the background.
        try
        {
            var page = await _frodoPersonalProvider.LoadInitialAsync(profileId, status, targetUrl, resolvedRequestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, "personal-status").ConfigureAwait(true);
            _ = EnsureFrodoPersonalIndexAsync(profileId, status, "personal-status-bootstrap");
            DiagnosticLogger.Write($"Unified Shell personal status loaded; Source=FrodoBootstrap; ProfileId={profileId}; Status={status}; Url={targetUrl}; Generation={generation}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException)
        {
            DiagnosticLogger.Write($"Unified Shell personal Frodo failed; ProfileId={profileId}; Status={status}; Url={targetUrl}; Fallback=DOM; Error={ex.Message}");
            NavigatePersonalDomFallback(targetUrl, resolvedRequestId, "personal-status-fallback", "frodo-initial-failed");
        }
    }
    private async Task HandleDoubanShellApplyPersonalFilterAsync(JsonElement root)
    {
        var targetUrl = ReadString(root, "url").Trim();
        var requestId = ReadString(root, "requestId");
        if (!IsAllowedDoubanPersonalUrl(targetUrl) || !IsSameDoubanPersonalScope(_activeDoubanSourceNavigationUrl, targetUrl))
            throw new InvalidDataException("豆瓣个人筛选地址无效，或筛选范围已离开当前状态。");

        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        var resolvedRequestId = string.IsNullOrWhiteSpace(requestId) ? $"personal-filter-{generation}" : requestId;
        _frodoPersonalQuery.Reset();
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "personal-filter" });

        if (FrodoPersonalProvider.TryReadScope(targetUrl, out var profileId, out var status) &&
            FrodoPersonalProvider.IsDefaultPersonalUrl(targetUrl, profileId, status))
        {
            _activeDoubanPlusNavigationUrl = targetUrl;
            _activeDoubanSourceNavigationUrl = targetUrl;
            _activeDoubanPersonalPageUrl = targetUrl;
            _doubanSourceNavigationCompleted = false;
            _frodoPersonalActive = true;
            try
            {
                var page = await _frodoPersonalProvider.LoadInitialAsync(profileId, status, targetUrl, resolvedRequestId, generation).ConfigureAwait(true);
                await ForwardDoubanSourceResultToShellAsync(page, "personal-filter").ConfigureAwait(true);
                _ = EnsureFrodoPersonalIndexAsync(profileId, status, "personal-filter-default");
                DiagnosticLogger.Write($"Unified Shell personal filter returned to default; Source=Frodo; ProfileId={profileId}; Status={status}; Url={targetUrl}; Generation={generation}");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException)
            {
                DiagnosticLogger.Write($"Unified Shell personal default filter Frodo failed; Url={targetUrl}; Fallback=DOM; Error={ex.Message}");
            }
        }

        NavigatePersonalDomFallback(targetUrl, resolvedRequestId, "personal-filter", "non-default-filter-or-frodo-failed");
        DiagnosticLogger.Write($"Unified Shell personal filter navigation; Source=DOM; Url={targetUrl}; Generation={generation}");
    }

    private async Task<bool> TryRenderFrodoPersonalStoreAsync(
        string profileId,
        string status,
        string requestId,
        int generation,
        string operation)
    {
        if (!_frodoPersonalIndex.TryGetStatus(profileId, status, out var snapshot)) return false;
        var criteria = new FrodoPersonalFilterCriteria();
        var result = _frodoPersonalIndex.Query(profileId, status, criteria);
        _frodoPersonalQuery.Start(profileId, status, criteria, result);
        var firstPage = _frodoPersonalQuery.TakeInitial();
        var page = BuildFrodoPersonalQueryPayload(requestId, generation, firstPage, snapshot);
        await ForwardDoubanSourceResultToShellAsync(page, operation).ConfigureAwait(true);
        DiagnosticLogger.Write($"Frodo personal Store rendered immediately; ProfileId={profileId}; Status={status}; StoreItems={snapshot.Items.Count}; CloudTotal={snapshot.Total}; Shown={_frodoPersonalQuery.Shown}");
        return true;
    }

    private async Task SyncFrodoPersonalStoreInBackgroundAsync(string profileId, string status, string reason)
    {
        try
        {
            var snapshot = await _frodoPersonalIndex.SyncStatusHeadAsync(profileId, status).ConfigureAwait(true);
            if (snapshot is null || !IsCurrentFrodoPersonal(profileId, status)) return;

            var criteria = _frodoPersonalQuery.IsActiveFor(profileId, status)
                ? _frodoPersonalQuery.Criteria
                : new FrodoPersonalFilterCriteria();
            var shownBefore = _frodoPersonalQuery.IsActiveFor(profileId, status)
                ? _frodoPersonalQuery.Shown
                : 20;
            var result = _frodoPersonalIndex.Query(profileId, status, criteria);
            _frodoPersonalQuery.Start(profileId, status, criteria, result);
            var firstPage = _frodoPersonalQuery.TakeInitial();
            while (_frodoPersonalQuery.Shown < Math.Min(shownBefore, _frodoPersonalQuery.Total))
                _frodoPersonalQuery.TakeNext();

            if (shownBefore <= 20)
            {
                var generation = Interlocked.Increment(ref _doubanSourceGeneration);
                var requestId = $"personal-background-sync-{generation}";
                var page = BuildFrodoPersonalQueryPayload(requestId, generation, firstPage, snapshot);
                await ForwardDoubanSourceResultToShellAsync(page, "personal-background-sync").ConfigureAwait(true);
            }
            else
            {
                PostFrodoPersonalFilterState(
                    profileId,
                    status,
                    snapshot,
                    criteria,
                    false,
                    snapshot.Items.Count,
                    snapshot.Total,
                    result.Count,
                    _frodoPersonalQuery.Shown,
                    "");
            }

            DiagnosticLogger.Write($"Frodo personal background bounded sync completed; ProfileId={profileId}; Status={status}; Reason={reason}; StoreItems={snapshot.Items.Count}; Total={snapshot.Total}; ShownPreserved={_frodoPersonalQuery.Shown}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            DiagnosticLogger.Write($"Frodo personal background bounded sync deferred; ProfileId={profileId}; Status={status}; Reason={reason}; Error={ex.Message}; StorePreserved=True");
        }
    }

    private void DeactivateFrodoPersonal(string reason)
    {
        if (_frodoPersonalActive)
            DiagnosticLogger.Write($"Unified Shell Frodo personal deactivated; Reason={reason}; Url={_activeDoubanSourceNavigationUrl}");
        _frodoPersonalActive = false;
        _frodoPersonalQuery.Reset();
        _frodoPersonalProvider.Reset();
    }

    private void NavigatePersonalDomFallback(string targetUrl, string requestId, string operation, string reason)
    {
        DeactivateFrodoPersonal(reason);
        _pendingDoubanSourceReadRequestId = requestId;
        _pendingDoubanSourceReadOperation = operation;
        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _activeDoubanPersonalPageUrl = targetUrl;
        _doubanSourceNavigationCompleted = false;
        Interlocked.Increment(ref _doubanSourceNavigationAttempt);
        _doubanSourceView.CoreWebView2!.Navigate(targetUrl);
        DiagnosticLogger.Write($"Unified Shell personal source fallback; Source=DOM; Reason={reason}; Url={targetUrl}; RequestId={requestId}");
    }

    private async Task RefreshFrodoPersonalAsync(string reason)
    {
        if (!_frodoPersonalActive ||
            !FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var profileId, out var status)) return;

        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        var requestId = $"personal-refresh-{generation}";
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "personal-refresh" });

        if (_frodoPersonalIndex.TryGetStatus(profileId, status, out _))
        {
            try
            {
                await _frodoPersonalIndex.SyncStatusHeadAsync(profileId, status).ConfigureAwait(true);
                if (!_frodoPersonalIndex.TryGetStatus(profileId, status, out var refreshed)) return;
                var criteria = _frodoPersonalQuery.IsActiveFor(profileId, status)
                    ? _frodoPersonalQuery.Criteria
                    : new FrodoPersonalFilterCriteria();
                var result = _frodoPersonalIndex.Query(profileId, status, criteria);
                _frodoPersonalQuery.Start(profileId, status, criteria, result);
                var firstPage = _frodoPersonalQuery.TakeInitial();
                var localPage = BuildFrodoPersonalQueryPayload(requestId, generation, firstPage, refreshed);
                await ForwardDoubanSourceResultToShellAsync(localPage, "personal-refresh").ConfigureAwait(true);
                DiagnosticLogger.Write($"Unified Shell personal refresh completed; Source=FrodoLocalStore+BoundedSync; Reason={reason}; ProfileId={profileId}; Status={status}; Matched={result.Count}; Generation={generation}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
            {
                // Refresh failure never destroys or abandons a usable Store.
                DiagnosticLogger.Write($"Unified Shell personal bounded refresh deferred; Reason={reason}; ProfileId={profileId}; Status={status}; Error={ex.Message}; StorePreserved=True");
                PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "personal-refresh" });
            }
            return;
        }

        _frodoPersonalQuery.Reset();
        var targetUrl = $"https://movie.douban.com/people/{profileId}/{status}";
        try
        {
            var page = await _frodoPersonalProvider.LoadInitialAsync(profileId, status, targetUrl, requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, "personal-refresh").ConfigureAwait(true);
            _ = EnsureFrodoPersonalIndexAsync(profileId, status, "personal-refresh-bootstrap");
            DiagnosticLogger.Write($"Unified Shell personal refresh completed; Source=FrodoBootstrap; Reason={reason}; Url={targetUrl}; Generation={generation}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException)
        {
            DiagnosticLogger.Write($"Unified Shell personal refresh Frodo failed; Reason={reason}; Url={targetUrl}; Fallback=DOM; Error={ex.Message}");
            NavigatePersonalDomFallback(targetUrl, requestId, "personal-refresh-fallback", "frodo-refresh-failed");
        }
    }
    private async Task MonitorDoubanSourceContentTypeNavigationAsync(string contentType, string targetUrl, int navigationAttempt)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!_closing &&
               navigationAttempt == Volatile.Read(ref _doubanSourceNavigationAttempt) &&
               AreEquivalentDoubanNavigationUrls(_activeDoubanSourceNavigationUrl, targetUrl) &&
               !_doubanSourceNavigationCompleted)
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= DoubanSourceDomWaitTimeout)
            {
                DiagnosticLogger.Write($"Unified Shell content type navigation timeout; ContentType={contentType}; Url={targetUrl}; Attempt={navigationAttempt}; NavigationVersion={_doubanSourceNavigationVersion}");
                PostShellMessage(new
                {
                    type = "doubanShellContentTypeError",
                    contentType,
                    error = "豆瓣探索页面加载超时，请重试。"
                });
                return;
            }
            await Task.Delay(DoubanSourceDomPollInterval).ConfigureAwait(true);
        }
    }

    private async Task<JsonElement> WaitForDoubanSourcePageAsync(string requestId, int generation, string beforeSignature, string operation)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        JsonElement result = default;
        while (true)
        {
            result = await ReadDoubanSourcePageAsync(requestId, generation).ConfigureAwait(true);
            var signature = ReadString(result, "signature");
            var itemCount = result.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array ? items.GetArrayLength() : 0;
            var pageReady = ReadBool(result, "pageReady");
            if ((itemCount > 0 || (IsAllowedDoubanPersonalUrl(_activeDoubanSourceNavigationUrl) && pageReady)) &&
                (beforeSignature.Length == 0 || !signature.Equals(beforeSignature, StringComparison.Ordinal))) break;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= DoubanSourceDomWaitTimeout) break;
            await Task.Delay(DoubanSourceDomPollInterval).ConfigureAwait(true);
        }
        DiagnosticLogger.Write($"Unified Shell Source operation settled; Operation={operation}; RequestId={requestId}; Signature={ReadString(result, "signature")}; Items={(result.TryGetProperty("items", out var settledItems) && settledItems.ValueKind == JsonValueKind.Array ? settledItems.GetArrayLength() : 0)}");
        return result;
    }

    private bool IsCurrentFrodoPersonal(string profileId, string status) =>
        !_closing && _frodoPersonalActive &&
        FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var activeProfileId, out var activeStatus) &&
        activeProfileId.Equals(profileId, StringComparison.Ordinal) &&
        activeStatus.Equals(status, StringComparison.Ordinal);

    private async Task EnsureFrodoPersonalIndexAsync(string profileId, string status, string reason)
    {
        try
        {
            await _frodoPersonalIndex.LoadCacheAsync(profileId).ConfigureAwait(true);
            if (_frodoPersonalIndex.TryGetStatus(profileId, status, out var cached))
            {
                if (IsCurrentFrodoPersonal(profileId, status))
                    PostFrodoPersonalFilterState(profileId, status, cached, new FrodoPersonalFilterCriteria(), false, cached.Items.Count, cached.Items.Count, cached.Items.Count, 0, "");
                return;
            }

            var key = $"{profileId}:{status}";
            lock (_frodoPersonalIndexBuildGate)
            {
                if (!_frodoPersonalIndexBuilds.Add(key)) return;
            }

            try
            {
                if (IsCurrentFrodoPersonal(profileId, status))
                    PostFrodoPersonalFilterState(profileId, status, null, new FrodoPersonalFilterCriteria(), true, 0, 0, 0, 0, "");

                var progress = new Progress<FrodoPersonalIndexProgress>(value =>
                {
                    if (!IsCurrentFrodoPersonal(profileId, status)) return;
                    PostFrodoPersonalFilterState(profileId, status, null, new FrodoPersonalFilterCriteria(), true, value.Loaded, value.Total, 0, 0, "");
                });
                var built = await _frodoPersonalIndex.BootstrapStatusAsync(profileId, status, progress).ConfigureAwait(true);
                if (IsCurrentFrodoPersonal(profileId, status))
                    PostFrodoPersonalFilterState(profileId, status, built, new FrodoPersonalFilterCriteria(), false, built.Items.Count, built.Items.Count, built.Items.Count, 0, "");
                DiagnosticLogger.Write($"Frodo personal local filter index ready; ProfileId={profileId}; Status={status}; Items={built.Items.Count}; Reason={reason}");
            }
            finally
            {
                lock (_frodoPersonalIndexBuildGate) _frodoPersonalIndexBuilds.Remove(key);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            DiagnosticLogger.Write($"Frodo personal local filter index failed; ProfileId={profileId}; Status={status}; Reason={reason}; Error={ex.Message}");
            if (IsCurrentFrodoPersonal(profileId, status))
                PostFrodoPersonalFilterState(profileId, status, null, new FrodoPersonalFilterCriteria(), false, 0, 0, 0, 0, ex.Message);
        }
    }

    private void PostFrodoPersonalFilterState(
        string profileId,
        string status,
        FrodoPersonalIndexStatus? snapshot,
        FrodoPersonalFilterCriteria criteria,
        bool building,
        int loaded,
        int sourceTotal,
        int matched,
        int shown,
        string error)
    {
        PostShellMessage(new
        {
            type = "doubanShellLocalPersonalFilters",
            personalStatus = status,
            filters = BuildFrodoPersonalFilterState(profileId, status, snapshot, criteria, building, loaded, sourceTotal, matched, shown, error)
        });
    }

    private static object BuildFrodoPersonalFilterState(
        string profileId,
        string status,
        FrodoPersonalIndexStatus? snapshot,
        FrodoPersonalFilterCriteria criteria,
        bool building,
        int loaded,
        int sourceTotal,
        int matched,
        int shown,
        string error)
    {
        return new
        {
            source = "frodo-local",
            ready = snapshot is not null,
            building,
            loaded,
            sourceTotal,
            total = snapshot?.Items.Count ?? 0,
            matched,
            shown,
            error,
            criteria = new
            {
                contentType = criteria.ContentType,
                playableOnly = criteria.PlayableOnly,
                scoreMin = criteria.ScoreMin,
                scoreMax = criteria.ScoreMax,
                myRating = criteria.MyRating,
                unrated = criteria.Unrated,
                period = criteria.Period,
                genre = criteria.Genre,
                country = criteria.Country,
                sort = criteria.Sort
            },
            facets = new
            {
                years = snapshot?.Years ?? new List<string>(),
                genres = snapshot?.Genres ?? new List<string>(),
                countries = snapshot?.Countries ?? new List<string>()
            }
        };
    }
    private async Task HandleDoubanShellApplyLocalPersonalFilterAsync(JsonElement root)
    {
        if (!_frodoPersonalActive ||
            !FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var profileId, out var status))
            throw new InvalidOperationException("当前个人页不在 Frodo 默认数据源，无法应用完整库本地筛选。");
        if (!_frodoPersonalIndex.TryGetStatus(profileId, status, out var snapshot))
        {
            _ = EnsureFrodoPersonalIndexAsync(profileId, status, "filter-request-index-missing");
            throw new InvalidOperationException("完整个人库筛选索引仍在建立，请稍后再试。");
        }

        var criteria = ReadFrodoPersonalFilterCriteria(root);
        var requestId = ReadString(root, "requestId");
        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        var resolvedRequestId = string.IsNullOrWhiteSpace(requestId) ? $"personal-local-filter-{generation}" : requestId;
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "personal-local-filter" });

        var result = _frodoPersonalIndex.Query(profileId, status, criteria);
        _frodoPersonalQuery.Start(profileId, status, criteria, result);
        var firstPage = _frodoPersonalQuery.TakeInitial();
        var page = BuildFrodoPersonalQueryPayload(resolvedRequestId, generation, firstPage, snapshot);
        await ForwardDoubanSourceResultToShellAsync(page, "personal-local-filter").ConfigureAwait(true);
        DiagnosticLogger.Write($"Frodo personal local filter applied; ProfileId={profileId}; Status={status}; Matched={result.Count}; Shown={_frodoPersonalQuery.Shown}; ContentType={criteria.ContentType}; PlayableOnly={criteria.PlayableOnly}; ScoreMin={criteria.ScoreMin?.ToString("0.0") ?? "all"}; ScoreMax={criteria.ScoreMax?.ToString("0.0") ?? "all"}; Rating={criteria.MyRating?.ToString() ?? (criteria.Unrated ? "unrated" : "all")}; Period={criteria.Period}; Genre={criteria.Genre}; Country={criteria.Country}; Sort={criteria.Sort}");
    }

    private static FrodoPersonalFilterCriteria ReadFrodoPersonalFilterCriteria(JsonElement root)
    {
        if (!root.TryGetProperty("criteria", out var criteria) || criteria.ValueKind != JsonValueKind.Object)
            return new FrodoPersonalFilterCriteria();

        var contentType = ReadString(criteria, "contentType").Trim().ToLowerInvariant();
        if (contentType is not ("" or "movie" or "tv")) throw new InvalidDataException("影片类型筛选无效。");

        var playableOnly = ReadBool(criteria, "playableOnly");
        double? scoreMin = null;
        double? scoreMax = null;
        if (criteria.TryGetProperty("scoreMin", out var scoreMinValue) && scoreMinValue.ValueKind == JsonValueKind.Number && scoreMinValue.TryGetDouble(out var parsedMin))
            scoreMin = parsedMin;
        if (criteria.TryGetProperty("scoreMax", out var scoreMaxValue) && scoreMaxValue.ValueKind == JsonValueKind.Number && scoreMaxValue.TryGetDouble(out var parsedMax))
            scoreMax = parsedMax;
        if ((scoreMin is not null && (scoreMin.Value < 0 || scoreMin.Value > 10)) ||
            (scoreMax is not null && (scoreMax.Value < 0 || scoreMax.Value > 10)) ||
            (scoreMin is not null && scoreMax is not null && scoreMin.Value > scoreMax.Value))
            throw new InvalidDataException("豆瓣评分区间无效。");

        var myRating = ReadNullableInt(criteria, "myRating", 1, 5);
        var unrated = ReadBool(criteria, "unrated");
        if (unrated) myRating = null;

        var period = ReadBoundedString(criteria, "period", 24).Trim().ToLowerInvariant();
        if (period.Length > 0)
        {
            var parts = period.Split(':', 2, StringSplitOptions.TrimEntries);
            var kindValid = parts.Length == 2 && (parts[0] == "year" || parts[0] == "decade");
            if (!kindValid || !int.TryParse(parts[1], out var periodValue) ||
                (parts[0] == "year" && (periodValue < 1880 || periodValue > 2100)) ||
                (parts[0] == "decade" && (periodValue < 1880 || periodValue > 2100 || periodValue % 10 != 0)))
                throw new InvalidDataException("年代筛选无效。");
        }

        var genre = ReadBoundedString(criteria, "genre", 80);
        var country = ReadBoundedString(criteria, "country", 80);
        var sort = ReadString(criteria, "sort").Trim();
        if (string.IsNullOrWhiteSpace(sort)) sort = "marked-desc";
        if (sort is not ("marked-desc" or "my-rating-desc" or "douban-score-desc" or "year-desc" or "title-asc"))
            throw new InvalidDataException("个人库排序方式无效。");

        return new FrodoPersonalFilterCriteria(contentType, playableOnly, scoreMin, scoreMax, myRating, unrated, period, genre, country, sort);
    }
    private JsonElement BuildFrodoPersonalQueryPayload(
        string requestId,
        int generation,
        IReadOnlyList<FrodoPersonalItem> items,
        FrodoPersonalIndexStatus snapshot)
    {
        var filters = BuildFrodoPersonalFilterState(
            _frodoPersonalQuery.ProfileId,
            _frodoPersonalQuery.Status,
            snapshot,
            _frodoPersonalQuery.Criteria,
            false,
            snapshot.Items.Count,
            snapshot.Items.Count,
            _frodoPersonalQuery.Total,
            _frodoPersonalQuery.Shown,
            "");
        return JsonSerializer.SerializeToElement(new
        {
            requestId,
            mode = $"personal-{_frodoPersonalQuery.Status}",
            generation,
            url = _activeDoubanSourceNavigationUrl,
            contentType = "personal",
            personalStatus = _frodoPersonalQuery.Status,
            profileId = _frodoPersonalQuery.ProfileId,
            pageReady = true,
            items,
            paging = new { hasMore = _frodoPersonalQuery.HasMore, label = "加载更多" },
            filters,
            signature = string.Join("|", items.Select(item => $"{item.SubjectId}:{item.Title}")),
            dom = new { gridItemCount = 0, paginator = _frodoPersonalQuery.HasMore, ready = true, source = "frodo-local-index", total = _frodoPersonalQuery.Total, nextStart = _frodoPersonalQuery.Shown },
            error = ""
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
    private async Task SyncFrodoPersonalAfterConfirmedWriteAsync(DoubanHistoryRecord record, string beforeStatus, string reason)
    {
        try
        {
            var profileId = _frodoPersonalIndex.CurrentProfileId;
            if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit) ||
                record.Status is not ("collect" or "wish" or "do")) return;

            var providerItem = await _frodoPersonalProvider.ApplyConfirmedReviewAsync(
                record.SubjectId,
                beforeStatus,
                record.Status,
                record.Rating,
                record.Comment,
                record.MarkedDate).ConfigureAwait(true);
            var indexedItem = await _frodoPersonalIndex.ApplyConfirmedReviewAsync(
                profileId,
                record.SubjectId,
                record.Status,
                record.Rating,
                record.Comment,
                record.MarkedDate).ConfigureAwait(true);

            var insertedFromRecentReadback = false;
            if (indexedItem is null)
            {
                var recent = await _frodoPersonalIndex.FetchRecentItemAsync(
                    profileId,
                    record.Status,
                    record.SubjectId).ConfigureAwait(true);
                if (recent is not null)
                {
                    providerItem = await _frodoPersonalProvider.ApplyConfirmedReviewAsync(
                        record.SubjectId,
                        beforeStatus,
                        record.Status,
                        record.Rating,
                        record.Comment,
                        record.MarkedDate,
                        recent).ConfigureAwait(true);
                    indexedItem = await _frodoPersonalIndex.ApplyConfirmedReviewAsync(
                        profileId,
                        record.SubjectId,
                        record.Status,
                        record.Rating,
                        record.Comment,
                        record.MarkedDate,
                        recent).ConfigureAwait(true);
                    insertedFromRecentReadback = indexedItem is not null;
                }
            }

            if (indexedItem is null)
            {
                DiagnosticLogger.Write($"Frodo personal authoritative write index upsert deferred; SubjectId={record.SubjectId}; BeforeStatus={beforeStatus}; TargetStatus={record.Status}; Reason={reason}; Cause=RecentFrodoItemNotVisible");
            }

            var currentStatus = FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var activeProfileId, out var activeStatus) &&
                                activeProfileId.Equals(profileId, StringComparison.Ordinal)
                ? activeStatus
                : "";
            var filtered = currentStatus.Length > 0 && _frodoPersonalQuery.IsActiveFor(profileId, currentStatus);
            await RefreshFrodoPersonalUiAfterMutationAsync(
                profileId,
                record.SubjectId,
                beforeStatus,
                record.Status,
                deleted: false,
                myRating: record.Rating,
                score: indexedItem?.Score ?? providerItem?.Score,
                reason: insertedFromRecentReadback ? reason + "-upsert" : reason).ConfigureAwait(true);

            if (insertedFromRecentReadback && !filtered && _frodoPersonalActive &&
                currentStatus.Equals(record.Status, StringComparison.Ordinal))
            {
                await RefreshFrodoPersonalAsync("authoritative-new-interest-upsert").ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException or HttpRequestException or TaskCanceledException)
        {
            DiagnosticLogger.Write($"Frodo personal authoritative write sync failed; SubjectId={record.SubjectId}; BeforeStatus={beforeStatus}; TargetStatus={record.Status}; Reason={reason}; Error={ex.Message}");
        }
    }
    private async Task SyncFrodoPersonalAfterConfirmedDeleteAsync(string subjectId, string beforeStatus, string reason)
    {
        try
        {
            var profileId = _frodoPersonalIndex.CurrentProfileId;
            if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit)) return;
            await _frodoPersonalProvider.ApplyConfirmedDeleteAsync(subjectId).ConfigureAwait(true);
            await _frodoPersonalIndex.ApplyConfirmedDeleteAsync(profileId, subjectId).ConfigureAwait(true);
            await RefreshFrodoPersonalUiAfterMutationAsync(
                profileId,
                subjectId,
                beforeStatus,
                "deleted",
                deleted: true,
                myRating: null,
                score: null,
                reason: reason).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            DiagnosticLogger.Write($"Frodo personal authoritative delete sync failed; SubjectId={subjectId}; BeforeStatus={beforeStatus}; Reason={reason}; Error={ex.Message}");
        }
    }

    private async Task RefreshFrodoPersonalUiAfterMutationAsync(
        string profileId,
        string subjectId,
        string beforeStatus,
        string targetStatus,
        bool deleted,
        int? myRating,
        double? score,
        string reason)
    {
        if (!_frodoPersonalActive ||
            !FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var activeProfileId, out var currentStatus) ||
            !activeProfileId.Equals(profileId, StringComparison.Ordinal)) return;
        if (!currentStatus.Equals(beforeStatus, StringComparison.Ordinal) &&
            !currentStatus.Equals(targetStatus, StringComparison.Ordinal)) return;

        if (_frodoPersonalQuery.IsActiveFor(profileId, currentStatus) &&
            _frodoPersonalIndex.TryGetStatus(profileId, currentStatus, out var filteredSnapshot))
        {
            var criteria = _frodoPersonalQuery.Criteria;
            var result = _frodoPersonalIndex.Query(profileId, currentStatus, criteria);
            _frodoPersonalQuery.Start(profileId, currentStatus, criteria, result);
            var firstPage = _frodoPersonalQuery.TakeInitial();
            var generation = Interlocked.Increment(ref _doubanSourceGeneration);
            var requestId = $"personal-authoritative-sync-{generation}";
            var page = BuildFrodoPersonalQueryPayload(requestId, generation, firstPage, filteredSnapshot);
            await ForwardDoubanSourceResultToShellAsync(page, "personal-authoritative-sync").ConfigureAwait(true);
            DiagnosticLogger.Write($"Frodo personal filtered view recomputed after authoritative mutation; SubjectId={subjectId}; Status={currentStatus}; Matched={result.Count}; Reason={reason}");
            return;
        }

        if (_frodoPersonalIndex.TryGetStatus(profileId, currentStatus, out var snapshot))
        {
            PostFrodoPersonalFilterState(
                profileId,
                currentStatus,
                snapshot,
                new FrodoPersonalFilterCriteria(),
                false,
                snapshot.Items.Count,
                snapshot.Items.Count,
                snapshot.Items.Count,
                0,
                "");
        }

        PostShellMessage(new
        {
            type = "doubanShellPersonalItemMutation",
            subjectId,
            fromStatus = beforeStatus,
            toStatus = targetStatus,
            deleted,
            myRating,
            score
        });
        DiagnosticLogger.Write($"Frodo personal card mutation posted; SubjectId={subjectId}; BeforeStatus={beforeStatus}; TargetStatus={targetStatus}; Deleted={deleted}; Rating={myRating?.ToString() ?? "null"}; Reason={reason}");
    }
    private async Task HandleDoubanShellFilterGroupAsync(JsonElement root)
    {
        var title = RequiredString(root, "title", 120);
        var requestId = ReadString(root, "requestId");
        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "filter-group" });
        var action = await ExecuteDoubanSourceBridgeAsync("openFilterGroup", new { title }).ConfigureAwait(true);
        if (!ReadBool(action, "ok"))
        {
            PostShellMessage(new { type = "doubanShellFilterError", requestId, error = ReadString(action, "error") });
            return;
        }

        JsonElement page = default;
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        while (true)
        {
            page = await ReadDoubanSourcePageAsync(requestId, generation).ConfigureAwait(true);
            var options = ReadSourceGroupOptions(ReadSourceFilters(page), title);
            if (options.GetArrayLength() > 0 || System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= DoubanSourceDomWaitTimeout) break;
            await Task.Delay(DoubanSourceDomPollInterval).ConfigureAwait(true);
        }
        var filters = ReadSourceFilters(page);
        var optionsResult = ReadSourceGroupOptions(filters, title);
        PostShellMessage(new { type = "doubanShellFilterOptions", requestId, groupTitle = title, options = optionsResult, filters });
        DiagnosticLogger.Write($"Unified Shell native filter group opened; Title={title}; Options={optionsResult.GetArrayLength()}; Generation={generation}");
    }

    private async Task HandleDoubanShellApplyFilterAsync(JsonElement root)
    {
        var kind = RequiredString(root, "kind", 30);
        var label = RequiredString(root, "label", 160);
        var title = ReadString(root, "title").Trim();
        var requestId = ReadString(root, "requestId");
        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "filter" });
        var action = await ExecuteDoubanSourceBridgeAsync("selectFilter", new { kind, label, title }).ConfigureAwait(true);
        if (!ReadBool(action, "ok"))
        {
            PostShellMessage(new { type = "doubanShellFilterError", requestId, error = ReadString(action, "error") });
            return;
        }
        var beforeSignature = ReadString(action, "beforeSignature");
        if (ReadBool(action, "noOp"))
        {
            var currentPage = await ReadDoubanSourcePageAsync(requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(currentPage, "filter-noop").ConfigureAwait(true);
            DiagnosticLogger.Write($"Unified Shell native filter no-op; Kind={kind}; Title={title}; Label={label}; Generation={generation}");
            return;
        }
        var page = await WaitForDoubanSourcePageAsync(requestId, generation, beforeSignature, "filter").ConfigureAwait(true);
        await ForwardDoubanSourceResultToShellAsync(page, "filter").ConfigureAwait(true);
    }

    private async Task HandleDoubanShellLoadMoreAsync(JsonElement root)
    {
        var requestId = ReadString(root, "requestId");
        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "load-more" });

        if (_frodoPersonalQuery.IsActive &&
            FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var localProfileId, out var localStatus) &&
            _frodoPersonalQuery.IsActiveFor(localProfileId, localStatus) &&
            _frodoPersonalIndex.TryGetStatus(_frodoPersonalQuery.ProfileId, _frodoPersonalQuery.Status, out var localSnapshot))
        {
            var localItems = _frodoPersonalQuery.TakeNext();
            var localPage = BuildFrodoPersonalQueryPayload(requestId, generation, localItems, localSnapshot);
            await ForwardDoubanSourceResultToShellAsync(localPage, "load-more").ConfigureAwait(true);
            PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "load-more" });
            DiagnosticLogger.Write($"Unified Shell Source load-more completed; Source=FrodoLocalIndex; RequestId={requestId}; Generation={generation}; Shown={_frodoPersonalQuery.Shown}; Total={_frodoPersonalQuery.Total}");
            return;
        }

        if (_frodoPersonalActive && _frodoPersonalProvider.IsActiveFor(_activeDoubanSourceNavigationUrl))
        {
            try
            {
                var page = await _frodoPersonalProvider.LoadMoreAsync(requestId, generation).ConfigureAwait(true);
                await ForwardDoubanSourceResultToShellAsync(page, "load-more").ConfigureAwait(true);
                PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "load-more" });
                DiagnosticLogger.Write($"Unified Shell Source load-more completed; Source=Frodo; RequestId={requestId}; Generation={generation}; Url={_activeDoubanSourceNavigationUrl}");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException)
            {
                var fallbackUrl = _frodoPersonalProvider.CurrentUrl;
                DiagnosticLogger.Write($"Unified Shell Source load-more failed; Source=Frodo; RequestId={requestId}; Generation={generation}; Url={fallbackUrl}; Fallback=DOM; Error={ex.Message}");
                NavigatePersonalDomFallback(fallbackUrl, requestId, "personal-api-fallback", "frodo-load-more-failed");
                return;
            }
        }

        var action = await ExecuteDoubanSourceBridgeAsync("loadMore").ConfigureAwait(true);
        if (!ReadBool(action, "ok"))
        {
            var error = ReadString(action, "error");
            DiagnosticLogger.Write($"Unified Shell Source load-more failed; RequestId={requestId}; Generation={generation}; Url={_activeDoubanSourceNavigationUrl}; Action={action}");
            PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "load-more" });
            PostShellMessage(new { type = "doubanShellLoadMoreError", requestId, error });
            return;
        }
        var beforeSignature = ReadString(action, "beforeSignature");
        if (ReadBool(action, "noOp"))
        {
            var currentPage = await ReadDoubanSourcePageAsync(requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(currentPage, "load-more-noop").ConfigureAwait(true);
            PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "load-more" });
            DiagnosticLogger.Write($"Unified Shell Source load-more no-op; RequestId={requestId}; Generation={generation}");
            return;
        }
        var pageDom = await WaitForDoubanSourcePageAsync(requestId, generation, beforeSignature, "load-more").ConfigureAwait(true);
        await ForwardDoubanSourceResultToShellAsync(pageDom, "load-more").ConfigureAwait(true);
        PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "load-more" });
    }

    private async Task<JsonElement> PrepareSourcePosterItemsAsync(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return items;

        using var gate = new SemaphoreSlim(4, 4);
        using var totalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var prepared = await Task.WhenAll(items.EnumerateArray().Select(async item =>
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(item.GetRawText()) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            var sourceUrl = node["posterUrl"]?.GetValue<string>() ?? node["poster"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(sourceUrl)) return node;

            var acquired = false;
            try
            {
                await gate.WaitAsync(totalTimeout.Token).ConfigureAwait(true);
                acquired = true;
                var dataUri = await TryFetchDoubanPosterDataUriAsync(sourceUrl, totalTimeout.Token).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(dataUri)) node["posterUrl"] = dataUri;
            }
            catch (OperationCanceledException)
            {
                // Poster materialization is an enhancement. If it misses the
                // short budget, keep the original poster URL and return cards.
            }
            finally
            {
                if (acquired) gate.Release();
            }
            return node;
        })).ConfigureAwait(true);

        return JsonSerializer.SerializeToElement(prepared);
    }

    private async Task<string> TryFetchDoubanPosterDataUriAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        var posterUrl = ValidatePosterSourceUrl(sourceUrl);
        if (posterUrl.Length == 0) return "";

        lock (DoubanPosterCacheGate)
        {
            if (DoubanPosterDataUriCache.TryGetValue(posterUrl, out var cached)) return cached;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var request = new HttpRequestMessage(HttpMethod.Get, posterUrl);
            request.Headers.Referrer = new Uri("https://movie.douban.com/");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131 Safari/537.36");
            using var response = await DoubanPlusHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 4_000_000) return "";

            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(true);
            if (bytes.Length == 0 || bytes.Length > 4_000_000) return "";
            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (mediaType is not ("image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "image/gif")) return "";
            var dataUri = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
            if (dataUri.Length <= DoubanPosterCacheItemLimit)
            {
                lock (DoubanPosterCacheGate)
                {
                    if (DoubanPosterDataUriCache.Count >= DoubanPosterCacheLimit)
                        DoubanPosterDataUriCache.Remove(DoubanPosterDataUriCache.Keys.First());
                    DoubanPosterDataUriCache[posterUrl] = dataUri;
                }
            }
            return dataUri;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            DiagnosticLogger.Write($"Unified Shell poster materialization failed; Url={posterUrl}; Error={ex.Message}");
            return "";
        }
    }

    private void PostPendingShellDataIfReady()
    {
        if (!_shellDocumentReady || string.IsNullOrWhiteSpace(_pendingShellDataJson) || _doubanPlusView.CoreWebView2 is null) return;
        var payload = _pendingShellDataJson;
        _doubanPlusView.CoreWebView2.PostWebMessageAsJson(payload);
        _pendingShellDataJson = "";
        DiagnosticLogger.Write($"Unified Shell data posted; Bytes={payload.Length}; Payload={payload}");
    }

    private void PostShellMessage(object message)
    {
        if (!_shellDocumentReady || _doubanPlusView.CoreWebView2 is null) return;
        var payload = JsonSerializer.Serialize(message);
        _doubanPlusView.CoreWebView2.PostWebMessageAsJson(payload);
        DiagnosticLogger.Write($"Unified Shell message posted; Bytes={payload.Length}; Type={ReadString(JsonSerializer.SerializeToElement(message), "type")}");
    }

    private bool IsShellMessageSource(string? source) =>
        ReferenceEquals(_doubanPlusView.CoreWebView2, null) is false &&
        (string.Equals(source, "about:blank", StringComparison.OrdinalIgnoreCase) || string.Equals(source, "data:text/html,", StringComparison.OrdinalIgnoreCase));

    private async Task HandleDoubanPlusWebMessageReceivedAsync(WebView2 responseView, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (!IsAllowedDoubanPlusMessageSource(e.Source) && !IsShellMessageSource(e.Source)) return;
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var messageType = ReadString(root, "type");
            if (messageType == "doubanShellReady")
            {
                _shellDocumentReady = true;
                _activeShellViewKind = "explore";
                _doubanPlusView.Visible = true;
                _doubanPlusView.BringToFront();
                _doubanAccountBar.Visible = false;
                PostPendingShellDataIfReady();
                _ = RequestDoubanSourceReadAsync("shell-ready");
                DiagnosticLogger.Write($"Unified Shell ready; Mode={ReadString(root, "mode")}; Version={ReadString(root, "version")}");
                return;
            }
            if (messageType == "doubanShellDataApplied")
            {
                DiagnosticLogger.Write($"Unified Shell data applied; RequestId={ReadString(root, "requestId")}; ItemCount={ReadString(root, "itemCount")}; Error={ReadString(root, "error")}");
                return;
            }
            if (messageType == "doubanShellCardHover")
            {
                DiagnosticLogger.Write($"Unified Shell card hover; SubjectId={ReadString(root, "subjectId")}; Visible={ReadString(root, "visible")}; PanelTextLength={ReadString(root, "panelTextLength")}");
                return;
            }
            if (messageType == "doubanShellPosterFailed")
            {
                var subjectId = RequiredDigits(root, "subjectId");
                var posterUrl = ValidatePosterSourceUrl(ReadBoundedString(root, "posterUrl", 1200));
                if (posterUrl.Length == 0) return;
                var dataUri = await TryFetchDoubanPosterDataUriAsync(posterUrl).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(dataUri))
                {
                    PostShellMessage(new { type = "doubanShellPosterFallback", subjectId, posterUrl, dataUri });
                    DiagnosticLogger.Write($"Unified Shell poster fallback posted; SubjectId={subjectId}; Url={posterUrl}; Bytes={dataUri.Length}");
                }
                return;
            }
            if (messageType == "doubanShellNavigateContentType")
            {
                _activeShellViewKind = "explore";
                await HandleDoubanShellContentTypeAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellNavigatePersonal")
            {
                _activeShellViewKind = "personal";
                await HandleDoubanShellPersonalStatusAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellSearch")
            {
                _activeShellViewKind = "search";
                await HandleDoubanShellSearchAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellSearchPage")
            {
                _activeShellViewKind = "search";
                await HandleDoubanShellSearchPageAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellApplyLocalPersonalFilter")
            {
                await HandleDoubanShellApplyLocalPersonalFilterAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellApplyPersonalFilter")
            {
                await HandleDoubanShellApplyPersonalFilterAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellFilterGroup")
            {
                await HandleDoubanShellFilterGroupAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellApplyFilter")
            {
                await HandleDoubanShellApplyFilterAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellLoadMore")
            {
                await HandleDoubanShellLoadMoreAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellLogin")
            {
                ShowDoubanLogin();
                return;
            }
            if (messageType == "doubanShellOpenDetail")
            {
                var subjectId = RequiredDigits(root, "subjectId");
                var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                var detailPayload = JsonSerializer.SerializeToElement(new { subjectId, subjectUrl, requestId = $"shell-{subjectId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" });
                await OpenDoubanPlusDetailAsync(detailPayload, $"shell-{subjectId}").ConfigureAwait(true);
                DiagnosticLogger.Write($"Unified Shell detail requested; SubjectId={subjectId}; Mode={ReadString(root, "mode")}; ReturnUrl={_activeDoubanReturnUrl}");
                return;
            }
            if (messageType == "doubanPersonalOpenSubject")
            {
                if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var sourceUri) ||
                    !sourceUri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return;
                var subjectId = RequiredDigits(root, "subjectId");
                var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                var personalUrl = ReadString(root, "personalUrl");
                if (!IsAllowedDoubanPersonalUrl(personalUrl))
                    throw new InvalidDataException("豆瓣个人页面地址无效。");
                _activeDoubanPersonalPageUrl = personalUrl;
                var detailPayload = JsonSerializer.SerializeToElement(new
                {
                    subjectId,
                    subjectUrl,
                    requestId = $"personal-{subjectId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                });
                await OpenDoubanPlusDetailAsync(detailPayload, $"personal-{subjectId}").ConfigureAwait(true);
                DiagnosticLogger.Write($"HTML Douban personal subject click; ProfileId={ReadString(root, "profileId")}; SubjectId={subjectId}; PersonalUrl={personalUrl}; ScrollY={ReadString(root, "scrollY")}");
                return;
            }
            if (messageType == "doubanExploreOpenSubject")
            {
                if (!IsAllowedDoubanExploreOrTvUrl(e.Source)) return;
                var subjectId = RequiredDigits(root, "subjectId");
                var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                var exploreUrl = ReadString(root, "exploreUrl");
                if (!IsAllowedDoubanExploreOrTvUrl(exploreUrl))
                    throw new InvalidDataException("豆瓣探索页面地址无效。");
                var detailPayload = JsonSerializer.SerializeToElement(new
                {
                    subjectId,
                    subjectUrl,
                    requestId = ReadDetailRequestId(root, $"explore-{subjectId}")
                });
                await OpenDoubanPlusDetailAsync(detailPayload, $"explore-{subjectId}").ConfigureAwait(true);
                DiagnosticLogger.Write($"HTML Douban Explore subject click; SubjectId={subjectId}; ExploreUrl={exploreUrl}; ScrollY={ReadString(root, "scrollY")}");
                return;
            }
            if (messageType == "doubanPageRefresh")
            {
                var refreshUrl = ReadString(root, "url");
                var shellSource = IsShellMessageSource(e.Source);
                if ((!shellSource && !IsAllowedDoubanPlusMessageSource(e.Source)) ||
                    (!shellSource && !DoubanWebView2Connector.IsAllowedDoubanTopLevel(refreshUrl))) return;
                _pendingDoubanHistoryReturnUrl = "";
                DiagnosticLogger.Write($"WebView=DoubanPlus; PageRefreshRequested=True; Source={e.Source}; Url={refreshUrl}; RequestId={ReadString(root, "requestId")}");
                if (shellSource)
                {
                    if (string.Equals(ReadString(root, "viewKind"), "watchlist", StringComparison.Ordinal))
                        PostShellMessage(new { type = "doubanShellWatchlistRefresh" });
                    else
                        RefreshDoubanPlusPage();
                }
                else responseView.CoreWebView2?.Reload();
                return;
            }
            if (messageType == "doubanPageHome")
            {
                var homeSource = ReadString(root, "url");
                var shellSource = IsShellMessageSource(e.Source);
                if ((!shellSource && !IsAllowedDoubanPlusMessageSource(e.Source)) ||
                    (!shellSource && !DoubanWebView2Connector.IsAllowedDoubanTopLevel(homeSource))) return;
                await NavigateDoubanHomeAsync().ConfigureAwait(true);
                return;
            }
            if (messageType.StartsWith("doubanWatchlist", StringComparison.Ordinal))
            {
                await HandleWatchlistMessageAsync(root, e.Source, responseView).ConfigureAwait(true);
                return;
            }
            if (messageType != "doubanPlusGmRequest") return;

            var id = ReadString(root, "id");
            var method = ReadString(root, "method").ToUpperInvariant();
            var url = ReadString(root, "url");
            if (id.Length is 0 or > 100 || method is not ("GET" or "POST") || !IsAllowedDoubanPlusRatingUrl(url))
            {
                DiagnosticLogger.Write($"WebView=DoubanPlus; GMRequestRejected; Id={id}; Method={method}; Url={url}");
                throw new InvalidDataException("Douban Plus 外部评分请求无效。");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("headers", out var headersValue) && headersValue.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in headersValue.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                        headers[property.Name] = property.Value.GetString() ?? "";
                }
            }

            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method == "POST")
            {
                var data = root.TryGetProperty("data", out var dataValue) && dataValue.ValueKind == JsonValueKind.String
                    ? dataValue.GetString() ?? ""
                    : "";
                request.Content = new StringContent(data, System.Text.Encoding.UTF8, "application/json");
            }
            foreach (var header in headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (request.Content is not null)
                        request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(header.Value);
                }
                else if (header.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                else if (header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            using var response = await DoubanPlusHttpClient.SendAsync(request).ConfigureAwait(true);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            PostDoubanPlusGmResponse(responseView, id, response.IsSuccessStatusCode, (int)response.StatusCode, responseText, "");
            DiagnosticLogger.Write($"WebView=DoubanPlus; GMRequest; Method={method}; Host={new Uri(url).Host}; Status={(int)response.StatusCode}; Bytes={responseText.Length}");
        }
        catch (Exception ex)
        {
            try
            {
                using var document = JsonDocument.Parse(e.WebMessageAsJson);
                var failedType = ReadString(document.RootElement, "type");
                if (failedType.StartsWith("doubanShell", StringComparison.Ordinal))
                {
                    DiagnosticLogger.Write($"Unified Shell message failed; Type={failedType}; Error={ex.Message}");
                    PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = failedType });
                    PostShellMessage(new { type = "doubanShellFilterError", requestId = ReadString(document.RootElement, "requestId"), error = ex.Message });
                    return;
                }
                var id = ReadString(document.RootElement, "id");
                if (id.Length > 0) PostDoubanPlusGmResponse(responseView, id, false, 0, "", ex.Message);
            }
            catch { }
        }
    }

    private void PostDoubanPlusGmResponse(WebView2 responseView, string id, bool ok, int status, string responseText, string error)
    {
        if (responseView.CoreWebView2 is null) return;
        responseView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "doubanPlusGmResponse",
            id,
            ok,
            status,
            statusText = "",
            responseText,
            error
        }));
    }

    private async Task HandleWatchlistMessageAsync(JsonElement root, string source, WebView2 responseView)
    {
        var requestId = ReadString(root, "requestId");
        var type = ReadString(root, "type");
        try
        {
            switch (type)
            {
                case "doubanWatchlistListRequest":
                    if (!IsAllowedWatchlistListSource(source)) throw new InvalidDataException("待看列表来源无效。");
                    if (IsShellMessageSource(source)) _activeShellViewKind = "watchlist";
                    PostWatchlistResponse(responseView, requestId, true, new { items = _watchlist.Snapshot().Select(WatchlistItemDto).ToList() });
                    return;

                case "doubanWatchlistStateRequest":
                {
                    var subjectId = RequiredDigits(root, "subjectId");
                    var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                    if (!IsAllowedWatchlistSubjectSource(source, subjectUrl)) throw new InvalidDataException("待看状态请求来源无效。");
                    DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "watchlist state");
                    var stateItem = _watchlist.Find(subjectId);
                    PostWatchlistResponse(responseView, requestId, true, new { item = stateItem is null ? null : WatchlistItemDto(stateItem) });
                    return;
                }

                case "doubanWatchlistAdd":
                {
                    var subjectId = RequiredDigits(root, "subjectId");
                    var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                    if (!IsAllowedWatchlistSubjectSource(source, subjectUrl)) throw new InvalidDataException("待看添加来源无效。");
                    DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "watchlist add");
                    var existing = _watchlist.Find(subjectId);
                    var item = _watchlist.AddOrUpdate(new LocalWatchlistItem
                    {
                        SubjectId = subjectId,
                        SubjectUrl = subjectUrl,
                        Title = ReadBoundedString(root, "title", 300),
                        OriginalTitle = ReadBoundedString(root, "originalTitle", 300),
                        Year = ReadBoundedString(root, "year", 20),
                        Identity = ReadBoundedString(root, "identity", 300),
                        Genre = ReadBoundedString(root, "genre", 300),
                        Director = ReadBoundedString(root, "director", 300),
                        Cast = ReadBoundedString(root, "cast", 600),
                        Score = ReadBoundedString(root, "score", 100),
                        Comment = ReadBoundedString(root, "comment", 1200),
                        PosterSourceUrl = ValidatePosterSourceUrl(ReadBoundedString(root, "posterSourceUrl", 1200)),
                        Source = NormalizeWatchlistSource(ReadBoundedString(root, "source", 30))
                    });
                    var posterSaved = false;
                    var posterError = "";
                    if (!string.IsNullOrWhiteSpace(item.PosterSourceUrl))
                    {
                        (item, posterSaved, posterError) = await SaveWatchlistPosterAsync(item).ConfigureAwait(true);
                    }
                    DiagnosticLogger.Write($"WebView=DoubanPlus; WatchlistAdd; SubjectId={subjectId}; Duplicate={existing is not null}; PosterSaved={posterSaved}; PosterError={posterError}; Source={item.Source}; Url={subjectUrl}");
                    PostWatchlistResponse(responseView, requestId, true, new { item = WatchlistItemDto(item), duplicate = existing is not null, posterSaved, posterError });
                    return;
                }

                case "doubanWatchlistDelete":
                {
                    var subjectId = RequiredDigits(root, "subjectId");
                    if (!IsAllowedWatchlistListSource(source)) throw new InvalidDataException("待看删除来源无效。");
                    var removed = _watchlist.Remove(subjectId);
                    DiagnosticLogger.Write($"WebView=DoubanPlus; WatchlistDelete; SubjectId={subjectId}; Removed={removed}");
                    PostWatchlistResponse(responseView, requestId, true, new { removed, items = _watchlist.Snapshot().Select(WatchlistItemDto).ToList() });
                    return;
                }

                default:
                    throw new InvalidDataException("未知的待看消息。");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView=DoubanPlus; WatchlistMessageFailed; Type={type}; RequestId={requestId}; Error={ex.Message}");
            PostWatchlistResponse(responseView, requestId, false, new { error = ex.Message });
        }
        await Task.CompletedTask;
    }

    private void PostWatchlistResponse(WebView2 responseView, string requestId, bool ok, object payload)
    {
        if (responseView.CoreWebView2 is null) return;
        responseView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "doubanWatchlistResponse",
            requestId,
            ok,
            payload
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private object WatchlistItemDto(LocalWatchlistItem item)
    {
        var posterUrl = _watchlist.HasPoster(item) && !string.IsNullOrWhiteSpace(item.PosterPath)
            ? $"https://{WatchlistPosterHost}/{Uri.EscapeDataString(item.PosterPath)}"
            : "";
        return new
        {
            item.SubjectId,
            item.SubjectUrl,
            item.Title,
            item.OriginalTitle,
            item.Year,
            item.Identity,
            item.Genre,
            item.Director,
            item.Cast,
            item.Score,
            item.Comment,
            item.PosterPath,
            item.PosterSourceUrl,
            item.AddedAt,
            item.UpdatedAt,
            item.Note,
            item.Source,
            PosterUrl = posterUrl
        };
    }

    private async Task<(LocalWatchlistItem Item, bool Saved, string Error)> SaveWatchlistPosterAsync(LocalWatchlistItem item)
    {
        if (_watchlist.HasPoster(item)) return (item, true, "");
        if (string.IsNullOrWhiteSpace(item.PosterSourceUrl)) return (item, false, "海报地址为空。");

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Get, item.PosterSourceUrl);
            request.Headers.Referrer = new Uri("https://movie.douban.com/");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131 Safari/537.36");
            using var response = await DoubanPlusHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 8_000_000)
                throw new InvalidDataException("海报文件超过 8 MB，已跳过本地保存。");

            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(true);
            if (bytes.Length == 0 || bytes.Length > 8_000_000) throw new InvalidDataException("海报文件为空或过大。");
            var extension = PosterExtension(response.Content.Headers.ContentType?.MediaType, item.PosterSourceUrl);
            if (extension.Length == 0) throw new InvalidDataException("海报格式不受支持。");

            Directory.CreateDirectory(_watchlist.PostersDirectory);
            var fileName = item.SubjectId + extension;
            var targetPath = Path.Combine(_watchlist.PostersDirectory, fileName);
            var temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes, timeout.Token).ConfigureAwait(true);
                File.Move(temporaryPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            var saved = _watchlist.SetPosterPath(item.SubjectId, fileName);
            return (saved, true, "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (item, false, ex.Message);
        }
    }

    private static string PosterExtension(string? mediaType, string sourceUrl)
    {
        var fromType = mediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ""
        };
        if (fromType.Length > 0) return fromType;
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (extension is ".jpg" or ".jpeg") return ".jpg";
            if (extension is ".png" or ".webp" or ".gif") return extension;
        }
        return "";
    }

    private bool IsAllowedWatchlistListSource(string? source) =>
        IsShellMessageSource(source) || IsAllowedDoubanPersonalUrl(source);

    private bool IsAllowedWatchlistSubjectSource(string? source, string subjectUrl)
    {
        var shellSource = IsShellMessageSource(source);
        if (!shellSource && !IsAllowedDoubanPlusMessageSource(source)) return false;
        if (!DoubanWebView2Connector.IsAllowedSubjectUrl(subjectUrl)) return false;
        if (shellSource) return true;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)) return false;
        return IsAllowedDoubanPersonalUrl(source) || IsAllowedDoubanExploreOrTvUrl(source) || IsDoubanSearchPageUrl(source) || IsDoubanSubjectPageUrl(sourceUri.AbsoluteUri);
    }

    private static string ReadBoundedString(JsonElement value, string name, int maximum)
    {
        var result = ReadString(value, name).Trim();
        return result.Length <= maximum ? result : result[..maximum];
    }

    private static string NormalizeWatchlistSource(string source) =>
        source is "personal" or "explore" or "search" or "detail" or "watchlist" or "shell" ? source : "unknown";

    private static string ValidatePosterSourceUrl(string source)
    {
        if (source.Length == 0) return "";
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return "";
        var host = uri.Host;
        return host.EndsWith(".doubanio.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("doubanio.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".douban.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("douban.com", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : "";
    }

    internal static bool IsAllowedDoubanPlusMessageSource(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("search.douban.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("www.douban.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("accounts.douban.com", StringComparison.OrdinalIgnoreCase));

    // 保留给评价管线自检；旧本地 HTML bridge 已移除，实际写入仍由显式操作分支控制。
    internal static bool IsAllowedOperation(string? operation) =>
        operation is "saveDoubanEntry" or "deleteEntry";

    internal static bool IsAllowedDoubanPlusRatingUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        return IsKnownRatingHost(uri.Host, "imdb.com") ||
               IsKnownRatingHost(uri.Host, "metacritic.com") ||
               IsKnownRatingHost(uri.Host, "rottentomatoes.com");
    }

    private static bool IsKnownRatingHost(string host, string baseDomain) =>
        host.Equals(baseDomain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + baseDomain, StringComparison.OrdinalIgnoreCase);

    private static string DoubanContentTypeForUrl(string? url) => IsDoubanSearchPageUrl(url) ? "search" : IsAllowedDoubanTvUrl(url) ? "tv" : IsAllowedDoubanPersonalUrl(url) ? "personal" : "movie";

    private static string DoubanExploreModeForUrl(string? url) => $"explore-{DoubanContentTypeForUrl(url)}";

    private static string DoubanSourceModeForUrl(string? url)
    {
        if (IsDoubanSearchPageUrl(url)) return "search";
        if (IsAllowedDoubanPersonalUrl(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var match = System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath, @"^/people/\d+/(collect|wish|do)/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return match.Success ? $"personal-{match.Groups[1].Value}" : "personal";
        }
        return DoubanExploreModeForUrl(url);
    }

    private static string ReadContentType(JsonElement root, string sourceUrl)
    {
        var contentType = ReadString(root, "contentType");
        return contentType is "movie" or "tv" or "personal" or "search" ? contentType : DoubanContentTypeForUrl(sourceUrl);
    }

    internal static bool IsAllowedDoubanPersonalUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/people/\\d+/(?:collect|wish|do)/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    internal static bool IsSameDoubanPersonalScope(string? left, string? right)
    {
        if (!IsAllowedDoubanPersonalUrl(left) || !IsAllowedDoubanPersonalUrl(right) ||
            !Uri.TryCreate(left, UriKind.Absolute, out var leftUri) || !Uri.TryCreate(right, UriKind.Absolute, out var rightUri)) return false;
        var leftMatch = System.Text.RegularExpressions.Regex.Match(leftUri.AbsolutePath, @"^/people/(\d+)/(collect|wish|do)/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var rightMatch = System.Text.RegularExpressions.Regex.Match(rightUri.AbsolutePath, @"^/people/(\d+)/(collect|wish|do)/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return leftMatch.Success && rightMatch.Success &&
               string.Equals(leftMatch.Groups[1].Value, rightMatch.Groups[1].Value, StringComparison.Ordinal) &&
               string.Equals(leftMatch.Groups[2].Value, rightMatch.Groups[2].Value, StringComparison.Ordinal);
    }

    internal static bool IsAllowedDoubanExploreUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(uri.AbsolutePath.TrimEnd('/'), "/explore", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAllowedDoubanTvUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(uri.AbsolutePath.TrimEnd('/'), "/tv", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAllowedDoubanExploreOrTvUrl(string? url) =>
        IsAllowedDoubanExploreUrl(url) || IsAllowedDoubanTvUrl(url);

    internal static bool IsAllowedDoubanSourceUrl(string? url) =>
        IsAllowedDoubanExploreOrTvUrl(url) || IsAllowedDoubanPersonalUrl(url) || IsDoubanSearchPageUrl(url);

    internal static bool IsDoubanSearchPageUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("search.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/movie/subject_search/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool IsDoubanSubjectPageUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/subject/\\d+/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool IsDoubanPlusListPageUrl(string? url) =>
        IsDoubanSearchPageUrl(url) || IsAllowedDoubanPersonalUrl(url) || IsAllowedDoubanExploreOrTvUrl(url);

    private static string DoubanReturnButtonText() => "返回";

    private bool CanShowDoubanPlusReturnButton() =>
        IsDoubanSubjectPageUrl(_activeDoubanPlusNavigationUrl) &&
        IsDoubanPlusListPageUrl(_activeDoubanReturnUrl);

    private async Task<bool> ProbeDoubanPlusPageAsync()
    {
        try
        {
            var probe = await _doubanPlusView.CoreWebView2.ExecuteScriptAsync("""
(() => {
  const state = window.__qbDoubanPlusProbe || {};
  return {
       href: location.href,
       title: document.title,
       readyState: document.readyState,
       topFrame: window.top === window,
       systemLoaded: !!state.systemLoaded,
       namedRegisterLoaded: !!state.namedRegisterLoaded,
       importStarted: !!state.importStarted,
       importResolved: !!state.importResolved,
       importRejected: !!state.importRejected,
       importError: state.importError || "",
       importReadyState: state.importReadyState || "",
       domContentLoadedAt: state.domContentLoadedAt || 0,
       bodyClass: document.body?.className || "",
     rootCount: document.querySelectorAll("#atv-douban-root, #qb-douban-personal-root, #qb-douban-explore-root").length,
     personalRootCount: document.querySelectorAll("#qb-douban-personal-root").length,
     exploreRootCount: document.querySelectorAll("#qb-douban-explore-root").length,
    wrapperHidden: document.querySelector("#wrapper")?.style.display || "",
    startedAt: state.startedAt || 0,
    warnings: state.warnings || [],
    errors: state.errors || [],
    rejections: state.rejections || []
  };
})()
""").ConfigureAwait(true);
            DiagnosticLogger.Write($"WebView=DoubanPlus; RuntimeProbe={probe}");
            using var document = JsonDocument.Parse(probe);
            var root = document.RootElement;
            return root.TryGetProperty("topFrame", out var topFrame) && topFrame.GetBoolean()
                && root.TryGetProperty("rootCount", out var rootCount) && rootCount.GetInt32() > 0;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView=DoubanPlus; RuntimeProbeFailed={ex.Message}");
            return false;
        }
    }

    private async Task RetryDoubanPlusNavigationAsync(string url, ulong previousNavigationId)
    {
        try
        {
            await Task.Delay(300).ConfigureAwait(true);
            if (_closing || _pendingDoubanPlusNavigationId != previousNavigationId ||
                !AreEquivalentDoubanNavigationUrls(_activeDoubanPlusNavigationUrl, url)) return;
            NavigateDoubanPlusToUrl(url, "content-probe-retry");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView=DoubanPlus; ContentRecoveryFailed=True; Url={url}; Error={ex.Message}");
        }
    }

    private async Task<bool> HasStableDoubanPlusRootAsync()
    {
        try
        {
            var result = await _doubanPlusView.CoreWebView2.ExecuteScriptAsync("""
(() => {
   const root = document.querySelector("#atv-douban-root, #qb-douban-personal-root, #qb-douban-explore-root");
  const body = document.body;
  const wrapper = document.querySelector("#wrapper");
  const probe = window.__qbDoubanPlusProbe || {};
  if (!root || !body) return { ready: false, reason: "root-missing" };
  const style = getComputedStyle(root);
  const rect = root.getBoundingClientRect();
   const enhanced = body.classList.contains("atv-enhanced") || body.classList.contains("qb-douban-personal-enhanced") || body.classList.contains("qb-douban-explore-enhanced");
  const wrapperHidden = !wrapper || getComputedStyle(wrapper).display === "none";
  const isSearch = location.hostname === "search.douban.com" && /^\/movie\/subject_search\/?$/u.test(location.pathname);
   const isSubject = location.hostname === "movie.douban.com" && /^\/subject\/\d+\/?$/u.test(location.pathname);
   const isPersonal = location.hostname === "movie.douban.com" && /^\/people\/\d+\/(?:collect|wish|do)\/?$/u.test(location.pathname);
   const isExplore = location.hostname === "movie.douban.com" && /^\/(?:explore|tv)\/?$/u.test(location.pathname);
   const isTv = location.hostname === "movie.douban.com" && /^\/tv\/?$/u.test(location.pathname);
  const searchCardCount = root.querySelectorAll(".atv-search-page-card").length;
  const searchEmpty = root.querySelector(".atv-search-page-empty") !== null;
  const nativeSubjectLinkCount = isSearch ? (wrapper?.querySelectorAll("a[href*='/subject/']").length || 0) : 0;
  const detailTitle = root.querySelector(".atv-hero-title") !== null;
   const personalContent = root.querySelector(".qb-personal-header, .qb-personal-content, .qb-personal-empty") !== null;
   const exploreContent = root.querySelector(".qb-explore-header, .qb-explore-grid, .qb-explore-empty") !== null;
   const contentReady = isSearch ? searchCardCount > 0 || (searchEmpty && nativeSubjectLinkCount === 0) : isSubject ? detailTitle : isPersonal ? personalContent : isExplore ? exploreContent : root.childElementCount > 0;
  const ready = enhanced && wrapperHidden && style.display !== "none" && style.visibility !== "hidden" && Number(style.opacity) >= 0.98 && rect.width > 0 && rect.height > 0 && probe.importResolved === true && probe.importRejected !== true && contentReady;
   return { ready, contentReady, detailTitle, exploreContent, isExplore, isTv, isPersonal, isSearch, isSubject, nativeSubjectLinkCount, personalContent, searchCardCount, searchEmpty };
})()
""").ConfigureAwait(true);
            DiagnosticLogger.Write($"WebView=DoubanPlus; ContentProbe={result}");
            using var probeDocument = JsonDocument.Parse(result);
            var probeRoot = probeDocument.RootElement;
            return probeRoot.TryGetProperty("ready", out var ready) && ready.GetBoolean();
        }
        catch { return false; }
    }

    private async Task<bool> WaitForStableDoubanPlusRootAsync(ulong navigationId)
    {
        var isExplore = IsAllowedDoubanExploreOrTvUrl(_activeDoubanPlusNavigationUrl);
        var maximumAttempts = isExplore ? 360 : 60;
        for (var attempt = 0; attempt < maximumAttempts && navigationId == _pendingDoubanPlusNavigationId; attempt++)
        {
            if (await HasStableDoubanPlusRootAsync().ConfigureAwait(true))
            {
                DiagnosticLogger.Write($"WebView=DoubanPlus; StableRenderProbe=True; NavigationId={navigationId}; Attempt={attempt}");
                return true;
            }
            await Task.Delay(50).ConfigureAwait(true);
        }
        DiagnosticLogger.Write($"WebView=DoubanPlus; StableRenderProbe=False; NavigationId={navigationId}; TimeoutMs={maximumAttempts * 50}; ExploreWait={isExplore}");
        return false;
    }

    private async Task<object> OpenDoubanPlusDetailAsync(JsonElement payload, string bridgeRequestId)
    {
        var subjectId = RequiredDigits(payload, "subjectId");
        var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
        var requestId = ReadDetailRequestId(payload, bridgeRequestId);
        await WaitForDoubanRecoveryAsync().ConfigureAwait(true);
        await EnsureDoubanPlusViewAsync().ConfigureAwait(true);
        await EnsureDoubanSubjectViewAsync().ConfigureAwait(true);

        _activeDetailSubjectId = subjectId;
        _activeDetailRequestId = requestId;
        _activeDoubanPlusSubjectUrl = subjectUrl;
        var listUrl = _activeDoubanPlusNavigationUrl;
        if (!IsDoubanPlusListPageUrl(listUrl)) listUrl = _activeDoubanReturnUrl;
        if (!IsDoubanPlusListPageUrl(listUrl))
            throw new InvalidOperationException("无法确定影片详情来源列表。请先返回列表页面。");
        _activeDoubanReturnUrl = listUrl;
        _activeDoubanSubjectNavigationUrl = subjectUrl;
        _returnToLibraryButton.Visible = false;
        _doubanPlusView.Visible = false;
        _doubanSubjectView.Visible = true;
        _doubanSubjectView.BringToFront();
        _doubanSubjectView.CoreWebView2!.Navigate(subjectUrl);
        DiagnosticLogger.Write($"HTML Douban Plus detail opened; WebView=DoubanSubject; ListWebView=DoubanPlus; SubjectId={subjectId}; RequestId={requestId}; Url={subjectUrl}; ReturnUrl={_activeDoubanReturnUrl}; Version={DoubanPlusWebView2Script.Version}; DualVisibleWebViews=True");
        return new { opened = true, source = "douban-plus-dual-webview2", version = DoubanPlusWebView2Script.Version, subjectId, requestId, subjectUrl };
    }

    private async Task<object> OpenDoubanPersonalPageAsync(JsonElement payload)
    {
        var status = ReadString(payload, "status");
        if (status is not ("collect" or "wish" or "do"))
            throw new InvalidDataException("豆瓣个人列表状态无效。");

        await WaitForDoubanRecoveryAsync().ConfigureAwait(true);
        await EnsureDoubanPlusViewAsync().ConfigureAwait(true);
        var session = await _workerConnector.VerifySessionAsync().ConfigureAwait(true);
        if (!session.IsLoggedIn || !System.Text.RegularExpressions.Regex.IsMatch(session.ProfileId ?? "", "^\\d+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidOperationException("豆瓣尚未登录，请先扫码登录。");

        var personalUrl = $"https://movie.douban.com/people/{session.ProfileId}/{status}";
        _activeDetailSubjectId = "";
        _activeDetailRequestId = "";
        _activeDoubanPlusSubjectUrl = "";
        _activeDoubanPlusNavigationUrl = personalUrl;
        _activeDoubanSubjectNavigationUrl = "";
        _activeDoubanPersonalPageUrl = personalUrl;
        _activeDoubanReturnUrl = "";
        _returnToLibraryButton.Text = "返回";
        _returnToLibraryButton.Visible = false;
        _doubanSubjectView.Visible = false;
        _doubanPlusView.Visible = true;
        _doubanPlusView.BringToFront();
        _doubanPlusView.CoreWebView2.Navigate(personalUrl);
        DiagnosticLogger.Write($"HTML Douban personal page opened; WebView=DoubanPlus; ProfileId={session.ProfileId}; Status={status}; Url={personalUrl}; ReadOnly=True");
        return new { opened = true, source = "douban-personal-webview2", profileId = session.ProfileId, status, personalUrl };
    }

    private void ReturnToLibrary()
    {
        if (!_doubanSubjectView.Visible && !IsDoubanSubjectPageUrl(_activeDoubanSubjectNavigationUrl))
        {
            _returnToLibraryButton.Visible = false;
            DiagnosticLogger.Write($"WebView=DoubanSubject; ReturnIgnored=True; CurrentUrl={_activeDoubanSubjectNavigationUrl}; Reason=NoActiveSubjectView");
            return;
        }

        var listUrl = _activeDoubanReturnUrl;
        _doubanSubjectView.Visible = false;
        _doubanPlusView.Visible = true;
        _doubanPlusView.BringToFront();
        _returnToLibraryButton.Visible = false;
        _activeDetailSubjectId = "";
        _activeDetailRequestId = "";
        _activeDoubanPlusSubjectUrl = "";
        _activeDoubanSubjectNavigationUrl = "";
        _activeDoubanReturnUrl = "";
        DiagnosticLogger.Write($"WebView=DoubanSubject; DetailClosed=True; Mode=SwitchToListView; ListUrl={listUrl}; ListViewStateRestore=True; NavigationReused=True");
    }

    private bool TryGoBackToExpectedUrl(string expectedUrl, string reason)
    {
        if (!IsDoubanPlusListPageUrl(expectedUrl) || !_doubanPlusView.CoreWebView2.CanGoBack)
        {
            DiagnosticLogger.Write($"WebView=DoubanPlus; HistoryReturnAttempt=False; Reason=NoMatchingBackHistory; ExpectedUrl={expectedUrl}; Operation={reason}");
            return false;
        }

        _pendingDoubanHistoryReturnUrl = expectedUrl;
        try
        {
            _doubanPlusView.CoreWebView2.GoBack();
            DiagnosticLogger.Write($"WebView=DoubanPlus; HistoryReturnAttempt=True; ExpectedUrl={expectedUrl}; Operation={reason}");
            return true;
        }
        catch (Exception ex)
        {
            _pendingDoubanHistoryReturnUrl = "";
            DiagnosticLogger.Write($"WebView=DoubanPlus; HistoryReturnAttempt=False; Reason=GoBackFailed; ExpectedUrl={expectedUrl}; Operation={reason}; Error={ex.Message}");
            return false;
        }
    }

    private void NavigateDoubanPlusToUrl(string url, string reason)
    {
        if (_closing) return;
        _activeDoubanPlusNavigationUrl = url;
        _activeDoubanSubjectNavigationUrl = "";
        _activeDoubanPersonalPageUrl = IsAllowedDoubanPersonalUrl(url) ? url : "";
        _returnToLibraryButton.Visible = false;
        _doubanSubjectView.Visible = false;
        _doubanPlusView.Visible = true;
        _doubanPlusView.BringToFront();
        _doubanPlusView.CoreWebView2.Navigate(url);
        DiagnosticLogger.Write($"WebView=DoubanPlus; NavigateFallback=True; Reason={reason}; Url={url}");
    }

    private static bool AreEquivalentDoubanNavigationUrls(string? left, string? right)
    {
        if (!Uri.TryCreate(left, UriKind.Absolute, out var leftUri) || !Uri.TryCreate(right, UriKind.Absolute, out var rightUri)) return false;
        return string.Equals(NormalizeDoubanNavigationUrl(leftUri), NormalizeDoubanNavigationUrl(rightUri), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDoubanNavigationUrl(Uri uri) =>
        $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}{uri.Query}";

    private DoubanHistoryRecord GetWritableRecord(JsonElement payload, bool allowTombstoneRecreate)
    {
        var subjectId = RequiredDigits(payload, "subjectId");
        var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
        var record = FindOrCreateRecord(subjectId, subjectUrl);
        DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "HTML save preflight", record);
        if (record.Tombstoned && !allowTombstoneRecreate)
            throw new InvalidOperationException("该条目已删除；如需重新添加，请选择想看、在看或看过并保存。");
        if (record.Tombstoned)
            DiagnosticLogger.Write($"HTML tombstone re-add requested; SubjectId={subjectId}; TombstonedAt={record.TombstonedAt:O}; Reason={record.TombstoneReason}; tombstone remains until confirmed official readback");
        record.SubjectUrl = subjectUrl;
        return record;
    }

    private void PromoteToHistory(DoubanHistoryRecord record)
    {
        if (_history.Items.TryGetValue(record.SubjectId, out var existing) && !ReferenceEquals(existing, record))
        {
            _history.Items[record.SubjectId] = record;
            _searchCache.Items.Remove(record.SubjectId);
            return;
        }
        if (!_history.Items.ContainsKey(record.SubjectId))
        {
            _history.Items[record.SubjectId] = record;
            _searchCache.Items.Remove(record.SubjectId);
        }
    }

    private static string ReadDetailRequestId(JsonElement payload, string bridgeRequestId)
    {
        var value = ReadString(payload, "requestId").Trim();
        if (string.IsNullOrWhiteSpace(value)) value = bridgeRequestId.Trim();
        return value.Length <= 80 ? value : value[..80];
    }

    private static string ReadString(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    private static string RequiredString(JsonElement value, string name, int maximumLength) { var result = ReadString(value, name).Trim(); if (result.Length is 0 || result.Length > maximumLength) throw new InvalidDataException($"{name}无效。"); return result; }
    private static string RequiredDigits(JsonElement value, string name) { var result = RequiredString(value, name, 30); if (!result.All(char.IsDigit)) throw new InvalidDataException($"{name}无效。"); return result; }
    private static string RequiredSubjectUrl(JsonElement value, string name) { var result = RequiredString(value, name, 300); if (!DoubanWebView2Connector.IsAllowedSubjectUrl(result)) throw new InvalidDataException("豆瓣影片地址无效。"); return result; }
    private static string RequiredLibraryStatus(JsonElement value, string name)
    {
        var result = RequiredString(value, name, 20);
        return result is "collect" or "wish" or "do" ? result : throw new InvalidDataException("豆瓣列表状态无效。");
    }
    private static int ReadInt(JsonElement value, string name, int minimum, int maximum) => value.TryGetProperty(name, out var property) && property.TryGetInt32(out var result) ? Math.Clamp(result, minimum, maximum) : minimum;
    private static int? ReadNullableInt(JsonElement value, string name, int minimum, int maximum)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (!property.TryGetInt32(out var result) || result < minimum || result > maximum) throw new InvalidDataException($"{name}无效。");
        return result;
    }
    private static bool ReadBool(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;
    private static ReviewFieldAction ReadReviewFieldAction(JsonElement value, string name)
    {
        var text = ReadString(value, name).Trim().ToLowerInvariant();
        return text switch
        {
            "keep" => ReviewFieldAction.Keep,
            "set" => ReviewFieldAction.Set,
            "clear" => ReviewFieldAction.Clear,
            _ => throw new InvalidDataException($"{name}无效。")
        };
    }
    private static void Copy(string value, Action<string> setter) { if (!string.IsNullOrWhiteSpace(value)) setter(value.Trim()); }
}
