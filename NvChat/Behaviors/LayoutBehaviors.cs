using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NvChat.Behaviors;

/// <summary>
/// 레이아웃 관련 첨부 동작.
/// - <see cref="CollapsedProperty"/> + <see cref="ExpandedWidthProperty"/> (FrameworkElement): 접힘 여부에 따라 폭을 0 ↔ ExpandedWidth 로 부드럽게 바꾼다(사이드바).
///   폭을 끌어서 바꿀 때(ExpandedWidth 변경)는 애니메이션 없이 곧바로 따라간다.
/// - <see cref="ResizeWidthProperty"/> (Thumb): 끌면 바인딩된 폭을 바꾼다(사이드바 가장자리 손잡이).
/// - <see cref="FadeInOnceProperty"/> (FrameworkElement): true 로 바인딩돼 있으면 처음 나타날 때 한 번 떠오르듯 나타나고 값을 false 로 되돌린다(새 메시지).
/// </summary>
public static class LayoutBehaviors
{
    private static readonly Duration CollapseDuration = TimeSpan.FromMilliseconds(180);
    private static readonly Duration FadeDuration = TimeSpan.FromMilliseconds(200);

    // ===== 접히는 폭 =====

    public static readonly DependencyProperty CollapsedProperty = DependencyProperty.RegisterAttached(
        "Collapsed", typeof(bool), typeof(LayoutBehaviors), new PropertyMetadata(false, OnCollapsedChanged));

    public static bool GetCollapsed(DependencyObject o) => (bool)o.GetValue(CollapsedProperty);

    public static void SetCollapsed(DependencyObject o, bool value) => o.SetValue(CollapsedProperty, value);

    public static readonly DependencyProperty ExpandedWidthProperty = DependencyProperty.RegisterAttached(
        "ExpandedWidth", typeof(double), typeof(LayoutBehaviors), new PropertyMetadata(double.NaN, OnExpandedWidthChanged));

    public static double GetExpandedWidth(DependencyObject o) => (double)o.GetValue(ExpandedWidthProperty);

    public static void SetExpandedWidth(DependencyObject o, double value) => o.SetValue(ExpandedWidthProperty, value);

    private static void OnExpandedWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || GetCollapsed(element)) return;

        element.BeginAnimation(FrameworkElement.WidthProperty, null);
        element.Width = (double)e.NewValue;
    }

    private static void OnCollapsedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        double expanded = GetExpandedWidth(element);
        if (double.IsNaN(expanded)) expanded = element.ActualWidth;
        double target = (bool)e.NewValue ? 0 : expanded;

        // 아직 화면에 올라오기 전(시작 시 저장된 상태 복원)이면 움직임 없이 곧바로 맞춘다.
        if (!element.IsLoaded || !SystemParameters.ClientAreaAnimation)
        {
            element.BeginAnimation(FrameworkElement.WidthProperty, null);
            element.Width = target;
            return;
        }

        double from = double.IsNaN(element.Width) ? element.ActualWidth : element.Width;
        DoubleAnimation animation = new(from, target, CollapseDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) =>
        {
            // 애니메이션이 값을 붙들고 있으면 이후의 폭 변경(끌어서 조절)이 먹지 않으므로, 끝나면 실제 값으로 옮기고 푼다.
            if (GetCollapsed(element) != (bool)e.NewValue) return;
            element.Width = target;
            element.BeginAnimation(FrameworkElement.WidthProperty, null);
        };
        element.BeginAnimation(FrameworkElement.WidthProperty, animation);
    }

    // ===== 끌어서 폭 조절 =====

    public static readonly DependencyProperty ResizeWidthProperty = DependencyProperty.RegisterAttached(
        "ResizeWidth", typeof(double), typeof(LayoutBehaviors), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnResizeWidthChanged));

    public static double GetResizeWidth(DependencyObject o) => (double)o.GetValue(ResizeWidthProperty);

    public static void SetResizeWidth(DependencyObject o, double value) => o.SetValue(ResizeWidthProperty, value);

    public static readonly DependencyProperty ResizeMinProperty = DependencyProperty.RegisterAttached(
        "ResizeMin", typeof(double), typeof(LayoutBehaviors), new PropertyMetadata(0.0));

    public static double GetResizeMin(DependencyObject o) => (double)o.GetValue(ResizeMinProperty);

    public static void SetResizeMin(DependencyObject o, double value) => o.SetValue(ResizeMinProperty, value);

    public static readonly DependencyProperty ResizeMaxProperty = DependencyProperty.RegisterAttached(
        "ResizeMax", typeof(double), typeof(LayoutBehaviors), new PropertyMetadata(double.PositiveInfinity));

    public static double GetResizeMax(DependencyObject o) => (double)o.GetValue(ResizeMaxProperty);

    public static void SetResizeMax(DependencyObject o, double value) => o.SetValue(ResizeMaxProperty, value);

    private static readonly DependencyProperty ResizeHookedProperty = DependencyProperty.RegisterAttached(
        "ResizeHooked", typeof(bool), typeof(LayoutBehaviors), new PropertyMetadata(false));

    private static void OnResizeWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Thumb thumb || (bool)thumb.GetValue(ResizeHookedProperty)) return;

        thumb.SetValue(ResizeHookedProperty, true);
        thumb.DragDelta += (_, args) =>
        {
            double current = GetResizeWidth(thumb);
            if (double.IsNaN(current)) return;
            double next = Math.Clamp(current + args.HorizontalChange, GetResizeMin(thumb), GetResizeMax(thumb));
            thumb.SetCurrentValue(ResizeWidthProperty, Math.Round(next));
        };
    }

    // ===== 한 번만 떠오르기 =====

    public static readonly DependencyProperty FadeInOnceProperty = DependencyProperty.RegisterAttached(
        "FadeInOnce", typeof(bool), typeof(LayoutBehaviors), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnFadeInOnceChanged));

    public static bool GetFadeInOnce(DependencyObject o) => (bool)o.GetValue(FadeInOnceProperty);

    public static void SetFadeInOnce(DependencyObject o, bool value) => o.SetValue(FadeInOnceProperty, value);

    private static void OnFadeInOnceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;

        // 값을 되돌려 두면(TwoWay 로 뷰모델까지) 가상화로 요소가 다시 만들어져도 되풀이되지 않는다.
        // 바인딩이 값을 내려 주는 도중에는 되쓰기가 무시되므로 한 박자 늦춰 쓴다.
        element.Dispatcher.BeginInvoke(() => element.SetCurrentValue(FadeInOnceProperty, false));
        if (!SystemParameters.ClientAreaAnimation) return;

        TranslateTransform shift = new(0, 8);
        element.RenderTransform = shift;
        CubicEase ease = new() { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, FadeDuration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, FadeDuration) { EasingFunction = ease });
    }
}
