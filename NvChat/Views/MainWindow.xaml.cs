using NvChat.Models;
using NvChat.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NvChat.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : WindowView
    {
        #region Constructors

        public MainWindow()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
            Closed += OnClosed;
        }

        #endregion


        #region Properties

        private MainViewModel ViewModel => DataContext as MainViewModel;

        #endregion


        #region Helpers - wiring

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MainViewModel oldViewModel)
            {
                oldViewModel.SettingsRequested -= OnSettingsRequested;
                oldViewModel.ConfirmCallback = null;
            }

            if (e.NewValue is MainViewModel newViewModel)
            {
                newViewModel.SettingsRequested += OnSettingsRequested;
                newViewModel.ConfirmCallback = (title, message) => ConfirmDialog.Show(this, title, message);
                RestoreWindowState(newViewModel.GetCurrentSettings());
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            SaveWindowStateToViewModel();
            ViewModel?.SaveState();
        }

        private void OnSettingsRequested(object sender, EventArgs e)
        {
            var viewModel = ViewModel;
            if (viewModel == null)
                return;

            var settingsViewModel = new SettingsViewModel(viewModel.GetCurrentSettings());
            var window = new SettingsWindow
            {
                Owner = this,
                DataContext = settingsViewModel
            };

            window.ShowDialog();

            if (settingsViewModel.Saved)
                viewModel.ApplySettings(settingsViewModel.BuildSettings());
        }

        #endregion


        #region Helpers - window state

        private void RestoreWindowState(AppSettings settings)
        {
            if (settings == null)
                return;

            if (settings.WindowWidth is double w && settings.WindowHeight is double h && w > 300 && h > 300)
            {
                Width = w;
                Height = h;

                if (settings.WindowLeft is double left && settings.WindowTop is double top && IsOnScreen(left, top))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = left;
                    Top = top;
                }

                if (settings.WindowMaximized)
                    WindowState = WindowState.Maximized;
            }
        }

        private static bool IsOnScreen(double left, double top)
        {
            var vsLeft = SystemParameters.VirtualScreenLeft;
            var vsTop = SystemParameters.VirtualScreenTop;
            var vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
            var vsBottom = vsTop + SystemParameters.VirtualScreenHeight;

            // 타이틀바가 화면 안에 조금이라도 걸치는지 대략 확인.
            return left > vsLeft - 200 && left < vsRight - 100 && top > vsTop - 40 && top < vsBottom - 60;
        }

        private void SaveWindowStateToViewModel()
        {
            var viewModel = ViewModel;
            if (viewModel == null)
                return;

            var maximized = WindowState == WindowState.Maximized;
            var bounds = maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);

            if (bounds.Width > 300 && bounds.Height > 300)
                viewModel.SaveWindowState(bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized);
        }

        #endregion


        #region Helpers - input

        private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var sendOnEnter = ViewModel?.SendOnEnter ?? true;

            // SendOnEnter: Enter=전송, Shift+Enter=줄바꿈.  아니면: Ctrl+Enter=전송, Enter=줄바꿈.
            var shouldSend = sendOnEnter ? (shift == false && ctrl == false) : ctrl;

            if (shouldSend == false)
                return;

            e.Handled = true;

            var command = ViewModel?.SendCommand;
            if (command != null && command.CanExecute(null))
                command.Execute(null);
        }

        private void RenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ConversationViewModel conversation)
                return;

            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                e.Handled = true;
                Execute(ViewModel?.CommitRenameCommand, conversation);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Execute(ViewModel?.CancelRenameCommand, conversation);
            }
        }

        private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ConversationViewModel conversation)
                Execute(ViewModel?.CommitRenameCommand, conversation);
        }

        private static void Execute(ICommand command, object parameter)
        {
            if (command != null && command.CanExecute(parameter))
                command.Execute(parameter);
        }

        #endregion


        #region Helpers - scroll to bottom

        private void MessagesScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
                return;

            var atBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 2.0;
            ScrollToBottomButton.Visibility = (atBottom || scrollViewer.ScrollableHeight <= 0)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ScrollToBottomButton_Click(object sender, RoutedEventArgs e)
        {
            MessagesScroller.ScrollToEnd();
        }

        #endregion
    }
}
