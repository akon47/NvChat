using System.Windows;
using System.Windows.Controls;

namespace NvChat.Behaviors;

/// <summary>
/// ScrollViewer 에 붙여, 콘텐츠가 늘어날 때(스트리밍 등) 사용자가 이미 맨 아래를 보고 있으면 자동으로 맨 아래로 스크롤한다.
/// 사용자가 위로 스크롤해 읽는 중이면 방해하지 않는다.
/// </summary>
public static class AutoScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(AutoScrollBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    // 내부 상태: 현재 맨 아래에 붙어있는지
    private static readonly DependencyProperty StickToBottomProperty = DependencyProperty.RegisterAttached(
        "StickToBottom", typeof(bool), typeof(AutoScrollBehavior), new PropertyMetadata(true));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer) return;
        scrollViewer.ScrollChanged -= OnScrollChanged;
        if (e.NewValue is true) scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        // 콘텐츠 높이 변화가 없다면(=사용자가 직접 스크롤한 경우) 현재 위치가 맨 아래인지 갱신한다.
        if (e.ExtentHeightChange == 0)
        {
            scrollViewer.SetValue(StickToBottomProperty, scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 1.0);
            return;
        }

        // 콘텐츠가 늘어났고, 직전까지 맨 아래에 붙어있었다면 따라 내려간다.
        if ((bool)scrollViewer.GetValue(StickToBottomProperty)) scrollViewer.ScrollToVerticalOffset(scrollViewer.ExtentHeight);
    }
}
