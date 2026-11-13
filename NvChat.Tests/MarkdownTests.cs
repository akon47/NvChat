using NvChat.Controls;

namespace NvChat.Tests;

public class MarkdownSegmenterTests
{
    private static List<MarkdownSegment> Split(string markdown) => MarkdownSegmenter.Split(markdown);

    [Fact]
    public void EmptyInputYieldsNothing()
    {
        Assert.Empty(Split(""));
        Assert.Empty(Split("\n\n  \n"));
    }

    [Fact]
    public void SplitsBasicBlocks()
    {
        var segments = Split("# Title\n\nSome text\nmore text\n\n---\n\n> quote\n\n- a\n- b\n");
        Assert.Equal(
            [SegmentKind.Heading, SegmentKind.Paragraph, SegmentKind.Rule, SegmentKind.Quote, SegmentKind.List],
            segments.Select(s => s.Kind));
        Assert.Equal(1, segments[0].Level);
        Assert.Equal("Title", segments[0].Lines[0]);
        Assert.Equal(2, segments[1].Lines.Count);
    }

    [Fact]
    public void CodeFenceKeepsContentVerbatim()
    {
        var segments = Split("```python\n# not a heading\n- not a list\n\n| not | a table |\n```\nafter");
        Assert.Equal(SegmentKind.Code, segments[0].Kind);
        Assert.Equal("python", segments[0].Language);
        Assert.Equal(["# not a heading", "- not a list", "", "| not | a table |"], segments[0].Lines);
        Assert.Equal(SegmentKind.Paragraph, segments[1].Kind);
    }

    [Fact]
    public void UnterminatedFenceRunsToTheEnd()
    {
        // 스트리밍 중에는 닫는 펜스가 아직 오지 않았다.
        var segments = Split("text\n\n```js\nconst a = 1;\nconst b");
        Assert.Equal(2, segments.Count);
        Assert.Equal(SegmentKind.Code, segments[1].Kind);
        Assert.Equal(["const a = 1;", "const b"], segments[1].Lines);
    }

    [Fact]
    public void LongerFenceCanContainShorterFence()
    {
        var segments = Split("````md\n```js\nx\n```\n````\ndone");
        Assert.Equal(SegmentKind.Code, segments[0].Kind);
        Assert.Equal(["```js", "x", "```"], segments[0].Lines);
        Assert.Equal(SegmentKind.Paragraph, segments[1].Kind);
    }

    [Fact]
    public void ListKeepsIndentedCodeAndLooseItemsTogether()
    {
        const string markdown = "1. first\n\n   ```python\n   x = 1\n   - still code\n   ```\n2. second\n   continuation\n\n3. third\n\nparagraph after";
        var segments = Split(markdown);

        Assert.Equal([SegmentKind.List, SegmentKind.Paragraph], segments.Select(s => s.Kind));
        Assert.Contains("2. second", segments[0].Lines);
        Assert.Contains("3. third", segments[0].Lines);
        Assert.Equal("paragraph after", segments[1].Lines[0]);
    }

    [Fact]
    public void TableNeedsSeparatorRow()
    {
        var table = Split("| a | b |\n|---|:-:|\n| 1 | 2 |\n\nnot | a table");
        Assert.Equal(SegmentKind.Table, table[0].Kind);
        Assert.Equal(3, table[0].Lines.Count);
        Assert.Equal(SegmentKind.Paragraph, table[1].Kind);
    }

    [Fact]
    public void TableCellsCanContainPipesInCode()
    {
        Assert.Equal(["a `x | y`", "b"], MarkdownSegmenter.SplitTableRow("| a `x | y` | b |"));
        Assert.Equal(["a | b", "c"], MarkdownSegmenter.SplitTableRow(@"| a \| b | c |"));
    }

    [Theory]
    [InlineData("$$\nE = mc^2\n$$", "E = mc^2")]
    [InlineData("$$ E = mc^2 $$", " E = mc^2 ")]
    [InlineData("\\[\nE = mc^2\n\\]", "E = mc^2")]
    public void MathBlocks(string markdown, string body)
    {
        var segments = Split(markdown + "\n\nafter");
        Assert.Equal(SegmentKind.Math, segments[0].Kind);
        Assert.Equal(body, string.Join("\n", segments[0].Lines));
        Assert.Equal(SegmentKind.Paragraph, segments[1].Kind);
    }

