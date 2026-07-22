namespace NvChat.Services
{
    /// <summary>
    /// 스트리밍 응답의 한 조각. 일반 답변 텍스트(<see cref="Content"/>)와
    /// 추론 모델의 사고 과정(<see cref="Reasoning"/>)을 구분해 전달한다.
    /// </summary>
    public readonly struct ChatStreamDelta
    {
        public ChatStreamDelta(string content, string reasoning, string finishReason = null)
        {
            Content = content;
            Reasoning = reasoning;
            FinishReason = finishReason;
        }

        public string Content { get; }

        public string Reasoning { get; }

        /// <summary>
        /// 종료 사유(stop / length / content_filter 등). 마지막 청크에만 들어온다.
        /// 본문이 비어 있을 때 원인을 설명하는 데 쓴다.
        /// </summary>
        public string FinishReason { get; }
    }
}
