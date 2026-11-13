namespace NvChat.Models;

/// <summary>채팅 완성 요청에 쓰이는 생성 파라미터.</summary>
public sealed class GenerationParameters
{
    /// <summary>슬라이더 상한. 추론 모델은 사고 과정만으로 수천 토큰을 쓰므로 넉넉하게 둔다.</summary>
    public const int MaxTokensLimit = 32768;

    public double Temperature { get; set; } = 0.7;

    public double TopP { get; set; } = 1.0;

    public int MaxTokens { get; set; } = 4096;

    public double FrequencyPenalty { get; set; }

    public double PresencePenalty { get; set; }

    public GenerationParameters Clone() => (GenerationParameters)MemberwiseClone();
}
