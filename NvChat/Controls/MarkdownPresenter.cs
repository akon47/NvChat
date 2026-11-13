using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using NvChat.Behaviors;

namespace NvChat.Controls;

/// <summary>
/// <see cref="Markdown"/> 문자열을 WPF 요소로 렌더링해 보여주는 컨트롤.
/// 본문은 읽기 전용 <see cref="RichTextBox"/> 에 담아 드래그 선택·복사가 가능하게 한다.
///
/// 스트리밍 중에는 본문이 계속 길어진다. 매번 문서 전체를 새로 만들면 답변이 길수록 비용이 커지고, 이미 완성된 코드 블록의
/// 선택·"복사됨" 표시까지 날아간다. 그래서 블록 단위로 이전 렌더와 비교해 앞쪽의 같은 블록은 그대로 두고 달라진 꼬리만 다시 만든다.
/// 화면에 보이지 않을 때는 재빌드를 미룬다.
/// </summary>
public sealed class MarkdownPresenter : ContentControl
{
    // 바뀐 꼬리 블록만 다시 만들므로 간격은 짧게 둔다. 그래도 아주 긴 글은 분할 비용이 있어 조금 늘린다.
    private const int MinRebuildIntervalMs = 80;

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownPresenter), new PropertyMetadata(null, OnMarkdownChanged));

    /// <summary>본문에서 배경색으로 강조할 문자열(대화 내 찾기). 비우면 강조를 지운다.</summary>
    public static readonly DependencyProperty HighlightTextProperty = DependencyProperty.Register(
        nameof(HighlightText), typeof(string), typeof(MarkdownPresenter), new PropertyMetadata(null, OnHighlightChanged));

    private readonly List<(string Key, List<Block> Blocks)> _rendered = [];
    private bool _dirty = true;
    private bool _resetOnRebuild;
    private RichTextBox? _viewer;
    private FlowDocument? _document;
    private DispatcherTimer? _throttle;
    private long _lastRebuildTick;
    private Block? _trimmedBlock;
    private Thickness _trimmedMargin;

    public MarkdownPresenter()
    {
        Focusable = false;
        IsVisibleChanged += (_, _) => { if (IsVisible && _dirty) ScheduleRebuild(); };
    }

    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string? HighlightText
    {
        get => (string?)GetValue(HighlightTextProperty);
        set => SetValue(HighlightTextProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var presenter = (MarkdownPresenter)d;
        presenter._dirty = true;
        if (presenter.IsVisible) presenter.ScheduleRebuild();
    }

    private static void OnHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 강조는 문서의 Run 을 쪼개 배경을 입히므로, 바꾸거나 지울 때는 깨끗한 문서에서 다시 시작한다.
        var presenter = (MarkdownPresenter)d;
        presenter._resetOnRebuild = true;
        presenter._dirty = true;
        if (presenter.IsVisible) presenter.ScheduleRebuild();
    }

    /// <summary>마지막 갱신에서 충분히 지났으면 곧바로, 아니면 타이머로 미뤄서 한 번만 다시 그린다.</summary>
    private void ScheduleRebuild()
    {
        int interval = CurrentInterval();
        if (Environment.TickCount64 - _lastRebuildTick >= interval)
        {
            Rebuild();
            if (!_dirty) return;
        }

        if (_throttle is null)
        {
            _throttle = new DispatcherTimer(DispatcherPriority.Background);
            _throttle.Tick += (_, _) =>
            {
                if (_dirty && IsVisible) Rebuild();
                if (!_dirty || !IsVisible) _throttle.Stop();
            };
        }

        _throttle.Interval = TimeSpan.FromMilliseconds(interval);
        if (!_throttle.IsEnabled) _throttle.Start();
    }

    private int CurrentInterval() => (Markdown?.Length ?? 0) switch
    {
        < 8000 => MinRebuildIntervalMs,
        < 30000 => MinRebuildIntervalMs * 2,
        _ => MinRebuildIntervalMs * 4,
    };

    private void Rebuild()
    {
        var viewer = EnsureViewer();
        var document = _document!;

        // 사용자가 드래그로 선택 중이면 다시 그리지 않는다(선택이 날아가므로). _dirty 를 유지해 두면 선택을 푼 뒤 다음 주기에 반영된다.
        if (viewer.Selection is { IsEmpty: false } && viewer.IsKeyboardFocusWithin) return;

        if (_resetOnRebuild)
        {
            _resetOnRebuild = false;
            document.Blocks.Clear();
            _rendered.Clear();
            _trimmedBlock = null;
        }

        var segments = MarkdownSegmenter.Split(Markdown);

        // 앞에서부터 이전 렌더와 같은 블록 수.
        int common = 0;
        while (common < _rendered.Count && common < segments.Count && _rendered[common].Key == segments[common].Key) common++;

        if (common < _rendered.Count || common < segments.Count)
        {
            // 마지막 블록의 아래 여백을 지워 두었다면, 그 블록이 더는 마지막이 아닐 수 있으므로 되돌린다.
            if (_trimmedBlock is not null)
            {
                _trimmedBlock.Margin = _trimmedMargin;
                _trimmedBlock = null;
            }

            for (int i = _rendered.Count - 1; i >= common; i--)
            {
                foreach (var block in _rendered[i].Blocks) document.Blocks.Remove(block);
                _rendered.RemoveAt(i);
            }

            for (int i = common; i < segments.Count; i++)
            {
                var blocks = MarkdownRenderer.Build(segments[i]);
                foreach (var block in blocks) document.Blocks.Add(block);
                _rendered.Add((segments[i].Key, blocks));
            }

            // 마지막 블록의 하단 마진 제거(말풍선 하단 여백 방지).
            if (document.Blocks.LastBlock is { } last)
            {
                _trimmedBlock = last;
                _trimmedMargin = last.Margin;
                last.Margin = new Thickness(_trimmedMargin.Left, _trimmedMargin.Top, _trimmedMargin.Right, 0);
            }

            if (!string.IsNullOrWhiteSpace(HighlightText)) ApplyHighlight(document, HighlightText!.Trim());
        }

        _dirty = false;
        _lastRebuildTick = Environment.TickCount64;
    }

    /// <summary>
    /// 본문을 담을 읽기 전용 RichTextBox 를 만든다. 스크롤을 끄면 내용 높이만큼 늘어나므로 말풍선 안에서 TextBlock 처럼 동작하면서도
    /// 텍스트 선택/복사(Ctrl+C, 우클릭 메뉴)가 그대로 지원된다.
    /// </summary>
    private RichTextBox EnsureViewer()
    {
        if (_viewer is not null) return _viewer;

        _document = MarkdownRenderer.CreateDocument();
        _viewer = new RichTextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            IsDocumentEnabled = true,
            AcceptsReturn = false,
            AcceptsTab = false,
            AllowDrop = false,
            IsTabStop = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Margin = new Thickness(-2, 0, -2, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            SelectionOpacity = 0.35,
            FocusVisualStyle = null,
            Document = _document,
        };
        _viewer.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "AccentBrush");

        // 내부에 스크롤이 남지 않도록 했지만, 코드 블록 등 자식이 휠을 삼키는 경우를 대비해 한 번 더 보장한다.
        NestedScroll.EnableWheelBubbling(_viewer);
        Content = _viewer;
        return _viewer;
    }

    /// <summary>문서의 텍스트에서 query 가 나오는 곳마다 배경색을 입힌다. (코드 블록 안쪽은 별도의 문서라 대상이 아니다)</summary>
    private void ApplyHighlight(FlowDocument document, string query)
    {
        if (TryFindResource("FindHighlightBrush") is not Brush brush) return;

        List<Run> runs = [];
        CollectRuns(document.Blocks, runs);

        foreach (Run run in runs)
        {
            string text = run.Text;
            if (text.Length < query.Length) continue;

            // 범위에 서식을 입히면 Run 이 쪼개지므로, 뒤쪽 일치부터 처리해 앞쪽 오프셋이 흔들리지 않게 한다.
            List<int> hits = [];
            for (int at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(query, at + query.Length, StringComparison.OrdinalIgnoreCase)) hits.Add(at);

            for (int i = hits.Count - 1; i >= 0; i--)
            {
                var start = run.ContentStart.GetPositionAtOffset(hits[i]);
                var end = run.ContentStart.GetPositionAtOffset(hits[i] + query.Length);
                if (start is not null && end is not null) new TextRange(start, end).ApplyPropertyValue(TextElement.BackgroundProperty, brush);
            }
        }
    }

    private static void CollectRuns(BlockCollection blocks, List<Run> runs)
    {
        foreach (Block block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    CollectRuns(paragraph.Inlines, runs);
                    break;
                case Section section:
                    CollectRuns(section.Blocks, runs);
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                    {
                        foreach (var row in group.Rows)
                        {
                            foreach (var cell in row.Cells) CollectRuns(cell.Blocks, runs);
                        }
                    }
                    break;
            }
        }
    }

    private static void CollectRuns(InlineCollection inlines, List<Run> runs)
    {
        foreach (Inline inline in inlines)
        {
            if (inline is Run run) runs.Add(run);
            else if (inline is Span span) CollectRuns(span.Inlines, runs);
        }
    }
}
