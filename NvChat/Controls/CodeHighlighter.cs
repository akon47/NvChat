using System.Text;

namespace NvChat.Controls;

/// <summary>토큰의 종류. 색은 렌더러가 테마 리소스(SyntaxKeywordBrush 등)로 입힌다.</summary>
internal enum TokenKind
{
    Default,
    Keyword,
    String,
    Comment,
    Number,
}

/// <summary>
/// 코드 문자열을 (텍스트, 종류) 토큰 줄들로 나누는 경량 구문 강조기.
/// 완벽한 파서가 아니라 대표적인 토큰(키워드/문자열/주석/숫자)만 가린다. 다만 언어 계열은 구분한다 —
/// 주석 기호와 키워드는 언어마다 달라서, 모든 언어에 같은 규칙을 쓰면 셸 스크립트의 URL(<c>https://…</c>)이 주석으로,
/// JSON 의 <c>type</c>·<c>in</c> 이 키워드로 칠해진다. 언어 표시가 없는 블록(대개 출력·일반 텍스트)은 칠하지 않는다.
/// </summary>
internal static class CodeHighlighter
{
    public readonly record struct Token(string Text, TokenKind Kind);

    /// <summary>언어 계열별 규칙.</summary>
    private sealed record Profile(
        HashSet<string> Keywords,
        bool SlashComments = false,
        bool HashComments = false,
        bool DashComments = false,
        bool MarkupComments = false,
        bool BacktickStrings = false,
        bool TripleQuoteStrings = false,
        bool SemicolonComments = false);

    private static readonly HashSet<string> None = new(StringComparer.Ordinal);

