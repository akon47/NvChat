using System;

namespace NvChat.Models
{
    /// <summary>
    /// 대화에 저장되는 하나의 메시지(직렬화용 POCO).
    /// </summary>
    public class ChatMessage
    {
        public ChatRole Role { get; set; }

        public string Content { get; set; }

        /// <summary>
        /// 추론(reasoning) 모델의 사고 과정. 일반 모델은 비어 있음.
        /// </summary>
        public string Reasoning { get; set; }

        public DateTime Timestamp { get; set; }
    }
}
