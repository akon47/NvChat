using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NvChat.Behaviors;

/// <summary>
/// 메시지 목록(ItemsControl)의 스크롤 동작을 한곳에서 맡는다. 목록은 가상화되어 있어(화면 근처의 메시지만 실제 요소로 만든다)
/// 스크롤 범위가 스크롤하는 동안에도 계속 다시 추정된다. 그래서 "사용자가 위로 올렸는가"를 범위 변화가 아니라 입력으로 판단한다.
///
/// <list type="bullet">
/// <item><b>맨 아래 붙기</b>: 맨 아래를 보고 있으면 내용이 늘어날 때(스트리밍) 따라 내려간다. 휠·키·스크롤 막대로 위로 올리면 놓아 주고, 다시 맨 아래에 닿으면 붙는다.</item>
/// <item><b>부드러운 휠</b>: 휠 한 칸을 뚝뚝 끊어 옮기지 않고 목표 위치로 감속하며 따라간다. 코드 블록처럼 안쪽에 스크롤이 남아 있으면 그쪽에 양보한다.</item>
/// <item><b>대화 전환</b>: 다른 대화가 걸리면 맨 아래에서 시작한다.</item>
/// <item><see cref="BringIntoViewProperty"/>: 바인딩한 항목(찾기의 현재 일치)을 화면 안으로 가져온다.</item>
/// <item><see cref="IsAtBottomProperty"/>: "맨 아래로" 버튼의 표시 여부에 쓴다.</item>
/// </list>
/// </summary>
public static class ChatScroll
{
    /// <summary>휠 한 칸(Delta 120)에 움직일 거리(DIP).</summary>
    private const double WheelStep = 110;

    /// <summary>목표까지 남은 거리가 1/e 로 줄어드는 시간(초). 작을수록 빠릿하다.</summary>
    private const double SmoothingSeconds = 0.075;

