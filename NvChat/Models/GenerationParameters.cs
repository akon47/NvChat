namespace NvChat.Models
{
    /// <summary>
    /// LLM 생성 파라미터. 대화별로 하나씩 가진다.
    /// </summary>
    public class GenerationParameters
    {
        public double Temperature { get; set; } = 0.7;

        public double TopP { get; set; } = 1.0;

        public int MaxTokens { get; set; } = 1024;

        public double FrequencyPenalty { get; set; } = 0.0;

        public double PresencePenalty { get; set; } = 0.0;

        public GenerationParameters Clone()
        {
            return (GenerationParameters)MemberwiseClone();
        }
    }
}
