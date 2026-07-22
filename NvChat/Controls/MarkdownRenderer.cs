using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NvChat.Controls
{
    /// <summary>
    /// 경량 마크다운 → WPF 요소 렌더러(외부 라이브러리 없음).
    /// 코드펜스(구문강조)/인라인코드/헤딩/목록(중첩·체크박스)/표/인용/구분선/굵게·기울임·취소선·링크 지원.
    /// </summary>
    public static class MarkdownRenderer
    {
        #region Constants

        private static readonly FontFamily MonoFont = new FontFamily("Consolas, Cascadia Mono, Courier New");
        private const int MaxInlineLength = 4000;   // 이보다 긴 줄은 인라인 파싱을 건너뛴다(백트래킹 방지).
        private const int MaxHighlightLength = 20000;

        // CommonMark 에 가깝게: 강조는 안쪽에 공백이 붙지 않아야 하고, 밑줄 변형은 단어 경계여야 한다.
        private static readonly Regex InlineRegex = new Regex(
            @"(?<code>`+)(?<codebody>.+?)\k<code>" +
            @"|\[(?<ltext>[^\]]+)\]\((?<lurl>(?:[^()\s]|\([^)\s]*\))+)\)" +
            @"|\*\*(?<bold>\S(?:.*?\S)?)\*\*" +
            @"|(?<![A-Za-z0-9])__(?<bold2>\S(?:.*?\S)?)__(?![A-Za-z0-9])" +
            @"|~~(?<strike>\S(?:.*?\S)?)~~" +
            @"|\*(?<italic>\S(?:.*?\S)?)\*" +
            @"|(?<![A-Za-z0-9])_(?<italic2>\S(?:.*?\S)?)_(?![A-Za-z0-9])",
            RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(150));

        #endregion


        #region Public

        public static FrameworkElement Render(string markdown)
        {
            var panel = new StackPanel();

            if (string.IsNullOrEmpty(markdown))
                return panel;

            var lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var index = 0;

            while (index < lines.Length)
            {
                var raw = lines[index];
                var trimmed = raw.TrimStart();

                // 코드 펜스
                if (IsFenceOpen(trimmed, out var fenceChar, out var language))
                {
                    index++;
                    var code = new StringBuilder();
                    while (index < lines.Length && IsFenceClose(lines[index].TrimStart(), fenceChar) == false)
                    {
                        code.Append(lines[index]).Append('\n');
                        index++;
                    }
                    if (index < lines.Length)
                        index++;

                    panel.Children.Add(BuildCodeBlock(language, code.ToString().TrimEnd('\n')));
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    index++;
                    continue;
                }

                if (IsHorizontalRule(trimmed))
                {
                    panel.Children.Add(BuildRule());
                    index++;
                    continue;
                }

                if (TryHeading(trimmed, out var level, out var headingText))
                {
                    panel.Children.Add(BuildHeading(level, headingText));
                    index++;
                    continue;
                }

                // 표: 현재 줄이 표 행이고 다음 줄이 구분선일 때.
                if (trimmed.Contains('|') && index + 1 < lines.Length && IsTableSeparator(lines[index + 1].TrimStart()))
                {
                    var table = ParseTable(lines, ref index);
                    if (table != null)
                    {
                        panel.Children.Add(table);
                        continue;
                    }
                }

                if (trimmed.StartsWith(">", StringComparison.Ordinal))
                {
                    var quoteLines = new List<string>();
                    while (index < lines.Length && lines[index].TrimStart().StartsWith(">", StringComparison.Ordinal))
                    {
                        quoteLines.Add(StripQuoteMarker(lines[index]));
                        index++;
                    }
                    panel.Children.Add(BuildBlockquote(quoteLines));
                    continue;
                }

                if (TryListItem(raw, out _, out _, out _, out _, out _))
                {
                    var items = new List<ListItem>();
                    while (index < lines.Length && TryListItem(lines[index], out var indent, out var ordered, out var marker, out var task, out var text))
                    {
                        items.Add(new ListItem { Indent = indent, Ordered = ordered, Marker = marker, Task = task, Text = text });
                        index++;
                    }
                    panel.Children.Add(BuildList(items));
                    continue;
                }

                // 문단
                var paragraph = new List<string>();
                while (index < lines.Length)
                {
                    var line = lines[index];
                    var t = line.TrimStart();

                    if (t.Length == 0
                        || IsFenceOpen(t, out _, out _)
                        || IsHorizontalRule(t)
                        || TryHeading(t, out _, out _)
                        || t.StartsWith(">", StringComparison.Ordinal)
                        || TryListItem(line, out _, out _, out _, out _, out _)
                        || (t.Contains('|') && index + 1 < lines.Length && IsTableSeparator(lines[index + 1].TrimStart())))
                        break;

                    paragraph.Add(line);
                    index++;
                }
                panel.Children.Add(BuildParagraph(paragraph));
            }

            return panel;
        }

        #endregion


        #region Block builders

        private static UIElement BuildParagraph(IReadOnlyList<string> lines)
        {
            var textBlock = CreateBodyTextBlock();
            AppendLinesWithBreaks(textBlock, lines);
            textBlock.Margin = new Thickness(0, 0, 0, 8);
            return textBlock;
        }

        private static UIElement BuildHeading(int level, string text)
        {
            double size = level switch
            {
                1 => 22,
                2 => 19,
                3 => 16.5,
                4 => 15,
                _ => 14
            };

            var textBlock = CreateBodyTextBlock();
            textBlock.FontSize = size;
            textBlock.FontWeight = FontWeights.SemiBold;
            textBlock.Margin = new Thickness(0, level <= 2 ? 10 : 6, 0, 4);
            AppendInlines(textBlock, text);
            return textBlock;
        }

        private static UIElement BuildList(IReadOnlyList<ListItem> items)
        {
            var panel = new StackPanel { Margin = new Thickness(2, 0, 0, 8) };

            // 순서 목록 시작 번호는 첫 항목의 마커에서 가져온다.
            var number = 1;
            var firstOrdered = items.FirstOrDefault(i => i.Ordered);
            if (firstOrdered.Ordered && int.TryParse(firstOrdered.Marker.TrimEnd('.'), out var startNo))
                number = startNo;

            foreach (var item in items)
            {
                var indentLevel = Math.Min(item.Indent / 2, 6);

                var grid = new Grid { Margin = new Thickness(indentLevel * 18, 1, 0, 1) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                FrameworkElement bullet;
                if (item.Task.HasValue)
                {
                    bullet = new TextBlock
                    {
                        Text = item.Task.Value ? "" : "",
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 12,
                        Foreground = item.Task.Value ? GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00)) : GetBrush("SecondaryForegroundBrush", Color.FromRgb(0x9E, 0xA2, 0xAE)),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 1, 8, 0)
                    };
                }
                else
                {
                    var bulletBlock = CreateBodyTextBlock();
                    bulletBlock.Text = item.Ordered ? $"{number++}." : "•";
                    bulletBlock.Foreground = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00));
                    bulletBlock.HorizontalAlignment = HorizontalAlignment.Right;
                    bulletBlock.Margin = new Thickness(0, 0, 8, 0);
                    bullet = bulletBlock;
                }
                Grid.SetColumn(bullet, 0);

                var content = CreateBodyTextBlock();
                AppendInlines(content, item.Text);
                Grid.SetColumn(content, 1);

                grid.Children.Add(bullet);
                grid.Children.Add(content);
                panel.Children.Add(grid);
            }

            return panel;
        }

        private static UIElement BuildBlockquote(IReadOnlyList<string> lines)
        {
            var textBlock = CreateBodyTextBlock();
            textBlock.Foreground = GetBrush("SecondaryForegroundBrush", Color.FromRgb(0x9E, 0xA2, 0xAE));
            AppendLinesWithBreaks(textBlock, lines);

            return new Border
            {
                BorderBrush = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00)),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Background = GetBrush("InlineCodeBackgroundBrush", Color.FromRgb(0x2A, 0x2A, 0x2B)),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 2, 0, 8),
                Child = textBlock
            };
        }

        private static UIElement BuildRule()
        {
            return new Border
            {
                Height = 1,
                Background = GetBrush("BorderBrush", Color.FromRgb(0x3A, 0x3A, 0x3C)),
                Margin = new Thickness(0, 8, 0, 8)
            };
        }

        private static UIElement BuildTable(IReadOnlyList<string> headers, IReadOnlyList<TextAlignment> alignments, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            var grid = new Grid();
            var columns = headers.Count;
            for (var c = 0; c < columns; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.RowDefinitions.Add(new RowDefinition());
            for (var r = 0; r < rows.Count; r++)
                grid.RowDefinitions.Add(new RowDefinition());

            var border = GetBrush("BorderBrush", Color.FromRgb(0x3A, 0x3A, 0x3C));
            var headerBg = GetBrush("CodeHeaderBackgroundBrush", Color.FromRgb(0x22, 0x22, 0x24));

            Border MakeCell(string text, int col, int row, bool header)
            {
                var tb = CreateBodyTextBlock();
                tb.TextAlignment = col < alignments.Count ? alignments[col] : TextAlignment.Left;
                if (header)
                    tb.FontWeight = FontWeights.SemiBold;
                AppendInlines(tb, text);

                var cell = new Border
                {
                    BorderBrush = border,
                    BorderThickness = new Thickness(0.5),
                    Background = header ? headerBg : Brushes.Transparent,
                    Padding = new Thickness(10, 6, 10, 6),
                    Child = tb
                };
                Grid.SetColumn(cell, col);
                Grid.SetRow(cell, row);
                return cell;
            }

            for (var c = 0; c < columns; c++)
                grid.Children.Add(MakeCell(c < headers.Count ? headers[c] : string.Empty, c, 0, true));

            for (var r = 0; r < rows.Count; r++)
            {
                for (var c = 0; c < columns; c++)
                {
                    var cellText = c < rows[r].Count ? rows[r][c] : string.Empty;
                    grid.Children.Add(MakeCell(cellText, c, r + 1, false));
                }
            }

            return new Border
            {
                BorderBrush = border,
                BorderThickness = new Thickness(0.5),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 2, 0, 10),
                ClipToBounds = true,
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = grid
                }
            };
        }

        private static UIElement BuildCodeBlock(string language, string code)
        {
            code = code ?? string.Empty;

            var container = new Border
            {
                Background = GetBrush("CodeBlockBackgroundBrush", Color.FromRgb(0x18, 0x18, 0x19)),
                BorderBrush = GetBrush("BorderBrush", Color.FromRgb(0x3A, 0x3A, 0x3C)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 2, 0, 10)
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Background = GetBrush("CodeHeaderBackgroundBrush", Color.FromRgb(0x22, 0x22, 0x24)) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var langLabel = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(language) ? "code" : language.ToLowerInvariant(),
                Foreground = GetBrush("SecondaryForegroundBrush", Color.FromRgb(0x9E, 0xA2, 0xAE)),
                FontSize = 11.5,
                Margin = new Thickness(12, 5, 8, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(langLabel, 0);

            var copyButton = new Button
            {
                Content = "복사",
                Margin = new Thickness(0, 3, 6, 3),
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 11.5,
                Cursor = System.Windows.Input.Cursors.Hand,
                Style = FindStyle("SubtleButtonStyle")
            };
            copyButton.Click += (_, __) =>
            {
                try
                {
                    Clipboard.SetText(code);
                    copyButton.Content = "복사됨";
                }
                catch
                {
                }
            };
            Grid.SetColumn(copyButton, 1);

            header.Children.Add(langLabel);
            header.Children.Add(copyButton);
            Grid.SetRow(header, 0);

            var codeView = BuildCodeView(code, language);
            Grid.SetRow(codeView, 1);

            root.Children.Add(header);
            root.Children.Add(codeView);
            container.Child = root;
            return container;
        }

        private static FrameworkElement BuildCodeView(string code, string language)
        {
            var richTextBox = new RichTextBox
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
                SelectionBrush = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00))
            };

            var document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = MonoFont,
                FontSize = 12.5,
                Foreground = richTextBox.Foreground
            };

            var lines = code.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var maxLen = lines.Length == 0 ? 0 : lines.Max(l => l.Length);
            document.PageWidth = Math.Max(80, maxLen * 7.4 + 28); // 줄바꿈 방지용 폭(가로 스크롤 유도)

            var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 18 };

            if (code.Length > MaxHighlightLength)
            {
                // 너무 크면 강조 없이(성능).
                for (var i = 0; i < lines.Length; i++)
                {
                    paragraph.Inlines.Add(new Run(lines[i]));
                    if (i < lines.Length - 1)
                        paragraph.Inlines.Add(new LineBreak());
                }
            }
            else
            {
                var tokenLines = CodeHighlighter.Tokenize(code, language);
                for (var i = 0; i < tokenLines.Count; i++)
                {
                    foreach (var token in tokenLines[i])
                        paragraph.Inlines.Add(new Run(token.Text) { Foreground = token.Brush });

                    if (i < tokenLines.Count - 1)
                        paragraph.Inlines.Add(new LineBreak());
                }
            }

            document.Blocks.Add(paragraph);
            richTextBox.Document = document;
            return richTextBox;
        }

        #endregion


        #region Inline building

        private static void AppendLinesWithBreaks(TextBlock textBlock, IReadOnlyList<string> lines)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                AppendInlines(textBlock, lines[i].TrimEnd());
                if (i < lines.Count - 1)
                    textBlock.Inlines.Add(new LineBreak());
            }
        }

        private static void AppendInlines(TextBlock textBlock, string text)
        {
            foreach (var inline in ParseInlines(text ?? string.Empty))
                textBlock.Inlines.Add(inline);
        }

        private static IEnumerable<Inline> ParseInlines(string text)
        {
            var result = new List<Inline>();

            if (string.IsNullOrEmpty(text))
            {
                result.Add(new Run(string.Empty));
                return result;
            }

            // 지나치게 긴 줄은 인라인 파싱을 건너뛴다(정규식 백트래킹 방지).
            if (text.Length > MaxInlineLength)
            {
                result.Add(new Run(text));
                return result;
            }

            MatchCollection matches;
            try
            {
                matches = InlineRegex.Matches(text);
            }
            catch (RegexMatchTimeoutException)
            {
                result.Add(new Run(text));
                return result;
            }

            var position = 0;

            foreach (Match match in matches)
            {
                if (match.Index > position)
                    result.Add(new Run(text.Substring(position, match.Index - position)));

                if (match.Groups["code"].Success)
                {
                    result.Add(CreateInlineCode(match.Groups["codebody"].Value));
                }
                else if (match.Groups["ltext"].Success)
                {
                    result.Add(CreateLink(match.Groups["ltext"].Value, match.Groups["lurl"].Value));
                }
                else if (match.Groups["bold"].Success || match.Groups["bold2"].Success)
                {
                    var inner = match.Groups["bold"].Success ? match.Groups["bold"].Value : match.Groups["bold2"].Value;
                    result.Add(new Bold(BuildSpanContent(inner)));
                }
                else if (match.Groups["strike"].Success)
                {
                    var span = new Span(BuildSpanContent(match.Groups["strike"].Value))
                    {
                        TextDecorations = TextDecorations.Strikethrough
                    };
                    result.Add(span);
                }
                else if (match.Groups["italic"].Success || match.Groups["italic2"].Success)
                {
                    var inner = match.Groups["italic"].Success ? match.Groups["italic"].Value : match.Groups["italic2"].Value;
                    result.Add(new Italic(BuildSpanContent(inner)));
                }

                position = match.Index + match.Length;
            }

            if (position < text.Length)
                result.Add(new Run(text.Substring(position)));

            if (result.Count == 0)
                result.Add(new Run(text));

            return result;
        }

        private static Inline BuildSpanContent(string inner)
        {
            var span = new Span();
            foreach (var inline in ParseInlines(inner))
                span.Inlines.Add(inline);
            return span;
        }

        private static Inline CreateInlineCode(string code)
        {
            return new Run(code)
            {
                FontFamily = MonoFont,
                FontSize = 12.5,
                Background = GetBrush("InlineCodeBackgroundBrush", Color.FromRgb(0x2A, 0x2A, 0x2B)),
                Foreground = GetBrush("InlineCodeForegroundBrush", Color.FromRgb(0xC5, 0xE1, 0x7A))
            };
        }

        private static Inline CreateLink(string text, string url)
        {
            var uri = TryCreateUri(url);

            if (uri == null)
            {
                // 주소를 만들 수 없으면 죽은 링크 대신 강조된 텍스트로.
                return new Run(text) { Foreground = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00)) };
            }

            var link = new Hyperlink(new Run(text))
            {
                Foreground = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00)),
                Cursor = System.Windows.Input.Cursors.Hand,
                NavigateUri = uri
            };

            link.RequestNavigate += (_, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
                }
                catch
                {
                }
                e.Handled = true;
            };

            return link;
        }

        #endregion


        #region Parsing helpers

        private struct ListItem
        {
            public int Indent;
            public bool Ordered;
            public string Marker;
            public bool? Task;   // null=목록, true=체크됨, false=체크안됨
            public string Text;
        }

        private static bool IsFenceOpen(string trimmed, out char fenceChar, out string language)
        {
            fenceChar = '`';
            language = null;

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
                fenceChar = '`';
            else if (trimmed.StartsWith("~~~", StringComparison.Ordinal))
                fenceChar = '~';
            else
                return false;

            var info = trimmed.TrimStart(fenceChar).Trim();
            language = info.Split(new[] { ' ', '\t' }, 2)[0];   // 첫 토큰만 언어로.
            return true;
        }

        private static bool IsFenceClose(string trimmed, char fenceChar)
        {
            var body = trimmed.Trim();
            return body.Length >= 3 && body.All(c => c == fenceChar);
        }

        private static bool IsHorizontalRule(string trimmed)
        {
            var compact = trimmed.Replace(" ", string.Empty);
            if (compact.Length < 3)
                return false;

            return compact.All(c => c == '-') || compact.All(c => c == '*') || compact.All(c => c == '_');
        }

        private static bool TryHeading(string trimmed, out int level, out string text)
        {
            level = 0;
            text = null;

            var hashes = 0;
            while (hashes < trimmed.Length && trimmed[hashes] == '#')
                hashes++;

            if (hashes < 1 || hashes > 6)
                return false;

            if (hashes >= trimmed.Length || trimmed[hashes] != ' ')
                return false;

            level = hashes;
            text = trimmed.Substring(hashes + 1).Trim().TrimEnd('#').TrimEnd();
            return true;
        }

        private static bool TryListItem(string raw, out int indent, out bool ordered, out string marker, out bool? task, out string text)
        {
            indent = 0;
            ordered = false;
            marker = null;
            task = null;
            text = null;

            var lead = 0;
            while (lead < raw.Length && (raw[lead] == ' ' || raw[lead] == '\t'))
                lead++;
            indent = lead;

            var trimmed = raw.Substring(lead);
            if (trimmed.Length < 2)
                return false;

            var c = trimmed[0];
            if ((c == '-' || c == '*' || c == '+') && trimmed[1] == ' ')
            {
                ordered = false;
                marker = c.ToString();
                text = trimmed.Substring(2).Trim();
            }
            else
            {
                var digits = 0;
                while (digits < trimmed.Length && char.IsDigit(trimmed[digits]))
                    digits++;

                if (digits > 0 && digits < trimmed.Length && trimmed[digits] == '.'
                    && digits + 1 < trimmed.Length && trimmed[digits + 1] == ' ')
                {
                    ordered = true;
                    marker = trimmed.Substring(0, digits + 1);
                    text = trimmed.Substring(digits + 2).Trim();
                }
                else
                {
                    return false;
                }
            }

            // 체크박스 목록: [ ] / [x]
            if (text.Length >= 3 && text[0] == '[' && text[2] == ']')
            {
                var mark = text[1];
                if (mark == ' ' || mark == 'x' || mark == 'X')
                {
                    task = mark != ' ';
                    text = text.Length > 3 ? text.Substring(3).Trim() : string.Empty;
                }
            }

            return true;
        }

        private static bool IsTableSeparator(string trimmed)
        {
            if (trimmed.Contains('|') == false && trimmed.Contains('-') == false)
                return false;

            var body = trimmed.Trim();
            if (body.Contains('-') == false)
                return false;

            foreach (var ch in body)
            {
                if (ch != '-' && ch != ':' && ch != '|' && ch != ' ')
                    return false;
            }
            return true;
        }

        private static UIElement ParseTable(string[] lines, ref int index)
        {
            var headerCells = SplitTableRow(lines[index]);
            var separatorCells = SplitTableRow(lines[index + 1]);

            var alignments = separatorCells.Select(s =>
            {
                var t = s.Trim();
                var left = t.StartsWith(":");
                var right = t.EndsWith(":");
                if (left && right) return TextAlignment.Center;
                if (right) return TextAlignment.Right;
                return TextAlignment.Left;
            }).ToList();

            index += 2;

            var rows = new List<IReadOnlyList<string>>();
            while (index < lines.Length)
            {
                var t = lines[index].TrimStart();
                if (t.Length == 0 || t.Contains('|') == false)
                    break;

                rows.Add(SplitTableRow(lines[index]));
                index++;
            }

            return BuildTable(headerCells, alignments, rows);
        }

        private static List<string> SplitTableRow(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("|"))
                trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("|"))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            var cells = new List<string>();
            var sb = new StringBuilder();
            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
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
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1);
                if (trimmed.StartsWith(" ", StringComparison.Ordinal))
                    trimmed = trimmed.Substring(1);
            }
            return trimmed;
        }

        private static Uri TryCreateUri(string url)
        {
            url = (url ?? string.Empty).Trim();
            if (url.Length == 0)
                return null;

            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
                return absolute;

            // 스킴이 없으면 https 를 붙여 시도(bare 도메인 지원).
            if (url.Contains("://") == false && Uri.TryCreate("https://" + url, UriKind.Absolute, out var https))
                return https;

            return null;
        }

        #endregion


        #region Element helpers

        private static TextBlock CreateBodyTextBlock()
        {
            return new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = GetBrush("ForegroundBrush", Color.FromRgb(0xF2, 0xF2, 0xF3)),
                LineHeight = 21,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
        }

        private static Brush GetBrush(string key, Color fallback)
        {
            if (Application.Current?.Resources[key] is Brush brush)
                return brush;

            var solid = new SolidColorBrush(fallback);
            solid.Freeze();
            return solid;
        }

        private static Style FindStyle(string key)
        {
            return Application.Current?.Resources[key] as Style;
        }

        #endregion
    }
}
