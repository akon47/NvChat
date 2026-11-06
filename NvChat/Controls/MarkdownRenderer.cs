using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using NvChat.Behaviors;
using NvChat.Localization;

namespace NvChat.Controls;

/// <summary>
/// 경량 마크다운 → WPF FlowDocument 렌더러(외부 라이브러리 없음).
/// 읽기 전용 RichTextBox 에 넣어 문단/목록/표/인용까지 드래그 선택·복사가 가능하다.
/// 코드펜스(구문강조·복사 버튼)/인라인코드/헤딩/체크리스트/구분선/굵게·기울임·취소선·링크 지원.
/// </summary>
public static partial class MarkdownRenderer
{
    private const int MaxInlineLength = 4000;
    private const int MaxHighlightLength = 20000;
    private const double BodyFontSize = 13;
    private const double ListIndent = 18;

    private static readonly FontFamily MonoFont = new("Consolas, Cascadia Mono, Courier New");
    private static readonly FontFamily BodyFont = new("Segoe UI, Malgun Gothic");
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    [GeneratedRegex(
        @"(?<code>`+)(?<codebody>.+?)\k<code>"
        + @"|\[(?<ltext>[^\]]+)\]\((?<lurl>(?:[^()\s]|\([^)\s]*\))+)\)"
        + @"|\*\*(?<bold>\S(?:.*?\S)?)\*\*"
        + @"|(?<![A-Za-z0-9])__(?<bold2>\S(?:.*?\S)?)__(?![A-Za-z0-9])"
        + @"|~~(?<strike>\S(?:.*?\S)?)~~"
        + @"|\*(?<italic>\S(?:.*?\S)?)\*"
        + @"|(?<![A-Za-z0-9])_(?<italic2>\S(?:.*?\S)?)_(?![A-Za-z0-9])",
        RegexOptions.None, 150)]
    private static partial Regex InlineRegex();

    private readonly record struct ListItemInfo(int Indent, bool Ordered, string Marker, bool? Task, string Text);

    public static FlowDocument RenderDocument(string? markdown)
    {
        FlowDocument document = new()
        {
            PagePadding = new Thickness(0),
            FontFamily = BodyFont,
            FontSize = BodyFontSize,
            Foreground = GetBrush("ForegroundBrush", Color.FromRgb(0xF2, 0xF2, 0xF3)),
        };

        if (string.IsNullOrEmpty(markdown)) return document;

        string[] lines = markdown.ReplaceLineEndings("\n").Split('\n');
        int index = 0;

        while (index < lines.Length)
        {
            string raw = lines[index];
            string trimmed = raw.TrimStart();

            // 코드 펜스
            if (IsFenceOpen(trimmed, out char fenceChar, out string? language))
            {
                index++;
                StringBuilder code = new();
                while (index < lines.Length && !IsFenceClose(lines[index].TrimStart(), fenceChar))
                {
                    code.Append(lines[index]).Append('\n');
                    index++;
                }
                if (index < lines.Length) index++;

                document.Blocks.Add(new BlockUIContainer(BuildCodeBlock(language, code.ToString().TrimEnd('\n'))) { Margin = new Thickness(0, 2, 0, 10) });
                continue;
            }

            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            if (IsHorizontalRule(trimmed))
            {
                document.Blocks.Add(BuildRule());
                index++;
                continue;
            }

            if (TryHeading(trimmed, out int level, out string? headingText))
            {
                document.Blocks.Add(BuildHeading(level, headingText));
                index++;
                continue;
            }

            if (trimmed.Contains('|') && index + 1 < lines.Length && IsTableSeparator(lines[index + 1].TrimStart()))
            {
                document.Blocks.Add(ParseTable(lines, ref index));
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                List<string> quoteLines = [];
                while (index < lines.Length && lines[index].TrimStart().StartsWith('>'))
                {
                    quoteLines.Add(StripQuoteMarker(lines[index]));
                    index++;
                }
                document.Blocks.Add(BuildBlockquote(quoteLines));
                continue;
            }

            if (TryListItem(raw, out _))
            {
                List<ListItemInfo> items = [];
                while (index < lines.Length && TryListItem(lines[index], out var item))
                {
                    items.Add(item);
                    index++;
                }
                foreach (var block in BuildList(items)) document.Blocks.Add(block);
                continue;
            }

            // 밑줄식(setext) 헤딩: 다음 줄이 === 로만 이루어져 있으면 제목이다. (--- 는 수평선과 겹치므로 === 만 처리한다)
            if (index + 1 < lines.Length && IsSetextUnderline(lines[index + 1].TrimStart()))
            {
                document.Blocks.Add(BuildHeading(1, trimmed));
                index += 2;
                continue;
            }

            // 문단
            List<string> paragraph = [];
            while (index < lines.Length)
            {
                string line = lines[index];
                string t = line.TrimStart();

                if (t.Length == 0
                    || IsFenceOpen(t, out _, out _)
                    || IsHorizontalRule(t)
                    || TryHeading(t, out _, out _)
                    || t.StartsWith('>')
                    || TryListItem(line, out _)
                    || (t.Contains('|') && index + 1 < lines.Length && IsTableSeparator(lines[index + 1].TrimStart())))
                    break;

                // 다음 줄이 === 밑줄이면 이 줄은 제목이므로 문단에 넣지 않고 바깥 루프로 넘긴다.
                if (index + 1 < lines.Length && IsSetextUnderline(lines[index + 1].TrimStart())) break;

                paragraph.Add(line);
                index++;
            }

            if (paragraph.Count > 0) document.Blocks.Add(BuildParagraph(paragraph));
        }

        // 마지막 블록의 하단 마진 제거(말풍선 하단 여백 방지)
        if (document.Blocks.LastBlock is { } last)
        {
            var m = last.Margin;
            last.Margin = new Thickness(m.Left, m.Top, m.Right, 0);
        }

        return document;
    }

