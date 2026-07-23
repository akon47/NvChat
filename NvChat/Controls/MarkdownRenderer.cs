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
    /// 경량 마크다운 → WPF FlowDocument 렌더러(외부 라이브러리 없음).
    /// 읽기 전용 RichTextBox 에 넣어 문단/목록/표/인용까지 드래그 선택·복사가 가능하다.
    /// 코드펜스(구문강조·복사 버튼)/인라인코드/헤딩/체크리스트/구분선/굵게·기울임·취소선·링크 지원.
    /// </summary>
    public static class MarkdownRenderer
    {
        #region Constants

        private static readonly FontFamily MonoFont = new FontFamily("Consolas, Cascadia Mono, Courier New");
        private static readonly FontFamily BodyFont = new FontFamily("Segoe UI, Malgun Gothic");
        private static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        private const int MaxInlineLength = 4000;
        private const int MaxHighlightLength = 20000;
        private const double BodyFontSize = 13;
        private const double ListIndent = 18;

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

        public static FlowDocument RenderDocument(string markdown)
        {
            var document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = BodyFont,
                FontSize = BodyFontSize,
                Foreground = GetBrush("ForegroundBrush", Color.FromRgb(0xF2, 0xF2, 0xF3))
            };

            if (string.IsNullOrEmpty(markdown))
                return document;

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

                    document.Blocks.Add(new BlockUIContainer(BuildCodeBlock(language, code.ToString().TrimEnd('\n')))
                    {
                        Margin = new Thickness(0, 2, 0, 10)
                    });
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

                if (TryHeading(trimmed, out var level, out var headingText))
                {
                    document.Blocks.Add(BuildHeading(level, headingText));
                    index++;
                    continue;
                }

                if (trimmed.Contains('|') && index + 1 < lines.Length && IsTableSeparator(lines[index + 1].TrimStart()))
                {
                    var table = ParseTable(lines, ref index);
                    if (table != null)
                    {
                        document.Blocks.Add(table);
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
                    document.Blocks.Add(BuildBlockquote(quoteLines));
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

                    foreach (var block in BuildList(items))
                        document.Blocks.Add(block);

                    continue;
                }

                // 밑줄식(setext) 헤딩: 다음 줄이 === 로만 이루어져 있으면 제목이다.
                // (--- 는 수평선과 겹치므로 === 만 처리한다)
                if (index + 1 < lines.Length && IsSetextUnderline(lines[index + 1].TrimStart()))
                {
                    document.Blocks.Add(BuildHeading(1, trimmed));
                    index += 2;
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

                    // 다음 줄이 === 밑줄이면 이 줄은 제목이므로 문단에 넣지 않고 바깥 루프로 넘긴다.
                    if (index + 1 < lines.Length && IsSetextUnderline(lines[index + 1].TrimStart()))
                        break;

                    paragraph.Add(line);
                    index++;
                }

                if (paragraph.Count > 0)
                    document.Blocks.Add(BuildParagraph(paragraph));
            }

            // 마지막 블록의 하단 마진 제거(말풍선 하단 여백 방지)
            var last = document.Blocks.LastBlock;
            if (last != null)
            {
                var m = last.Margin;
                last.Margin = new Thickness(m.Left, m.Top, m.Right, 0);
            }

            return document;
        }

        #endregion


        #region Block builders

        private static Block BuildParagraph(IReadOnlyList<string> lines)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            AppendLinesWithBreaks(paragraph, lines);
            return paragraph;
        }

        private static Block BuildHeading(int level, string text)
        {
            double size = level switch
            {
                1 => 21,
                2 => 18,
                3 => 16,
                4 => 14.5,
                _ => 13.5
            };

            var paragraph = new Paragraph
            {
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, level <= 2 ? 10 : 6, 0, 4)
            };

            AppendInlines(paragraph, text);
            return paragraph;
        }

        private static IEnumerable<Block> BuildList(IReadOnlyList<ListItem> items)
        {
            var number = 1;
            var firstOrdered = items.FirstOrDefault(i => i.Ordered);
            if (firstOrdered.Ordered && int.TryParse(firstOrdered.Marker.TrimEnd('.'), out var startNo))
                number = startNo;

            var accent = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00));
            var muted = GetBrush("SecondaryForegroundBrush", Color.FromRgb(0x9E, 0xA2, 0xAE));
            var blocks = new List<Block>();

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var indentLevel = Math.Min(item.Indent / 2, 6);

                var paragraph = new Paragraph
                {
                    Margin = new Thickness(indentLevel * ListIndent + ListIndent, 1, 0, i == items.Count - 1 ? 8 : 1),
                    TextIndent = -ListIndent
                };

                if (item.Task.HasValue)
                {
                    paragraph.Inlines.Add(new Run(item.Task.Value ? "  " : "  ")
                    {
                        FontFamily = IconFont,
                        FontSize = 12,
                        Foreground = item.Task.Value ? accent : muted
                    });
                }
                else
                {
                    var marker = item.Ordered ? $"{number++}." : "•";
                    paragraph.Inlines.Add(new Run(marker + "  ") { Foreground = accent });
                }

                AppendInlines(paragraph, item.Text);
                blocks.Add(paragraph);
            }

            return blocks;
        }

        private static Block BuildBlockquote(IReadOnlyList<string> lines)
        {
            var paragraph = new Paragraph
            {
                Foreground = GetBrush("SecondaryForegroundBrush", Color.FromRgb(0x9E, 0xA2, 0xAE)),
                Background = GetBrush("InlineCodeBackgroundBrush", Color.FromRgb(0x2A, 0x2A, 0x2B)),
                BorderBrush = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00)),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 2, 0, 8)
            };

            AppendLinesWithBreaks(paragraph, lines);
            return paragraph;
        }

        private static Block BuildRule()
        {
            return new Paragraph
            {
                BorderBrush = GetBrush("BorderBrush", Color.FromRgb(0x3A, 0x3A, 0x3C)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 8, 0, 8),
                FontSize = 1
            };
        }

        private static Table BuildTable(IReadOnlyList<string> headers, IReadOnlyList<TextAlignment> alignments, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            var border = GetBrush("BorderBrush", Color.FromRgb(0x3A, 0x3A, 0x3C));
            var headerBg = GetBrush("CodeHeaderBackgroundBrush", Color.FromRgb(0x22, 0x22, 0x24));

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 10) };
            for (var c = 0; c < headers.Count; c++)
                table.Columns.Add(new TableColumn { Width = GridLength.Auto });

            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            TableCell MakeCell(string text, int col, bool header)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0),
                    TextAlignment = col < alignments.Count ? alignments[col] : TextAlignment.Left
                };

                if (header)
                    paragraph.FontWeight = FontWeights.SemiBold;

                AppendInlines(paragraph, text);

                return new TableCell(paragraph)
                {
                    BorderBrush = border,
                    BorderThickness = new Thickness(0.5),
                    Background = header ? headerBg : Brushes.Transparent,
                    Padding = new Thickness(10, 6, 10, 6)
                };
            }

            var headerRow = new TableRow();
            for (var c = 0; c < headers.Count; c++)
                headerRow.Cells.Add(MakeCell(headers[c], c, true));
            group.Rows.Add(headerRow);

            foreach (var row in rows)
            {
                var tableRow = new TableRow();
                for (var c = 0; c < headers.Count; c++)
                    tableRow.Cells.Add(MakeCell(c < row.Count ? row[c] : string.Empty, c, false));
                group.Rows.Add(tableRow);
            }

            return table;
        }

        private static FrameworkElement BuildCodeBlock(string language, string code)
        {
            code = code ?? string.Empty;

            var container = new Border
            {
                Background = GetBrush("CodeBlockBackgroundBrush", Color.FromRgb(0x18, 0x18, 0x19)),
                BorderBrush = GetBrush("BorderBrush", Color.FromRgb(0x3A, 0x3A, 0x3C)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
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
                Content = Localization.LocalizationManager.Instance["CodeCopy"],
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
                    copyButton.Content = Localization.LocalizationManager.Instance["CodeCopied"];
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
            document.PageWidth = Math.Max(80, maxLen * 7.4 + 28);

            var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 18 };

            if (code.Length > MaxHighlightLength)
            {
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

            // 코드 블록이 휠을 삼켜 바깥 대화 목록이 멈추지 않도록, 한계에 닿으면 부모로 넘긴다.
            Behaviors.NestedScroll.EnableWheelBubbling(richTextBox);

            return richTextBox;
        }

        #endregion


        #region Inline building

        private static void AppendLinesWithBreaks(Paragraph paragraph, IReadOnlyList<string> lines)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                AppendInlines(paragraph, lines[i].TrimEnd());
                if (i < lines.Count - 1)
                    paragraph.Inlines.Add(new LineBreak());
            }
        }

        private static void AppendInlines(Paragraph paragraph, string text)
        {
            foreach (var inline in ParseInlines(text ?? string.Empty))
                paragraph.Inlines.Add(inline);
        }

        private static IEnumerable<Inline> ParseInlines(string text)
        {
            var result = new List<Inline>();

            if (string.IsNullOrEmpty(text))
            {
                result.Add(new Run(string.Empty));
                return result;
            }

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
                    result.Add(new Span(BuildSpanContent(match.Groups["strike"].Value))
                    {
                        TextDecorations = TextDecorations.Strikethrough
                    });
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
                return new Run(text) { Foreground = GetBrush("AccentBrush", Color.FromRgb(0x76, 0xB9, 0x00)) };

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
            public bool? Task;
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
            language = info.Split(new[] { ' ', '\t' }, 2)[0];
            return true;
        }

        private static bool IsFenceClose(string trimmed, char fenceChar)
        {
            var body = trimmed.Trim();
            return body.Length >= 3 && body.All(c => c == fenceChar);
        }

        /// <summary>"====" 처럼 = 로만 이루어진 줄인지. (밑줄식 제목의 밑줄)</summary>
        private static bool IsSetextUnderline(string trimmed)
        {
            var body = trimmed.TrimEnd();
            return body.Length >= 2 && body.All(c => c == '=');
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

        private static Table ParseTable(string[] lines, ref int index)
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

                // 모델이 행 사이마다 구분선(|---|---|)을 넣는 경우가 있어 데이터 행으로 세지 않는다.
                if (IsTableSeparator(t) == false)
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

            if (url.Contains("://") == false && Uri.TryCreate("https://" + url, UriKind.Absolute, out var https))
                return https;

            return null;
        }

        #endregion


        #region Element helpers

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
