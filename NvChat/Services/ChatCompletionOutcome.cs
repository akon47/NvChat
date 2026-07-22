namespace NvChat.Services
{
    /// <summary>
    /// 비스트리밍 채팅 완성 결과. 본문과 토큰 사용량을 함께 돌려준다.
    /// </summary>
    public readonly struct ChatCompletionOutcome
    {
        public ChatCompletionOutcome(string content, ChatUsage usage)
        {
            Content = content;
            Usage = usage;
        }

        public string Content { get; }

        public ChatUsage Usage { get; }
    }
}
