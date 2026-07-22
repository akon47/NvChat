namespace NvChat.Services
{
    /// <summary>
    /// 스트리밍 응답의 한 조각. 일반 답변 텍스트(<see cref="Content"/>)와
    /// 추론 모델의 사고 과정(<see cref="Reasoning"/>)을 구분해 전달한다.
    /// </summary>
    public readonly struct ChatStreamDelta
    {
        public ChatStreamDelta(string content, string reasoning)
        {
            Content = content;
            Reasoning = reasoning;
        }

        public string Content { get; }

        public string Reasoning { get; }
    }
}
