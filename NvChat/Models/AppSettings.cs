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

        // ===== 창 상태(위치/크기) 복원용 =====
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }
    }
}
