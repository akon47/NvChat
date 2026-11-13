using NvChat.Models;
using NvChat.Services;

namespace NvChat.Tests;

public class UrlSafetyTests
{
    [Theory]
    [InlineData("https://example.com/a?b=1")]
    [InlineData("http://example.com")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("example.com/docs")]
    public void AllowsWebAndMail(string url) => Assert.NotNull(UrlSafety.ToSafeUri(url));

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData(@"\\attacker\share\run.exe")]
    [InlineData("//attacker/share/run.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("calculator:")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsEverythingElse(string? url) => Assert.Null(UrlSafety.ToSafeUri(url));

    [Fact]
    public void BareHostBecomesHttps() => Assert.Equal("https", UrlSafety.ToSafeUri("example.com/x")!.Scheme);
}

public class TitleCleanerTests
{
    [Theory]
    [InlineData("Quicksort Overview", "Quicksort Overview")]
    [InlineData("\"Quicksort Overview.\"", "Quicksort Overview")]
    [InlineData("## Quicksort Overview", "Quicksort Overview")]
    [InlineData("**Quicksort Overview** Quicksort is a divide", "Quicksort Overview Quicksort is a divide")]
    [InlineData("Title: Rust 소유권", "Rust 소유권")]
    [InlineData("제목: Rust 소유권", "Rust 소유권")]
    [InlineData("<think>\nuser wants a title\n</think>\n\nRust 소유권 모델", "Rust 소유권 모델")]
    [InlineData("Rust 소유권\n\n이 대화는 러스트에 관한 것입니다.", "Rust 소유권")]
    public void CleansTitle(string raw, string expected) => Assert.Equal(expected, TitleCleaner.Clean(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<think>\nOkay, the user is asking about")] // 추론이 닫히기 전에 잘린 응답
    [InlineData("def is_prime(n):")]
    [InlineData("def is_prime(n): \"\"\" Check")]
    [InlineData("```python")]
    [InlineData("<html>")]
    public void RejectsNonTitles(string raw) => Assert.Null(TitleCleaner.Clean(raw));

    [Fact]
    public void TruncatesLongTitles()
    {
        string? title = TitleCleaner.Clean(new string('가', 100));
        Assert.NotNull(title);
        Assert.Equal(TitleCleaner.MaxLength + 1, title!.Length);
        Assert.EndsWith("…", title);
    }
}

public class ThinkTagSplitterTests
{
    private static (string Content, string Reasoning) Run(params string[] chunks)
    {
        ThinkTagSplitter splitter = new();
        string content = "";
        string reasoning = "";
        foreach (string chunk in chunks)
        {
            var (c, r) = splitter.Push(chunk);
            content += c;
            reasoning += r;
        }
        var (fc, fr) = splitter.Flush();
        return (content + fc, reasoning + fr);
    }

    [Fact]
    public void PlainContentPassesThrough()
    {
        var (content, reasoning) = Run("Hello", ", ", "world");
        Assert.Equal("Hello, world", content);
        Assert.Equal("", reasoning);
    }

    [Fact]
    public void SplitsThinkBlock()
    {
        var (content, reasoning) = Run("<think>step one</think>\n\nAnswer");
        Assert.Equal("Answer", content);
        Assert.Equal("step one", reasoning);
    }

    [Fact]
    public void HandlesTagsSplitAcrossChunks()
    {
        var (content, reasoning) = Run("<th", "ink>", "rea", "son", "ing</th", "ink>", "\n\nThe ", "answer");
        Assert.Equal("The answer", content);
        Assert.Equal("reasoning", reasoning);
    }

    [Fact]
    public void CharacterByCharacterStream()
    {
        const string text = "<think>a < b and b > c</think>Result: <b>ok</b>";
        var (content, reasoning) = Run(text.Select(ch => ch.ToString()).ToArray());
        Assert.Equal("Result: <b>ok</b>", content);
        Assert.Equal("a < b and b > c", reasoning);
    }

    [Fact]
    public void UnterminatedThinkStaysReasoning()
    {
        var (content, reasoning) = Run("<think>still thinking when the stream ended");
        Assert.Equal("", content);
        Assert.Equal("still thinking when the stream ended", reasoning);
    }

    [Fact]
    public void ThinkTagInTheMiddleOfAnAnswerIsLiteral()
    {
        var (content, reasoning) = Run("Use the <think> tag like this: <think>x</think>");
        Assert.Equal("Use the <think> tag like this: <think>x</think>", content);
        Assert.Equal("", reasoning);
    }

    [Fact]
    public void LessThanAtStartIsNotSwallowed()
    {
        var (content, _) = Run("<", "div> is an element");
        Assert.Equal("<div> is an element", content);
    }

    [Fact]
    public void OrphanCloseMovesPrefixToReasoning()
    {
        var split = ThinkTagSplitter.SplitOrphanClose("thinking without an opening tag</think>\n\nFinal answer");
        Assert.NotNull(split);
        Assert.Equal("Final answer", split!.Value.Content);
        Assert.Equal("thinking without an opening tag", split.Value.Reasoning);
        Assert.Null(ThinkTagSplitter.SplitOrphanClose("no tags here"));
    }
}

public class ContextWindowTests
{
    private static ChatMessage Message(ChatRole role, int length) => new() { Role = role, Content = new string('a', length) };

    [Fact]
    public void EstimatesWideCharactersHigher()
    {
        Assert.True(ContextWindow.EstimateTokens("안녕하세요 반갑습니다") > ContextWindow.EstimateTokens("hello there"));
        Assert.Equal(0, ContextWindow.EstimateTokens(""));
    }

    [Fact]
    public void KeepsEverythingWithinBudget()
    {
        List<ChatMessage> messages = [Message(ChatRole.System, 40), Message(ChatRole.User, 40), Message(ChatRole.Assistant, 40)];
        var (kept, dropped, _) = ContextWindow.Trim(messages, 10_000);
        Assert.Equal(3, kept.Count);
        Assert.Equal(0, dropped);
    }

    [Fact]
    public void ZeroBudgetMeansUnlimited()
    {
        List<ChatMessage> messages = [Message(ChatRole.User, 400_000), Message(ChatRole.Assistant, 400_000), Message(ChatRole.User, 10)];
        Assert.Equal(0, ContextWindow.Trim(messages, 0).Dropped);
    }

    [Fact]
    public void DropsOldestButKeepsSystemAndLast()
    {
        List<ChatMessage> messages = [Message(ChatRole.System, 40)];
        for (int i = 0; i < 20; i++)
        {
            messages.Add(Message(ChatRole.User, 400));
            messages.Add(Message(ChatRole.Assistant, 400));
        }
        messages.Add(Message(ChatRole.User, 400));

        var (kept, dropped, estimated) = ContextWindow.Trim(messages, 1000);

        Assert.True(dropped > 0);
        Assert.Equal(ChatRole.System, kept[0].Role);
        Assert.Same(messages[^1], kept[^1]);
        Assert.Equal(ChatRole.User, kept[1].Role); // 짝 잃은 어시스턴트 답변으로 시작하지 않는다
        Assert.True(estimated <= 1000);
        Assert.Equal(messages.Count - kept.Count, dropped);
    }

    [Fact]
    public void AlwaysSendsTheLastMessageEvenIfItAloneExceedsTheBudget()
    {
        List<ChatMessage> messages = [Message(ChatRole.User, 100), Message(ChatRole.Assistant, 100), Message(ChatRole.User, 100_000)];
        var (kept, _, _) = ContextWindow.Trim(messages, 500);
        Assert.Single(kept);
        Assert.Same(messages[^1], kept[0]);
    }

    [Fact]
    public void ImagesCountTowardsTheEstimate()
    {
        ChatMessage withImage = new() { Role = ChatRole.User, Content = "look", Images = ["data:image/jpeg;base64,AAAA"] };
        ChatMessage without = new() { Role = ChatRole.User, Content = "look" };
        Assert.Equal(ContextWindow.ImageTokens, ContextWindow.EstimateTokens(withImage) - ContextWindow.EstimateTokens(without));
    }
}

public class HotKeyTests
{
    [Theory]
    [InlineData("끄기")] // 1.8 까지 저장되던 번역된 표시 문구
    [InlineData("Off")]
    [InlineData("off")]
    [InlineData("")]
    [InlineData(null)]
    public void DisabledValuesNormalizeToOff(string? stored)
    {
        Assert.True(HotKeyService.IsDisabled(stored));
        Assert.Equal(AppSettings.HotkeyOff, HotKeyService.Normalize(stored));
    }

    [Theory]
    [InlineData("Ctrl+Shift+Space")]
    [InlineData("Alt+`")]
    [InlineData("Ctrl+Alt+K")]
    [InlineData("Win+Shift+F9")]
    public void ParsesCombinations(string hotkey)
    {
        Assert.True(HotKeyService.TryParse(hotkey, out uint modifiers, out uint key));
        Assert.NotEqual(0u, modifiers);
        Assert.NotEqual(0u, key);
        Assert.Equal(hotkey, HotKeyService.Normalize(hotkey));
    }

    [Theory]
    [InlineData("Space")]      // 수식키 없는 전역 단축키는 일반 타이핑을 가로챈다
    [InlineData("Ctrl+")]
    [InlineData("Hyper+K")]
    public void RejectsInvalid(string hotkey) => Assert.False(HotKeyService.TryParse(hotkey, out _, out _));

    [Fact]
    public void FormatRoundTrips()
    {
        string? formatted = HotKeyService.Format(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.Space);
        Assert.Equal("Ctrl+Shift+Space", formatted);
        Assert.True(HotKeyService.TryParse(formatted, out _, out _));
        Assert.Null(HotKeyService.Format(System.Windows.Input.ModifierKeys.None, System.Windows.Input.Key.A));
        Assert.Null(HotKeyService.Format(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.LeftCtrl));
    }
}

public class StreamParsingTests
{
    [Fact]
    public void ParsesContentDelta()
    {
        var chunk = NvidiaClient.ParseChunk("""{"choices":[{"delta":{"content":"Hi"},"finish_reason":null}],"usage":null}""");
        Assert.Equal("Hi", chunk.Delta!.Value.Content);
        Assert.Null(chunk.Usage);
        Assert.Null(chunk.Error);
    }

    [Theory]
    [InlineData("reasoning_content")]
    [InlineData("reasoning")]
    public void ParsesReasoningUnderEitherName(string field)
    {
        var chunk = NvidiaClient.ParseChunk("{\"choices\":[{\"delta\":{\"" + field + "\":\"hmm\"}}]}");
        Assert.Equal("hmm", chunk.Delta!.Value.Reasoning);
    }

    [Fact]
    public void ParsesUsageOnlyChunk()
    {
        var chunk = NvidiaClient.ParseChunk("""{"choices":[],"usage":{"prompt_tokens":12,"completion_tokens":34,"total_tokens":46}}""");
        Assert.Null(chunk.Delta);
        Assert.Equal(12, chunk.Usage!.Value.PromptTokens);
        Assert.Equal(34, chunk.Usage.Value.CompletionTokens);
    }

    [Fact]
    public void KeepsFinishReasonWithoutContent()
    {
        var chunk = NvidiaClient.ParseChunk("""{"choices":[{"delta":{},"finish_reason":"length"}]}""");
        Assert.Equal("length", chunk.Delta!.Value.FinishReason);
    }

    [Theory]
    [InlineData("""{"error":{"message":"model overloaded"}}""", "model overloaded")]
    [InlineData("""{"error":"bad request"}""", "bad request")]
    public void SurfacesInStreamErrors(string data, string expected) => Assert.Equal(expected, NvidiaClient.ParseChunk(data).Error);

    [Fact]
    public void ContentThatMentionsErrorIsNotAnError()
    {
        var chunk = NvidiaClient.ParseChunk("""{"choices":[{"delta":{"content":"the \"error\" field"}}]}""");
        Assert.Null(chunk.Error);
        Assert.Equal("the \"error\" field", chunk.Delta!.Value.Content);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    public void IgnoresMalformedChunks(string data)
    {
        var chunk = NvidiaClient.ParseChunk(data);
        Assert.Null(chunk.Delta);
        Assert.Null(chunk.Usage);
        Assert.Null(chunk.Error);
    }
}

public class ConversationStoreTests
{
    [Theory]
    [InlineData("0f3a9c1e5b7d4a2f8c6e1d3b5a7f9c0e")]
    [InlineData("rich0001")]
    [InlineData("a-b_c")]
    public void KeepsSafeIds(string id) => Assert.Equal(id, ConversationStore.NormalizeId(id));

    [Theory]
    [InlineData(@"..\..\evil")]
    [InlineData("a/b")]
    [InlineData("con:1")]
    [InlineData("")]
    [InlineData(null)]
    public void ReplacesUnsafeIds(string? id)
    {
        string normalized = ConversationStore.NormalizeId(id);
        Assert.NotEqual(id, normalized);
        Assert.Equal(32, normalized.Length);
        Assert.True(normalized.All(char.IsAsciiLetterOrDigit));
    }
}

public class ThemeManagerTests
{
    [Theory]
    [InlineData("ForegroundBrush", "ForegroundColor")]
    [InlineData("AccentSoftBrush", "AccentColor")]
    [InlineData("OverlayHoverBrush", "OverlayColor")]
    [InlineData("OverlayPressedBrush", "OverlayColor")]
    [InlineData("SyntaxKeywordBrush", "SyntaxKeywordColor")]
    public void MapsBrushKeysToPaletteKeys(string brush, string color) => Assert.Equal(color, ThemeManager.ColorKeyFor(brush));
}
