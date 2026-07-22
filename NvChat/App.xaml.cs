using NvChat.Services;
using NvChat.ViewModels;
using NvChat.Views;
using System;
using System.IO;
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

        private MainViewModel _viewModel;
        private MainWindow _mainWindow;
        private QuickChatWindow _quickWindow;
        private TrayService _tray;
        private HotKeyService _hotKey;

        #endregion


        #region Overrides

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 창을 닫아도(트레이로 최소화) 앱이 계속 살아있도록 명시적 종료 모드.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            try
            {
                _viewModel = new MainViewModel();
                _viewModel.SettingsChanged += (_, __) => RegisterHotKey();

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
                MessageBox.Show(Describe(ex), "NvChat 시작 오류", MessageBoxButton.OK, MessageBoxImage.Error);
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
            _hotKey?.Register(hotkey);
        }

        /// <summary>트레이 '종료' 등에서 호출되는 실제 앱 종료.</summary>
        public void RequestExit()
        {
            try
            {
                _viewModel?.SaveState();
            }
            catch
            {
            }

            _hotKey?.Dispose();
            _tray?.Dispose();

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
            MessageBox.Show(Describe(e.Exception), "NvChat 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log("domain", e.ExceptionObject as Exception);
        }

        private static string Describe(Exception ex)
        {
            if (ex == null)
                return "알 수 없는 오류";

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
