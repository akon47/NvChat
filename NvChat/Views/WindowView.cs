using NvChat.Commands;
using NvChat.ViewModels;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace NvChat.Views
{
    /// <summary>
    /// 커스텀(보더리스) 크롬을 가진 창의 기반 클래스.
    /// 타이틀바/최소·최대·닫기 버튼은 테마의 컨트롤 템플릿(Window.xaml)이 제공하고,
    /// 커맨드와 상태 속성은 이 클래스가 제공한다. (Gaia.Views.WindowView 스타일의 경량 이식본)
    /// </summary>
    public class WindowView : Window
    {
        #region Constructors

        public WindowView()
        {
            MinimizeCommand = new DelegateCommand(() => WindowState = WindowState.Minimized);
            MaximizeCommand = new DelegateCommand(ToggleMaximize);
            CloseCommand = new DelegateCommand(OnCloseFromChrome);

            StateChanged += (_, __) => IsMaximized = WindowState == WindowState.Maximized;
            DataContextChanged += OnDataContextChanged;
        }

        #endregion


        #region Fields

        private bool _closeConfirmed;
        private WindowViewModel _boundViewModel;

        #endregion


        #region Dependency Properties

        private static readonly DependencyPropertyKey MinimizeCommandPropertyKey = DependencyProperty.RegisterReadOnly
        (
            nameof(MinimizeCommand), typeof(ICommand), typeof(WindowView), new FrameworkPropertyMetadata(null)
        );

        public static readonly DependencyProperty MinimizeCommandProperty = MinimizeCommandPropertyKey.DependencyProperty;

        public ICommand MinimizeCommand
        {
            get => (ICommand)GetValue(MinimizeCommandProperty);
            private set => SetValue(MinimizeCommandPropertyKey, value);
        }


        private static readonly DependencyPropertyKey MaximizeCommandPropertyKey = DependencyProperty.RegisterReadOnly
        (
            nameof(MaximizeCommand), typeof(ICommand), typeof(WindowView), new FrameworkPropertyMetadata(null)
        );

        public static readonly DependencyProperty MaximizeCommandProperty = MaximizeCommandPropertyKey.DependencyProperty;

        public ICommand MaximizeCommand
        {
            get => (ICommand)GetValue(MaximizeCommandProperty);
            private set => SetValue(MaximizeCommandPropertyKey, value);
        }


        private static readonly DependencyPropertyKey CloseCommandPropertyKey = DependencyProperty.RegisterReadOnly
        (
            nameof(CloseCommand), typeof(ICommand), typeof(WindowView), new FrameworkPropertyMetadata(null)
        );

        public static readonly DependencyProperty CloseCommandProperty = CloseCommandPropertyKey.DependencyProperty;

        public ICommand CloseCommand
        {
            get => (ICommand)GetValue(CloseCommandProperty);
            private set => SetValue(CloseCommandPropertyKey, value);
        }


        private static readonly DependencyPropertyKey IsMaximizedPropertyKey = DependencyProperty.RegisterReadOnly
        (
            nameof(IsMaximized), typeof(bool), typeof(WindowView), new FrameworkPropertyMetadata(false)
        );

        public static readonly DependencyProperty IsMaximizedProperty = IsMaximizedPropertyKey.DependencyProperty;

        /// <summary>
        /// 현재 창이 최대화 상태인지 여부.
        /// </summary>
        public bool IsMaximized
        {
            get => (bool)GetValue(IsMaximizedProperty);
            private set => SetValue(IsMaximizedPropertyKey, value);
        }


        public static readonly DependencyProperty TitleBarContentProperty = DependencyProperty.Register
        (
            nameof(TitleBarContent), typeof(object), typeof(WindowView), new FrameworkPropertyMetadata(null)
        );

        /// <summary>
        /// 타이틀바 가운데 영역에 임베드할 커스텀 콘텐츠.
        /// </summary>
        public object TitleBarContent
        {
            get => GetValue(TitleBarContentProperty);
            set => SetValue(TitleBarContentProperty, value);
        }


        public static readonly DependencyProperty ShowMinimizeButtonProperty = DependencyProperty.Register
        (
            nameof(ShowMinimizeButton), typeof(bool), typeof(WindowView), new FrameworkPropertyMetadata(true)
        );

        public bool ShowMinimizeButton
        {
            get => (bool)GetValue(ShowMinimizeButtonProperty);
            set => SetValue(ShowMinimizeButtonProperty, value);
        }


        public static readonly DependencyProperty ShowMaximizeButtonProperty = DependencyProperty.Register
        (
            nameof(ShowMaximizeButton), typeof(bool), typeof(WindowView), new FrameworkPropertyMetadata(true)
        );

        public bool ShowMaximizeButton
        {
            get => (bool)GetValue(ShowMaximizeButtonProperty);
            set => SetValue(ShowMaximizeButtonProperty, value);
        }


        public static readonly DependencyProperty ShowCloseButtonProperty = DependencyProperty.Register
        (
            nameof(ShowCloseButton), typeof(bool), typeof(WindowView), new FrameworkPropertyMetadata(true)
        );

        public bool ShowCloseButton
        {
            get => (bool)GetValue(ShowCloseButtonProperty);
            set => SetValue(ShowCloseButtonProperty, value);
        }

        #endregion


        #region Overrides

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            TryEnableRoundedCorners();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // 뷰모델과 결합되어 있고 아직 닫기가 확정되지 않았다면,
            // 실제 닫기를 취소하고 뷰모델의 닫기 요청 프로토콜을 태운다.
            if (_boundViewModel != null && _closeConfirmed == false && e.Cancel == false)
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(new Action(() => _boundViewModel.RequestClose()));
            }
        }

        #endregion


        #region Helpers

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void OnCloseFromChrome()
        {
            if (_boundViewModel != null)
                _boundViewModel.RequestClose();
            else
                Close();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_boundViewModel != null)
                _boundViewModel.CloseRequested -= OnViewModelCloseRequested;

            _boundViewModel = e.NewValue as WindowViewModel;

            if (_boundViewModel != null)
                _boundViewModel.CloseRequested += OnViewModelCloseRequested;
        }

        private void OnViewModelCloseRequested(object sender, EventArgs e)
        {
            _closeConfirmed = true;
            Close();
        }

        /// <summary>
        /// Windows 11 에서 창 모서리를 둥글게 처리하도록 DWM 에 요청한다. (구버전에서는 무시)
        /// </summary>
        private void TryEnableRoundedCorners()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                const int DWMWCP_ROUND = 2;

                var preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch
            {
                // DWM 미지원 환경은 무시.
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        #endregion
    }
}