    private static readonly HashSet<string> CLikeKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "auto", "await", "base", "bool", "boolean", "break", "byte", "case", "catch", "chan", "char", "class", "const", "constexpr", "continue",
        "crate", "data", "default", "defer", "delegate", "delete", "do", "double", "dyn", "else", "enum", "event", "explicit", "export", "extends", "extension", "extern",
        "false", "final", "finally", "float", "fn", "for", "foreach", "from", "fun", "func", "function", "get", "go", "goto", "guard", "if", "impl", "implements",
        "implicit", "import", "in", "init", "inline", "instanceof", "int", "interface", "internal", "is", "let", "lock", "long", "loop", "map", "match", "mod", "move", "mut",
        "namespace", "new", "nil", "null", "nullptr", "object", "of", "open", "operator", "out", "override", "package", "params", "private", "protected", "protocol", "pub",
        "public", "range", "readonly", "record", "ref", "return", "sealed", "select", "self", "Self", "set", "short", "sizeof", "static", "string", "struct", "super",
        "suspend", "switch", "template", "this", "throw", "throws", "trait", "true", "try", "type", "typedef", "typename", "typeof", "uint", "ulong", "undefined", "union",
        "unsafe", "use", "using", "val", "var", "virtual", "void", "volatile", "when", "where", "while", "yield",
    };

    private static readonly HashSet<string> PythonKeywords = new(StringComparer.Ordinal)
    {
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "case", "class", "continue", "def", "del", "elif", "else", "except", "finally", "for",
        "from", "global", "if", "import", "in", "is", "lambda", "match", "nonlocal", "not", "or", "pass", "raise", "return", "self", "try", "while", "with", "yield",
    };

    private static readonly HashSet<string> RubyKeywords = new(StringComparer.Ordinal)
    {
        "alias", "and", "begin", "break", "case", "class", "def", "do", "else", "elsif", "end", "ensure", "false", "for", "if", "in", "include", "module", "next", "nil",
        "not", "or", "puts", "require", "rescue", "return", "self", "super", "then", "true", "unless", "until", "when", "while", "yield",
    };

    private static readonly HashSet<string> ShellKeywords = new(StringComparer.Ordinal)
    {
        "alias", "break", "case", "cd", "continue", "do", "done", "echo", "elif", "else", "esac", "exit", "export", "fi", "for", "function", "if", "in", "local", "readonly",
        "return", "set", "source", "then", "unset", "until", "while",
    };

    private static readonly HashSet<string> PowerShellKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "begin", "break", "catch", "class", "continue", "do", "else", "elseif", "end", "exit", "filter", "finally", "for", "foreach", "function", "if", "in", "param",
        "process", "return", "switch", "throw", "trap", "try", "until", "using", "while",
    };

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "all", "alter", "and", "as", "asc", "begin", "between", "by", "case", "commit", "constraint", "create", "cross", "default", "delete", "desc", "distinct",
        "drop", "else", "end", "exists", "foreign", "from", "full", "group", "having", "if", "in", "index", "inner", "insert", "into", "is", "join", "key", "left", "like",
        "limit", "not", "null", "offset", "on", "or", "order", "outer", "primary", "references", "right", "rollback", "select", "set", "table", "then", "union", "unique",
        "update", "values", "view", "when", "where", "with",
    };

    private static readonly HashSet<string> LiteralKeywords = new(StringComparer.Ordinal) { "true", "false", "null", "yes", "no", "on", "off" };

    private static readonly Profile CLike = new(CLikeKeywords, SlashComments: true, BacktickStrings: true);
    private static readonly Profile Python = new(PythonKeywords, HashComments: true, TripleQuoteStrings: true);
    private static readonly Profile Ruby = new(RubyKeywords, HashComments: true);
    private static readonly Profile Shell = new(ShellKeywords, HashComments: true, BacktickStrings: true);
    private static readonly Profile PowerShell = new(PowerShellKeywords, HashComments: true);
    private static readonly Profile Sql = new(SqlKeywords, DashComments: true, BacktickStrings: true);
    private static readonly Profile Lua = new(CLikeKeywords, DashComments: true);
    private static readonly Profile Config = new(LiteralKeywords, HashComments: true);
    private static readonly Profile Ini = new(LiteralKeywords, HashComments: true, SemicolonComments: true);
    private static readonly Profile Json = new(new HashSet<string>(StringComparer.Ordinal) { "true", "false", "null" });
    private static readonly Profile Markup = new(None, MarkupComments: true);
    private static readonly Profile Css = new(None, SlashComments: true);

    private static readonly Dictionary<string, Profile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = Python, ["py"] = Python, ["python3"] = Python, ["ipython"] = Python,
        ["ruby"] = Ruby, ["rb"] = Ruby, ["elixir"] = Ruby, ["ex"] = Ruby, ["crystal"] = Ruby,
        ["sh"] = Shell, ["bash"] = Shell, ["shell"] = Shell, ["zsh"] = Shell, ["fish"] = Shell, ["console"] = Shell, ["makefile"] = Shell, ["make"] = Shell,
        ["dockerfile"] = Shell, ["docker"] = Shell,
        ["powershell"] = PowerShell, ["ps1"] = PowerShell, ["pwsh"] = PowerShell,
        ["sql"] = Sql, ["mysql"] = Sql, ["postgres"] = Sql, ["postgresql"] = Sql, ["sqlite"] = Sql, ["tsql"] = Sql, ["plsql"] = Sql,
        ["lua"] = Lua, ["haskell"] = Lua, ["hs"] = Lua, ["ada"] = Lua,
        ["yaml"] = Config, ["yml"] = Config, ["toml"] = Config, ["conf"] = Config, ["nginx"] = Config, ["properties"] = Config, ["env"] = Config, ["r"] = Config,
        ["perl"] = Config, ["pl"] = Config,
        ["ini"] = Ini, ["cfg"] = Ini,
        ["json"] = Json, ["jsonc"] = Json, ["json5"] = Json,
        ["html"] = Markup, ["xml"] = Markup, ["xaml"] = Markup, ["svg"] = Markup, ["vue"] = Markup, ["htm"] = Markup,
        ["css"] = Css, ["scss"] = Css, ["less"] = Css,
    };

    /// <summary>강조하지 않는 표시들. 언어 표시가 아예 없는 블록도 여기에 든다.</summary>
    private static readonly HashSet<string> PlainLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "text", "txt", "plain", "plaintext", "output", "log", "markdown", "md", "csv", "tsv", "diff", "patch", "http", "mermaid", "ascii", "tree", "none",
    };

    public static List<List<Token>> Tokenize(string code, string? language)
    {
        string[] lines = code.ReplaceLineEndings("\n").Split('\n');
        string tag = (language ?? "").Trim();

        if (PlainLanguages.Contains(tag)) return lines.Select(line => new List<Token> { new(line, TokenKind.Default) }).ToList();

        // 표에 없는 언어(zig, nim, …)는 가장 흔한 C 계열 규칙으로 본다.
        Profile profile = Profiles.TryGetValue(tag, out var known) ? known : CLike;

        List<List<Token>> result = [];
        LineState state = new();
        foreach (string line in lines) result.Add(TokenizeLine(line, profile, ref state));
        return result;
    }

    /// <summary>여러 줄에 걸치는 구조(블록 주석, 삼중 따옴표 문자열)의 진행 상태.</summary>
    private struct LineState
    {
        public string? BlockEnd;       // 지금 안에 있는 블록의 닫는 표시("*/", "-->", "\"\"\"" 등). 밖이면 null
        public TokenKind BlockKind;
    }

    private static List<Token> TokenizeLine(string line, Profile profile, ref LineState state)
    {
        List<Token> tokens = [];
        StringBuilder buffer = new();
        int i = 0;
        int n = line.Length;

        void FlushDefault()
        {
            if (buffer.Length == 0) return;
            tokens.Add(new Token(buffer.ToString(), TokenKind.Default));
            buffer.Clear();
        }

        while (i < n)
        {
            // 여러 줄 블록의 안쪽
            if (state.BlockEnd is { } blockEnd)
            {
                int end = line.IndexOf(blockEnd, i, StringComparison.Ordinal);
                if (end < 0)
                {
                    tokens.Add(new Token(line[i..], state.BlockKind));
                    return tokens;
                }
                tokens.Add(new Token(line[i..(end + blockEnd.Length)], state.BlockKind));
                i = end + blockEnd.Length;
                state.BlockEnd = null;
                continue;
            }

            char c = line[i];

            // 블록 주석: /* … */  ,  <!-- … -->
            if (profile.SlashComments && c == '/' && i + 1 < n && line[i + 1] == '*')
            {
                FlushDefault();
                OpenBlock(line, ref i, 2, "*/", TokenKind.Comment, tokens, ref state);
                continue;
            }
            if (profile.MarkupComments && c == '<' && line.AsSpan(i).StartsWith("<!--"))
            {
                FlushDefault();
                OpenBlock(line, ref i, 4, "-->", TokenKind.Comment, tokens, ref state);
                continue;
            }

            // 삼중 따옴표 문자열(파이썬 독스트링)
            if (profile.TripleQuoteStrings && c is '"' or '\'' && i + 2 < n && line[i + 1] == c && line[i + 2] == c)
            {
                FlushDefault();
                OpenBlock(line, ref i, 3, new string(c, 3), TokenKind.String, tokens, ref state);
                continue;
            }

            // 줄 주석
            if (IsLineCommentStart(line, i, profile))
            {
                FlushDefault();
                tokens.Add(new Token(line[i..], TokenKind.Comment));
                return tokens;
            }

            // 문자열
            if (c == '"' || (c == '`' && profile.BacktickStrings) || (c == '\'' && HasClosingQuote(line, i)))
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
                tokens.Add(new Token(line[start..i], TokenKind.String));
                continue;
            }

            // 숫자
            if (char.IsAsciiDigit(c))
            {
                FlushDefault();
                int start = i;
                while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] is '.' or '_')) i++;
                tokens.Add(new Token(line[start..i], TokenKind.Number));
                continue;
            }

            // 식별자/키워드
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;

                string word = line[start..i];
                if (profile.Keywords.Contains(word))
                {
                    FlushDefault();
                    tokens.Add(new Token(word, TokenKind.Keyword));
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

    /// <summary>블록(주석/문자열)을 연다. 같은 줄에서 닫히면 한 토큰으로, 아니면 줄 끝까지 칠하고 상태에 남긴다.</summary>
    private static void OpenBlock(string line, ref int i, int openLength, string close, TokenKind kind, List<Token> tokens, ref LineState state)
    {
        int end = line.IndexOf(close, i + openLength, StringComparison.Ordinal);
        if (end < 0)
        {
            tokens.Add(new Token(line[i..], kind));
            state.BlockEnd = close;
            state.BlockKind = kind;
            i = line.Length;
            return;
        }

        tokens.Add(new Token(line[i..(end + close.Length)], kind));
        i = end + close.Length;
    }

    private static bool IsLineCommentStart(string line, int i, Profile profile)
    {
        char c = line[i];
        char previous = i > 0 ? line[i - 1] : ' ';
        bool atBoundary = i == 0 || char.IsWhiteSpace(previous);

        // "//" 는 URL 의 "://" 가 아닐 때만 주석이다.
        if (profile.SlashComments && c == '/' && i + 1 < line.Length && line[i + 1] == '/' && previous != ':') return true;

        // "#" 과 ";" 은 줄 처음이나 공백 뒤에서만 주석이다($#, ${#var}, URL 의 #조각, 색상 코드 등은 아니다).
        if (profile.HashComments && c == '#' && atBoundary) return true;
        if (profile.SemicolonComments && c == ';' && atBoundary) return true;

        return profile.DashComments && c == '-' && i + 1 < line.Length && line[i + 1] == '-';
    }

    /// <summary>
    /// 작은따옴표는 같은 줄에 닫는 따옴표가 있을 때만 문자열의 시작으로 본다.
    /// (영어 축약형 don't, 러스트의 수명 'a 처럼 짝 없는 작은따옴표가 줄 끝까지 문자열로 칠해지는 것을 막는다)
    /// </summary>
    private static bool HasClosingQuote(string line, int start)
    {
        for (int i = start + 1; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '\'') return true;
        }
        return false;
    }
}
