using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using NvChat.Models;

namespace NvChat.Behaviors;

/// <summary>
/// 창 위치/크기 기억: <see cref="PlacementProperty"/> 에 바인딩한 모델을 처음 뜰 때(SourceInitialized) 적용하고,
/// 닫힐 때(Closing — 트레이로 숨기며 취소돼도 발생) 모델에 써 넣은 뒤 <see cref="SavePlacementCommandProperty"/> 를 실행한다.
/// 저장된 위치가 화면 밖이면(모니터 구성 변경) 무시한다.
/// </summary>
public static class WindowBehaviors
{
    public static readonly DependencyProperty PlacementProperty = DependencyProperty.RegisterAttached(
        "Placement", typeof(WindowPlacement), typeof(WindowBehaviors), new PropertyMetadata(null, OnPlacementChanged));

    public static WindowPlacement? GetPlacement(DependencyObject o) => (WindowPlacement?)o.GetValue(PlacementProperty);

    public static void SetPlacement(DependencyObject o, WindowPlacement? value) => o.SetValue(PlacementProperty, value);

    public static readonly DependencyProperty SavePlacementCommandProperty = DependencyProperty.RegisterAttached(
        "SavePlacementCommand", typeof(ICommand), typeof(WindowBehaviors), new PropertyMetadata(null));

    public static ICommand? GetSavePlacementCommand(DependencyObject o) => (ICommand?)o.GetValue(SavePlacementCommandProperty);

    public static void SetSavePlacementCommand(DependencyObject o, ICommand? value) => o.SetValue(SavePlacementCommandProperty, value);

    /// <summary>창 폭이 이 값보다 좁아지면 <see cref="IsNarrowProperty"/> 를 true 로 만든다(좁은 창에서 사이드바를 자동으로 접는 데 쓴다).</summary>
    public static readonly DependencyProperty NarrowBelowProperty = DependencyProperty.RegisterAttached(
        "NarrowBelow", typeof(double), typeof(WindowBehaviors), new PropertyMetadata(0.0, OnNarrowBelowChanged));

    public static double GetNarrowBelow(DependencyObject o) => (double)o.GetValue(NarrowBelowProperty);

    public static void SetNarrowBelow(DependencyObject o, double value) => o.SetValue(NarrowBelowProperty, value);

    public static readonly DependencyProperty IsNarrowProperty = DependencyProperty.RegisterAttached(
        "IsNarrow", typeof(bool), typeof(WindowBehaviors), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static bool GetIsNarrow(DependencyObject o) => (bool)o.GetValue(IsNarrowProperty);

    public static void SetIsNarrow(DependencyObject o, bool value) => o.SetValue(IsNarrowProperty, value);

    private static void OnNarrowBelowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window w) return;
        w.SizeChanged -= OnWindowSizeChanged;
        if ((double)e.NewValue > 0) w.SizeChanged += OnWindowSizeChanged;
    }

    private static void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var w = (Window)sender;
        bool narrow = e.NewSize.Width < GetNarrowBelow(w);
        if (GetIsNarrow(w) != narrow) w.SetCurrentValue(IsNarrowProperty, narrow);
    }

    /// <summary>작업 표시줄 버튼을 깜빡여 주의를 끈다(창이 앞으로 오면 멈춘다). 뒤에 가려진 창에서 응답이 끝났을 때 쓴다.</summary>
    public static void Flash(Window window)
    {
        try
        {
            nint handle = new WindowInteropHelper(window).Handle;
            if (handle == 0) return;

            FlashInfo info = new()
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(),
                Handle = handle,
                Flags = 0x00000002 | 0x0000000C, // FLASHW_TRAY | FLASHW_TIMERNOFG
                Count = 3,
            };
            FlashWindowEx(ref info);
        }
        catch { /* 알림 실패는 무시 */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Handle;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    private static void OnPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window w) return;
        w.SourceInitialized -= OnSourceInitialized;
        w.Closing -= OnClosing;
        if (e.NewValue is WindowPlacement)
        {
            w.SourceInitialized += OnSourceInitialized;
            w.Closing += OnClosing;
        }
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window w || GetPlacement(w) is not { Width: > 0, Height: > 0 } p) return;
        if (p.Width < w.MinWidth || p.Height < w.MinHeight) return;

        // 가상 화면(모든 모니터)과 타이틀바를 잡을 만큼은 겹쳐야 복원한다.
        Rect saved = new(p.Left, p.Top, p.Width, p.Height);
        Rect screen = new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        Rect visible = Rect.Intersect(saved, screen);
        if (visible.IsEmpty || visible.Width < 100 || visible.Height < 60) return;

        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = p.Left;
        w.Top = p.Top;
        w.Width = p.Width;
        w.Height = p.Height;
        if (p.IsMaximized) w.WindowState = WindowState.Maximized;
    }

    private static void OnClosing(object? sender, EventArgs e)
    {
        if (sender is not Window w || GetPlacement(w) is not { } p || w.WindowState == WindowState.Minimized) return;

        Rect bounds = w.WindowState == WindowState.Maximized ? w.RestoreBounds : new Rect(w.Left, w.Top, w.Width, w.Height);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;

        p.Left = bounds.Left;
        p.Top = bounds.Top;
        p.Width = bounds.Width;
        p.Height = bounds.Height;
        p.IsMaximized = w.WindowState == WindowState.Maximized;

        if (GetSavePlacementCommand(w) is { } cmd && cmd.CanExecute(null)) cmd.Execute(null);
    }
}
