using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NvChat.Behaviors;
using NvChat.Localization;
using NvChat.Services;

namespace NvChat.Controls;

/// <summary>
/// 경량 마크다운 → WPF FlowDocument 렌더러(외부 라이브러리 없음).
/// 읽기 전용 RichTextBox 에 넣어 문단/목록/표/인용까지 드래그 선택·복사가 가능하다.
/// 코드펜스(구문강조·복사·줄바꿈 전환)/인라인코드/헤딩/중첩 목록·체크리스트/인용/표/구분선/굵게·기울임·취소선/링크·자동 링크/수식(유니코드 근사)을 지원한다.
///
/// 블록 나누기는 <see cref="MarkdownSegmenter"/> 가 하고 여기서는 블록 하나를 요소로 만든다(<see cref="Build"/>).
/// 색은 값이 아니라 리소스 참조로 걸어 두므로 테마를 바꾸면 이미 그려진 문서도 따라 바뀐다.
/// </summary>
public static partial class MarkdownRenderer
{
    private const int MaxInlineLength = 4000;
    private const int MaxHighlightLength = 20000;

    /// <summary>본문 글자 크기와 줄 높이(약 1.6배). 사용자 말풍선의 일반 텍스트도 같은 값을 쓴다.</summary>
    public const double BodyFontSize = 14.5;
    public const double BodyLineHeight = 23;

    private const double CodeFontSize = 13;
    private const double CodeLineHeight = 20;
    private const double ListIndent = 22;

    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");
    private static readonly FontFamily BodyFont = new("Segoe UI, Malgun Gothic");
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly FontFamily MathFont = new("Cambria Math, Segoe UI Symbol, Segoe UI");

    private static LocalizationManager L => LocalizationManager.Instance;

    [GeneratedRegex(
        @"(?<code>`+)(?<codebody>.+?)\k<code>"
        + @"|\\\((?<imath>.+?)\\\)"
        + @"|(?<![\\$\w])\$(?<imath2>[^\s$](?:[^$\n]*?[^\s$\\])?)\$(?![\w$])"
        + @"|!\[(?<ialt>[^\]]*)\]\((?<iurl>(?:[^()\s]|\([^)\s]*\))+)(?:\s+""[^""]*"")?\)"
        + @"|\[(?<ltext>[^\]]+)\]\((?<lurl>(?:[^()\s]|\([^)\s]*\))+)(?:\s+""[^""]*"")?\)"
        + @"|(?<auto>https?://[^\s<>\[\]`""']+)"
        + @"|(?<br><br\s*/?>)"
        + @"|\\(?<esc>[\\`*_{}\[\]()#+\-.!|~$<>])"
        + @"|\*\*(?<bold>\S(?:.*?\S)?)\*\*"
        + @"|(?<![A-Za-z0-9])__(?<bold2>\S(?:.*?\S)?)__(?![A-Za-z0-9])"
        + @"|~~(?<strike>\S(?:.*?\S)?)~~"
        + @"|\*(?<italic>\S(?:.*?\S)?)\*"
        + @"|(?<![A-Za-z0-9])_(?<italic2>\S(?:.*?\S)?)_(?![A-Za-z0-9])",
        RegexOptions.None, 150)]
    private static partial Regex InlineRegex();

    /// <summary>본문 스타일이 적용된 빈 문서.</summary>
    public static FlowDocument CreateDocument()
    {
        FlowDocument document = new()
        {
            PagePadding = new Thickness(0),
            FontFamily = BodyFont,
            FontSize = BodyFontSize,
            LineHeight = BodyLineHeight,
            TextAlignment = TextAlignment.Left,
        };
        document.SetResourceReference(TextElement.ForegroundProperty, "ForegroundBrush");
        return document;
    }

    /// <summary>마크다운 전체를 한 번에 문서로 만든다. (스트리밍 화면은 MarkdownPresenter 가 블록 단위로 갱신한다)</summary>
    public static FlowDocument RenderDocument(string? markdown)
    {
        var document = CreateDocument();
        AddBlocks(document.Blocks, markdown);
        TrimLastMargin(document.Blocks);
        return document;
    }

