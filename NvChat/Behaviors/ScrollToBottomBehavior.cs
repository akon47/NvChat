using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NvChat.Behaviors;

/// <summary>
/// "맨 아래로" 버튼: <see cref="TargetProperty"/> 에 바인딩한 ScrollViewer 가 맨 아래가 아닐 때만 버튼을 보이고, 누르면 끝으로 스크롤한다.
/// </summary>
public static class ScrollToBottomBehavior
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(ScrollViewer), typeof(ScrollToBottomBehavior), new PropertyMetadata(null, OnTargetChanged));

    public static ScrollViewer? GetTarget(DependencyObject o) => (ScrollViewer?)o.GetValue(TargetProperty);

    public static void SetTarget(DependencyObject o, ScrollViewer? value) => o.SetValue(TargetProperty, value);

    // 버튼별로 등록한 ScrollChanged 핸들러(대상이 바뀌면 이전 대상에서 떼기 위해 보관).
    private static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
        "Handler", typeof(ScrollChangedEventHandler), typeof(ScrollToBottomBehavior), new PropertyMetadata(null));

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;

        if (e.OldValue is ScrollViewer old && button.GetValue(HandlerProperty) is ScrollChangedEventHandler oldHandler) old.ScrollChanged -= oldHandler;
        button.Click -= OnClick;

        if (e.NewValue is ScrollViewer viewer)
        {
            ScrollChangedEventHandler handler = (_, _) => Update(button, viewer);
            button.SetValue(HandlerProperty, handler);
            viewer.ScrollChanged += handler;
            button.Click += OnClick;
            Update(button, viewer);
        }
    }

    private static void OnClick(object sender, RoutedEventArgs e) => GetTarget((DependencyObject)sender)?.ScrollToEnd();

    private static void Update(UIElement button, ScrollViewer viewer)
    {
        bool atBottom = viewer.VerticalOffset >= viewer.ScrollableHeight - 2.0;
        button.Visibility = atBottom || viewer.ScrollableHeight <= 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
