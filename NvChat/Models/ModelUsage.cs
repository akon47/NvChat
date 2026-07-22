using System;

namespace NvChat.Models
{
    /// <summary>
    /// 한 대화 안에서 특정 모델이 쓴 양. 대화 도중 모델을 바꿔도
    /// 모델별로 따로 쌓이도록 대화마다 목록으로 보관한다.
    /// </summary>
    public sealed class ModelUsage
    {
        #region Properties

        /// <summary>모델 식별자(예: meta/llama-3.1-8b-instruct).</summary>
        public string ModelId { get; set; }

        /// <summary>이 모델로 보낸 요청 수.</summary>
        public int Requests { get; set; }

        /// <summary>입력(프롬프트) 토큰 수.</summary>
        public long PromptTokens { get; set; }

        /// <summary>출력(생성) 토큰 수.</summary>
        public long CompletionTokens { get; set; }

        /// <summary>서버가 토큰 수를 알려준 적이 있는지. 없으면 요청 수만 유효하다.</summary>
        public bool HasTokens => PromptTokens > 0 || CompletionTokens > 0;

        public long TotalTokens => PromptTokens + CompletionTokens;

        #endregion


        #region Helpers

        /// <summary>모델 이름만 짧게(퍼블리셔 접두사 제거).</summary>
        public string ShortName
        {
            get
            {
                if (string.IsNullOrEmpty(ModelId))
                    return "(알 수 없음)";

                var slash = ModelId.LastIndexOf('/');
                return slash >= 0 && slash < ModelId.Length - 1 ? ModelId.Substring(slash + 1) : ModelId;
            }
        }

        /// <summary>"1.2k" 처럼 읽기 좋은 토큰 수.</summary>
        public static string FormatTokens(long tokens)
        {
            if (tokens <= 0)
                return "0";

            if (tokens < 10000)
                return tokens.ToString("N0");

            return (tokens / 1000.0).ToString("0.#") + "k";
        }

        public string TotalText => FormatTokens(TotalTokens);

        public string DetailText => $"요청 {Requests:N0}회 · 입력 {FormatTokens(PromptTokens)} · 출력 {FormatTokens(CompletionTokens)}";

        #endregion
    }
}