    private static void AddBlocks(BlockCollection target, string? markdown)
    {
        foreach (var segment in MarkdownSegmenter.Split(markdown))
        {
            foreach (var block in Build(segment)) target.Add(block);
        }
    }

    /// <summary>마지막 블록의 하단 마진 제거(말풍선 하단 여백 방지).</summary>
    private static void TrimLastMargin(BlockCollection blocks)
    {
        if (blocks.LastBlock is not { } last) return;
        var m = last.Margin;
        last.Margin = new Thickness(m.Left, m.Top, m.Right, 0);
    }

    /// <summary>블록 하나를 WPF 요소로 만든다. 목록처럼 항목마다 문단이 되는 블록은 여러 요소를 돌려준다.</summary>
    internal static List<Block> Build(MarkdownSegment segment) => segment.Kind switch
    {
        SegmentKind.Code => [new BlockUIContainer(BuildCodeBlock(segment.Language, string.Join('\n', segment.Lines))) { Margin = new Thickness(0, 2, 0, 12) }],
        SegmentKind.Rule => [BuildRule()],
        SegmentKind.Heading => [BuildHeading(segment.Level, segment.Lines.Count > 0 ? segment.Lines[0] : "")],
        SegmentKind.Table => [BuildTable(segment.Lines)],
        SegmentKind.Quote => [BuildBlockquote(segment.Lines)],
        SegmentKind.List => BuildList(segment.Lines),
        SegmentKind.Math => [BuildMath(segment.Lines)],
        _ => [BuildParagraph(segment.Lines)],
    };

    // ===== 블록 =====

    private static Block BuildParagraph(IReadOnlyList<string> lines)
    {
        Paragraph paragraph = new() { Margin = new Thickness(0, 0, 0, 10) };
        AppendLinesWithBreaks(paragraph, lines);
        return paragraph;
    }

