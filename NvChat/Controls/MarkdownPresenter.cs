using NvChat.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

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

        private bool _dirty = true;
        private RichTextBox _viewer;

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
                presenter.Rebuild();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible && _dirty)
                Rebuild();
        }

        private void Rebuild()
        {
            EnsureViewer().Document = MarkdownRenderer.RenderDocument(Markdown);
            _dirty = false;
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
