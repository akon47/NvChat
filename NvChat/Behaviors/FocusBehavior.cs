using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NvChat.Behaviors;

/// <summary>
/// 포커스 관련 첨부 동작.
/// - <see cref="FocusWhenVisibleProperty"/>: 요소가 화면에 보이게 되는 순간 포커스를 준다(인라인 이름 변경, 빠른 채팅 입력창 등).
/// - <see cref="FocusTokenProperty"/>: 바인딩된 정수가 바뀔 때마다 포커스를 준다. VM 이 값을 올리면(예: Ctrl+F) 뷰의 요소에 포커스가 간다.
/// - <see cref="SelectAllProperty"/>: 위 두 경우에 TextBox 면 전체 선택(기본은 캐럿을 끝으로).
/// </summary>
public static class FocusBehavior
{
    public static readonly DependencyProperty FocusWhenVisibleProperty = DependencyProperty.RegisterAttached(
        "FocusWhenVisible", typeof(bool), typeof(FocusBehavior), new PropertyMetadata(false, OnFocusWhenVisibleChanged));

    public static bool GetFocusWhenVisible(DependencyObject obj) => (bool)obj.GetValue(FocusWhenVisibleProperty);

    public static void SetFocusWhenVisible(DependencyObject obj, bool value) => obj.SetValue(FocusWhenVisibleProperty, value);

    public static readonly DependencyProperty FocusTokenProperty = DependencyProperty.RegisterAttached(
        "FocusToken", typeof(int), typeof(FocusBehavior), new PropertyMetadata(0, OnFocusTokenChanged));

    public static int GetFocusToken(DependencyObject obj) => (int)obj.GetValue(FocusTokenProperty);

    public static void SetFocusToken(DependencyObject obj, int value) => obj.SetValue(FocusTokenProperty, value);

    public static readonly DependencyProperty SelectAllProperty = DependencyProperty.RegisterAttached(
        "SelectAll", typeof(bool), typeof(FocusBehavior), new PropertyMetadata(false));

    public static bool GetSelectAll(DependencyObject obj) => (bool)obj.GetValue(SelectAllProperty);

    public static void SetSelectAll(DependencyObject obj, bool value) => obj.SetValue(SelectAllProperty, value);

    private static void OnFocusWhenVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.IsVisibleChanged -= OnIsVisibleChanged;
        if (e.NewValue is true) element.IsVisibleChanged += OnIsVisibleChanged;
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement { IsVisible: true } element) FocusLater(element);
    }

    private static void OnFocusTokenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && (int)e.NewValue != 0) FocusLater(element);
    }

    private static void FocusLater(FrameworkElement element) =>
        element.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            element.Focus();
            if (element is not TextBox textBox) return;
            if (GetSelectAll(textBox)) textBox.SelectAll();
            else textBox.CaretIndex = textBox.Text.Length;
        });
}
