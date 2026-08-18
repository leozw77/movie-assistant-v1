namespace QbPotDoubanAi;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        DiagnosticLogger.WriteStartup();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashLogger.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLogger.Write(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashLogger.Write(e.Exception); e.SetObserved(); };
        if (args.Contains("--webview2-runtime-test", StringComparer.OrdinalIgnoreCase))
        {
            var probe = WebView2EnvironmentProvider.ProbeRuntime();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "webview2-runtime-test-result.txt"),
                $"{(probe.Available ? "通过" : "失败")}：WebView2 Evergreen Runtime\n版本：{probe.Version}\n错误：{probe.Error}");
            return;
        }
        if (args.Contains("--review-self-test", StringComparer.OrdinalIgnoreCase))
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "review-self-test-result.txt"),
                ReviewPipelineSelfTest.Run(),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return;
        }
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), SelfTest.Run());
            return;
        }
        if (args.Contains("--restore-browser-window-test", StringComparer.OrdinalIgnoreCase))
        {
            var store = new Store(); var service = new BrowserCdpService(store.DataDirectory);
            try
            {
                var session = service.LaunchAsync("Chrome").GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "browser-window-restore-result.txt"), $"通过：已请求恢复 {session.BrowserName} 窗口\nCDP：127.0.0.1:{session.Port}\n进程：{session.ProcessId}");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "browser-window-restore-result.txt"), "失败：" + ex); }
            return;
        }
        if (args.Length >= 2 && args[0].Equals("--douban-subject-test", StringComparison.OrdinalIgnoreCase))
        {
            var store = new Store(); var service = new BrowserCdpService(store.DataDirectory);
            try
            {
                service.EnsureBackgroundAsync("Chrome").GetAwaiter().GetResult();
                var metadata = service.ReadDoubanSubjectMetadataAsync(args[1]).GetAwaiter().GetResult();
                var result = $"标题：{metadata.Title}\n简介长度：{metadata.Summary.Length}\nCaptcha：{metadata.Captcha}\n错误：{metadata.Error}";
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "douban-subject-test-result.txt"), result);
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "douban-subject-test-result.txt"), "失败：" + ex); }
            finally { service.RestoreDoubanWorkerAsync().GetAwaiter().GetResult(); }
            return;
        }
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), SelfTest.Run());
            return;
        }
        if (args.Contains("--pot-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            var player = PotPlayer.Read();
            var result = player.HasMedia
                ? $"通过：检测到 PotPlayer\n片名：{player.Title}\n进度：{TimeSpan.FromMilliseconds(player.CurrentMs):hh\\:mm\\:ss}/{TimeSpan.FromMilliseconds(player.TotalMs):hh\\:mm\\:ss}\n比例：{player.Ratio:P1}\n状态码：{player.Status}（2=播放）"
                : "未通过：当前未检测到正在播放且可读取时长的 PotPlayer 影片。请先用 PotPlayer 打开并播放一部影片后重试。";
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "pot-smoke-result.txt"), result);
            return;
        }
        if (args.Length >= 2 && args[0].Equals("--douban-search-test", StringComparison.OrdinalIgnoreCase))
        {
            var store = new Store(); var service = new BrowserCdpService(store.DataDirectory);
            try
            {
                service.EnsureBackgroundAsync("Chrome").GetAwaiter().GetResult();
                var start = args.Length >= 3 && int.TryParse(args[2], out var requestedStart) ? Math.Max(0, requestedStart) : 0;
                var result = service.ReadDoubanMovieSearchPageAsync(args[1], start).GetAwaiter().GetResult();
                var lines = new List<string> { $"起始位置：{start}", $"登录：{result.LoggedIn}", $"验证码：{result.Captcha}", $"候选数：{result.Items.Count}", $"下一页：{result.HasMore}", $"错误：{result.Error}" };
                lines.AddRange(result.Items.Take(3).Select((item, index) => $"\n候选 {index + 1}\nID：{item.SubjectId}\nURL：{item.SubjectUrl}\n原始可见文本：\n{item.VisibleText}"));
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "douban-search-test-result.txt"), string.Join(Environment.NewLine, lines));
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "douban-search-test-result.txt"), "失败：" + ex); }
            return;
        }
        if (args.Length >= 2 && args[0].Equals("--pt-search-test", StringComparison.OrdinalIgnoreCase))
        {
            var store = new Store(); var service = new BrowserCdpService(store.DataDirectory);
            try
            {
                service.EnsureBackgroundAsync("Chrome").GetAwaiter().GetResult();
                service.OpenPtDepilerSearchAsync(args[1]).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "pt-search-test-result.txt"), $"通过：已使用 IMDb {args[1]} 打开 PT-Depiler 搜索页。");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "pt-search-test-result.txt"), "失败：" + ex); }
            return;
        }
        if (args.Contains("--browser-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            var service = new BrowserCdpService(Path.Combine(AppContext.BaseDirectory, "smoke-test-data"));
            try
            {
                var session = service.LaunchAsync("Chrome").GetAwaiter().GetResult();
                var connected = service.IsConnectedAsync().GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "browser-smoke-result.txt"), $"{(connected ? "通过" : "失败")}：{session.BrowserName} CDP 已连接到 127.0.0.1:{session.Port}\n配置目录：{session.ProfileDirectory}");
                try { System.Diagnostics.Process.GetProcessById(session.ProcessId).Kill(true); } catch { }
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "browser-smoke-result.txt"), "失败：" + ex); }
            return;
        }
        if (args.Contains("--douban-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            var service = new BrowserCdpService(Path.Combine(AppContext.BaseDirectory, "douban-smoke-data"));
            try
            {
                var session = service.LaunchAsync("Chrome").GetAwaiter().GetResult(); service.OpenDoubanLoginAsync().GetAwaiter().GetResult(); Thread.Sleep(2500);
                var found = service.HasDoubanPageAsync().GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "douban-smoke-result.txt"), $"{(found ? "通过" : "失败")}：已在 {session.BrowserName} 打开并检测到豆瓣登录/我的电影页面\n配置目录：{session.ProfileDirectory}");
                try { System.Diagnostics.Process.GetProcessById(session.ProcessId).Kill(true); } catch { }
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "douban-smoke-result.txt"), "失败：" + ex); }
            return;
        }
        ApplicationConfiguration.Initialize();
        var unifiedShellPreview = args.Contains("--unified-shell-preview", StringComparer.OrdinalIgnoreCase);
        var previewIdentity = Environment.ProcessPath ?? AppContext.BaseDirectory;
        if (unifiedShellPreview) SingleInstanceControl.UseUnifiedShellPreviewPipe(previewIdentity);
        var openDoubanPlus = unifiedShellPreview || args.Contains("--open-douban-plus", StringComparer.OrdinalIgnoreCase);
        var mutexName = unifiedShellPreview
            ? SingleInstanceControl.GetUnifiedShellPreviewMutexName(previewIdentity)
            : "Local\\DoubanBrowserReminder";
        using var mutex = new Mutex(true, mutexName, out var first);
        if (!first)
        {
            var command = args.Contains("--exit", StringComparer.OrdinalIgnoreCase)
                ? "exit"
                : args.Contains("--open-douban-plus", StringComparer.OrdinalIgnoreCase)
                    ? "open"
                    : "show";
            if (!SingleInstanceControl.TrySendCommand(command))
                MessageBox.Show("程序已经在后台运行，但当前实例控制通道不可用。请稍后重试。", "观影助手");
            return;
        }
        var previewDataDirectory = unifiedShellPreview
            ? Path.Combine(AppContext.BaseDirectory, "preview-data")
            : null;
        var trayContext = new TrayContext(previewDataDirectory);
        if (openDoubanPlus) trayContext.ShowMediaLibrary();
        Application.Run(trayContext);
    }


}
