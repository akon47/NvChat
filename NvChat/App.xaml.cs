using NvChat.Localization;
using NvChat.Services;
using NvChat.ViewModels;
using NvChat.Views;
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace NvChat
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        #region Fields

        private const string MutexName = "NvChat_SingleInstance_2f9a7c";
        private const string ShowSignalName = "NvChat_Show_2f9a7c";

        private MainViewModel _viewModel;
        private MainWindow _mainWindow;
        private QuickChatWindow _quickWindow;
        private TrayService _tray;
        private HotKeyService _hotKey;
        private Mutex _instanceMutex;
        private EventWaitHandle _showSignal;
        private RegisteredWaitHandle _showWait;

        #endregion


        #region Overrides

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 단일 인스턴스: 이미 실행 중이면 기존 인스턴스를 띄우고 종료.
            // (신호용 이벤트를 뮤텍스보다 먼저 무조건 생성/열어 두어 시작 경합을 없앤다.)
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName, out _);
            _instanceMutex = new Mutex(true, MutexName, out var createdNew);

            if (createdNew == false)
            {
                _showSignal.Set();
                _showSignal.Dispose();
                _instanceMutex.Dispose();
                Shutdown();
                return;
            }

            _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, __) => Dispatcher.BeginInvoke(new Action(ShowMain)), null, Timeout.Infinite, false);

            // 창을 닫아도(트레이로 최소화) 앱이 계속 살아있도록 명시적 종료 모드.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // 이전 업데이트가 남긴 .old / .new 파일 정리.
            UpdateService.CleanupLeftovers();

            try
            {
                _viewModel = new MainViewModel();
                _viewModel.SettingsChanged += (_, __) => RegisterHotKey();
                // 업데이트를 적용하면 새 버전이 이미 떠 있으므로 현재 인스턴스는 완전히 종료한다.
                _viewModel.RequestExitForUpdate += (_, __) => RequestExit();

                _mainWindow = new MainWindow { DataContext = _viewModel };
                _mainWindow.Loaded += async (_, __) => await _viewModel.LoadAsync();
                MainWindow = _mainWindow;

                _tray = new TrayService();
                _tray.OpenRequested += ShowMain;
                _tray.QuickChatRequested += ShowQuick;
                _tray.NewChatRequested += OnTrayNewChat;
                _tray.ExitRequested += RequestExit;

                _hotKey = new HotKeyService();
                _hotKey.HotKeyPressed += ToggleQuick;
                RegisterHotKey();

                _mainWindow.Show();
            }
            catch (Exception ex)
            {
                Log("startup", ex);
                MessageBox.Show(Describe(ex), LocalizationManager.Instance["StartupErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        #endregion


        #region Launcher

        public void ShowMain()
        {
            if (_mainWindow == null)
                return;

            if (_mainWindow.IsVisible == false)
                _mainWindow.Show();

            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;

            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
        }

        private void ShowQuick()
        {
            if (_quickWindow == null)
            {
                _quickWindow = new QuickChatWindow { DataContext = _viewModel };
                _quickWindow.OpenMainRequested += (_, __) => ShowMain();
            }

            _quickWindow.ShowAndActivate();
        }

        private void ToggleQuick()
        {
            if (_quickWindow != null && _quickWindow.IsVisible)
                _quickWindow.Hide();
            else
                ShowQuick();
        }

        private void OnTrayNewChat()
        {
            if (_viewModel?.NewConversationCommand?.CanExecute(null) == true)
                _viewModel.NewConversationCommand.Execute(null);

            ShowMain();
        }

        private void RegisterHotKey()
        {
            var hotkey = _viewModel?.GetCurrentSettings()?.GlobalHotkey;
            var registered = _hotKey?.Register(hotkey) ?? false;

            // 다른 앱이 이미 쓰는 조합이면 조용히 죽지 않도록 사용자에게 알린다.
            var wantsHotkey = HotKeyService.IsDisabled(hotkey) == false;

            if (registered == false && wantsHotkey && _viewModel != null)
                _viewModel.StatusMessage = LocalizationManager.Instance.Tr("HotkeyRegisterFailed", hotkey);
        }

        /// <summary>트레이 '종료' 등에서 호출되는 실제 앱 종료.</summary>
        public void RequestExit()
        {
            try
            {
                // 실제 종료 경로에서만 진행 중인 스트림을 취소한다(트레이 최소화 시에는 계속 생성).
                _viewModel?.CancelStreaming();
                _viewModel?.SaveState();
            }
            catch
            {
            }

            _hotKey?.Dispose();
            _tray?.Dispose();
            _showWait?.Unregister(_showSignal);
            _showSignal?.Dispose();
            try { _instanceMutex?.ReleaseMutex(); } catch { }
            _instanceMutex?.Dispose();

            if (_mainWindow != null)
                _mainWindow.AllowClose = true;

            _quickWindow?.CloseForReal();

            Shutdown();
        }

        #endregion


        #region Error handling

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log("dispatcher", e.Exception);
            MessageBox.Show(Describe(e.Exception), LocalizationManager.Instance["ErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log("domain", e.ExceptionObject as Exception);
        }

        private static string Describe(Exception ex)
        {
            if (ex == null)
                return LocalizationManager.Instance["UnknownError"];

            var message = ex.Message;
            if (ex.InnerException != null)
                message += "\n\n" + ex.InnerException.Message;

            return message;
        }

        private static void Log(string source, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                File.AppendAllText(Path.Combine(AppPaths.DataDirectory, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source}) {ex}\n\n");
            }
            catch
            {
            }
        }

        #endregion
    }
}
