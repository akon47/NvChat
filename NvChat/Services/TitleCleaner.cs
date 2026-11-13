using System.Text.RegularExpressions;

namespace NvChat.Services;

/// <summary>
/// 모델이 만든 대화 제목을 다듬는다. 작은 모델은 제목 대신 답변·코드·마크다운을 돌려주는 일이 잦아,
/// 쓸 만한 한 줄만 골라내고 그렇지 못하면 null 을 돌려 기존(첫 메시지 요약) 제목을 유지하게 한다.
/// </summary>
internal static partial class TitleCleaner
{
    public const int MaxLength = 40;

    [GeneratedRegex(@"^\s*(title|subject|제목|대화\s*제목)\s*[:：\-]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPrefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string text = raw;

        // 추론 흔적: 닫힌 블록은 걷어내고, 닫히지 않은 채 잘린 응답은 제목이 아니므로 버린다.
        int close = text.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (close >= 0) text = text[(close + "</think>".Length)..];
        else if (text.Contains("<think>", StringComparison.OrdinalIgnoreCase)) return null;

        // 첫 번째 의미 있는 줄만 쓴다.
        string? line = text.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("```", StringComparison.Ordinal));
        if (line is null) return null;

        // 마크다운 서식 기호 제거. (예: "## 제목", "**제목** 설명", "- 제목", "> 제목")
        line = line.TrimStart('#', '>', '-', '+', ' ', '\t');
        line = line.Replace("**", "").Replace("__", "").Replace("`", "");
        line = LabelPrefix().Replace(line, "");
        line = Whitespace().Replace(line, " ").Trim();
        line = line.Trim('"', '\'', '「', '」', '『', '』', '“', '”', '‘', '’', '*', '_', ' ').TrimEnd('.', '。', ':', '：', ' ');

        if (line.Length == 0 || LooksLikeCode(line)) return null;

        return line.Length > MaxLength ? line[..MaxLength].TrimEnd() + "…" : line;
    }

    /// <summary>제목이 아니라 코드/마크업 조각으로 보이는지.</summary>
    private static bool LooksLikeCode(string line)
    {
        if (line.Contains("\"\"\"") || line.Contains("=>") || line.Contains("();") || line.Contains("</")) return true;
        if (line.StartsWith("def ", StringComparison.Ordinal) || line.StartsWith("function ", StringComparison.Ordinal) || line.StartsWith("class ", StringComparison.Ordinal)
            || line.StartsWith("import ", StringComparison.Ordinal) || line.StartsWith("#include", StringComparison.Ordinal) || line.StartsWith("<", StringComparison.Ordinal))
            return true;

        // "name(args):" / "name(args) {" 꼴
        int open = line.IndexOf('(');
        int closeParen = line.IndexOf(')');
        return open > 0 && closeParen > open && (line.EndsWith(':') || line.EndsWith('{') || line.EndsWith(';'));
    }
}
