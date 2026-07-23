using System.Collections.Generic;

namespace NvChat.Models
{
    /// <summary>
    /// 앱 전역 설정. %APPDATA%\NvChat\settings.json 에 저장된다.
    /// (<see cref="ApiKey"/> 는 저장 시 Windows DPAPI 로 암호화된다.)
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// build.nvidia.com 에서 발급받은 API 키(nvapi-...). 메모리 상에서는 평문.
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// OpenAI 호환 엔드포인트의 베이스 URL.
        /// </summary>
        public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1";

        /// <summary>
        /// 새 대화에 기본으로 선택되는 모델 ID.
        /// </summary>
        public string DefaultModelId { get; set; } = "meta/llama-3.3-70b-instruct";

        /// <summary>
        /// 새 대화에 기본으로 채워지는 시스템 프롬프트.
        /// </summary>
        public string DefaultSystemPrompt { get; set; } = "";

        /// <summary>
        /// 새 대화에 기본으로 적용되는 생성 파라미터.
        /// </summary>
        public GenerationParameters DefaultParameters { get; set; } = new GenerationParameters();

        /// <summary>
        /// Enter 로 전송할지(true) Shift+Enter 로 전송할지(false).
        /// </summary>
        public bool SendOnEnter { get; set; } = true;

        /// <summary>
        /// 첫 응답 후 모델을 이용해 대화 제목을 자동 생성할지 여부.
        /// </summary>
        public bool GenerateTitles { get; set; } = true;

        // ===== 개인화(커스텀 지침) — 모든 대화에 자동 적용 =====
        /// <summary>나에 대한 정보(모델이 알아야 할 것).</summary>
        public string AboutYou { get; set; } = "";

        /// <summary>응답 방식/톤 지침.</summary>
        public string ResponseStyle { get; set; } = "";

        /// <summary>저장된 프롬프트 프리셋.</summary>
        public List<PromptPreset> Presets { get; set; } = DefaultPresets();

        // ===== 데스크톱 런처 =====
        /// <summary>빠른 채팅 전역 단축키(예: "Ctrl+Shift+Space", "끄기").
        /// Ctrl+Alt+Space 는 다른 앱/IME 가 선점하는 경우가 많아 기본값에서 제외했다.</summary>
        public string GlobalHotkey { get; set; } = "Ctrl+Shift+Space";

        /// <summary>닫기 버튼을 누르면 종료하지 않고 트레이로 최소화.</summary>
        public bool MinimizeToTrayOnClose { get; set; } = true;

        /// <summary>시작할 때 GitHub Releases 에서 새 버전을 확인할지 여부.</summary>
        public bool AutoCheckUpdates { get; set; } = true;

        /// <summary>UI 표시 언어(컬처 코드, 예: "en-US" / "ko-KR"). 비어 있으면 첫 실행 시 OS 언어로 결정.</summary>
        public string Language { get; set; } = "";


        // ===== 창 상태(위치/크기) 복원용 =====
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }

        public static List<PromptPreset> DefaultPresets()
        {
            var L = Localization.LocalizationManager.Instance;
            return new List<PromptPreset>
            {
                new PromptPreset(L["PresetTranslateEnName"], L["PresetTranslateEnBody"]),
                new PromptPreset(L["PresetTranslateKoName"], L["PresetTranslateKoBody"]),
                new PromptPreset(L["PresetSummarizeName"], L["PresetSummarizeBody"]),
                new PromptPreset(L["PresetCodeReviewName"], L["PresetCodeReviewBody"]),
                new PromptPreset(L["PresetExplainName"], L["PresetExplainBody"]),
                new PromptPreset(L["PresetGrammarName"], L["PresetGrammarBody"])
            };
        }
    }
}
