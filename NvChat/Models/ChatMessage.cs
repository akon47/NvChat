using System;
using System.Collections.Generic;

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
        /// 첨부 이미지(data URI, base64). 비전 모델에 전달된다. 없으면 null.
        /// </summary>
        public List<string> Images { get; set; }

        /// <summary>
        /// 추론(reasoning) 모델의 사고 과정. 일반 모델은 비어 있음.
        /// </summary>
        public string Reasoning { get; set; }

        public DateTime Timestamp { get; set; }
    }
}
