using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NvChat.Behaviors;
using NvChat.Localization;
using NvChat.Services;
using NvChat.ViewModels;
using NvChat.Views;

namespace NvChat;

/// <summary>
/// 합성 루트. 단일 인스턴스 / 트레이 / 전역 단축키 / 창 간 배선(설정·확인 대화상자·닫기 정책) / 전역 예외 처리 / 주기적 업데이트 확인을 맡는다.
/// 개발·검증용으로 <c>--screenshot &lt;png&gt; [--page main|settings|quick|dialog|menu] [--theme Dark|Light|HighContrast] [--scale 1.25] [--size 900x700] [--state advanced,find,…] [--tab n]</c>
/// 모드를 제공한다(창을 렌더해 PNG 로 저장하고 종료).
/// </summary>
public partial class App : Application
{
    private const string MutexName = "NvChat_SingleInstance_2f9a7c";
    private const string ShowSignalName = "NvChat_Show_2f9a7c";
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    private static LocalizationManager L => LocalizationManager.Instance;

    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private QuickChatWindow? _quickWindow;
    private TrayService? _tray;
    private HotKeyService? _hotKey;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private DispatcherTimer? _updateTimer;
    private bool _exiting;
    private bool _loadStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 창을 닫아도(트레이로 최소화) 앱이 계속 살아있도록 명시적 종료 모드.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log("domain", args.ExceptionObject as Exception);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // 기다리지 않고 흘려보낸 작업(_ = FooAsync())에서 난 예외. 앱은 계속 가지만 원인을 찾을 수 있게 기록은 남긴다.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("task", args.Exception);
            args.SetObserved();
        };

        if (TryHandleScreenshot(e.Args)) return;

        // 단일 인스턴스: 이미 실행 중이면 기존 인스턴스를 띄우고 종료. (신호용 이벤트를 뮤텍스보다 먼저 무조건 생성/열어 두어 시작 경합을 없앤다.)
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName, out _);
        _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _showSignal.Set();
            _showSignal.Dispose();
            _instanceMutex.Dispose();
            Shutdown();
            return;
        }

        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, Timeout.Infinite, false);

        // 이전 업데이트가 남긴 .old / .new 파일 정리.
        UpdateService.CleanupLeftovers();

        try
        {
            _viewModel = new MainViewModel();
            _viewModel.SettingsRequested += (_, _) => OpenSettings();
            _viewModel.SettingsChanged += (_, _) => RegisterHotKey();
            _viewModel.RequestExitForUpdate += (_, _) => RequestExit(); // 업데이트를 적용하면 새 버전이 이미 떠 있으므로 완전히 종료한다.
            _viewModel.ResponseCompleted += OnResponseCompleted;
            _viewModel.ConfirmCallback = (title, message, confirmText) => MessageDialog.Confirm(ActiveWindow, title, message, confirmText);

            _mainWindow = new MainWindow { DataContext = _viewModel };
            _mainWindow.Closing += OnMainWindowClosing;
            _mainWindow.Loaded += async (_, _) =>
            {
                if (_loadStarted) return;
                _loadStarted = true;
                await _viewModel.LoadAsync();
            };
            MainWindow = _mainWindow;

            _tray = new TrayService();
            _tray.OpenRequested += ShowMain;
            _tray.QuickChatRequested += ShowQuick;
            _tray.NewChatRequested += OnTrayNewChat;
            _tray.SettingsRequested += OpenSettings;
            _tray.ExitRequested += RequestExit;

            _hotKey = new HotKeyService();
            _hotKey.HotKeyPressed += ToggleQuick;
            RegisterHotKey();

            _updateTimer = new DispatcherTimer(UpdateCheckInterval, DispatcherPriority.Background, (_, _) => _ = _viewModel.CheckForUpdateAsync(), Dispatcher);
            _updateTimer.Start();

            // 자동 시작을 켜 둔 경우 실행 파일을 옮겼어도 경로가 맞도록 다시 써 둔다.
            if (_viewModel.GetCurrentSettings().StartWithWindows) AutoStartService.Apply(true);

            // 윈도우 로그인으로 자동 시작됐으면 창 없이 트레이로만 뜬다. (창이 Loaded 되지 않으므로 로드는 직접 시작한다)
            if (e.Args.Contains(AutoStartService.StartHiddenArgument))
            {
                _loadStarted = true;
                _ = _viewModel.LoadAsync();
            }
            else
            {
                _mainWindow.Show();
            }
        }
        catch (Exception ex)
        {
            Log("startup", ex);
            ShowErrorDialog(L["StartupErrorTitle"], Describe(ex));
            Shutdown(1);
        }
    }

    /// <summary>대화상자의 소유자로 쓸 창: 활성 창 → 보이는 메인 창 → 없음.</summary>
    private Window? ActiveWindow => Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? (_mainWindow is { IsVisible: true } ? _mainWindow : null);

    // ===== 창 =====

    private void ShowMain()
    {
        if (_mainWindow is null) return;

        _viewModel?.RefreshDateGroups();
        if (!_mainWindow.IsVisible) _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
    }

    private void ShowQuick()
    {
        if (_quickWindow is null)
        {
            _quickWindow = new QuickChatWindow { DataContext = _viewModel };
            _quickWindow.OpenMainRequested += (_, _) => ShowMain();
        }
        _quickWindow.ShowAndActivate();
    }

    private void ToggleQuick()
    {
        if (_quickWindow is { IsVisible: true }) _quickWindow.Hide();
        else ShowQuick();
    }

    private void OnTrayNewChat()
    {
        if (_viewModel?.NewConversationCommand.CanExecute(null) == true) _viewModel.NewConversationCommand.Execute(null);
        ShowMain();
    }

    /// <summary>메인 창 닫기 정책: 설정에 따라 트레이로 숨기거나(최초 1회 안내 풍선) 앱을 종료한다.</summary>
    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting || _viewModel is null || _mainWindow is null) return;

        if (_viewModel.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            _mainWindow.Hide();
            _viewModel.SaveState();
            ShowTrayHintOnce();
            return;
        }

        // 종료: 창은 그대로 닫히게 두고, 정리는 이 Closing 처리가 끝난 뒤 진행한다.
        _exiting = true;
        Dispatcher.BeginInvoke(RequestExit);
    }

    private void ShowTrayHintOnce()
    {
        if (_viewModel is null || _viewModel.GetCurrentSettings().CloseToTrayHintShown) return;
        _viewModel.MarkCloseToTrayHintShown();
        _tray?.ShowBalloon(L["TrayHintTitle"], L["TrayHintText"]);
    }

    private void OpenSettings()
    {
        if (_viewModel is null) return;

        SettingsViewModel settingsViewModel = new(_viewModel.GetCurrentSettings(), _viewModel.GetModelIds());
        SettingsWindow window = new() { DataContext = settingsViewModel };
        if (_mainWindow is { IsVisible: true }) window.Owner = _mainWindow;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // 설정 창이 떠 있는 동안에는 전역 단축키를 풀어 둔다. 단축키 칸에 지금 쓰는 조합을 다시 눌러도 가로채이지 않고 입력된다.
        _hotKey?.Unregister();
        window.ShowDialog();

        if (settingsViewModel.Saved)
        {
            _viewModel.ApplySettings(settingsViewModel.BuildSettings()); // SettingsChanged → RegisterHotKey
        }
        else
        {
            settingsViewModel.RevertPreview();
            RegisterHotKey();
        }

        if (settingsViewModel.FoundUpdate is { } update) _viewModel.OfferUpdate(update);
    }

    /// <summary>
    /// 응답이 끝났는데 사용자가 보고 있지 않으면(창이 숨겨졌거나 다른 앱이 앞에 있으면) 알린다.
    /// 긴 답변을 걸어 두고 다른 일을 하다가 끝난 줄 모르고 지나치지 않도록.
    /// </summary>
    private void OnResponseCompleted(string title, string preview)
    {
        if (Windows.OfType<Window>().Any(w => w.IsActive)) return;

        _tray?.ShowBalloon(title, preview);
        if (_mainWindow is { IsVisible: true }) WindowBehaviors.Flash(_mainWindow);
    }

    private void RegisterHotKey()
    {
        if (_viewModel is null || _hotKey is null) return;

        string? hotkey = _viewModel.GetCurrentSettings().GlobalHotkey;
        bool registered = _hotKey.Register(hotkey);

        // 다른 앱이 이미 쓰는 조합이면 조용히 죽지 않도록 사용자에게 알린다.
        if (!registered && !HotKeyService.IsDisabled(hotkey)) _viewModel.StatusMessage = L.Tr("HotkeyRegisterFailed", hotkey);
    }

    /// <summary>트레이 '종료' 등에서 호출되는 실제 앱 종료.</summary>
    private void RequestExit()
    {
        _exiting = true;

        try
        {
            // 실제 종료 경로에서만 진행 중인 스트림을 취소한다(트레이 최소화 시에는 계속 생성).
            _viewModel?.CancelStreaming();
            _viewModel?.SaveState();
        }
        catch { /* 종료 중 저장 실패는 무시 */ }

        _updateTimer?.Stop();
        _hotKey?.Dispose();
        _tray?.Dispose();
        if (_showSignal is not null) _showWait?.Unregister(_showSignal);
        _showSignal?.Dispose();
        try { _instanceMutex?.ReleaseMutex(); } catch { /* 소유 스레드가 아니면 무시 */ }
        _instanceMutex?.Dispose();

        _quickWindow?.CloseForReal();
        if (_mainWindow is { IsLoaded: true }) _mainWindow.Close(); // 창 위치 저장(WindowBehaviors)은 Closing 에서 처리된다

        // 스트림 취소로 마무리되는 대화까지 포함해, 백그라운드 저장이 디스크에 내려간 뒤 끝낸다.
        try
        {
            _viewModel?.SaveState();
            _viewModel?.FlushStorage();
        }
        catch { /* 종료 중 저장 실패는 무시 */ }

        Shutdown();
    }

    // ===== 오류 처리 =====

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("dispatcher", e.Exception);
        e.Handled = true;
        ShowErrorDialog(L["ErrorTitle"], Describe(e.Exception));
    }

    /// <summary>테마 대화상자로 오류를 알린다. 테마 자체가 깨진 상황이면 시스템 메시지 박스로 폴백.</summary>
    private void ShowErrorDialog(string title, string message)
    {
        try { MessageDialog.ShowError(ActiveWindow, title, message); }
        catch { MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static string Describe(Exception? ex) => ex switch
    {
        null => L["UnknownError"],
        { InnerException: { } inner } => ex.Message + "\n\n" + inner.Message,
        _ => ex.Message,
    };

    private static void Log(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(AppPaths.ErrorLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source}) {ex}\n\n");
        }
        catch { /* 로그 기록 실패는 무시 */ }
    }

    // ===== 스크린샷 모드(UI 검증용) =====

    /// <summary>
    /// <c>--screenshot &lt;png&gt; [--page …] [--theme …] [--scale …] [--size WxH] [--state …] [--tab n]</c>: 해당 창을 띄워 렌더링한 뒤 PNG 로 저장하고 종료한다.
    /// 사람이 보지 않고도(에이전트·CI) UI 변경을 확인하기 위한 개발용 모드. 바인딩 오류는 <c>&lt;png&gt;.binding.log</c> 에 남긴다. 처리했으면 true.
    /// </summary>
    private bool TryHandleScreenshot(string[] args)
    {
        int si = Array.IndexOf(args, "--screenshot");
        if (si < 0 || si + 1 >= args.Length) return false;

        string output = Path.GetFullPath(args[si + 1]);
        string page = ArgValue(args, "--page") ?? "main";
        string? theme = ArgValue(args, "--theme");
        string[] states = (ArgValue(args, "--state") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(output + ".binding.log"));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        MainViewModel viewModel = new();

        // 가짜 서버로 전송 경로까지 확인할 때: --api-key x --base-url http://127.0.0.1:8765/v1 --send "질문" --delay 6000
        if (ArgValue(args, "--api-key") is { } apiKey) viewModel.GetCurrentSettings().ApiKey = apiKey;
        if (ArgValue(args, "--base-url") is { } baseUrl) viewModel.GetCurrentSettings().BaseUrl = baseUrl;
        int delay = int.TryParse(ArgValue(args, "--delay"), out int customDelay) ? customDelay : 2500;

        if (theme is not null) ThemeManager.Instance.Apply(theme);
        if (double.TryParse(ArgValue(args, "--scale"), System.Globalization.CultureInfo.InvariantCulture, out double scale)) ThemeManager.Instance.Scale = scale;

        // 트레이 메뉴 견본: 메뉴만 띄워 한 줄을 강조한 상태로 저장한다. --page tray [--hover n]
        if (page == "tray")
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await RenderTrayMenuAsync(output, int.TryParse(ArgValue(args, "--hover"), out int hovered) ? hovered : 1);
                }
                finally
                {
                    Shutdown();
                }
            });
            return true;
        }

        Window window = page switch
        {
            "settings" => new SettingsWindow
            {
                DataContext = new SettingsViewModel(viewModel.GetCurrentSettings(), viewModel.GetModelIds()) { SelectedTabIndex = int.TryParse(ArgValue(args, "--tab"), out int tab) ? tab : 0 },
            },
            "quick" => new QuickChatWindow { DataContext = viewModel },
            "dialog" => new MessageDialog
            {
                DataContext = new MessageDialogViewModel
                {
                    Title = L["DeleteConversationTitle"],
                    Message = L.Tr("DeleteConversationMsg", "Sample conversation"),
                    ConfirmText = L["Delete"],
                    IsConfirmation = true,
                    IsDestructive = true,
                },
            },
            _ => new MainWindow { DataContext = viewModel },
        };
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();

        // 저장된 창 크기 복원(SourceInitialized) 뒤에 적용되도록 Show 다음에 맞춘다.
        if (ArgValue(args, "--size")?.Split('x') is [var w, var h] && double.TryParse(w, out double width) && double.TryParse(h, out double height))
        {
            window.WindowState = WindowState.Normal;
            window.Width = width;
            window.Height = height;
        }

        if (page is "main" or "quick" or "menu") _ = LoadForScreenshotAsync(viewModel, states, ArgValue(args, "--select"), ArgValue(args, "--send"), ArgValue(args, "--then"));

        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(delay); // 모델 목록 조회·애니메이션이 안정될 때까지
            try
            {
                // 목록 맨 위를 찍고 싶을 때: --state top
                if (states.Contains("top"))
                {
                    foreach (var viewer in FindDescendants<ScrollViewer>(window).Where(v => v.ScrollableHeight > 0)) viewer.ScrollToTop();
                    await Task.Delay(700);
                }

                if (page == "menu") await RenderConversationMenuAsync(window, output);
                else RenderToPng(window, output);
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Flush();
                Shutdown();
            }
        });
        return true;
    }

    private static string? ArgValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>스크린샷용 상태를 만든다: <c>--state advanced,find,collapsed,toast,banner,top</c>, <c>--select 제목일부</c>.</summary>
    private static async Task LoadForScreenshotAsync(MainViewModel viewModel, string[] states, string? selectTitle, string? send, string? then)
    {
        try
        {
            await viewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            Log("screenshot", ex); // 로드가 실패하면 빈 화면만 찍히므로 원인을 남긴다
            throw;
        }

        if (selectTitle is not null && viewModel.Conversations.FirstOrDefault(c => c.Title.Contains(selectTitle, StringComparison.OrdinalIgnoreCase)) is { } selected)
            viewModel.SelectedConversation = selected;

        foreach (string state in states)
        {
            switch (state)
            {
                case "advanced": viewModel.IsAdvancedOpen = true; break;
                case "collapsed": viewModel.IsSidebarCollapsed = true; break;
                case "new": viewModel.NewConversationCommand.Execute(null); break;
                case "regen": viewModel.RegenerateLastCommand.Execute(null); break;
                case "toast": viewModel.ShowToast("Sample toast", "Undo", () => { }); break;
                case "banner": viewModel.StatusMessage = "Sample status banner message."; break;
                case "find":
                    viewModel.OpenFindCommand.Execute(null);
                    viewModel.FindText = "the";
                    break;
            }
        }

        if (send is not null)
        {
            viewModel.InputText = send;
            viewModel.SendCommand.Execute(null);
        }

        // 응답을 받은 뒤 이어서 할 동작: --then regen,prev,next,delete,undo,stop (각 동작 사이에 잠깐 기다린다)
        foreach (string action in (then ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await Task.Delay(1500);
            var last = viewModel.SelectedConversation?.Messages.LastOrDefault();
            switch (action)
            {
                case "regen": viewModel.RegenerateLastCommand.Execute(null); break;
                case "prev": viewModel.PreviousVariantCommand.Execute(last); break;
                case "next": viewModel.NextVariantCommand.Execute(last); break;
                case "delete": viewModel.DeleteMessageCommand.Execute(last); break;
                case "undo": viewModel.InvokeToastActionCommand.Execute(null); break;
                case "stop": viewModel.StopCommand.Execute(null); break;
                case "retry": viewModel.RetryCommand.Execute(null); break;
            }
        }
    }

    /// <summary>사이드바 첫 대화의 우클릭 메뉴를 열어 렌더한다(메뉴/구분선 스타일 검증용).</summary>
    private static async Task RenderConversationMenuAsync(Window window, string output)
    {
        if (FindDescendants<FrameworkElement>(window).FirstOrDefault(f => f.Name == "ConvRow" && f.ContextMenu is not null) is not { ContextMenu: { } menu } row) return;

        menu.PlacementTarget = row;
        menu.IsOpen = true;
        await Task.Delay(500);
        RenderToPng(menu, output);
        menu.IsOpen = false;
    }

    /// <summary>트레이 메뉴를 열어 index 번째 줄을 강조(키보드 포커스)한 상태로 렌더한다.</summary>
    private static async Task RenderTrayMenuAsync(string output, int index)
    {
        var menu = TrayService.CreatePreviewMenu();
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = 200;
        menu.VerticalOffset = 200;
        menu.IsOpen = true;
        await Task.Delay(400);

        // 마우스를 올린 상태(IsHighlighted)는 실제 포인터 없이는 생기지 않는다. 견본에서는 그 속성을 직접 켜서 호버 모양을 확인한다.
        if (index >= 0 && index < menu.Items.Count && menu.Items[index] is MenuItem item)
            typeof(MenuItem).GetProperty(nameof(MenuItem.IsHighlighted))?.SetValue(item, true);
        await Task.Delay(300);

        RenderToPng(menu, output);
        menu.IsOpen = false;
    }

    private static void RenderToPng(FrameworkElement element, string output)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        RenderTargetBitmap bitmap = new((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(output);
        encoder.Save(stream);
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) yield return hit;
            foreach (var deeper in FindDescendants<T>(child)) yield return deeper;
        }
    }
}
