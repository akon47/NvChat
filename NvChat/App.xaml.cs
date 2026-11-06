using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NvChat.Localization;
using NvChat.Services;
using NvChat.ViewModels;
using NvChat.Views;

namespace NvChat;

/// <summary>
/// 합성 루트. 단일 인스턴스 / 트레이 / 전역 단축키 / 창 간 배선(설정·확인 대화상자·닫기 정책) / 전역 예외 처리 / 주기적 업데이트 확인을 맡는다.
/// 개발·검증용으로 <c>--screenshot &lt;png&gt; [--page main|settings|quick|dialog|menu]</c> 모드를 제공한다(창을 렌더해 PNG 로 저장하고 종료).
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 창을 닫아도(트레이로 최소화) 앱이 계속 살아있도록 명시적 종료 모드.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log("domain", args.ExceptionObject as Exception);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

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
            _viewModel.ConfirmCallback = (title, message, confirmText) => MessageDialog.Confirm(ActiveWindow, title, message, confirmText);

            _mainWindow = new MainWindow { DataContext = _viewModel };
            _mainWindow.Closing += OnMainWindowClosing;
            _mainWindow.Loaded += async (_, _) => await _viewModel.LoadAsync();
            MainWindow = _mainWindow;

            _tray = new TrayService();
            _tray.OpenRequested += ShowMain;
            _tray.QuickChatRequested += ShowQuick;
            _tray.NewChatRequested += OnTrayNewChat;
            _tray.ExitRequested += RequestExit;

            _hotKey = new HotKeyService();
            _hotKey.HotKeyPressed += ToggleQuick;
            RegisterHotKey();

            _updateTimer = new DispatcherTimer(UpdateCheckInterval, DispatcherPriority.Background, (_, _) => _ = _viewModel.CheckForUpdateAsync(), Dispatcher);
            _updateTimer.Start();

            _mainWindow.Show();
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

        SettingsViewModel settingsViewModel = new(_viewModel.GetCurrentSettings());
        SettingsWindow window = new() { DataContext = settingsViewModel };
        if (_mainWindow is { IsVisible: true }) window.Owner = _mainWindow;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();

        if (settingsViewModel.Saved) _viewModel.ApplySettings(settingsViewModel.BuildSettings());
        else settingsViewModel.RevertLanguage();

        if (settingsViewModel.FoundUpdate is { } update) _viewModel.OfferUpdate(update);
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
    /// <c>--screenshot &lt;png&gt; [--page main|settings|quick|dialog|menu]</c>: 해당 창을 띄워 렌더링한 뒤 PNG 로 저장하고 종료한다.
    /// 사람이 보지 않고도(에이전트·CI) UI 변경을 확인하기 위한 개발용 모드. 바인딩 오류는 <c>&lt;png&gt;.binding.log</c> 에 남긴다. 처리했으면 true.
    /// </summary>
    private bool TryHandleScreenshot(string[] args)
    {
        int si = Array.IndexOf(args, "--screenshot");
        if (si < 0 || si + 1 >= args.Length) return false;

        string output = Path.GetFullPath(args[si + 1]);
        int pi = Array.IndexOf(args, "--page");
        string page = pi >= 0 && pi + 1 < args.Length ? args[pi + 1] : "main";

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(output + ".binding.log"));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        MainViewModel viewModel = new();
        Window window = page switch
        {
            "settings" => new SettingsWindow { DataContext = new SettingsViewModel(viewModel.GetCurrentSettings()) },
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
        if (page is "main" or "quick" or "menu") _ = viewModel.LoadAsync();

        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(2500); // 모델 목록 조회·애니메이션이 안정될 때까지
            try
            {
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
