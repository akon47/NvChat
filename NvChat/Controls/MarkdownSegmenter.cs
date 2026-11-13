namespace NvChat.Controls;

/// <summary>마크다운 최상위 블록의 종류.</summary>
internal enum SegmentKind
{
    Paragraph,
    Heading,
    Code,
    Rule,
    Table,
    Quote,
    List,
    Math,
}

/// <summary>
/// 마크다운의 최상위 블록 하나. 원문 줄을 그대로 들고 있어 <see cref="Key"/> 가 같으면 렌더 결과도 같다 —
/// MarkdownPresenter 가 이 키로 이전 렌더와 비교해, 스트리밍 중에는 바뀐 꼬리 블록만 다시 만든다.
/// </summary>
internal sealed record MarkdownSegment(SegmentKind Kind, IReadOnlyList<string> Lines, string? Language = null, int Level = 0)
{
    private string? _key;

    public string Key => _key ??= $"{Kind}|{Language}|{Level}\n{string.Join('\n', Lines)}";
}

/// <summary>
/// 마크다운 문자열을 최상위 블록으로 나눈다. WPF 에 의존하지 않는 순수 로직이라 단위 테스트가 가능하다.
/// 블록을 실제 요소로 만드는 일은 <see cref="MarkdownRenderer"/> 가 한다.
/// </summary>
internal static class MarkdownSegmenter
{
    public static List<MarkdownSegment> Split(string? markdown)
    {
        List<MarkdownSegment> segments = [];
        if (string.IsNullOrEmpty(markdown)) return segments;

        string[] lines = markdown.ReplaceLineEndings("\n").Split('\n');
        int index = 0;

        while (index < lines.Length)
        {
            string raw = lines[index];
            string trimmed = raw.TrimStart();

            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            if (IsFenceOpen(trimmed, out char fenceChar, out int fenceLength, out string? language))
            {
                index++;
                segments.Add(new MarkdownSegment(SegmentKind.Code, ReadFence(lines, ref index, fenceChar, fenceLength, stripIndent: raw.Length - trimmed.Length), language));
                continue;
            }

            if (IsMathOpen(trimmed, out string mathClose))
            {
                segments.Add(new MarkdownSegment(SegmentKind.Math, ReadMath(lines, ref index, mathClose)));
                continue;
            }

            if (IsHorizontalRule(trimmed))
            {
                segments.Add(new MarkdownSegment(SegmentKind.Rule, []));
                index++;
                continue;
            }

            if (TryHeading(trimmed, out int level, out string headingText))
            {
                segments.Add(new MarkdownSegment(SegmentKind.Heading, [headingText], Level: level));
                index++;
                continue;
            }

            if (IsTableStart(lines, index))
            {
                List<string> tableLines = [lines[index], lines[index + 1]];
                index += 2;
                while (index < lines.Length && lines[index].TrimStart() is { Length: > 0 } row && row.Contains('|'))
                {
                    tableLines.Add(lines[index]);
                    index++;
                }
                segments.Add(new MarkdownSegment(SegmentKind.Table, tableLines));
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
                segments.Add(new MarkdownSegment(SegmentKind.Quote, quoteLines));
                continue;
            }

            if (TryListItem(raw, out _))
            {
                segments.Add(new MarkdownSegment(SegmentKind.List, ReadList(lines, ref index)));
                continue;
            }

            // 밑줄식(setext) 헤딩: 다음 줄이 === 로만 이루어져 있으면 제목이다. (--- 는 수평선과 겹치므로 === 만 처리한다)
            if (index + 1 < lines.Length && IsSetextUnderline(lines[index + 1].TrimStart()))
            {
                segments.Add(new MarkdownSegment(SegmentKind.Heading, [trimmed], Level: 1));
                index += 2;
                continue;
            }

            List<string> paragraph = [];
            while (index < lines.Length)
            {
                string line = lines[index];
                string t = line.TrimStart();

                if (t.Length == 0
                    || IsFenceOpen(t, out _, out _, out _)
                    || IsMathOpen(t, out _)
                    || IsHorizontalRule(t)
                    || TryHeading(t, out _, out _)
                    || t.StartsWith('>')
                    || TryListItem(line, out _)
                    || IsTableStart(lines, index))
                    break;

                // 다음 줄이 === 밑줄이면 이 줄은 제목이므로 문단에 넣지 않고 바깥 루프로 넘긴다.
                if (paragraph.Count > 0 && index + 1 < lines.Length && IsSetextUnderline(lines[index + 1].TrimStart())) break;

                paragraph.Add(line);
                index++;
            }

            if (paragraph.Count > 0) segments.Add(new MarkdownSegment(SegmentKind.Paragraph, paragraph));
            else index++; // 어떤 규칙에도 걸리지 않은 줄에서 멈추지 않도록
        }

        return segments;
    }