    // ===== 블록 =====

    private static Block BuildParagraph(IReadOnlyList<string> lines)
    {
        Paragraph paragraph = new() { Margin = new Thickness(0, 0, 0, 8) };
        AppendLinesWithBreaks(paragraph, lines);
        return paragraph;
    }

    private static Block BuildHeading(int level, string text)
    {
        Paragraph paragraph = new()
        {
            FontSize = level switch { 1 => 21, 2 => 18, 3 => 16, 4 => 14.5, _ => 13.5 },
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, level <= 2 ? 10 : 6, 0, 4),
        };
        AppendInlines(paragraph, text);
        return paragraph;
    }

    private static IEnumerable<Block> BuildList(IReadOnlyList<ListItemInfo> items)
    {
        int number = 1;
        if (items.FirstOrDefault(i => i.Ordered) is { Ordered: true } firstOrdered && int.TryParse(firstOrdered.Marker.TrimEnd('.'), out int startNo)) number = startNo;

        var accent = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00));
        var muted = GetBrush("SecondaryForegroundBrush", Color.FromRgb(0x9E, 0xA2, 0xAE));
        List<Block> blocks = [];

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            int indentLevel = Math.Min(item.Indent / 2, 6);

            Paragraph paragraph = new()
            {
                Margin = new Thickness(indentLevel * ListIndent + ListIndent, 1, 0, i == items.Count - 1 ? 8 : 1),
                TextIndent = -ListIndent,
            };

            if (item.Task is { } done)
            {
                paragraph.Inlines.Add(new Run(done ? "  " : "  ") { FontFamily = IconFont, FontSize = 12, Foreground = done ? accent : muted });
            }
            else
            {
                string marker = item.Ordered ? $"{number++}." : "•";
                paragraph.Inlines.Add(new Run(marker + "  ") { Foreground = accent });
            }

            AppendInlines(paragraph, item.Text);
            blocks.Add(paragraph);
        }

        return blocks;
    }

    private static Block BuildBlockquote(IReadOnlyList<string> lines)
    {
        Paragraph paragraph = new()
        {
            Foreground = GetBrush("SecondaryForegroundBrush", Color.FromRgb(0x9E, 0xA2, 0xAE)),
            Background = GetBrush("InlineCodeBackgroundBrush", Color.FromRgb(0x2A, 0x2A, 0x2B)),
            BorderBrush = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 2, 0, 8),
        };
        AppendLinesWithBreaks(paragraph, lines);
        return paragraph;
    }

    private static Block BuildRule() => new Paragraph
    {
        BorderBrush = GetBrush("BorderBrush", Color.FromRgb(0x3A, 0x3A, 0x3C)),
        BorderThickness = new Thickness(0, 0, 0, 1),
        Margin = new Thickness(0, 8, 0, 8),
        FontSize = 1,
    };

    private static Table BuildTable(IReadOnlyList<string> headers, IReadOnlyList<TextAlignment> alignments, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var border = GetBrush("BorderBrush", Color.FromRgb(0x3A, 0x3A, 0x3C));
        var headerBackground = GetBrush("CodeHeaderBackgroundBrush", Color.FromRgb(0x22, 0x22, 0x24));

        Table table = new() { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 10) };
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

            return new TableCell(paragraph)
            {
                BorderBrush = border,
                BorderThickness = new Thickness(0.5),
                Background = header ? headerBackground : Brushes.Transparent,
                Padding = new Thickness(10, 6, 10, 6),
            };
        }

        TableRow headerRow = new();
        for (int c = 0; c < headers.Count; c++) headerRow.Cells.Add(MakeCell(headers[c], c, true));
        group.Rows.Add(headerRow);

        foreach (var row in rows)
        {
            TableRow tableRow = new();
            for (int c = 0; c < headers.Count; c++) tableRow.Cells.Add(MakeCell(c < row.Count ? row[c] : "", c, false));
            group.Rows.Add(tableRow);
        }

        return table;
    }

    private static FrameworkElement BuildCodeBlock(string? language, string code)
    {
        Border container = new()
        {
            Background = GetBrush("CodeBlockBackgroundBrush", Color.FromRgb(0x18, 0x18, 0x19)),
            BorderBrush = GetBrush("BorderBrush", Color.FromRgb(0x3A, 0x3A, 0x3C)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid header = new() { Background = GetBrush("CodeHeaderBackgroundBrush", Color.FromRgb(0x22, 0x22, 0x24)) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock languageLabel = new()
        {
            Text = string.IsNullOrWhiteSpace(language) ? "code" : language.ToLowerInvariant(),
            Foreground = GetBrush("SecondaryForegroundBrush", Color.FromRgb(0x9E, 0xA2, 0xAE)),
            FontSize = 11.5,
            Margin = new Thickness(12, 5, 8, 5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(languageLabel, 0);

        Button copyButton = new()
        {
            Content = LocalizationManager.Instance["CodeCopy"],
            Margin = new Thickness(0, 3, 6, 3),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11.5,
            Cursor = Cursors.Hand,
            Style = Application.Current?.Resources["SubtleButtonStyle"] as Style,
        };
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(code);
                copyButton.Content = LocalizationManager.Instance["CodeCopied"];
            }
            catch { /* 클립보드 잠김 등은 무시 */ }
        };
        Grid.SetColumn(copyButton, 1);

        header.Children.Add(languageLabel);
        header.Children.Add(copyButton);
        Grid.SetRow(header, 0);

        var codeView = BuildCodeView(code, language);
        Grid.SetRow(codeView, 1);

        root.Children.Add(header);
        root.Children.Add(codeView);
        container.Child = root;
        return container;
    }

    private static FrameworkElement BuildCodeView(string code, string? language)
    {
        RichTextBox richTextBox = new()
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = GetBrush("CodeForegroundBrush", Color.FromRgb(0xE6, 0xE6, 0xE6)),
            FontFamily = MonoFont,
            FontSize = 12.5,
            Padding = new Thickness(12, 8, 12, 10),
            MaxHeight = 460,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            SelectionBrush = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00)),
        };

        FlowDocument document = new()
        {
            PagePadding = new Thickness(0),
            FontFamily = MonoFont,
            FontSize = 12.5,
            Foreground = richTextBox.Foreground,
        };

        string[] lines = code.ReplaceLineEndings("\n").Split('\n');
        int maxLength = lines.Length == 0 ? 0 : lines.Max(l => l.Length);
        document.PageWidth = Math.Max(80, maxLength * 7.4 + 28);

        Paragraph paragraph = new() { Margin = new Thickness(0), LineHeight = 18 };

        if (code.Length > MaxHighlightLength)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                paragraph.Inlines.Add(new Run(lines[i]));
                if (i < lines.Length - 1) paragraph.Inlines.Add(new LineBreak());
            }
        }
        else
        {
            var tokenLines = CodeHighlighter.Tokenize(code, language);
            for (int i = 0; i < tokenLines.Count; i++)
            {
                foreach (var token in tokenLines[i]) paragraph.Inlines.Add(new Run(token.Text) { Foreground = token.Brush });
                if (i < tokenLines.Count - 1) paragraph.Inlines.Add(new LineBreak());
            }
        }

        document.Blocks.Add(paragraph);
        richTextBox.Document = document;

        // 코드 블록이 휠을 삼켜 바깥 대화 목록이 멈추지 않도록, 한계에 닿으면 부모로 넘긴다.
        NestedScroll.EnableWheelBubbling(richTextBox);
        return richTextBox;
    }

    // ===== 인라인 =====

    private static void AppendLinesWithBreaks(Paragraph paragraph, IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            AppendInlines(paragraph, lines[i].TrimEnd());
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
        try { matches = InlineRegex().Matches(text); }
        catch (RegexMatchTimeoutException) { return [new Run(text)]; }

        List<Inline> result = [];
        int position = 0;

        foreach (Match match in matches)
        {
            if (match.Index > position) result.Add(new Run(text[position..match.Index]));

            if (match.Groups["code"].Success) result.Add(CreateInlineCode(match.Groups["codebody"].Value));
            else if (match.Groups["ltext"].Success) result.Add(CreateLink(match.Groups["ltext"].Value, match.Groups["lurl"].Value));
            else if (match.Groups["bold"].Success || match.Groups["bold2"].Success) result.Add(new Bold(BuildSpanContent(match.Groups["bold"].Success ? match.Groups["bold"].Value : match.Groups["bold2"].Value)));
            else if (match.Groups["strike"].Success) result.Add(new Span(BuildSpanContent(match.Groups["strike"].Value)) { TextDecorations = TextDecorations.Strikethrough });
            else if (match.Groups["italic"].Success || match.Groups["italic2"].Success) result.Add(new Italic(BuildSpanContent(match.Groups["italic"].Success ? match.Groups["italic"].Value : match.Groups["italic2"].Value)));

            position = match.Index + match.Length;
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

    private static Run CreateInlineCode(string code) => new(code)
    {
        FontFamily = MonoFont,
        FontSize = 12.5,
        Background = GetBrush("InlineCodeBackgroundBrush", Color.FromRgb(0x2A, 0x2A, 0x2B)),
        Foreground = GetBrush("InlineCodeForegroundBrush", Color.FromRgb(0xC5, 0xE1, 0x7A)),
    };

    private static Inline CreateLink(string text, string url)
    {
        var accent = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00));
        if (TryCreateUri(url) is not { } uri) return new Run(text) { Foreground = accent };

        Hyperlink link = new(new Run(text)) { Foreground = accent, Cursor = Cursors.Hand, NavigateUri = uri };
        link.RequestNavigate += (_, e) =>
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true }); }
            catch { /* 브라우저 실행 실패는 무시 */ }
            e.Handled = true;
        };
        return link;
    }

    // ===== 파싱 =====

    private static bool IsFenceOpen(string trimmed, out char fenceChar, out string? language)
    {
        fenceChar = '`';
        language = null;

        if (trimmed.StartsWith("```", StringComparison.Ordinal)) fenceChar = '`';
        else if (trimmed.StartsWith("~~~", StringComparison.Ordinal)) fenceChar = '~';
        else return false;

        string info = trimmed.TrimStart(fenceChar).Trim();
        language = info.Split([' ', '\t'], 2)[0];
        return true;
    }

    private static bool IsFenceClose(string trimmed, char fenceChar)
    {
        string body = trimmed.Trim();
        return body.Length >= 3 && body.All(c => c == fenceChar);
    }

    /// <summary>"====" 처럼 = 로만 이루어진 줄인지. (밑줄식 제목의 밑줄)</summary>
    private static bool IsSetextUnderline(string trimmed)
    {
        string body = trimmed.TrimEnd();
        return body.Length >= 2 && body.All(c => c == '=');
    }

    private static bool IsHorizontalRule(string trimmed)
    {
        string compact = trimmed.Replace(" ", "");
        return compact.Length >= 3 && (compact.All(c => c == '-') || compact.All(c => c == '*') || compact.All(c => c == '_'));
    }

    private static bool TryHeading(string trimmed, out int level, out string text)
    {
        level = 0;
        text = "";

        int hashes = 0;
        while (hashes < trimmed.Length && trimmed[hashes] == '#') hashes++;

        if (hashes is < 1 or > 6) return false;
        if (hashes >= trimmed.Length || trimmed[hashes] != ' ') return false;

        level = hashes;
        text = trimmed[(hashes + 1)..].Trim().TrimEnd('#').TrimEnd();
        return true;
    }

    private static bool TryListItem(string raw, out ListItemInfo item)
    {
        item = default;

        int lead = 0;
        while (lead < raw.Length && raw[lead] is ' ' or '\t') lead++;

        string trimmed = raw[lead..];
        if (trimmed.Length < 2) return false;

        bool ordered;
        string marker;
        string text;

        char c = trimmed[0];
        if (c is '-' or '*' or '+' && trimmed[1] == ' ')
        {
            ordered = false;
            marker = c.ToString();
            text = trimmed[2..].Trim();
        }
        else
        {
            int digits = 0;
            while (digits < trimmed.Length && char.IsDigit(trimmed[digits])) digits++;

            if (digits > 0 && digits + 1 < trimmed.Length && trimmed[digits] == '.' && trimmed[digits + 1] == ' ')
            {
                ordered = true;
                marker = trimmed[..(digits + 1)];
                text = trimmed[(digits + 2)..].Trim();
            }
            else
            {
                return false;
            }
        }

        bool? task = null;
        if (text.Length >= 3 && text[0] == '[' && text[2] == ']' && text[1] is ' ' or 'x' or 'X')
        {
            task = text[1] != ' ';
            text = text.Length > 3 ? text[3..].Trim() : "";
        }

        item = new ListItemInfo(lead, ordered, marker, task, text);
        return true;
    }

    private static bool IsTableSeparator(string trimmed)
    {
        string body = trimmed.Trim();
        return body.Contains('-') && body.All(ch => ch is '-' or ':' or '|' or ' ');
    }

    private static Table ParseTable(string[] lines, ref int index)
    {
        var headerCells = SplitTableRow(lines[index]);
        var alignments = SplitTableRow(lines[index + 1]).Select(s =>
        {
            string t = s.Trim();
            bool left = t.StartsWith(':');
            bool right = t.EndsWith(':');
            return left && right ? TextAlignment.Center : right ? TextAlignment.Right : TextAlignment.Left;
        }).ToList();

        index += 2;

        List<IReadOnlyList<string>> rows = [];
        while (index < lines.Length)
        {
            string t = lines[index].TrimStart();
            if (t.Length == 0 || !t.Contains('|')) break;

            // 모델이 행 사이마다 구분선(|---|---|)을 넣는 경우가 있어 데이터 행으로 세지 않는다.
            if (!IsTableSeparator(t)) rows.Add(SplitTableRow(lines[index]));
            index++;
        }

        return BuildTable(headerCells, alignments, rows);
    }

    private static List<string> SplitTableRow(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];

        List<string> cells = [];
        StringBuilder sb = new();
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (c == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
            {
                sb.Append('|');
                i++;
                continue;
            }
            if (c == '|')
            {
                cells.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }

    private static string StripQuoteMarker(string line)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith('>')) return trimmed;
        trimmed = trimmed[1..];
        return trimmed.StartsWith(' ') ? trimmed[1..] : trimmed;
    }

    private static Uri? TryCreateUri(string? url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)) return absolute;
        if (!url.Contains("://") && Uri.TryCreate("https://" + url, UriKind.Absolute, out var https)) return https;
        return null;
    }

    private static Brush GetBrush(string key, Color fallback)
    {
        if (Application.Current?.Resources[key] is Brush brush) return brush;

        SolidColorBrush solid = new(fallback);
        solid.Freeze();
        return solid;
    }
}