    [Fact]
    public void SetextHeading()
    {
        var segments = Split("Title\n=====\n\nbody");
        Assert.Equal(SegmentKind.Heading, segments[0].Kind);
        Assert.Equal("Title", segments[0].Lines[0]);
    }

    [Theory]
    [InlineData("- item", 0, false, 0)]
    [InlineData("  * nested", 2, false, 0)]
    [InlineData("3. third", 0, true, 3)]
    [InlineData("    10) tenth", 4, true, 10)]
    [InlineData("\t- tab", 4, false, 0)]
    public void ParsesListItems(string line, int indent, bool ordered, int number)
    {
        Assert.True(MarkdownSegmenter.TryListItem(line, out var item));
        Assert.Equal(indent, item.Indent);
        Assert.Equal(ordered, item.Ordered);
        Assert.Equal(number, item.Number);
    }

    [Theory]
    [InlineData("-not a list")]
    [InlineData("1.5 is a number")]
    [InlineData("**bold** text")]
    [InlineData("3.14 is pi")]
    public void RejectsNonListLines(string line) => Assert.False(MarkdownSegmenter.TryListItem(line, out _));

    [Fact]
    public void TaskItems()
    {
        Assert.True(MarkdownSegmenter.TryListItem("- [x] done", out var done));
        Assert.True(done.Task);
        Assert.Equal("done", done.Text);

        Assert.True(MarkdownSegmenter.TryListItem("- [ ] todo", out var todo));
        Assert.False(todo.Task);
    }

    [Fact]
    public void StablePrefixKeysWhileStreaming()
    {
        // 증분 렌더의 전제: 뒤에 글이 덧붙어도 앞쪽 완성된 블록의 키는 바뀌지 않는다.
        const string full = "# Title\n\nFirst paragraph.\n\n```js\nconst a = 1;\n```\n\nSecond paragraph that is still being wri";
        var before = Split(full[..^20]);
        var after = Split(full);

        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count - 1; i++) Assert.Equal(before[i].Key, after[i].Key);
        Assert.NotEqual(before[^1].Key, after[^1].Key);
    }

    [Fact]
    public void NeverLoopsOnOddInput()
    {
        // 어떤 규칙에도 걸리지 않는 줄에서 멈추지 않는지(무한 루프 방지).
        foreach (string odd in new[] { "|", "$$", "\\[", "```", "~~~", ">", "#", "=", "===", "1.", "-", "| a |\n|", "   ```\n" })
        {
            _ = Split(odd);
        }
    }
}

public class CodeHighlighterTests
{
    private static List<CodeHighlighter.Token> Line(string code, string? language) => CodeHighlighter.Tokenize(code, language)[0];

    private static TokenKind KindOf(List<CodeHighlighter.Token> tokens, string text) => tokens.First(t => t.Text.Contains(text)).Kind;

    [Fact]
    public void UrlInShellIsNotAComment()
    {
        var tokens = Line("curl -fsSL https://example.com/install.sh | sh  # fetch", "bash");
        Assert.Equal(TokenKind.Default, KindOf(tokens, "example.com"));
        Assert.Equal(TokenKind.Comment, KindOf(tokens, "# fetch"));
    }

