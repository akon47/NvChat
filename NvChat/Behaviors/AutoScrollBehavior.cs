using System.Windows;
using System.Windows.Controls;

namespace NvChat.Behaviors
{
    /// <summary>
    /// ScrollViewer 에 붙여, 콘텐츠가 늘어날 때(스트리밍 등) 사용자가 이미 맨 아래를 보고 있으면
    /// 자동으로 맨 아래로 스크롤한다. 사용자가 위로 스크롤해 읽는 중이면 방해하지 않는다.
    /// </summary>
    public static class AutoScrollBehavior
    {
        #region IsEnabled

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached
        (
            "IsEnabled",
            typeof(bool),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged)
        );

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        #endregion


        #region StickToBottom (내부 상태: 현재 맨 아래에 붙어있는지)

        private static readonly DependencyProperty StickToBottomProperty = DependencyProperty.RegisterAttached
        (
            "StickToBottom",
            typeof(bool),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(true)
        );

        private static bool GetStickToBottom(DependencyObject obj) => (bool)obj.GetValue(StickToBottomProperty);

        private static void SetStickToBottom(DependencyObject obj, bool value) => obj.SetValue(StickToBottomProperty, value);

        #endregion


        #region Helpers

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer scrollViewer)
                return;

            if ((bool)e.NewValue)
                scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            else
                scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
        }

        private static void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
                return;

            // 콘텐츠 높이 변화가 없다면(=사용자가 직접 스크롤한 경우) 현재 위치가 맨 아래인지 갱신한다.
            if (e.ExtentHeightChange == 0)
            {
                var atBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 1.0;
                SetStickToBottom(scrollViewer, atBottom);
                return;
            }

            // 콘텐츠가 늘어났고, 직전까지 맨 아래에 붙어있었다면 따라 내려간다.
            if (GetStickToBottom(scrollViewer))
                scrollViewer.ScrollToVerticalOffset(scrollViewer.ExtentHeight);
        }

        #endregion
    }
}
