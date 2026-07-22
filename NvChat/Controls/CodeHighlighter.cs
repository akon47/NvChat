using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NvChat.Controls
{
    /// <summary>
    /// 코드 문자열을 (텍스트, 색) 토큰 줄들로 나누는 경량 구문 강조기.
    /// 완벽한 파서가 아니라 대표적인 토큰(키워드/문자열/주석/숫자)만 색칠한다.
    /// </summary>
    internal static class CodeHighlighter
    {
        public readonly struct Token
        {
            public Token(string text, Brush brush)
            {
                Text = text;
                Brush = brush;
            }

            public string Text { get; }
            public Brush Brush { get; }
        }

        // VS Code Dark 계열 색상.
        private static readonly Brush DefaultBrush = Frozen(0xE6, 0xE6, 0xE6);
        private static readonly Brush KeywordBrush = Frozen(0x56, 0x9C, 0xD6);
        private static readonly Brush StringBrush = Frozen(0xCE, 0x91, 0x78);
        private static readonly Brush CommentBrush = Frozen(0x6A, 0x99, 0x55);
        private static readonly Brush NumberBrush = Frozen(0xB5, 0xCE, 0xA8);

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            // 여러 언어 공통 키워드 합집합
            "abstract","and","as","assert","async","await","base","bool","break","byte","case","catch","char","class","const","continue",
            "def","default","del","delegate","do","double","elif","else","end","enum","event","except","export","extends","extern","false",
            "final","finally","float","fn","for","foreach","from","func","function","global","go","goto","if","impl","implements","import",
            "in","init","instanceof","int","interface","internal","is","lambda","let","lock","long","match","module","mut","namespace","new",
            "nil","none","not","null","object","operator","or","out","override","package","params","pass","private","protected","public",
            "raise","readonly","record","ref","return","sbyte","sealed","select","self","short","sizeof","static","str","string","struct",
            "super","switch","template","then","this","throw","throws","trait","true","try","type","typedef","typeof","uint","ulong","union",
            "unsafe","use","using","var","virtual","void","volatile","when","where","while","with","yield"
        };

        public static List<List<Token>> Tokenize(string code, string language)
        {
            var result = new List<List<Token>>();
            if (code == null)
                code = string.Empty;

            var hashComment = UsesHashComment(language);
            var dashComment = UsesDashComment(language);

            var lines = code.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var inBlockComment = false;

            foreach (var line in lines)
            {
                result.Add(TokenizeLine(line, hashComment, dashComment, ref inBlockComment));
            }

            return result;
        }

        private static List<Token> TokenizeLine(string line, bool hashComment, bool dashComment, ref bool inBlockComment)
        {
            var tokens = new List<Token>();
            var i = 0;
            var n = line.Length;
            var buffer = new System.Text.StringBuilder();

            void FlushDefault()
            {
                if (buffer.Length > 0)
                {
                    tokens.Add(new Token(buffer.ToString(), DefaultBrush));
                    buffer.Clear();
                }
            }

            while (i < n)
            {
                if (inBlockComment)
                {
                    var end = line.IndexOf("*/", i, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        tokens.Add(new Token(line.Substring(i), CommentBrush));
                        return tokens;
                    }

                    tokens.Add(new Token(line.Substring(i, end + 2 - i), CommentBrush));
                    i = end + 2;
                    inBlockComment = false;
                    continue;
                }

                var c = line[i];

                // 블록 주석 시작
                if (c == '/' && i + 1 < n && line[i + 1] == '*')
                {
                    FlushDefault();
                    var end = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        tokens.Add(new Token(line.Substring(i), CommentBrush));
                        inBlockComment = true;
                        return tokens;
                    }
                    tokens.Add(new Token(line.Substring(i, end + 2 - i), CommentBrush));
                    i = end + 2;
                    continue;
                }

                // 라인 주석
                if ((c == '/' && i + 1 < n && line[i + 1] == '/')
                    || (hashComment && c == '#')
                    || (dashComment && c == '-' && i + 1 < n && line[i + 1] == '-'))
                {
                    FlushDefault();
                    tokens.Add(new Token(line.Substring(i), CommentBrush));
                    return tokens;
                }

                // 문자열
                if (c == '"' || c == '\'' || c == '`')
                {
                    FlushDefault();
                    var start = i;
                    var quote = c;
                    i++;
                    while (i < n)
                    {
                        if (line[i] == '\\' && i + 1 < n)
                        {
                            i += 2;
                            continue;
                        }
                        if (line[i] == quote)
                        {
                            i++;
                            break;
                        }
                        i++;
                    }
                    tokens.Add(new Token(line.Substring(start, i - start), StringBrush));
                    continue;
                }

                // 숫자
                if (char.IsDigit(c))
                {
                    FlushDefault();
                    var start = i;
                    while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '.' || line[i] == '_'))
                        i++;
                    tokens.Add(new Token(line.Substring(start, i - start), NumberBrush));
                    continue;
                }

                // 식별자/키워드
                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                        i++;

                    var word = line.Substring(start, i - start);
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

        private static bool UsesHashComment(string language)
        {
            switch ((language ?? string.Empty).ToLowerInvariant())
            {
                case "python":
                case "py":
                case "ruby":
                case "rb":
                case "sh":
                case "bash":
                case "shell":
                case "zsh":
                case "yaml":
                case "yml":
                case "toml":
                case "ini":
                case "r":
                case "perl":
                case "pl":
                case "makefile":
                case "make":
                case "dockerfile":
                case "conf":
                case "cfg":
                    return true;
                default:
                    return false;
            }
        }

        private static bool UsesDashComment(string language)
        {
            switch ((language ?? string.Empty).ToLowerInvariant())
            {
                case "sql":
                case "lua":
                case "haskell":
                case "hs":
                case "ada":
                    return true;
                default:
                    return false;
            }
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
