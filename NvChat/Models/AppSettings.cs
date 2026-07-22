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


        // ===== 창 상태(위치/크기) 복원용 =====
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }

        public static List<PromptPreset> DefaultPresets()
        {
            return new List<PromptPreset>
            {
                new PromptPreset("영어로 번역", "다음 문장을 자연스러운 영어로 번역해줘:\n\n"),
                new PromptPreset("한국어로 번역", "Translate the following into natural Korean:\n\n"),
                new PromptPreset("요약", "다음 내용을 핵심만 간단히 요약해줘:\n\n"),
                new PromptPreset("코드 리뷰", "다음 코드를 리뷰하고 버그·개선점을 알려줘:\n\n"),
                new PromptPreset("쉽게 설명", "다음 개념을 초보자도 이해하도록 예시와 함께 쉽게 설명해줘:\n\n"),
                new PromptPreset("문법 교정", "다음 글의 맞춤법과 문법을 교정하고, 무엇을 고쳤는지 알려줘:\n\n")
            };
        }
    }
}
