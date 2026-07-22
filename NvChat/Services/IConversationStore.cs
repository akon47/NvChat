using NvChat.Models;
using System.Collections.Generic;

namespace NvChat.Services
{
    public interface IConversationStore
    {
        /// <summary>로드 실패(잠금 등)로 저장이 차단되었는지. false 면 SaveAll 은 무시된다.</summary>
        bool CanSave { get; }

        /// <summary>로드 중 문제가 있었을 경우의 안내 메시지(정상 시 null).</summary>
        string LoadError { get; }

        List<Conversation> LoadAll();

        void SaveAll(IEnumerable<Conversation> conversations);
    }
}
