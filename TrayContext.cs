using System.Text.Json;

using System.IO.Pipes;

namespace QbPotDoubanAi;

public sealed class TrayContext : ApplicationContext
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".webm" };
    private readonly Store _store;
    private readonly BrowserCdpService _browser;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private AppSettings _settings;
    private PersistentState _state;
    private BrowserMediaSnapshot? _previous;
    private DateTime _lastTick = DateTime.UtcNow;
    private bool _busy;
    private string _status = "尚未启动观影浏览器";
    private BrowserStatusForm? _statusForm;
    private bool _quickCompletionTest;
    private double _quickTestSeconds;
    private string _quickTestTarget = "";
    private ToolStripMenuItem? _quickTestMenu;
    private DateTime _lastPotScan = DateTime.MinValue;
    private string _potStatus = "等待 PotPlayer 播放";
    private readonly CancellationTokenSource _controlCancellation = new();
    private readonly SynchronizationContext _uiContext;
    private NamedPipeServerStream? _controlServer;
    private Task? _controlTask;

    public TrayContext(string? dataDirectory = null)
    {
        _store = new Store(dataDirectory);
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = _store.LoadSettings(); _state = _store.LoadState(); _browser = new BrowserCdpService(_store.DataDirectory);
        var menu = new ContextMenuStrip();
        menu.Items.Add("启动观影浏览器", null, async (_, _) => await LaunchBrowserAsync());
        menu.Items.Add("连接状态…", null, (_, _) => ShowStatus());
        menu.Items.Add("设置…", null, (_, _) => EditSettings());
        menu.Items.Add("运行内置自检…", null, (_, _) => MessageBox.Show(SelfTest.Run(), "内置自检", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add("测试当前爱奇艺电影（完整流程）…", null, async (_, _) => await TestCurrentIqiyiAsync());
        menu.Items.Add("测试当前 PotPlayer 影片（完整流程）…", null, (_, _) => TestCurrentPotPlayer());
        var quickTest = new ToolStripMenuItem("完成提醒快捷测试（30秒＋70%）") { CheckOnClick = true };
        _quickTestMenu = quickTest;
        quickTest.CheckedChanged += (_, _) => { _quickCompletionTest = quickTest.Checked; _quickTestSeconds = 0; _quickTestTarget = ""; };
        menu.Items.Add(quickTest);
        menu.Items.Add("Douban Plus…", null, (_, _) => ShowMediaLibrary());
        menu.Items.Add("豆瓣扫码登录…", null, (_, _) => ShowDoubanLogin());
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出", null, (_, _) => ExitThread());
        _tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "观影助手", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += async (_, _) => await LaunchBrowserAsync();
        _timer.Tick += async (_, _) => await TickAsync(); _timer.Start();
        ScanPotPlayerDirectory();
        _store.Save(_settings, _state);
        _controlTask = ListenControlCommandsAsync(_controlCancellation.Token);
        _tray.ShowBalloonTip(2500, "观影助手已启动", $"双击托盘图标，启动独立的 {_settings.PreferredBrowser} 观影浏览器。", ToolTipIcon.Info);
    }

    private async Task ListenControlCommandsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = SingleInstanceControl.CreateServer();
                _controlServer = server;
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server);
                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(command))
                    _uiContext.Post(_ => HandleControlCommand(command), null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                CrashLogger.Write(ex);
                try { await Task.Delay(250, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            }
            finally
            {
                if (ReferenceEquals(_controlServer, server)) _controlServer = null;
                server?.Dispose();
            }
        }
    }

    private void HandleControlCommand(string command)
    {
        switch (command.Trim().ToLowerInvariant())
        {
            case "open":
            case "show":
                ShowMediaLibrary();
                break;
            case "exit":
                ExitThread();
                break;
        }
    }

    public string Status => _status;
    public BrowserSession? Session => _browser.Session;
    internal BrowserCdpService Browser => _browser;
    public BrowserMediaSnapshot? Current { get; private set; }
    public VideoRecord? CurrentRecord => Current is null ? null : _state.Videos.GetValueOrDefault(Current.Key);

    public async Task LaunchBrowserAsync()
    {
        try { var s = await _browser.LaunchAsync(_settings.PreferredBrowser); _status = $"已连接 {s.BrowserName}（127.0.0.1:{s.Port}）"; }
        catch (Exception ex) { _status = "启动失败：" + ex.Message; MessageBox.Show(ex.Message, "无法启动观影浏览器", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefreshStatusForm();
    }
    public Task<BrowserSession> EnsureBackgroundBrowserAsync() => _browser.EnsureBackgroundAsync(_settings.PreferredBrowser);
    public async Task PrepareDoubanLoginAsync() { await LaunchBrowserAsync(); if (await _browser.IsConnectedAsync()) await _browser.OpenDoubanLoginAsync(); }

    private async Task TickAsync()
    {
        if (_busy) return; _busy = true;
        try
        {
            var now = DateTime.UtcNow; var elapsed = Math.Clamp((now - _lastTick).TotalSeconds, 0, 5); _lastTick = now;
            TickPotPlayer(elapsed);
            if (!await _browser.IsConnectedAsync()) { Current = null; _previous = null; _status = _potStatus != "等待 PotPlayer 播放" ? _potStatus : (_browser.Session is null ? "尚未启动观影浏览器；等待 PotPlayer 播放" : "观影浏览器连接已断开；等待 PotPlayer 播放"); _store.Save(_settings, _state); return; }
            var snap = await _browser.ReadIqiyiAsync(); Current = snap;
            if (snap is null) { _previous = null; _status = _potStatus != "等待 PotPlayer 播放" ? _potStatus : "浏览器已连接；等待爱奇艺视频标签页或 PotPlayer"; _store.Save(_settings, _state); return; }
            if (!_state.Videos.TryGetValue(snap.Key, out var record)) _state.Videos[snap.Key] = record = new VideoRecord { Key = snap.Key, Source = "Browser" };
            record.Title = snap.Title; record.Year = snap.Year; record.Genre = snap.Genre; record.Url = snap.Url; record.DurationSeconds = snap.Duration; record.HighestRatio = Math.Max(record.HighestRatio, snap.Ratio);

            var countable = WatchTimeRules.CountableSeconds(_previous, snap, elapsed);
            record.WatchedSeconds += countable;
            if (_quickCompletionTest)
            {
                if (_quickTestTarget != snap.Key) { _quickTestTarget = snap.Key; _quickTestSeconds = 0; }
                _quickTestSeconds += countable;
            }
            _previous = snap;
            _status = $"正在观看：{record.Title} · {Format(snap.CurrentTime)}/{Format(snap.Duration)} · {(snap.Paused ? "已暂停" : "播放中")}";
            var localNow = DateTime.Now;
            if (!record.Reminded && (record.SnoozedUntil is null || record.SnoozedUntil <= localNow) && record.WatchedSeconds >= _settings.MinimumWatchMinutes * 60 && snap.Ratio >= _settings.CompletionThreshold)
                ShowReminder(record);
            if (_quickCompletionTest && WatchTimeRules.IsQuickCompletionReady(_quickTestSeconds, snap.Ratio))
            {
                _quickCompletionTest = false; _quickTestSeconds = 0; if (_quickTestMenu is not null) _quickTestMenu.Checked = false; ShowReminder(record, true);
            }
            _store.Save(_settings, _state);
        }
        catch (Exception ex) { _status = "读取爱奇艺标签页失败：" + ex.Message; }
        finally { _busy = false; RefreshStatusForm(); }
    }

    private void TickPotPlayer(double elapsed)
    {
        if (DateTime.Now - _lastPotScan > TimeSpan.FromSeconds(30)) ScanPotPlayerDirectory();
        var player = PotPlayer.Read();
        if (!player.HasMedia) { _potStatus = "等待 PotPlayer 播放"; return; }
        var record = MatchPotPlayer(player.Title);
        if (record is null) { _potStatus = $"PotPlayer正在播放：{player.Title}（不在影视目录）"; return; }
        record.HighestRatio = Math.Max(record.HighestRatio, player.Ratio);
        if (player.Status == 2) record.WatchedSeconds += elapsed;
        _potStatus = $"PotPlayer正在观看：{record.Title} · {player.Ratio:P0}";
        var localNow = DateTime.Now;
        if (!record.Reminded && (record.SnoozedUntil is null || record.SnoozedUntil <= localNow) && record.WatchedSeconds >= _settings.MinimumWatchMinutes * 60 && player.Ratio >= _settings.CompletionThreshold)
            ShowReminder(record);
    }

    private void ScanPotPlayerDirectory()
    {
        _lastPotScan = DateTime.Now;
        if (!Directory.Exists(_settings.VideoDirectory)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(_settings.VideoDirectory, "*", SearchOption.AllDirectories))
            {
                if (!VideoExtensions.Contains(Path.GetExtension(path)) || new FileInfo(path).Length < 50L * 1024 * 1024) continue;
                var key = "pot:" + path;
                if (!_state.Videos.TryGetValue(key, out var record)) _state.Videos[key] = record = new VideoRecord { Key = key, Source = "PotPlayer", Path = path };
                record.Source = "PotPlayer"; record.Path = path; record.Title = MovieTitle.FromPath(path); record.Year = MovieTitle.YearFromPath(path); record.Url = path;
            }
        }
        catch (Exception ex) { _potStatus = "PotPlayer目录扫描失败：" + ex.Message; }
    }

    private VideoRecord? MatchPotPlayer(string title)
    {
        var normalizedTitle = MovieTitle.Normalize(title);
        return _state.Videos.Values.Where(v => v.Source == "PotPlayer" && File.Exists(v.Path)).FirstOrDefault(v =>
        {
            var normalizedFile = MovieTitle.Normalize(Path.GetFileNameWithoutExtension(v.Path));
            return normalizedTitle.Contains(normalizedFile) || normalizedFile.Contains(normalizedTitle);
        });
    }

    private void ShowReminder(VideoRecord record, bool testMode = false)
    {
        if (Application.OpenForms.OfType<ReminderForm>().Any()) return;
        var titleParts = record.Source == "PotPlayer" && !string.IsNullOrWhiteSpace(record.Path)
            ? MovieTitle.ParsePath(record.Path)
            : MovieTitle.FromTitle(record.Title, record.Year);
        var displayBase = string.Join(" / ", titleParts.Aliases);
        var displayTitle = displayBase + (titleParts.Year is null ? "" : $" ({titleParts.Year})");
        var mediaLocation = record.Source == "PotPlayer" ? record.Path : record.Url;
        var bindingStem = record.Source == "PotPlayer" ? "pot-" + Path.GetFileName(record.Path) : "browser-" + record.Key;
        var autoBindingKey = "auto-binding-v3-" + bindingStem;
        var confirmedBindingKey = "confirmed-binding-v1-" + bindingStem;
        async Task StartAiAsync(bool forceReidentify)
        {
            if (forceReidentify)
            {
                _store.DeleteCache(autoBindingKey);
                _store.DeleteCache(confirmedBindingKey);
            }
            MovieIdentity? identity = LoadRecognitionCache(confirmedBindingKey, RecognitionCache.ConfirmedBindingVersion)
                ?? LoadRecognitionCache(autoBindingKey, RecognitionCache.AutoBindingVersion);
            var candidates = new List<MovieIdentity>();
            MovieIdentity? attempted = null;
            if (identity is null)
            {
                candidates = (await new ForeignMetadataService().FindCandidatesAsync(titleParts)).ToList();
                identity = candidates.FirstOrDefault(x => x.MatchScore >= 80 && RecognitionMatcher.IsStrongMatch(titleParts, x.Title, x.OriginalTitle, x.Year));
                if (identity is not null)
                {
                    PrepareRecognition(identity, titleParts, "自动-Wikipedia/Wikidata", RecognitionCache.AutoBindingVersion);
                    _store.SaveCache(autoBindingKey, JsonSerializer.Serialize(identity));
                }
                else
                {
                    try { attempted = await new DeepSeekService().ResolveMovieFromFileNameAsync(_settings, record.Source == "PotPlayer" ? record.Path : displayTitle); } catch { }
                    // DeepSeek is an explanation/sorting hint only. It never supplies an IMDb binding by itself.
                    using var confirm = new DoubanConfirmForm(displayTitle, candidates, attempted);
                    if (confirm.ShowDialog() != DialogResult.OK || confirm.Result is null) throw new InvalidOperationException("尚未确认影片，已停止生成问题。");
                    identity = confirm.Result;
                    PrepareRecognition(identity, titleParts, "用户确认-豆瓣官方页面", RecognitionCache.ConfirmedBindingVersion);
                    _store.SaveCache(confirmedBindingKey, JsonSerializer.Serialize(identity));
                }
            }
            var aliases = titleParts.Aliases
                .Concat(new[] { identity.Title, identity.OriginalTitle })
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var target = new DoubanPlusOpenTarget(
                SearchTitle: identity.Title,
                Year: identity.Year ?? titleParts.Year,
                ImdbId: identity.ImdbId,
                Aliases: aliases,
                PreferredStatus: "collect",
                 SourceDescription: record.Source == "PotPlayer" ? "PotPlayer播放完成提醒" : "浏览器播放完成提醒");
            ShowMediaLibrary(target: target);
        }
        var form = new ReminderForm(testMode ? "[快捷测试] " + displayTitle : displayTitle, mediaLocation, StartAiAsync, () => { if (!testMode) { record.Reminded = true; record.SnoozedUntil = null; _store.Save(_settings, _state); } },
        () => { if (!testMode) { record.SnoozedUntil = DateTime.Now.AddMinutes(30); _store.Save(_settings, _state); } });
        form.Show(); form.Activate();
    }

    private MovieIdentity? LoadRecognitionCache(string key, string expectedVersion)
    {
        var cached = _store.LoadCache(key);
        if (string.IsNullOrWhiteSpace(cached)) return null;
        try
        {
            var identity = JsonSerializer.Deserialize<MovieIdentity>(cached);
            return identity is not null && identity.CacheVersion == expectedVersion ? identity : null;
        }
        catch { return null; }
    }

    private static void PrepareRecognition(MovieIdentity identity, MovieTitleParts input, string confirmationMethod, string cacheVersion)
    {
        identity.CacheVersion = cacheVersion;
        identity.ConfirmationMethod = confirmationMethod;
        identity.InputFileName = input.SourceFileName;
        identity.InputAliases = input.Aliases.ToList();
        if (string.IsNullOrWhiteSpace(identity.RecognitionSource)) identity.RecognitionSource = confirmationMethod;
        if (identity.MatchScore == 0) identity.MatchScore = confirmationMethod.StartsWith("用户", StringComparison.Ordinal) ? 100 : 80;
        if (string.IsNullOrWhiteSpace(identity.MatchEvidence)) identity.MatchEvidence = confirmationMethod.StartsWith("用户", StringComparison.Ordinal) ? "用户通过豆瓣官方页面确认" : identity.Evidence;
    }

    private void TestCurrentPotPlayer()
    {
        ScanPotPlayerDirectory();
        var player = PotPlayer.Read();
        if (!player.HasMedia) { MessageBox.Show("PotPlayer 当前没有正在播放的影片。", "无法测试", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var record = MatchPotPlayer(player.Title);
        if (record is null) { MessageBox.Show($"检测到 PotPlayer 正在播放：\n{player.Title}\n\n但没有在设置的影视目录中匹配到对应文件。", "没有匹配到文件", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        ShowReminder(record, true);
    }

    public async Task TestCurrentIqiyiAsync()
    {
        try
        {
            var snap = await _browser.ReadIqiyiAsync();
            if (snap is null) { MessageBox.Show("没有检测到含 HTML5 video 的爱奇艺电影页面。\n\n请先点“启动观影浏览器”，在该独立浏览器中打开一部爱奇艺电影并开始播放，然后再测试。", "未发现爱奇艺电影", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!_state.Videos.TryGetValue(snap.Key, out var r)) _state.Videos[snap.Key] = r = new VideoRecord { Key = snap.Key };
            r.Title = snap.Title; r.Year = snap.Year; r.Genre = snap.Genre; r.Url = snap.Url; r.DurationSeconds = snap.Duration; r.HighestRatio = Math.Max(r.HighestRatio, snap.Ratio); _store.Save(_settings, _state);
            var details = $"已成功读取当前爱奇艺电影：\n\n片名：{snap.Title}\n年份：{snap.Year?.ToString() ?? "页面未提供"}\n类型：{(string.IsNullOrWhiteSpace(snap.Genre) ? "页面未提供" : snap.Genre)}\n时长：{TimeSpan.FromSeconds(snap.Duration):hh\\:mm\\:ss}\n当前：{TimeSpan.FromSeconds(snap.CurrentTime):hh\\:mm\\:ss}\n状态：{(snap.Paused ? "暂停" : "播放")}\n累计真实观看：{TimeSpan.FromSeconds(r.WatchedSeconds):hh\\:mm\\:ss}\n\n继续测试“提醒 → 身份识别 → 评分 → 问题 → 短评”完整流程吗？";
            if (MessageBox.Show(details, "爱奇艺读取成功", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) ShowReminder(r);
        }
        catch (Exception ex) { MessageBox.Show("读取失败：" + ex.Message, "爱奇艺测试失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private void ShowStatus() { if (_statusForm is null || _statusForm.IsDisposed) _statusForm = new BrowserStatusForm(this); _statusForm.Show(); _statusForm.Activate(); RefreshStatusForm(); }
    internal void ShowMediaLibrary(bool reload = false, DoubanPlusOpenTarget? target = null)
    {
        var existing = Application.OpenForms.OfType<HtmlMediaLibraryForm>().FirstOrDefault();
        if (target is not null && existing is not null) { existing.Close(); existing = null; }
        if (reload && existing is not null) { existing.Close(); existing = null; }
        if (existing is not null) { existing.Show(); existing.WindowState = FormWindowState.Maximized; existing.Activate(); return; }
        var library = new HtmlMediaLibraryForm(_state, new DoubanHistoryState(), _settings, _browser, _settings.PreferredBrowser, target, _store.DataDirectory);
        library.Show();
        library.WindowState = FormWindowState.Maximized;
    }
    private void ShowDoubanLogin()
    {
        var library = Application.OpenForms.OfType<HtmlMediaLibraryForm>().FirstOrDefault();
        if (library is null) { ShowMediaLibrary(); library = Application.OpenForms.OfType<HtmlMediaLibraryForm>().FirstOrDefault(); }
        library?.ShowDoubanLogin();
    }
    private void RefreshStatusForm() => _statusForm?.RefreshFromOwner();
    private void EditSettings() { using var f = new SettingsForm(_settings); if (f.ShowDialog() == DialogResult.OK) { _settings = f.Result; _lastPotScan = DateTime.MinValue; ScanPotPlayerDirectory(); _store.Save(_settings, _state); } }
    private static string Format(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    protected override void ExitThreadCore()
    {
        _controlCancellation.Cancel();
        try { _controlServer?.Dispose(); } catch { }
        _timer.Stop();
        _store.Save(_settings, _state);
        _tray.Visible = false;
        _tray.Dispose();
        _controlCancellation.Dispose();
        base.ExitThreadCore();
    }
}

