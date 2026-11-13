using NvChat.Models;

namespace NvChat.Services;

/// <summary>
/// 컨텍스트 한도 관리. 대화가 길어지면 전체 기록이 모델의 컨텍스트 창을 넘어 요청이 실패하므로,
/// 추정 토큰 수가 예산을 넘으면 가장 오래된 메시지부터 전송 대상에서 뺀다. (저장된 대화 자체는 건드리지 않는다)
/// </summary>
internal static class ContextWindow
{
    /// <summary>이미지 한 장의 대략적인 토큰 비용(1024px 로 줄여 보내는 기준의 보수적 추정).</summary>
    public const int ImageTokens = 800;

    /// <summary>메시지 하나의 역할/구분자 오버헤드.</summary>
    private const int MessageOverhead = 4;

    /// <summary>
    /// 토크나이저 없이 쓰는 근사치. 라틴 문자는 4자당 1토큰, 한글·한자·가나 등은 글자당 1토큰 남짓으로 본다.
    /// 일부러 넉넉하게(많게) 추정해 한도를 넘기지 않게 한다.
    /// </summary>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int narrow = 0;
        int wide = 0;
        foreach (char c in text)
        {
            if (c < 0x0300) narrow++;
            else wide++;
        }
        return (int)Math.Ceiling(narrow / 4.0 + wide / 1.2);
    }

    public static int EstimateTokens(ChatMessage message) =>
        MessageOverhead + EstimateTokens(message.Content) + (message.Images?.Count ?? 0) * ImageTokens;

    /// <summary>
    /// 예산에 맞게 앞쪽(오래된) 메시지를 잘라낸다. 시스템 메시지와 마지막 메시지는 항상 남긴다.
    /// 잘린 뒤 첫 메시지가 어시스턴트면 짝 잃은 답변이므로 함께 뺀다.
    /// </summary>
    /// <param name="messages">전송할 메시지(시스템 메시지가 있다면 맨 앞).</param>
    /// <param name="tokenBudget">입력 토큰 예산. 0 이하면 제한 없음.</param>
    /// <returns>(전송할 메시지, 빠진 메시지 수, 전송분의 추정 토큰)</returns>
    public static (List<ChatMessage> Messages, int Dropped, int EstimatedTokens) Trim(IReadOnlyList<ChatMessage> messages, int tokenBudget)
    {
        int[] costs = messages.Select(EstimateTokens).ToArray();
        int total = costs.Sum();
        if (tokenBudget <= 0 || total <= tokenBudget || messages.Count <= 1) return (messages.ToList(), 0, total);

        int first = messages[0].Role == ChatRole.System ? 1 : 0;
        int last = messages.Count - 1;
        int start = first;

        // 마지막 메시지 하나는 예산을 넘더라도 보낸다(안 보내면 요청 자체가 의미 없다).
        while (start < last && total > tokenBudget)
        {
            total -= costs[start];
            start++;
        }

        while (start < last && messages[start].Role == ChatRole.Assistant)
        {
            total -= costs[start];
            start++;
        }

        List<ChatMessage> kept = [];
        if (first == 1) kept.Add(messages[0]);
        for (int i = start; i < messages.Count; i++) kept.Add(messages[i]);

        return (kept, start - first, total);
    }
}
