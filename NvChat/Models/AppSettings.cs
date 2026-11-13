using System.Text.Json.Serialization;
using NvChat.Localization;

namespace NvChat.Models;

/// <summary>
/// 앱 전역 설정. %APPDATA%\NvChat\settings.json 에 저장된다.
/// (<see cref="ApiKey"/> 는 저장 시 Windows DPAPI 로 암호화된다.)
/// </summary>
public sealed class AppSettings
{
    public const string DefaultBaseUrl = "https://integrate.api.nvidia.com/v1";
    public const string DefaultHotkey = "Ctrl+Shift+Space";

    /// <summary>전역 단축키를 끈 상태의 저장값. (표시 문구는 언어별로 번역하되 저장값은 언어와 무관해야 한다)</summary>
    public const string HotkeyOff = "Off";

    /// <summary>build.nvidia.com 에서 발급받은 API 키(nvapi-...). 메모리 상에서는 평문.</summary>
    public string? ApiKey { get; set; }

    /// <summary>OpenAI 호환 엔드포인트의 베이스 URL.</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>새 대화에 기본으로 선택되는 모델 ID.</summary>
    public string DefaultModelId { get; set; } = "meta/llama-3.3-70b-instruct";

    /// <summary>새 대화에 기본으로 채워지는 시스템 프롬프트.</summary>
    public string DefaultSystemPrompt { get; set; } = "";

    /// <summary>새 대화에 기본으로 적용되는 생성 파라미터.</summary>
    public GenerationParameters DefaultParameters { get; set; } = new();

    /// <summary>Enter 로 전송할지(true) Ctrl+Enter 로 전송할지(false).</summary>
    public bool SendOnEnter { get; set; } = true;

    /// <summary>첫 응답 후 모델을 이용해 대화 제목을 자동 생성할지 여부.</summary>
    public bool GenerateTitles { get; set; } = true;

    // ===== 개인화(커스텀 지침) — 모든 대화에 자동 적용 =====

    /// <summary>나에 대한 정보(모델이 알아야 할 것).</summary>
    public string AboutYou { get; set; } = "";

    /// <summary>응답 방식/톤 지침.</summary>
    public string ResponseStyle { get; set; } = "";

    /// <summary>저장된 프롬프트 프리셋.</summary>
    public List<PromptPreset> Presets { get; set; } = DefaultPresets();

    // ===== 데스크톱 런처 =====

    /// <summary>빠른 채팅 전역 단축키(예: "Ctrl+Shift+Space", "Off").
    /// Ctrl+Alt+Space 는 다른 앱/IME 가 선점하는 경우가 많아 기본값에서 제외했다.</summary>
    public string GlobalHotkey { get; set; } = DefaultHotkey;

    /// <summary>닫기 버튼을 누르면 종료하지 않고 트레이로 최소화.</summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>"창을 닫아도 트레이에서 계속 실행됩니다" 안내를 이미 보였는지(최초 1회만).</summary>
    public bool CloseToTrayHintShown { get; set; }

    /// <summary>시작할 때(그리고 6시간마다) GitHub Releases 에서 새 버전을 확인할지 여부.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>UI 표시 언어(컬처 코드, 예: "en-US" / "ko-KR"). 비어 있으면 첫 실행 시 OS 언어로 결정.</summary>
    public string Language { get; set; } = "";

    // ===== 모양 =====

    /// <summary>테마: System(윈도우 설정을 따름) / Dark / Light / HighContrast.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>본문 확대 배율(1.0 = 100%). Ctrl+= / Ctrl+- / Ctrl+0 으로도 바꾼다.</summary>
    public double UiScale { get; set; } = 1.0;

    /// <summary>사이드바 폭(DIP)과 접힘 상태.</summary>
    public double SidebarWidth { get; set; } = 280;

    public bool SidebarCollapsed { get; set; }

    // ===== 동작 =====

    /// <summary>입력 컨텍스트 한도(추정 토큰). 넘으면 오래된 메시지부터 전송에서 뺀다. 0 이면 제한 없음.</summary>
    public int ContextTokenLimit { get; set; } = 32000;

    /// <summary>창이 가려져 있거나 숨겨진 동안 응답이 끝나면 알림을 띄울지.</summary>
    public bool NotifyOnComplete { get; set; } = true;

    /// <summary>윈도우 로그인 시 트레이로 자동 시작할지.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>메인 창 위치/크기 기억값. null 이면 기본 위치(화면 중앙).</summary>
    public WindowPlacement? Window { get; set; }

    // ===== 1.7 이전 형식(평면 필드)과의 호환. 로드 시 <see cref="Window"/> 로 옮긴 뒤 비운다. =====
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? WindowLeft { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? WindowTop { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? WindowWidth { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? WindowHeight { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool WindowMaximized { get; set; }

    /// <summary>구버전 평면 창 상태 필드를 <see cref="Window"/> 로 옮긴다(한 번만).</summary>
    public void MigrateLegacyWindowState()
    {
        if (Window is null && WindowWidth is > 300 && WindowHeight is > 300)
        {
            Window = new WindowPlacement
            {
                Left = WindowLeft ?? 0,
                Top = WindowTop ?? 0,
                Width = WindowWidth.Value,
                Height = WindowHeight.Value,
                IsMaximized = WindowMaximized,
            };
        }

        WindowLeft = WindowTop = WindowWidth = WindowHeight = null;
        WindowMaximized = false;
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>손으로 고친 파일이나 이전 버전의 값을 유효 범위로 맞춘다.</summary>
    public void Normalize()
    {
        // 1.8 까지는 꺼짐을 번역된 표시 문구("끄기")로 저장했다. 언어와 무관한 값으로 바꾼다.
        GlobalHotkey = Services.HotKeyService.Normalize(GlobalHotkey);

        if (!Services.ThemeNames.All.Contains(Theme)) Theme = Services.ThemeNames.System;
        if (!double.IsFinite(UiScale) || UiScale <= 0) UiScale = 1.0;
        UiScale = Math.Clamp(UiScale, Services.ThemeManager.MinScale, Services.ThemeManager.MaxScale);
        if (!double.IsFinite(SidebarWidth)) SidebarWidth = 280;
        SidebarWidth = Math.Clamp(SidebarWidth, MinSidebarWidth, MaxSidebarWidth);
        if (ContextTokenLimit < 0) ContextTokenLimit = 0;
    }

    public const double MinSidebarWidth = 220;
    public const double MaxSidebarWidth = 460;

    public static List<PromptPreset> DefaultPresets()
    {
        var L = LocalizationManager.Instance;
        return
        [
            new(L["PresetTranslateEnName"], L["PresetTranslateEnBody"]),
            new(L["PresetTranslateKoName"], L["PresetTranslateKoBody"]),
            new(L["PresetSummarizeName"], L["PresetSummarizeBody"]),
            new(L["PresetCodeReviewName"], L["PresetCodeReviewBody"]),
            new(L["PresetExplainName"], L["PresetExplainBody"]),
            new(L["PresetGrammarName"], L["PresetGrammarBody"]),
        ];
    }
}
