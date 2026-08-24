using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed partial class HtmlMediaLibraryForm : Form
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
    private readonly DoubanPublicScoreCache _doubanPublicScoreCache;
    private readonly object _doubanPublicScoreFetchGate = new();
    private readonly HashSet<string> _doubanPublicScoreFetchRunning = new(StringComparer.Ordinal);
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
        _doubanPublicScoreCache = new DoubanPublicScoreCache(_store.DataDirectory);
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

}
