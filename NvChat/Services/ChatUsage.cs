namespace NvChat.Services
{
    /// <summary>
    /// 채팅 요청 한 건이 끝났음을 알리는 사용량 보고.
    /// 서버가 토큰 수를 알려주지 않으면 <see cref="HasTokens"/> 가 false 이고 요청 수만 유효하다.
    /// </summary>
    public readonly struct ChatUsage
    {
        public ChatUsage(int promptTokens, int completionTokens, bool hasTokens)
        {
            PromptTokens = promptTokens;
            CompletionTokens = completionTokens;
            HasTokens = hasTokens;
        }

        public int PromptTokens { get; }

        public int CompletionTokens { get; }

        public bool HasTokens { get; }
    }
}
