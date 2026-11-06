using System.Text;
using System.Windows.Media;

namespace NvChat.Controls;

/// <summary>
/// 코드 문자열을 (텍스트, 색) 토큰 줄들로 나누는 경량 구문 강조기.
/// 완벽한 파서가 아니라 대표적인 토큰(키워드/문자열/주석/숫자)만 색칠한다.
/// </summary>
internal static class CodeHighlighter
{
    public readonly record struct Token(string Text, Brush Brush);

    // VS Code Dark 계열 색상.
    private static readonly Brush DefaultBrush = Frozen(0xE6, 0xE6, 0xE6);
    private static readonly Brush KeywordBrush = Frozen(0x56, 0x9C, 0xD6);
    private static readonly Brush StringBrush = Frozen(0xCE, 0x91, 0x78);
    private static readonly Brush CommentBrush = Frozen(0x6A, 0x99, 0x55);
    private static readonly Brush NumberBrush = Frozen(0xB5, 0xCE, 0xA8);

    // 여러 언어 공통 키워드 합집합
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract","and","as","assert","async","await","base","bool","break","byte","case","catch","char","class","const","continue",
        "def","default","del","delegate","do","double","elif","else","end","enum","event","except","export","extends","extern","false",
        "final","finally","float","fn","for","foreach","from","func","function","global","go","goto","if","impl","implements","import",
        "in","init","instanceof","int","interface","internal","is","lambda","let","lock","long","match","module","mut","namespace","new",
        "nil","none","not","null","object","operator","or","out","override","package","params","pass","private","protected","public",
        "raise","readonly","record","ref","return","sbyte","sealed","select","self","short","sizeof","static","str","string","struct",
        "super","switch","template","then","this","throw","throws","trait","true","try","type","typedef","typeof","uint","ulong","union",
        "unsafe","use","using","var","virtual","void","volatile","when","where","while","with","yield",
    };

    private static readonly HashSet<string> HashCommentLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "py", "ruby", "rb", "sh", "bash", "shell", "zsh", "yaml", "yml", "toml", "ini", "r", "perl", "pl", "makefile", "make", "dockerfile", "conf", "cfg",
    };

    private static readonly HashSet<string> DashCommentLanguages = new(StringComparer.OrdinalIgnoreCase) { "sql", "lua", "haskell", "hs", "ada" };

    public static List<List<Token>> Tokenize(string code, string? language)
    {
        bool hashComment = HashCommentLanguages.Contains(language ?? "");
        bool dashComment = DashCommentLanguages.Contains(language ?? "");
        bool inBlockComment = false;

        List<List<Token>> result = [];
        foreach (string line in code.ReplaceLineEndings("\n").Split('\n'))
            result.Add(TokenizeLine(line, hashComment, dashComment, ref inBlockComment));
        return result;
    }

    private static List<Token> TokenizeLine(string line, bool hashComment, bool dashComment, ref bool inBlockComment)
    {
        List<Token> tokens = [];
        StringBuilder buffer = new();
        int i = 0;
        int n = line.Length;

        void FlushDefault()
        {
            if (buffer.Length == 0) return;
            tokens.Add(new Token(buffer.ToString(), DefaultBrush));
            buffer.Clear();
        }

        while (i < n)
        {
            if (inBlockComment)
            {
                int end = line.IndexOf("*/", i, StringComparison.Ordinal);
                if (end < 0)
                {
                    tokens.Add(new Token(line[i..], CommentBrush));
                    return tokens;
                }
                tokens.Add(new Token(line[i..(end + 2)], CommentBrush));
                i = end + 2;
                inBlockComment = false;
                continue;
            }

            char c = line[i];

            // 블록 주석 시작
            if (c == '/' && i + 1 < n && line[i + 1] == '*')
            {
                FlushDefault();
                int end = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    tokens.Add(new Token(line[i..], CommentBrush));
                    inBlockComment = true;
                    return tokens;
                }
                tokens.Add(new Token(line[i..(end + 2)], CommentBrush));
                i = end + 2;
                continue;
            }

            // 라인 주석
            if ((c == '/' && i + 1 < n && line[i + 1] == '/') || (hashComment && c == '#') || (dashComment && c == '-' && i + 1 < n && line[i + 1] == '-'))
            {
                FlushDefault();
                tokens.Add(new Token(line[i..], CommentBrush));
                return tokens;
            }

            // 문자열
            if (c is '"' or '\'' or '`')
            {
                FlushDefault();
                int start = i;
                char quote = c;
                i++;
                while (i < n)
                {
                    if (line[i] == '\\' && i + 1 < n) { i += 2; continue; }
                    if (line[i] == quote) { i++; break; }
                    i++;
                }
                tokens.Add(new Token(line[start..i], StringBrush));
                continue;
            }

            // 숫자
            if (char.IsDigit(c))
            {
                FlushDefault();
                int start = i;
                while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] is '.' or '_')) i++;
                tokens.Add(new Token(line[start..i], NumberBrush));
                continue;
            }

            // 식별자/키워드
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;

                string word = line[start..i];
                if (Keywords.Contains(word))
                {
                    FlushDefault();
                    tokens.Add(new Token(word, KeywordBrush));
                }
                else
                {
                    buffer.Append(word);
                }
                continue;
            }

            buffer.Append(c);
            i++;
        }

        FlushDefault();
        return tokens;
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        SolidColorBrush brush = new(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