    // ===== 코드 펜스 =====

    public static bool IsFenceOpen(string trimmed, out char fenceChar, out int fenceLength, out string? language)
    {
        fenceChar = '`';
        fenceLength = 0;
        language = null;

        if (trimmed.StartsWith("```", StringComparison.Ordinal)) fenceChar = '`';
        else if (trimmed.StartsWith("~~~", StringComparison.Ordinal)) fenceChar = '~';
        else return false;

        while (fenceLength < trimmed.Length && trimmed[fenceLength] == fenceChar) fenceLength++;

        string info = trimmed[fenceLength..].Trim();
        if (fenceChar == '`' && info.Contains('`')) return false; // "```code```" 처럼 한 줄에 닫히는 것은 인라인 코드다
        language = info.Split([' ', '\t', '{', ','], 2)[0];
        return true;
    }

    /// <summary>닫는 펜스: 여는 펜스와 같은 문자로, 그 이상 길이로만 이루어진 줄.</summary>
    public static bool IsFenceClose(string trimmed, char fenceChar, int fenceLength)
    {
        string body = trimmed.Trim();
        return body.Length >= Math.Max(3, fenceLength) && body.All(c => c == fenceChar);
    }

    /// <summary>여는 펜스 다음 줄부터 닫는 펜스(또는 끝)까지의 코드 줄을 읽는다. 닫는 펜스는 소비한다.</summary>
    private static List<string> ReadFence(string[] lines, ref int index, char fenceChar, int fenceLength, int stripIndent)
    {
        List<string> code = [];
        while (index < lines.Length && !IsFenceClose(lines[index].TrimStart(), fenceChar, fenceLength))
        {
            code.Add(StripLeading(lines[index], stripIndent));
            index++;
        }
        if (index < lines.Length) index++;

        while (code.Count > 0 && code[^1].Length == 0) code.RemoveAt(code.Count - 1);
        return code;
    }

    /// <summary>목록 안의 코드 블록처럼 펜스가 들여쓰여 있으면 그만큼의 앞 공백을 코드에서도 떼어 낸다.</summary>
    private static string StripLeading(string line, int count)
    {
        int strip = 0;
        while (strip < count && strip < line.Length && line[strip] == ' ') strip++;
        return line[strip..];
    }

    // ===== 수식 블록 =====

    /// <summary>블록 수식의 시작(<c>$$</c> 또는 <c>\[</c>)인지. 닫는 구분자를 돌려준다.</summary>
    public static bool IsMathOpen(string trimmed, out string close)
    {
        close = "";
        if (trimmed.StartsWith("$$", StringComparison.Ordinal)) close = "$$";
        else if (trimmed.StartsWith(@"\[", StringComparison.Ordinal)) close = @"\]";
        else return false;
        return true;
    }

    /// <summary>블록 수식의 본문 줄(구분자 제외)을 읽는다. 한 줄짜리(<c>$$ x $$</c>)와 여러 줄 모두 처리한다.</summary>
    private static List<string> ReadMath(string[] lines, ref int index, string close)
    {
        List<string> body = [];
        string first = lines[index].TrimStart()[2..];
        index++;

        int end = first.IndexOf(close, StringComparison.Ordinal);
        if (end >= 0)
        {
            body.Add(first[..end]);
            return body;
        }

        if (first.Trim().Length > 0) body.Add(first);
        while (index < lines.Length)
        {
            string line = lines[index];
            index++;

            end = line.IndexOf(close, StringComparison.Ordinal);
            if (end >= 0)
            {
                if (line[..end].Trim().Length > 0) body.Add(line[..end]);
                break;
            }
            body.Add(line);
        }
        return body;
    }

    // ===== 목록 =====

    /// <summary>목록 한 항목의 머리.</summary>
    public readonly record struct ListItemInfo(int Indent, bool Ordered, int Number, bool? Task, string Text);

