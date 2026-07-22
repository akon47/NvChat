using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NvChat.Behaviors
{
    /// <summary>
    /// 중첩 스크롤 처리기. 코드 블록처럼 자체 스크롤을 가진 요소 위에 마우스가 있으면
    /// WPF 는 휠 이벤트를 그 요소가 삼켜버려 바깥 대화 목록이 멈춘다.
    /// 내부에서 더 스크롤할 여지가 없을 때만 부모로 휠 이벤트를 넘겨 바깥이 이어서 스크롤되게 한다.
    /// </summary>
    internal static class NestedScroll
    {
        #region Helpers

        /// <summary>
        /// 대상 요소의 휠 이벤트가 한계에 도달하면 부모로 전달되도록 한다.
        /// </summary>
        public static void EnableWheelBubbling(FrameworkElement element)
        {
            if (element == null)
                return;

            element.PreviewMouseWheel -= OnPreviewMouseWheel;
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || e.Delta == 0)
                return;

            var element = (FrameworkElement)sender;
            var viewer = FindScrollViewer(element);

            if (viewer != null && viewer.ScrollableHeight > 0.5)
            {
                var atTop = viewer.VerticalOffset <= 0.5;
                var atBottom = viewer.VerticalOffset >= viewer.ScrollableHeight - 0.5;

                // 내부에서 아직 진행할 방향이 남아 있으면 내부가 처리하도록 둔다.
                if ((e.Delta > 0 && atTop == false) || (e.Delta < 0 && atBottom == false))
                    return;
            }

            e.Handled = true;

            var bubbled = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = element
            };

            if (VisualTreeHelper.GetParent(element) is UIElement parent)
                parent.RaiseEvent(bubbled);
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer self)
                return self;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                    return found;
            }

            return null;
        }

        #endregion
    }
}
