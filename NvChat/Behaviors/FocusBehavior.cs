using System.Windows;
using System.Windows.Controls;

namespace NvChat.Behaviors
{
    /// <summary>
    /// 요소가 화면에 보이게 되는 순간 포커스를 준다(TextBox 면 전체 선택).
    /// 이름 변경 인라인 편집 등에 사용.
    /// </summary>
    public static class FocusBehavior
    {
        public static readonly DependencyProperty FocusWhenVisibleProperty = DependencyProperty.RegisterAttached
        (
            "FocusWhenVisible",
            typeof(bool),
            typeof(FocusBehavior),
            new PropertyMetadata(false, OnChanged)
        );

        public static bool GetFocusWhenVisible(DependencyObject obj) => (bool)obj.GetValue(FocusWhenVisibleProperty);

        public static void SetFocusWhenVisible(DependencyObject obj, bool value) => obj.SetValue(FocusWhenVisibleProperty, value);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            if ((bool)e.NewValue)
                element.IsVisibleChanged += Element_IsVisibleChanged;
            else
                element.IsVisibleChanged -= Element_IsVisibleChanged;
        }

        private static void Element_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.IsVisible == false)
                return;

            element.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                element.Focus();
                if (element is TextBox textBox)
                    textBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
