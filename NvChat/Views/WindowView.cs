using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using NvChat.ViewModels;

namespace NvChat.Views;

/// <summary>
/// 커스텀 크롬(다크 타이틀바)을 가진 창의 기반 클래스. 타이틀바/캡션 버튼은 테마의 컨트롤 템플릿(Window.xaml)이 그리고,
/// 커맨드와 상태 속성은 이 클래스가 제공한다.
/// <list type="bullet">
/// <item>WindowChrome 의 GlassFrameThickness=1 로 DWM 이 그림자·테두리·둥근 모서리를 그리게 두고, 다크 모드·테두리 색 힌트를 DWM 에 알린다.</item>
/// <item>Windows 11 스냅 레이아웃: 최대화 버튼 위에서 <c>WM_NCHITTEST</c> 에 <c>HTMAXBUTTON</c> 을 돌려주면 시스템이 스냅 플라이아웃을 띄운다.
///       그 영역의 마우스 메시지는 비클라이언트로 바뀌어 WPF 버튼이 받지 못하므로 호버 배경과 클릭을 여기서 직접 처리한다.</item>
/// <item>최대화 시 프레임이 화면 밖으로 밀리는 만큼을 DPI 에 맞게 계산해 <see cref="ContentMargin"/> 으로 노출한다(템플릿이 바인딩).</item>
/// </list>
/// </summary>
public class WindowView : Window
{
    private const int BorderColorRef = 0x00363633; // BorderColor #333336 — COLORREF 는 0x00BBGGRR

