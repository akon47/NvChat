using System.Windows;
using System.Windows.Controls;

namespace NvChat.Controls
{
    /// <summary>
    /// <see cref="Markdown"/> 문자열을 WPF 요소로 렌더링해 보여주는 컨트롤.
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
            Content = MarkdownRenderer.Render(Markdown);
            _dirty = false;
        }

        #endregion
    }
}