    public static bool TryListItem(string raw, out ListItemInfo item)
    {
        item = default;

        int lead = 0;
        while (lead < raw.Length && raw[lead] is ' ' or '\t') lead++;

        string trimmed = raw[lead..];
        if (trimmed.Length < 2) return false;

        bool ordered;
        int number = 0;
        string text;

        char c = trimmed[0];
        if (c is '-' or '*' or '+' && trimmed[1] == ' ')
        {
            ordered = false;
            text = trimmed[2..].Trim();
        }
        else
        {
            int digits = 0;
            while (digits < trimmed.Length && digits < 9 && char.IsAsciiDigit(trimmed[digits])) digits++;

            if (digits > 0 && digits + 1 < trimmed.Length && trimmed[digits] is '.' or ')' && trimmed[digits + 1] == ' ')
            {
                ordered = true;
                number = int.Parse(trimmed[..digits]);
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

        // 탭은 네 칸으로 센다.
        int indent = raw[..lead].Sum(ch => ch == '\t' ? 4 : 1);
        item = new ListItemInfo(indent, ordered, number, task, text);
        return true;
    }

    /// <summary>
    /// 목록 블록의 줄들을 읽는다. 항목 줄뿐 아니라, 항목에 딸린 들여쓴 내용(이어지는 문단·코드 블록)과
    /// 항목 사이의 빈 줄(느슨한 목록)도 같은 목록으로 묶는다. 그래야 중간에 코드가 낀 번호 목록이 끊기지 않는다.
    /// </summary>
    private static List<string> ReadList(string[] lines, ref int index)
    {
        List<string> result = [];

        while (index < lines.Length)
        {
            string line = lines[index];
            string trimmed = line.TrimStart();
            int indent = line.Length - trimmed.Length;

            if (TryListItem(line, out _))
            {
                result.Add(line);
                index++;
                continue;
            }

            if (trimmed.Length == 0)
            {
                // 빈 줄: 그 뒤에 목록이 이어지거나 들여쓴 내용이 오면 목록의 일부다.
                int next = index + 1;
                while (next < lines.Length && lines[next].Trim().Length == 0) next++;
                if (next >= lines.Length) break;

                string after = lines[next];
                bool continues = TryListItem(after, out _) || (after.Length - after.TrimStart().Length >= 2);
                if (!continues) break;

                result.Add("");
                index = next;
                continue;
            }

            if (indent < 2) break; // 들여쓰지 않은 일반 줄은 목록 밖이다

            // 들여쓴 코드 펜스는 닫힐 때까지 통째로 가져온다(안의 줄이 목록 항목처럼 보여도 코드다).
            if (IsFenceOpen(trimmed, out char fenceChar, out int fenceLength, out _))
            {
                result.Add(line);
                index++;
                while (index < lines.Length)
                {
                    string codeLine = lines[index];
                    result.Add(codeLine);
                    index++;
                    if (IsFenceClose(codeLine.TrimStart(), fenceChar, fenceLength)) break;
                }
                continue;
            }

            result.Add(line);
            index++;
        }

        while (result.Count > 0 && result[^1].Length == 0) result.RemoveAt(result.Count - 1);
        return result;
    }

    // ===== 표 =====

    public static bool IsTableStart(string[] lines, int index) =>
        lines[index].Contains('|') && index + 1 < lines.Length && IsTableSeparator(lines[index + 1].TrimStart());

    public static bool IsTableSeparator(string trimmed)
    {
        string body = trimmed.Trim();
        return body.Contains('-') && body.Contains('|') && body.All(ch => ch is '-' or ':' or '|' or ' ');
    }

    public static List<string> SplitTableRow(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal)) trimmed = trimmed[..^1];

        List<string> cells = [];
        System.Text.StringBuilder sb = new();
        bool inCode = false;
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (c == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
            {
                sb.Append('|');
                i++;
                continue;
            }
            if (c == '`') inCode = !inCode;
            if (c == '|' && !inCode)
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

    // ===== 그 밖의 줄 판별 =====

    /// <summary>"====" 처럼 = 로만 이루어진 줄인지. (밑줄식 제목의 밑줄)</summary>
    public static bool IsSetextUnderline(string trimmed)
    {
        string body = trimmed.TrimEnd();
        return body.Length >= 2 && body.All(c => c == '=');
    }

    public static bool IsHorizontalRule(string trimmed)
    {
        string compact = trimmed.Replace(" ", "");
        return compact.Length >= 3 && (compact.All(c => c == '-') || compact.All(c => c == '*') || compact.All(c => c == '_'));
    }

    public static bool TryHeading(string trimmed, out int level, out string text)
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

    public static string StripQuoteMarker(string line)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith('>')) return trimmed;
        trimmed = trimmed[1..];
        return trimmed.StartsWith(' ') ? trimmed[1..] : trimmed;
    }
}