    private const double BottomTolerance = 2.0;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ChatScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);

    public static readonly DependencyProperty IsAtBottomProperty = DependencyProperty.RegisterAttached(
        "IsAtBottom", typeof(bool), typeof(ChatScroll), new PropertyMetadata(true));

    public static bool GetIsAtBottom(DependencyObject o) => (bool)o.GetValue(IsAtBottomProperty);

    public static void SetIsAtBottom(DependencyObject o, bool value) => o.SetValue(IsAtBottomProperty, value);

    public static readonly DependencyProperty BringIntoViewProperty = DependencyProperty.RegisterAttached(
        "BringIntoView", typeof(object), typeof(ChatScroll), new PropertyMetadata(null, OnBringIntoViewChanged));

    public static object? GetBringIntoView(DependencyObject o) => o.GetValue(BringIntoViewProperty);

    public static void SetBringIntoView(DependencyObject o, object? value) => o.SetValue(BringIntoViewProperty, value);

    /// <summary>"맨 아래로" 버튼에 건다: 값으로 준 목록을 맨 아래로 스크롤한다.</summary>
    public static readonly DependencyProperty ScrollToBottomOfProperty = DependencyProperty.RegisterAttached(
        "ScrollToBottomOf", typeof(ItemsControl), typeof(ChatScroll), new PropertyMetadata(null, OnScrollToBottomOfChanged));

    public static ItemsControl? GetScrollToBottomOf(DependencyObject o) => (ItemsControl?)o.GetValue(ScrollToBottomOfProperty);

    public static void SetScrollToBottomOf(DependencyObject o, ItemsControl? value) => o.SetValue(ScrollToBottomOfProperty, value);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(ChatScroll), new PropertyMetadata(null));

    private sealed class State(ItemsControl owner)
    {
        public ItemsControl Owner { get; } = owner;
        public ScrollViewer? Viewer { get; set; }
        public bool Stick { get; set; } = true;
        public bool Animating { get; set; }
        public double Target { get; set; }
        public TimeSpan LastFrame { get; set; }
        public EventHandler? Frame { get; set; }
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list || e.NewValue is not true) return;

        State state = new(list);
        list.SetValue(StateProperty, state);

        if (list.IsLoaded) Attach(state);
        else list.Loaded += (_, _) => Attach(state);

        // 다른 대화가 걸리면(ItemsSource 교체) 맨 아래에서 시작한다.
        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ItemsControl))
            ?.AddValueChanged(list, (_, _) => ResetToBottom(state));
    }

    private static void Attach(State state)
    {
        if (state.Viewer is not null) return;
        if (FindDescendant<ScrollViewer>(state.Owner) is not { } viewer) return;

        state.Viewer = viewer;
        viewer.ScrollChanged += (_, e) => OnScrollChanged(state, e);
        viewer.PreviewMouseWheel += (_, e) => OnPreviewMouseWheel(state, e);
        viewer.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.PageUp or Key.Up or Key.Home) Release(state);
        };
        viewer.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => Release(state)));

        ResetToBottom(state);
    }

    private static void ResetToBottom(State state)
    {
        StopAnimation(state);
        state.Stick = true;
        state.Owner.SetCurrentValue(IsAtBottomProperty, true);

        // 새 항목들이 배치된 뒤에 끝으로 간다.
        state.Owner.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => state.Viewer?.ScrollToEnd());
    }

    /// <summary>사용자가 위로 올리려 한다: 맨 아래 붙기를 놓는다.</summary>
    private static void Release(State state) => state.Stick = false;

    private static void OnScrollChanged(State state, ScrollChangedEventArgs e)
    {
        if (state.Viewer is not { } viewer) return;

        bool atBottom = viewer.VerticalOffset >= viewer.ScrollableHeight - BottomTolerance;
        state.Owner.SetCurrentValue(IsAtBottomProperty, atBottom || viewer.ScrollableHeight <= 0);

        if (atBottom)
        {
            // 부드러운 스크롤이 진행 중이면 그 목표가 맨 아래일 때만 다시 붙는다(위로 가는 도중에 붙들지 않도록).
            if (!state.Animating || state.Target >= viewer.ScrollableHeight - BottomTolerance) state.Stick = true;
            return;
        }

        bool sizeChanged = e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0;
        if (state.Stick && sizeChanged)
        {
            // 붙어 있는 동안 내용이 늘었다(스트리밍·가상화 재추정·창 크기 변경): 따라 내려간다.
            StopAnimation(state);
            viewer.ScrollToEnd();
        }
        else if (state.Stick && !state.Animating && e.VerticalChange < 0)
        {
            // 입력으로 잡히지 않은 위쪽 이동(코드에서 스크롤한 경우 등)도 놓아 준다.
            state.Stick = false;
        }
    }

    private static void OnPreviewMouseWheel(State state, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || state.Viewer is not { } viewer) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return; // Ctrl+휠은 확대/축소용으로 남겨 둔다

        // 코드 블록처럼 안쪽에 스크롤이 남아 있으면 그쪽이 먼저 쓴다.
        if (InnerCanScroll(e.OriginalSource as DependencyObject, viewer, e.Delta)) return;

        e.Handled = true;
        if (viewer.ScrollableHeight <= 0) return;
        if (e.Delta > 0) Release(state);

        double from = state.Animating ? state.Target : viewer.VerticalOffset;
        state.Target = Math.Clamp(from - e.Delta / 120.0 * WheelStep, 0, viewer.ScrollableHeight);
        StartAnimation(state);
    }

    private static void StartAnimation(State state)
    {
        if (state.Animating) return;

        state.Animating = true;
        state.LastFrame = TimeSpan.Zero;
        state.Frame = (_, args) => OnFrame(state, ((RenderingEventArgs)args).RenderingTime);
        CompositionTarget.Rendering += state.Frame;
    }

    private static void StopAnimation(State state)
    {
        if (!state.Animating) return;

        state.Animating = false;
        if (state.Frame is not null) CompositionTarget.Rendering -= state.Frame;
        state.Frame = null;
    }

    private static void OnFrame(State state, TimeSpan time)
    {
        if (state.Viewer is not { } viewer)
        {
            StopAnimation(state);
            return;
        }

        // 같은 프레임에 Rendering 이 여러 번 올 수 있다. 시간이 흐르지 않았으면 건너뛴다.
        double dt = state.LastFrame == TimeSpan.Zero ? 1 / 60.0 : (time - state.LastFrame).TotalSeconds;
        if (dt <= 0) return;
        state.LastFrame = time;

        double target = Math.Clamp(state.Target, 0, viewer.ScrollableHeight);
        double current = viewer.VerticalOffset;
        double remaining = target - current;

        if (Math.Abs(remaining) < 0.5)
        {
            viewer.ScrollToVerticalOffset(target);
            StopAnimation(state);
            return;
        }

        // 프레임 간격에 무관한 지수 감속.
        viewer.ScrollToVerticalOffset(current + remaining * (1 - Math.Exp(-Math.Min(dt, 0.1) / SmoothingSeconds)));
    }

    /// <summary>마우스 아래에서 바깥 목록까지 올라가며, 그 방향으로 더 스크롤할 수 있는 안쪽 ScrollViewer 가 있는지 본다.</summary>
    private static bool InnerCanScroll(DependencyObject? source, ScrollViewer outer, int delta)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, outer); node = ParentOf(node))
        {
            if (node is not ScrollViewer { ScrollableHeight: > 0.5 } inner) continue;

            bool atTop = inner.VerticalOffset <= 0.5;
            bool atBottom = inner.VerticalOffset >= inner.ScrollableHeight - 0.5;
            if ((delta > 0 && !atTop) || (delta < 0 && !atBottom)) return true;
        }
        return false;
    }

    /// <summary>시각 트리의 부모. FlowDocument 안의 Run/Paragraph 처럼 시각 요소가 아닌 것은 논리 트리로 올라간다.</summary>
    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    private static void OnBringIntoViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list || e.NewValue is null || list.GetValue(StateProperty) is not State state) return;

        int index = list.Items.IndexOf(e.NewValue);
        if (index < 0) return;

        StopAnimation(state);
        Release(state);

        // 가상화된 목록에서는 화면 밖 항목의 요소가 아직 없다. 먼저 패널에게 그 인덱스를 만들게 한 뒤 요소를 화면 안으로 가져온다.
        if (FindDescendant<VirtualizingStackPanel>(list) is { } panel) panel.BringIndexIntoViewPublic(index);

        list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container) container.BringIntoView();
        });
    }

    private static void OnScrollToBottomOfChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;
        button.Click -= OnScrollToBottomClick;
        if (e.NewValue is ItemsControl) button.Click += OnScrollToBottomClick;
    }

    private static void OnScrollToBottomClick(object sender, RoutedEventArgs e)
    {
        if (GetScrollToBottomOf((DependencyObject)sender)?.GetValue(StateProperty) is State state) ResetToBottom(state);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