    private static Block BuildHeading(int level, string text)
    {
        double size = level switch { 1 => 22, 2 => 19, 3 => 16.5, 4 => 15, _ => BodyFontSize };
        Paragraph paragraph = new()
        {
            FontSize = size,
            LineHeight = Math.Round(size * 1.35),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, level <= 2 ? 14 : 10, 0, 6),
        };
        AppendInlines(paragraph, text);
        return paragraph;
    }

    private static List<Block> BuildList(IReadOnlyList<string> lines)
    {
        List<Block> blocks = [];
        List<int> indents = [];                 // 중첩 수준별 들여쓰기 폭(스택)
        Dictionary<int, int> counters = [];     // 수준별 다음 번호. 수준마다 따로 세어야 중첩 번호 목록이 1,2,3 으로 이어지지 않는다
        Paragraph? current = null;
        bool blankBefore = false;
        int index = 0;

        double TextLeft() => Math.Max(0, indents.Count - 1) * ListIndent + ListIndent;

        while (index < lines.Count)
        {
            string line = lines[index];
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                blankBefore = true;
                index++;
                continue;
            }

            if (MarkdownSegmenter.TryListItem(line, out var item))
            {
                // 들여쓰기가 줄면 그만큼 수준을 닫고, 늘면 새 수준을 연다.
                while (indents.Count > 1 && item.Indent < indents[^1])
                {
                    counters.Remove(indents.Count - 1);
                    indents.RemoveAt(indents.Count - 1);
                }
                if (indents.Count == 0 || item.Indent > indents[^1] + 1) indents.Add(item.Indent);

                int level = Math.Min(indents.Count - 1, 8);

                Paragraph paragraph = new()
                {
                    Margin = new Thickness(level * ListIndent + ListIndent, blankBefore ? 6 : 2, 0, 2),
                    TextIndent = -ListIndent,
                };

                if (item.Task is { } done)
                {
                    Run box = new(done ? "  " : "  ") { FontFamily = IconFont, FontSize = 13 };
                    box.SetResourceReference(TextElement.ForegroundProperty, done ? "AccentBrush" : "SecondaryForegroundBrush");
                    paragraph.Inlines.Add(box);
                }
                else
                {
                    string marker;
                    if (item.Ordered)
                    {
                        // 그 수준의 첫 항목은 원문의 번호에서 시작하고(중간에 끊겼다 이어지는 "3." 등), 이후는 차례로 센다.
                        int number = counters.TryGetValue(level, out int next) ? next : item.Number;
                        counters[level] = number + 1;
                        marker = number + ".";
                    }
                    else
                    {
                        counters.Remove(level);
                        marker = (level % 3) switch { 0 => "•", 1 => "◦", _ => "▪" };
                    }

                    Run markerRun = new(marker + "  ");
                    markerRun.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                    paragraph.Inlines.Add(markerRun);
                }

                AppendInlines(paragraph, item.Text);
                blocks.Add(paragraph);
                current = paragraph;
                blankBefore = false;
                index++;
                continue;
            }

            // 항목에 딸린 코드 블록: 항목 본문과 같은 들여쓰기로 놓는다.
            if (MarkdownSegmenter.IsFenceOpen(trimmed, out char fenceChar, out int fenceLength, out string? language))
            {
                int strip = line.Length - trimmed.Length;
                List<string> code = [];
                index++;
                while (index < lines.Count && !MarkdownSegmenter.IsFenceClose(lines[index].TrimStart(), fenceChar, fenceLength))
                {
                    string codeLine = lines[index];
                    int lead = 0;
                    while (lead < strip && lead < codeLine.Length && codeLine[lead] == ' ') lead++;
                    code.Add(codeLine[lead..]);
                    index++;
                }
                if (index < lines.Count) index++;

                blocks.Add(new BlockUIContainer(BuildCodeBlock(language, string.Join('\n', code))) { Margin = new Thickness(TextLeft(), 4, 0, 8) });
                current = null;
                blankBefore = false;
                continue;
            }

            // 항목에 이어지는 들여쓴 줄: 같은 문단에 줄바꿈으로 붙이고, 빈 줄 뒤면 같은 들여쓰기의 새 문단으로 둔다.
            if (current is not null && !blankBefore)
            {
                current.Inlines.Add(new LineBreak());
                AppendInlines(current, trimmed.TrimEnd());
            }
            else
            {
                Paragraph continuation = new() { Margin = new Thickness(TextLeft(), 4, 0, 2) };
                AppendInlines(continuation, trimmed.TrimEnd());
                blocks.Add(continuation);
                current = continuation;
            }
            blankBefore = false;
            index++;
        }

        if (blocks.Count > 0)
        {
            var m = blocks[^1].Margin;
            blocks[^1].Margin = new Thickness(m.Left, m.Top, m.Right, 10);
        }
        return blocks;
    }

    /// <summary>인용: 안쪽 내용도 마크다운으로 다시 렌더한다(인용 안의 목록·코드·중첩 인용).</summary>
    private static Block BuildBlockquote(IReadOnlyList<string> lines)
    {
        Section section = new()
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(14, 2, 8, 2),
            Margin = new Thickness(0, 2, 0, 10),
        };
        section.SetResourceReference(Block.BorderBrushProperty, "AccentBrush");
        section.SetResourceReference(TextElement.ForegroundProperty, "SecondaryForegroundBrush");

        AddBlocks(section.Blocks, string.Join('\n', lines));
        TrimLastMargin(section.Blocks);
        return section;
    }

    private static Block BuildRule()
    {
        Paragraph rule = new()
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 10, 0, 10),
            FontSize = 1,
            LineHeight = 1,
        };
        rule.SetResourceReference(Block.BorderBrushProperty, "BorderStrongBrush");
        return rule;
    }

    /// <summary>블록 수식. 조판 대신 유니코드 근사로 가운데에 크게 놓는다.</summary>
    private static Block BuildMath(IReadOnlyList<string> lines)
    {
        Paragraph paragraph = new()
        {
            FontFamily = MathFont,
            FontSize = 16.5,
            LineHeight = 26,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
        };

        string[] converted = LatexToUnicode.Convert(string.Join('\n', lines)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < converted.Length; i++)
        {
            paragraph.Inlines.Add(new Run(converted[i]));
            if (i < converted.Length - 1) paragraph.Inlines.Add(new LineBreak());
        }
        return paragraph;
    }

    private static Table BuildTable(IReadOnlyList<string> lines)
    {
        var headers = MarkdownSegmenter.SplitTableRow(lines[0]);
        var alignments = MarkdownSegmenter.SplitTableRow(lines.Count > 1 ? lines[1] : "").Select(s =>
        {
            string t = s.Trim();
            bool left = t.StartsWith(':');
            bool right = t.EndsWith(':');
            return left && right ? TextAlignment.Center : right ? TextAlignment.Right : TextAlignment.Left;
        }).ToList();

        Table table = new() { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 12) };
        for (int c = 0; c < headers.Count; c++) table.Columns.Add(new TableColumn { Width = GridLength.Auto });

        TableRowGroup group = new();
        table.RowGroups.Add(group);

        TableCell MakeCell(string text, int column, bool header)
        {
            Paragraph paragraph = new()
            {
                Margin = new Thickness(0),
                TextAlignment = column < alignments.Count ? alignments[column] : TextAlignment.Left,
            };
            if (header) paragraph.FontWeight = FontWeights.SemiBold;
            AppendInlines(paragraph, text);

            TableCell cell = new(paragraph)
            {
                BorderThickness = new Thickness(0.5),
                Padding = new Thickness(10, 5, 10, 5),
            };
            cell.SetResourceReference(TableCell.BorderBrushProperty, "BorderStrongBrush");
            if (header) cell.SetResourceReference(TextElement.BackgroundProperty, "CodeHeaderBackgroundBrush");
            return cell;
        }

        TableRow headerRow = new();
        for (int c = 0; c < headers.Count; c++) headerRow.Cells.Add(MakeCell(headers[c], c, true));
        group.Rows.Add(headerRow);

        for (int r = 2; r < lines.Count; r++)
        {
            // 모델이 행 사이마다 구분선(|---|---|)을 넣는 경우가 있어 데이터 행으로 세지 않는다.
            if (MarkdownSegmenter.IsTableSeparator(lines[r].TrimStart())) continue;

            var row = MarkdownSegmenter.SplitTableRow(lines[r]);
            TableRow tableRow = new();
            for (int c = 0; c < headers.Count; c++) tableRow.Cells.Add(MakeCell(c < row.Count ? row[c] : "", c, false));
            group.Rows.Add(tableRow);
        }

        return table;
    }

    // ===== 코드 블록 =====

    private static FrameworkElement BuildCodeBlock(string? language, string code)
    {
        Border container = new()
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            SnapsToDevicePixels = true,
        };
        container.SetResourceReference(Border.BackgroundProperty, "CodeBlockBackgroundBrush");
        container.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 헤더는 위쪽 모서리만 둥글게 해 바깥 테두리의 둥근 모서리 밖으로 삐져나오지 않게 한다.
        Border header = new() { CornerRadius = new CornerRadius(7, 7, 0, 0) };
        header.SetResourceReference(Border.BackgroundProperty, "CodeHeaderBackgroundBrush");

        Grid headerGrid = new();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Child = headerGrid;

        TextBlock languageLabel = new()
        {
            Text = string.IsNullOrWhiteSpace(language) ? "text" : language.ToLowerInvariant(),
            FontSize = 11.5,
            Margin = new Thickness(12, 5, 8, 5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        languageLabel.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        Grid.SetColumn(languageLabel, 0);

        var (codeView, document, naturalWidth) = BuildCodeView(code, language);

        // 줄바꿈 전환: 긴 줄을 가로 스크롤 대신 접어서 본다.
        Button wrapButton = CreateHeaderButton("CodeWrap");
        bool wrapped = false;
        wrapButton.Click += (_, _) =>
        {
            wrapped = !wrapped;
            document.PageWidth = wrapped ? double.NaN : naturalWidth;
            codeView.HorizontalScrollBarVisibility = wrapped ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            if (wrapped) wrapButton.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
            else wrapButton.ClearValue(Control.ForegroundProperty);
        };
        Grid.SetColumn(wrapButton, 1);

        Button copyButton = CreateHeaderButton("CodeCopy");
        copyButton.Margin = new Thickness(0, 3, 6, 3);
        DispatcherTimer? revert = null;
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(code);
            }
            catch
            {
                return; // 클립보드 잠김 등은 무시
            }

            // "복사됨"을 잠깐 보였다가 원래 문구로 되돌린다.
            copyButton.Content = L["CodeCopied"];
            revert?.Stop();
            revert = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            revert.Tick += (_, _) =>
            {
                revert?.Stop();
                BindLabel(copyButton, "CodeCopy");
            };
            revert.Start();
        };
        Grid.SetColumn(copyButton, 2);

        headerGrid.Children.Add(languageLabel);
        headerGrid.Children.Add(wrapButton);
        headerGrid.Children.Add(copyButton);
        Grid.SetRow(header, 0);
        Grid.SetRow(codeView, 1);

        root.Children.Add(header);
        root.Children.Add(codeView);
        container.Child = root;
        return container;
    }

    private static Button CreateHeaderButton(string labelKey)
    {
        Button button = new()
        {
            Margin = new Thickness(0, 3, 2, 3),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11.5,
            Cursor = Cursors.Hand,
            Style = Application.Current?.TryFindResource("SubtleButtonStyle") as Style,
        };
        BindLabel(button, labelKey);
        return button;
    }

    /// <summary>버튼 문구를 번역 문자열에 바인딩한다(언어를 바꾸면 이미 그려진 버튼도 따라 바뀐다).</summary>
    private static void BindLabel(Button button, string key)
    {
        Binding binding = new($"[{key}]") { Source = L, Mode = BindingMode.OneWay };
        button.SetBinding(ContentControl.ContentProperty, binding);
        button.SetBinding(AutomationProperties.NameProperty, binding);
    }

    private static (RichTextBox View, FlowDocument Document, double NaturalWidth) BuildCodeView(string code, string? language)
    {
        // 세로는 내용만큼 늘어난다(안쪽 세로 스크롤을 두면 스트리밍 중 새 코드가 접힌 영역 아래로 숨고, 바깥 목록과 휠이 엇갈린다).
        RichTextBox richTextBox = new()
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = MonoFont,
            FontSize = CodeFontSize,
            Padding = new Thickness(12, 8, 12, 10),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            SelectionOpacity = 0.35,
            FocusVisualStyle = null,
        };
        richTextBox.SetResourceReference(Control.ForegroundProperty, "CodeForegroundBrush");
        richTextBox.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "AccentBrush");

        FlowDocument document = new()
        {
            PagePadding = new Thickness(0),
            FontFamily = MonoFont,
            FontSize = CodeFontSize,
        };
        document.SetResourceReference(TextElement.ForegroundProperty, "CodeForegroundBrush");

        // 탭은 폭이 글꼴/문서마다 달라 측정과 어긋나므로 화면에는 공백 네 칸으로 보인다. (복사는 원문 그대로)
        string display = code.Replace("\t", "    ");
        string[] lines = display.ReplaceLineEndings("\n").Split('\n');

        // FlowDocument 는 기본적으로 폭에 맞춰 줄을 접는다. 가장 긴 줄의 실제 폭을 재서 페이지 폭으로 주면 접지 않고 가로 스크롤이 생긴다.
        double naturalWidth = MeasureWidest(lines);
        document.PageWidth = naturalWidth;

        Paragraph paragraph = new() { Margin = new Thickness(0), LineHeight = CodeLineHeight };

        if (display.Length > MaxHighlightLength)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                paragraph.Inlines.Add(new Run(lines[i]));
                if (i < lines.Length - 1) paragraph.Inlines.Add(new LineBreak());
            }
        }
        else
        {
            var tokenLines = CodeHighlighter.Tokenize(display, language);
            for (int i = 0; i < tokenLines.Count; i++)
            {
                foreach (var token in tokenLines[i])
                {
                    Run run = new(token.Text);
                    if (BrushKeyFor(token.Kind) is { } key) run.SetResourceReference(TextElement.ForegroundProperty, key);
                    paragraph.Inlines.Add(run);
                }
                if (i < tokenLines.Count - 1) paragraph.Inlines.Add(new LineBreak());
            }
        }

        document.Blocks.Add(paragraph);
        richTextBox.Document = document;

        NestedScroll.EnableWheelBubbling(richTextBox);
        return (richTextBox, document, naturalWidth);
    }

    private static string? BrushKeyFor(TokenKind kind) => kind switch
    {
        TokenKind.Keyword => "SyntaxKeywordBrush",
        TokenKind.String => "SyntaxStringBrush",
        TokenKind.Comment => "SyntaxCommentBrush",
        TokenKind.Number => "SyntaxNumberBrush",
        _ => null,
    };

    /// <summary>가장 긴 줄의 실제 렌더 폭. 한글 등 전각 문자는 반각의 두 배 가까이 차지하므로 글자 수가 아니라 측정으로 구한다.</summary>
    private static double MeasureWidest(string[] lines)
    {
        static int Columns(string line)
        {
            int columns = 0;
            foreach (char c in line) columns += c >= 0x1100 ? 2 : 1;
            return columns;
        }

        string widest = "";
        int widestColumns = 0;
        foreach (string line in lines)
        {
            int columns = Columns(line);
            if (columns <= widestColumns) continue;
            widestColumns = columns;
            widest = line;
        }

        if (widest.Length == 0) return 60;
        if (widest.Length > 2000) return widestColumns * 8.2 + 40; // 비정상적으로 긴 한 줄은 측정 비용을 피한다

        // 화면은 Display 모드(글자 폭을 픽셀에 맞춤)로 그리므로 같은 모드로 잰다. Ideal 로 재면 실제보다 좁게 나와 마지막 단어가 접힌다.
        // 그래도 배율·글꼴 대체에 따라 조금씩 달라지므로 여유를 둔다(모자라면 줄이 접히지만 남으면 스크롤 여백이 조금 생길 뿐이다).
        FormattedText text = new(widest, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(MonoFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), CodeFontSize, Brushes.Black, null, TextFormattingMode.Display, 1.0);
        return Math.Ceiling(text.WidthIncludingTrailingWhitespace * 1.03) + 14;
    }

    // ===== 인라인 =====

    private static void AppendLinesWithBreaks(Paragraph paragraph, IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            AppendInlines(paragraph, lines[i].Trim());
            if (i < lines.Count - 1) paragraph.Inlines.Add(new LineBreak());
        }
    }

    private static void AppendInlines(Paragraph paragraph, string text)
    {
        foreach (var inline in ParseInlines(text)) paragraph.Inlines.Add(inline);
    }

    private static List<Inline> ParseInlines(string text)
    {
        if (string.IsNullOrEmpty(text)) return [new Run("")];
        if (text.Length > MaxInlineLength) return [new Run(text)];

        MatchCollection matches;
        try
        {
            matches = InlineRegex().Matches(text);
            _ = matches.Count; // 지연 평가라 여기서 끝까지 돌려 타임아웃을 이 try 안에서 잡는다
        }
        catch (RegexMatchTimeoutException)
        {
            return [new Run(text)];
        }

        List<Inline> result = [];
        int position = 0;

        foreach (Match match in matches)
        {
            if (match.Index > position) result.Add(new Run(text[position..match.Index]));
            position = match.Index + match.Length;

            if (match.Groups["code"].Success)
            {
                result.Add(CreateInlineCode(match.Groups["codebody"].Value));
            }
            else if (match.Groups["imath"].Success || match.Groups["imath2"].Success)
            {
                string latex = match.Groups["imath"].Success ? match.Groups["imath"].Value : match.Groups["imath2"].Value;
                result.Add(new Run(LatexToUnicode.Convert(latex)) { FontFamily = MathFont, FontSize = BodyFontSize + 1 });
            }
            else if (match.Groups["iurl"].Success)
            {
                // 이미지를 본문에 내려받아 그리지는 않는다(모델이 만든 URL 로 원격 요청이 나가면 추적에 쓰일 수 있다). 링크로 보여 준다.
                string alt = match.Groups["ialt"].Value.Trim();
                result.Add(CreateLink("\U0001F5BC " + (alt.Length > 0 ? alt : L["MarkdownImage"]), match.Groups["iurl"].Value));
            }
            else if (match.Groups["ltext"].Success)
            {
                result.Add(CreateLink(match.Groups["ltext"].Value, match.Groups["lurl"].Value));
            }
            else if (match.Groups["auto"].Success)
            {
                // 문장 끝의 구두점은 주소가 아니다. (괄호는 주소 안에 짝이 맞게 들어 있을 때만 남긴다)
                string url = match.Groups["auto"].Value;
                string trimmedUrl = url.TrimEnd('.', ',', ';', ':', '!', '?', '*', '_');
                while (trimmedUrl.EndsWith(')') && trimmedUrl.Count(c => c == ')') > trimmedUrl.Count(c => c == '(')) trimmedUrl = trimmedUrl[..^1];

                result.Add(CreateLink(trimmedUrl, trimmedUrl, parseText: false));
                if (trimmedUrl.Length < url.Length) result.Add(new Run(url[trimmedUrl.Length..]));
            }
            else if (match.Groups["br"].Success)
            {
                result.Add(new LineBreak());
            }
            else if (match.Groups["esc"].Success)
            {
                result.Add(new Run(match.Groups["esc"].Value));
            }
            else if (match.Groups["bold"].Success || match.Groups["bold2"].Success)
            {
                result.Add(new Bold(BuildSpanContent(match.Groups["bold"].Success ? match.Groups["bold"].Value : match.Groups["bold2"].Value)));
            }
            else if (match.Groups["strike"].Success)
            {
                result.Add(new Span(BuildSpanContent(match.Groups["strike"].Value)) { TextDecorations = TextDecorations.Strikethrough });
            }
            else if (match.Groups["italic"].Success || match.Groups["italic2"].Success)
            {
                result.Add(new Italic(BuildSpanContent(match.Groups["italic"].Success ? match.Groups["italic"].Value : match.Groups["italic2"].Value)));
            }
        }

        if (position < text.Length) result.Add(new Run(text[position..]));
        if (result.Count == 0) result.Add(new Run(text));
        return result;
    }

    private static Span BuildSpanContent(string inner)
    {
        Span span = new();
        foreach (var inline in ParseInlines(inner)) span.Inlines.Add(inline);
        return span;
    }

    private static Run CreateInlineCode(string code)
    {
        Run run = new(code.Trim()) { FontFamily = MonoFont, FontSize = CodeFontSize };
        run.SetResourceReference(TextElement.BackgroundProperty, "InlineCodeBackgroundBrush");
        run.SetResourceReference(TextElement.ForegroundProperty, "InlineCodeForegroundBrush");
        return run;
    }

    /// <summary>
    /// 링크. 웹/메일 주소만 실제 링크로 만들고(<see cref="UrlSafety"/>), 그 밖의 스킴(file:, 사용자 지정 프로토콜 등)은 열 수 없는 일반 텍스트로 둔다.
    /// 누르기 전에 어디로 가는지 알 수 있게 주소를 툴팁으로 보여 준다.
    /// </summary>
    private static Inline CreateLink(string text, string url, bool parseText = true)
    {
        Span content = parseText ? BuildSpanContent(text) : new Span(new Run(text));
        if (UrlSafety.ToSafeUri(url) is not { } uri) return content;

        Hyperlink link = new(content) { Cursor = Cursors.Hand, NavigateUri = uri, ToolTip = uri.ToString() };
        link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
        link.RequestNavigate += (_, e) =>
        {
            // 클릭 시점에도 한 번 더 확인한다.
            if (UrlSafety.ToSafeUri(e.Uri?.ToString()) is { } target)
            {
                try { Process.Start(new ProcessStartInfo(target.ToString()) { UseShellExecute = true }); }
                catch { /* 브라우저 실행 실패는 무시 */ }
            }
            e.Handled = true;
        };
        return link;
    }
}
