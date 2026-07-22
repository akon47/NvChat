using NvChat.Behaviors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace NvChat.Controls
{
    /// <summary>
    /// <see cref="Markdown"/> 문자열을 WPF 요소로 렌더링해 보여주는 컨트롤.
    /// 본문은 읽기 전용 <see cref="RichTextBox"/> 에 담아 드래그 선택·복사가 가능하게 한다.
    /// 화면에 보이지 않을 때(예: 스트리밍 중 숨김 상태)는 재빌드를 미뤄 성능을 아낀다.
    /// </summary>
    public class MarkdownPresenter : ContentControl
    {
        #region Constructors

        public MarkdownPresenter()
        {
            Focusable = false;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        #endregion


        #region Fields

        // 스트리밍 중에는 토큰마다 Markdown 이 바뀌므로, 매번 문서를 다시 만들면 비싸다.
        // 한 번 다시 그리는 비용은 글이 길수록 커지므로 길이에 따라 간격을 늘린다.
        private const int MinRebuildIntervalMs = 150;

        private bool _dirty = true;
        private RichTextBox _viewer;
        private DispatcherTimer _throttle;
        private long _lastRebuildTick;

        #endregion


        #region Dependency Properties

        public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register
        (
            nameof(Markdown),
            typeof(string),
            typeof(MarkdownPresenter),
            new PropertyMetadata(null, OnMarkdownChanged)
        );

        public string Markdown
        {
            get => (string)GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        #endregion


        #region Helpers

        private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var presenter = (MarkdownPresenter)d;
            presenter._dirty = true;

            if (presenter.IsVisible)
                presenter.ScheduleRebuild();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible && _dirty)
                ScheduleRebuild();
        }

        /// <summary>
        /// 마지막 갱신에서 충분히 지났으면 곧바로, 아니면 타이머로 미뤄서 한 번만 다시 그린다.
        /// </summary>
        private void ScheduleRebuild()
        {
            var interval = CurrentInterval();

            if (Environment.TickCount64 - _lastRebuildTick >= interval)
            {
                Rebuild();
                return;
            }

            if (_throttle == null)
            {
                _throttle = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(interval)
                };
                _throttle.Tick += (_, __) =>
                {
                    if (_dirty == false)
                    {
                        _throttle.Stop();
                        return;
                    }

                    Rebuild();

                    if (_dirty == false)
                        _throttle.Stop();
                };
            }

            _throttle.Interval = TimeSpan.FromMilliseconds(interval);

            if (_throttle.IsEnabled == false)
                _throttle.Start();
        }

        /// <summary>
        /// 현재 길이에 맞는 최소 갱신 간격. 짧은 글은 촘촘하게, 긴 글은 성기게 갱신해
        /// 답변이 길어져도 CPU 사용이 계속 늘지 않게 한다.
        /// </summary>
        private int CurrentInterval()
        {
            var length = Markdown?.Length ?? 0;

            if (length < 3000)
                return MinRebuildIntervalMs;

            if (length < 12000)
                return MinRebuildIntervalMs * 2;

            return MinRebuildIntervalMs * 4;
        }

        private void Rebuild()
        {
            var viewer = EnsureViewer();

            // 사용자가 드래그로 선택 중이면 다시 그리지 않는다(선택이 날아가므로).
            // _dirty 를 유지해 두면 선택을 푼 뒤 다음 주기에 반영된다.
            if (viewer.Selection != null && viewer.Selection.IsEmpty == false && viewer.IsKeyboardFocusWithin)
                return;

            viewer.Document = MarkdownRenderer.RenderDocument(Markdown);

            _dirty = false;
            _lastRebuildTick = Environment.TickCount64;
        }

        /// <summary>
        /// 본문을 담을 읽기 전용 RichTextBox 를 만든다.
        /// 스크롤을 끄면 내용 높이만큼 늘어나므로 말풍선 안에서 TextBlock 처럼 동작하면서도
        /// 텍스트 선택/복사(Ctrl+C, 우클릭 메뉴)가 그대로 지원된다.
        /// </summary>
        private RichTextBox EnsureViewer()
        {
            if (_viewer != null)
                return _viewer;

            _viewer = new RichTextBox
            {
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                IsDocumentEnabled = true,
                AcceptsReturn = false,
                AcceptsTab = false,
                AllowDrop = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(-2, 0, -2, 0),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                SelectionBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
                SelectionOpacity = 0.35
            };

            // 내부에 스크롤이 남지 않도록 했지만, 코드 블록 등 자식이 휠을 삼키는 경우를 대비해 한 번 더 보장한다.
            NestedScroll.EnableWheelBubbling(_viewer);

            Content = _viewer;
            return _viewer;
        }

        #endregion
    }
}
