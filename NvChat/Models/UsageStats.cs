using System;

namespace NvChat.Models
{
    /// <summary>
    /// 로컬 사용량 집계.
    ///
    /// build.nvidia.com 은 잔여 할당량을 응답 헤더로 알려주지 않는다(실측 확인:
    /// nvcf-reqid / nvcf-status 만 내려온다). 또한 무료 크레딧은 하루 단위로
    /// 리셋되는 것이 아니라 가입 시 일정량이 지급되고 누적 소모되는 구조이므로,
    /// 실제 잔액에 대응하는 값은 <see cref="TotalRequests"/>(누적)이다.
    /// <see cref="Requests"/>(오늘)는 참고용 보조 지표다.
    /// </summary>
    public sealed class UsageStats
    {
        #region Properties - 누적

        /// <summary>이 PC 에서 지금까지 보낸 총 요청 수. 초기화하지 않는다.</summary>
        public int TotalRequests { get; set; }

        /// <summary>지금까지 사용한 총 입력 토큰 수.</summary>
        public long TotalPromptTokens { get; set; }

        /// <summary>지금까지 사용한 총 출력 토큰 수.</summary>
        public long TotalCompletionTokens { get; set; }

        /// <summary>누적 집계를 시작(또는 마지막으로 초기화)한 시각.</summary>
        public DateTime? CountingSince { get; set; }

        #endregion


        #region Properties - 오늘

        /// <summary>오늘 집계 기준 날짜(로컬).</summary>
        public string Date { get; set; }

        /// <summary>오늘 보낸 요청 수.</summary>
        public int Requests { get; set; }

        /// <summary>오늘 사용한 입력 토큰 수.</summary>
        public int PromptTokens { get; set; }

        /// <summary>오늘 사용한 출력 토큰 수.</summary>
        public int CompletionTokens { get; set; }

        /// <summary>오늘 사용한 총 토큰 수.</summary>
        public int TotalTokensToday => PromptTokens + CompletionTokens;

        #endregion


        #region Helpers

        public static string Today => DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>
        /// 날짜가 바뀌었으면 '오늘' 카운터만 0 으로 되돌린다. (누적은 유지)
        /// 실제로 초기화했으면 true.
        /// </summary>
        public bool ResetTodayIfStale()
        {
            var today = Today;
            if (Date == today)
                return false;

            Date = today;
            Requests = 0;
            PromptTokens = 0;
            CompletionTokens = 0;
            return true;
        }

        /// <summary>
        /// 누적 집계를 처음부터 다시 시작한다. (크레딧을 새로 받았을 때)
        /// </summary>
        public void ResetTotals()
        {
            TotalRequests = 0;
            TotalPromptTokens = 0;
            TotalCompletionTokens = 0;
            CountingSince = DateTime.Now;
        }

        #endregion
    }
}
