using System;
using System.Collections.Generic;

namespace NvChat.Models
{
    /// <summary>
    /// 하나의 대화 세션(직렬화용 POCO). %APPDATA%\NvChat\conversations.json 에 저장된다.
    /// </summary>
    public class Conversation
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string ModelId { get; set; }

        public string SystemPrompt { get; set; }

        public bool Pinned { get; set; }

        public GenerationParameters Parameters { get; set; } = new GenerationParameters();

        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
