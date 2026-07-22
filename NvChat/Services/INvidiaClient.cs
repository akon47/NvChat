using NvChat.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NvChat.Services
{
    /// <summary>
    /// build.nvidia.com(OpenAI 호환) 채팅/모델 API 클라이언트.
    /// </summary>
    public interface INvidiaClient
    {
        /// <summary>
        /// 채팅 요청 한 건이 끝날 때마다 발생한다. (로컬 사용량 집계용)
        /// </summary>
        event EventHandler<ChatUsage> UsageReported;

        /// <summary>
        /// 채팅 완성을 스트리밍으로 받아 델타(본문/추론)를 순차적으로 반환한다.
        /// </summary>
        IAsyncEnumerable<ChatStreamDelta> StreamChatAsync(
            string model,
            IEnumerable<ChatMessage> messages,
            GenerationParameters parameters,
            CancellationToken cancellationToken);

        /// <summary>
        /// 채팅 완성을 한 번에(비스트리밍) 받아 전체 본문을 반환한다. (제목 자동 생성 등)
        /// </summary>
        Task<string> CompleteChatAsync(
            string model,
            IEnumerable<ChatMessage> messages,
            GenerationParameters parameters,
            CancellationToken cancellationToken);

        /// <summary>
        /// 사용 가능한 모델 목록을 가져온다.
        /// </summary>
        Task<IReadOnlyList<NvModel>> GetModelsAsync(CancellationToken cancellationToken);
    }
}