    private static readonly DependencyPropertyKey MinimizeCommandPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MinimizeCommand), typeof(ICommand), typeof(WindowView), new FrameworkPropertyMetadata(null));

    private static readonly DependencyPropertyKey MaximizeCommandPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MaximizeCommand), typeof(ICommand), typeof(WindowView), new FrameworkPropertyMetadata(null));

    private static readonly DependencyPropertyKey CloseCommandPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CloseCommand), typeof(ICommand), typeof(WindowView), new FrameworkPropertyMetadata(null));

    private static readonly DependencyPropertyKey IsMaximizedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMaximized), typeof(bool), typeof(WindowView), new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey ContentMarginPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ContentMargin), typeof(Thickness), typeof(WindowView), new FrameworkPropertyMetadata(default(Thickness)));

    public static readonly DependencyProperty MinimizeCommandProperty = MinimizeCommandPropertyKey.DependencyProperty;
    public static readonly DependencyProperty MaximizeCommandProperty = MaximizeCommandPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CloseCommandProperty = CloseCommandPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMaximizedProperty = IsMaximizedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty ContentMarginProperty = ContentMarginPropertyKey.DependencyProperty;

    public static readonly DependencyProperty TitleBarContentProperty = DependencyProperty.Register(
        nameof(TitleBarContent), typeof(object), typeof(WindowView), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ShowTitleTextProperty = DependencyProperty.Register(
        nameof(ShowTitleText), typeof(bool), typeof(WindowView), new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty ShowMinimizeButtonProperty = DependencyProperty.Register(
        nameof(ShowMinimizeButton), typeof(bool), typeof(WindowView), new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty ShowMaximizeButtonProperty = DependencyProperty.Register(
        nameof(ShowMaximizeButton), typeof(bool), typeof(WindowView), new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty ShowCloseButtonProperty = DependencyProperty.Register(
        nameof(ShowCloseButton), typeof(bool), typeof(WindowView), new FrameworkPropertyMetadata(true));

    private bool _closeConfirmed;
    private WindowViewModel? _boundViewModel;
    private Button? _maximizeButton;
    private Button? _snapHovered;

    public WindowView()
    {
        SetValue(MinimizeCommandPropertyKey, new RelayCommand(() => WindowState = WindowState.Minimized));
        SetValue(MaximizeCommandPropertyKey, new RelayCommand(ToggleMaximize));
        SetValue(CloseCommandPropertyKey, new RelayCommand(OnCloseFromChrome));

        StateChanged += (_, _) =>
        {
            SetValue(IsMaximizedPropertyKey, WindowState == WindowState.Maximized);
            UpdateContentMargin();
        };
        DataContextChanged += OnDataContextChanged;
    }

    public ICommand MinimizeCommand => (ICommand)GetValue(MinimizeCommandProperty);

    public ICommand MaximizeCommand => (ICommand)GetValue(MaximizeCommandProperty);

    public ICommand CloseCommand => (ICommand)GetValue(CloseCommandProperty);

    /// <summary>현재 창이 최대화 상태인지 여부.</summary>
    public bool IsMaximized => (bool)GetValue(IsMaximizedProperty);

    /// <summary>최대화 상태에서 콘텐츠를 안쪽으로 밀 여백(프레임이 화면 밖으로 나가는 만큼). 보통 때는 0.</summary>
    public Thickness ContentMargin => (Thickness)GetValue(ContentMarginProperty);

    /// <summary>타이틀바 가운데 영역에 임베드할 커스텀 콘텐츠.</summary>
    public object? TitleBarContent
    {
        get => GetValue(TitleBarContentProperty);
        set => SetValue(TitleBarContentProperty, value);
    }

    /// <summary>타이틀바에 창 제목 텍스트를 표시할지 여부. (브랜드 아이콘만으로 충분한 창은 false)</summary>
    public bool ShowTitleText
    {
        get => (bool)GetValue(ShowTitleTextProperty);
        set => SetValue(ShowTitleTextProperty, value);
    }

    public bool ShowMinimizeButton
    {
        get => (bool)GetValue(ShowMinimizeButtonProperty);
        set => SetValue(ShowMinimizeButtonProperty, value);
    }

    public bool ShowMaximizeButton
    {
        get => (bool)GetValue(ShowMaximizeButtonProperty);
        set => SetValue(ShowMaximizeButtonProperty, value);
    }

    public bool ShowCloseButton
    {
        get => (bool)GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    /// <summary>
    /// 뷰모델의 닫기 요청 프로토콜(RequestClose/CloseRequested)을 사용할지 여부.
    /// 여러 창이 하나의 WindowViewModel 을 공유하는 경우(메인/빠른채팅) 서로를 닫아버리므로, 자체 닫기 정책을 가진 창은 false 로 재정의한다.
    /// </summary>
    protected virtual bool UsesViewModelCloseProtocol => true;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _maximizeButton = GetTemplateChild("MaximizeButton") as Button;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDwmAttributes(new WindowInteropHelper(this).Handle);
        if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(WndProc);
        UpdateContentMargin();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateContentMargin();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // 뷰모델과 결합되어 있고 아직 닫기가 확정되지 않았다면, 실제 닫기를 취소하고 뷰모델의 닫기 요청 프로토콜을 태운다.
        if (UsesViewModelCloseProtocol && _boundViewModel is not null && !_closeConfirmed && !e.Cancel)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(() => _boundViewModel.RequestClose());
        }
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseFromChrome()
    {
        if (UsesViewModelCloseProtocol && _boundViewModel is not null) _boundViewModel.RequestClose();
        else Close();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_boundViewModel is not null) _boundViewModel.CloseRequested -= OnViewModelCloseRequested;
        _boundViewModel = e.NewValue as WindowViewModel;
        // 프로토콜을 쓰지 않는 창은 공유 뷰모델의 CloseRequested 에 반응하지 않는다.
        if (_boundViewModel is not null && UsesViewModelCloseProtocol) _boundViewModel.CloseRequested += OnViewModelCloseRequested;
    }

    private void OnViewModelCloseRequested(object? sender, EventArgs e)
    {
        _closeConfirmed = true;
        Close();
    }

    /// <summary>DWM 힌트: 다크 프레임, 둥근 모서리(Win11), 테마색 1px 테두리. 지원하지 않는 OS 에선 조용히 실패한다.</summary>
    private static void ApplyDwmAttributes(nint hwnd)
    {
        if (hwnd == 0) return;
        try
        {
            int on = 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
            int round = NativeMethods.DwmwcpRound;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref round, sizeof(int));
            int color = BorderColorRef;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaBorderColor, ref color, sizeof(int));
        }
        catch { /* DWM 미지원 환경은 무시. */ }
    }

    /// <summary>최대화 시 OS 가 창을 프레임 두께만큼 화면 밖으로 밀어내므로 그만큼 콘텐츠 여백을 준다(DPI 반영).</summary>
    private void UpdateContentMargin()
    {
        if (WindowState != WindowState.Maximized)
        {
            SetValue(ContentMarginPropertyKey, default(Thickness));
            return;
        }

        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            uint dpiValue = (uint)Math.Round(96 * dpi.DpiScaleX);
            double x = (NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxFrame, dpiValue) + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, dpiValue)) / dpi.DpiScaleX;
            double y = (NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCyFrame, dpiValue) + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, dpiValue)) / dpi.DpiScaleY;
            SetValue(ContentMarginPropertyKey, new Thickness(x, y, x, y));
        }
        catch
        {
            SetValue(ContentMarginPropertyKey, new Thickness(8));
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WmNcHitTest:
                if (ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip && ShowMaximizeButton && HitTestMaximizeButton(lParam) is { } button)
                {
                    SetSnapHover(button);
                    handled = true;
                    return NativeMethods.HtMaxButton;
                }
                SetSnapHover(null);
                break;

            case NativeMethods.WmNcMouseLeave:
                SetSnapHover(null);
                break;

            case NativeMethods.WmNcLButtonDown when wParam == NativeMethods.HtMaxButton:
                handled = true; // 기본 처리를 막고 클릭은 WM_NCLBUTTONUP 에서 완성한다
                break;

            case NativeMethods.WmNcLButtonUp when wParam == NativeMethods.HtMaxButton:
                handled = true;
                ToggleMaximize();
                break;
        }
        return 0;
    }

    /// <summary>화면 좌표(lParam)가 최대화 버튼 위인지.</summary>
    private Button? HitTestMaximizeButton(nint lParam)
    {
        if (_maximizeButton is not { IsVisible: true, IsEnabled: true } button) return null;

        long packed = lParam.ToInt64();
        Point local = button.PointFromScreen(new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF)));
        bool inside = local.X >= 0 && local.Y >= 0 && local.X < button.ActualWidth && local.Y < button.ActualHeight;
        return inside ? button : null;
    }

    /// <summary>비클라이언트로 처리되는 동안 WPF 호버 트리거가 안 돌므로 배경을 직접 칠한다.</summary>
    private void SetSnapHover(Button? button)
    {
        if (ReferenceEquals(_snapHovered, button)) return;
        _snapHovered?.ClearValue(BackgroundProperty);
        _snapHovered = button;
        if (button is not null && TryFindResource("OverlayHoverBrush") is Brush hover) button.Background = hover;
    }
}