    [Fact]
    public void UrlInCLikeIsNotAComment()
    {
        var tokens = Line("fetch(https://example.com/a); // real comment", "js");
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Comment && t.Text.Contains("example.com"));
        Assert.Equal(TokenKind.Comment, KindOf(tokens, "real comment"));
    }

    [Fact]
    public void JsonDoesNotHighlightWordsAsKeywords()
    {
        var tokens = Line("{ \"type\": \"x\", \"in\": [1], \"ok\": true }", "json");
        Assert.Equal(TokenKind.String, KindOf(tokens, "\"type\""));
        Assert.Equal(TokenKind.String, KindOf(tokens, "\"in\""));
        Assert.Equal(TokenKind.Keyword, KindOf(tokens, "true"));
        Assert.Equal(TokenKind.Number, KindOf(tokens, "1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("text")]
    [InlineData("output")]
    public void PlainBlocksAreNotHighlighted(string? language)
    {
        var tokens = Line("it's 100% done in 3 steps // not a comment", language);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Default, tokens[0].Kind);
    }

    [Fact]
    public void LoneApostropheDoesNotStartAString()
    {
        var tokens = Line("fn longest<'a>(x: &'a str) -> &'a str { x }", "rust");
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.String && t.Text.Length > 12);
        Assert.Equal(TokenKind.Keyword, KindOf(tokens, "fn"));
    }

    [Fact]
    public void PythonKeywordsCommentsAndDocstrings()
    {
        var lines = CodeHighlighter.Tokenize("def f(x):\n    \"\"\"Doc\n    more doc\"\"\"\n    return x  # done", "python");
        Assert.Equal(TokenKind.Keyword, lines[0][0].Kind);
        Assert.Equal(TokenKind.String, lines[1].Last().Kind);
        Assert.Equal(TokenKind.String, lines[2][0].Kind);          // 여러 줄 문자열이 다음 줄까지 이어진다
        Assert.Equal(TokenKind.Keyword, KindOf(lines[3], "return"));
        Assert.Equal(TokenKind.Comment, lines[3].Last().Kind);
    }

    [Fact]
    public void BlockCommentSpansLines()
    {
        var lines = CodeHighlighter.Tokenize("int a; /* start\nstill comment\nend */ int b;", "c");
        Assert.Equal(TokenKind.Comment, lines[0].Last().Kind);
        Assert.Single(lines[1]);
        Assert.Equal(TokenKind.Comment, lines[1][0].Kind);
        Assert.Equal(TokenKind.Comment, lines[2][0].Kind);
        Assert.Equal(TokenKind.Keyword, KindOf(lines[2], "int"));
    }

    [Fact]
    public void SqlKeywordsAreCaseInsensitive()
    {
        var tokens = Line("select * FROM users -- all rows", "sql");
        Assert.Equal(TokenKind.Keyword, KindOf(tokens, "select"));
        Assert.Equal(TokenKind.Keyword, KindOf(tokens, "FROM"));
        Assert.Equal(TokenKind.Comment, tokens.Last().Kind);
    }

    [Fact]
    public void TokensReassembleToTheOriginalLine()
    {
        const string code = "var s = \"a // b\"; // c 'd' /* e */ 1_000 `t`";
        foreach (string language in new[] { "csharp", "python", "bash", "sql", "json", "html", "zig", "" })
        {
            Assert.Equal(code, string.Concat(Line(code, language).Select(t => t.Text)));
        }
    }
}

public class LatexToUnicodeTests
{
    [Theory]
    [InlineData(@"\alpha + \beta", "α + β")]
    [InlineData(@"x^2 + y^{10}", "x² + y¹⁰")]
    [InlineData(@"a_1 + a_{n}", "a₁ + aₙ")]
    [InlineData(@"\frac{a}{b}", "a/b")]
    [InlineData(@"\frac{n(n+1)}{2}", "(n(n+1))/2")]
    [InlineData(@"\sqrt{x}", "√x")]
    [InlineData(@"\sqrt{a+b}", "√(a+b)")]
    [InlineData(@"a \leq b \neq c", "a ≤ b ≠ c")]
    [InlineData(@"\mathbb{R}^n", "ℝⁿ")]
    [InlineData(@"\text{if } x > 0", "if  x > 0")] // 원문의 공백은 그대로 둔다(\quad 같은 의도된 간격을 지우지 않으려고)
    [InlineData(@"\left( x \right)", "( x )")]
    [InlineData(@"\sin\theta", "sinθ")]
    [InlineData(@"\sin x", "sin x")]
    [InlineData(@"90^\circ", "90°")]
    [InlineData(@"\vec{v}", "v\u20D7")]
    [InlineData(@"\unknowncommand", "unknowncommand")]
    public void Converts(string latex, string expected) => Assert.Equal(expected, LatexToUnicode.Convert(latex));

    [Fact]
    public void UnmappableScriptsFallBackToMarkers()
    {
        Assert.Equal("e^(iπ)", LatexToUnicode.Convert(@"e^{i\pi}"));
        Assert.Equal("x_(αβ)", LatexToUnicode.Convert(@"x_{\alpha\beta}"));
        Assert.Equal("xₘₐₓ", LatexToUnicode.Convert(@"x_{max}"));
    }

    [Fact]
    public void EnvironmentsBecomeLines()
    {
        string result = LatexToUnicode.Convert(@"\begin{aligned} a &= b \\ c &= d \end{aligned}");
        Assert.Equal(["a    = b", "c    = d"], result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"\")]
    [InlineData(@"\frac{")]
    [InlineData(@"x^")]
    [InlineData(@"}}}{{{")]
    [InlineData(@"\sqrt[")]
    public void NeverThrowsOnMalformedInput(string latex) => _ = LatexToUnicode.Convert(latex);
}
