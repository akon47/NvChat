using NvChat.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace NvChat.Views
{
    /// <summary>
    /// 전역 단축키로 뜨는 작은 플로팅 채팅창. MainViewModel 을 공유한다.
    /// 닫기는 실제 종료가 아니라 숨김(런처 특성).
    /// </summary>
    public partial class QuickChatWindow : WindowView
    {
        #region Constructors

        public QuickChatWindow()
        {
            InitializeComponent();
        }

        #endregion


        #region Fields

        private bool _forceClose;

        #endregion


        #region Events

        public event EventHandler OpenMainRequested;

        #endregion


        #region Properties

        private MainViewModel ViewModel => DataContext as MainViewModel;

        #endregion


        #region Helpers

        public void ShowAndActivate()
        {
            PositionNearTop();

            if (IsVisible == false)
                Show();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Activate();
            Topmost = true;
            QuickInput.Focus();
        }

        public void CloseForReal()
        {
            _forceClose = true;
            Close();
        }

        private void PositionNearTop()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + area.Height * 0.14;
        }

        // MainViewModel 을 메인 창과 공유하므로 VM 닫기 프로토콜에서 제외한다.
        protected override bool UsesViewModelCloseProtocol => false;

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_forceClose == false)
            {
                e.Cancel = true;
                Hide();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        private void QuickInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var sendOnEnter = ViewModel?.SendOnEnter ?? true;
            var shouldSend = sendOnEnter ? (shift == false && ctrl == false) : ctrl;

            if (shouldSend == false)
                return;

            e.Handled = true;

            var command = ViewModel?.SendCommand;
            if (command != null && command.CanExecute(null))
                command.Execute(null);
        }

        private void OpenMain_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            OpenMainRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        #endregion
    }
}
