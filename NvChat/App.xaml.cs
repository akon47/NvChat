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
        #region Overrides

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 디스패처 루프 밖(시작 경로)과 비-UI 스레드의 예외까지 포착.
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            try
            {
                var viewModel = new MainViewModel();
                var window = new MainWindow { DataContext = viewModel };

                window.Loaded += async (_, __) => await viewModel.LoadAsync();

                MainWindow = window;
                window.Show();
            }
            catch (Exception ex)
            {
                Log("startup", ex);
                MessageBox.Show(Describe(ex), "NvChat 시작 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        #endregion


        #region Helpers

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log("dispatcher", e.Exception);
            MessageBox.Show(Describe(e.Exception), "NvChat 오류", MessageBoxButton.OK, MessageBoxImage.Error);

            // 개별 UI 예외로 앱 전체가 죽지 않도록 처리 완료로 표시.
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
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source}) {ex}\n\n";
                File.AppendAllText(Path.Combine(AppPaths.DataDirectory, "error.log"), line);
            }
            catch
            {
                // 로깅 실패는 무시.
            }
        }

        #endregion
    }
}
