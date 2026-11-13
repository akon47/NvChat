using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvChat.Localization;
using NvChat.Models;
using NvChat.Services;

namespace NvChat.ViewModels;

/// <summary>언어 선택 항목.</summary>
public sealed record LanguageOption(string Culture, string Display);

/// <summary>테마 선택 항목.</summary>
public sealed record ThemeOption(string Value, string Display);

/// <summary>설정 > 키보드 단축키 표의 한 줄.</summary>
public sealed record ShortcutEntry(string Description, string Keys);

/// <summary>설정 창의 뷰모델. API 키/베이스 URL/기본 파라미터/개인화/런처/정보를 편집한다.</summary>
public sealed partial class SettingsViewModel : WindowViewModel
{
    private const string ApiKeyPage = "https://build.nvidia.com/";
    private const string GitHubPage = "https://github.com/akon47/NvChat";

    private static LocalizationManager L => LocalizationManager.Instance;

    private readonly string _originalCulture;
    private readonly string _originalTheme;
    private readonly double _originalScale;

    public SettingsViewModel(AppSettings current, IReadOnlyList<string>? modelIds = null)
    {
        var parameters = current.DefaultParameters ?? new GenerationParameters();

        ApiKey = current.ApiKey;
        BaseUrl = string.IsNullOrWhiteSpace(current.BaseUrl) ? AppSettings.DefaultBaseUrl : current.BaseUrl;
        DefaultModelId = current.DefaultModelId;
        DefaultSystemPrompt = current.DefaultSystemPrompt;
        SendOnEnter = current.SendOnEnter;
        GenerateTitles = current.GenerateTitles;
        AboutYou = current.AboutYou;
        ResponseStyle = current.ResponseStyle;
        GlobalHotkey = HotKeyService.Normalize(current.GlobalHotkey);
        MinimizeToTrayOnClose = current.MinimizeToTrayOnClose;
        StartWithWindows = current.StartWithWindows;
        NotifyOnComplete = current.NotifyOnComplete;
        AutoCheckUpdates = current.AutoCheckUpdates;
        ContextTokenLimit = current.ContextTokenLimit;
        Temperature = parameters.Temperature;
        TopP = parameters.TopP;
        MaxTokens = parameters.MaxTokens;
        FrequencyPenalty = parameters.FrequencyPenalty;
        PresencePenalty = parameters.PresencePenalty;

        // 기본 모델은 목록에서 고르되, 목록에 없는 ID 도 직접 입력할 수 있다.
        ModelIds = modelIds is { Count: > 0 } ? modelIds : [current.DefaultModelId];

        Languages = LocalizationManager.Available.Select(a => new LanguageOption(a.Culture, a.Display)).ToArray();
        _originalCulture = L.Culture;
        SelectedLanguage = Languages.FirstOrDefault(l => l.Culture == _originalCulture) ?? Languages.FirstOrDefault();

        Themes =
        [
            new(ThemeNames.System, L["ThemeSystem"]),
            new(ThemeNames.Dark, L["ThemeDark"]),
            new(ThemeNames.Light, L["ThemeLight"]),
            new(ThemeNames.HighContrast, L["ThemeHighContrast"]),
        ];
        _originalTheme = current.Theme;
        _originalScale = ThemeManager.Instance.Scale;
        SelectedTheme = Themes.FirstOrDefault(t => t.Value == current.Theme) ?? Themes[0];
        UiScalePercent = Math.Round(_originalScale * 100);

        Presets = new ObservableCollection<PromptPreset>((current.Presets ?? AppSettings.DefaultPresets()).Select(p => new PromptPreset(p.Name, p.Text)));
        Shortcuts =
        [
            new(L["ScSend"], "Enter"),
            new(L["ScNewLine"], "Shift+Enter"),
            new(L["ScPasteImage"], "Ctrl+V"),
            new(L["ScStop"], "Esc"),
            new(L["ScNewChat"], "Ctrl+N"),
            new(L["ScFind"], "Ctrl+F"),
            new(L["ScSearch"], "Ctrl+Shift+F"),
            new(L["ScRename"], "F2"),
            new(L["ScSidebar"], "Ctrl+B"),
            new(L["ScModel"], "Ctrl+M"),
            new(L["ScRegen"], "Ctrl+R"),
            new(L["ScCopy"], "Ctrl+Shift+C"),
            new(L["ScExport"], "Ctrl+E"),
            new(L["ScZoom"], "Ctrl+=  /  Ctrl+-  /  Ctrl+0"),
            new(L["ScSettings"], "Ctrl+,"),
            new(L["ScQuick"], HotkeyDisplay),
        ];
    }

    public IReadOnlyList<ShortcutEntry> Shortcuts { get; }

    /// <summary>저장 버튼으로 닫혔는지 여부. 창이 닫힌 뒤 호출측에서 확인한다.</summary>
    public bool Saved { get; private set; }

    /// <summary>"지금 확인"으로 찾은 새 버전. 창이 닫힌 뒤 메인 창이 배너로 안내한다.</summary>
    public UpdateInfo? FoundUpdate { get; private set; }

    public LanguageOption[] Languages { get; }

    public ThemeOption[] Themes { get; }

    public IReadOnlyList<string> ModelIds { get; }

    public ObservableCollection<PromptPreset> Presets { get; }

    /// <summary>"현재 버전 1.3.0" 표시.</summary>
    public string CurrentVersionText => L.Tr("CurrentVersion", UpdateService.CurrentVersion.ToString(3));

    public string DataFolder => AppPaths.DataDirectory;

    /// <summary>왼쪽 탭에서 고른 설정 분류.</summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    public int MaxTokensLimit => GenerationParameters.MaxTokensLimit;

    // ===== 연결 =====

    /// <summary>null = 사용자가 키 칸을 한 번도 건드리지 않음(복호화 실패로 비어 보이는 경우 포함) → 저장소가 기존 키를 보존한다. "" = 명시적으로 비움.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    public partial string? ApiKey { get; set; }

    /// <summary>API 키를 가리지 않고 보여 줄지(기본은 가림 — 화면 공유·어깨너머 노출 방지).</summary>
    [ObservableProperty]
    public partial bool IsApiKeyVisible { get; set; }

    [ObservableProperty]
    public partial string BaseUrl { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    public partial bool IsTesting { get; set; }

    [ObservableProperty]
    public partial string? TestStatus { get; set; }

    [ObservableProperty]
    public partial bool TestSucceeded { get; set; }

    // ===== 새 대화 기본값 =====

    [ObservableProperty]
    public partial string DefaultModelId { get; set; }

    [ObservableProperty]
    public partial string DefaultSystemPrompt { get; set; }

    [ObservableProperty]
    public partial double Temperature { get; set; }

    [ObservableProperty]
    public partial double TopP { get; set; }

    [ObservableProperty]
    public partial int MaxTokens { get; set; }

    [ObservableProperty]
    public partial double FrequencyPenalty { get; set; }

    [ObservableProperty]
    public partial double PresencePenalty { get; set; }

    /// <summary>입력 컨텍스트 한도(추정 토큰). 0 이면 제한 없음.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextLimitText))]
    public partial int ContextTokenLimit { get; set; }

    public string ContextLimitText => ContextTokenLimit <= 0 ? L["ContextUnlimited"] : ModelUsage.FormatTokens(ContextTokenLimit);

    // ===== 모양 =====

    [ObservableProperty]
    public partial ThemeOption? SelectedTheme { get; set; }

    /// <summary>본문 확대 배율(%).</summary>
    [ObservableProperty]
    public partial double UiScalePercent { get; set; }

    // ===== 동작 / 개인화 / 런처 =====

    [ObservableProperty]
    public partial bool SendOnEnter { get; set; }

    [ObservableProperty]
    public partial bool GenerateTitles { get; set; }

    [ObservableProperty]
    public partial string AboutYou { get; set; }

    [ObservableProperty]
    public partial string ResponseStyle { get; set; }

    /// <summary>저장값("Ctrl+Shift+Space" 또는 꺼짐을 뜻하는 "Off"). 화면에는 <see cref="HotkeyDisplay"/> 로 보인다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay), nameof(IsHotkeyEnabled))]
    public partial string GlobalHotkey { get; set; }

    /// <summary>단축키 입력 칸의 표시 문구. 꺼져 있으면 현재 언어의 "끄기".</summary>
    public string HotkeyDisplay => HotKeyService.IsDisabled(GlobalHotkey) ? L["HotkeyOff"] : GlobalHotkey;

    public bool IsHotkeyEnabled => !HotKeyService.IsDisabled(GlobalHotkey);

    [ObservableProperty]
    public partial bool MinimizeToTrayOnClose { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool NotifyOnComplete { get; set; }

    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    // ===== 업데이트 / 정보 =====

    /// <summary>시작할 때(그리고 주기적으로) 새 버전을 확인할지 여부.</summary>
    [ObservableProperty]
    public partial bool AutoCheckUpdates { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdateCommand))]
    public partial bool IsCheckingUpdate { get; set; }

    [ObservableProperty]
    public partial string? UpdateStatus { get; set; }

    partial void OnApiKeyChanged(string? value) => ResetTestStatus();

    partial void OnBaseUrlChanged(string value) => ResetTestStatus();

    partial void OnTemperatureChanged(double value) => Temperature = Math.Round(value, 2);

    partial void OnTopPChanged(double value) => TopP = Math.Round(value, 2);

    partial void OnFrequencyPenaltyChanged(double value) => FrequencyPenalty = Math.Round(value, 2);

    partial void OnPresencePenaltyChanged(double value) => PresencePenalty = Math.Round(value, 2);

    // 라이브 미리보기: 고르는 즉시 UI 언어/테마/배율을 바꾼다. (취소하면 RevertPreview 로 되돌린다)
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is not null) LocalizationManager.Instance.Culture = value.Culture;
        OnPropertyChanged(nameof(HotkeyDisplay));
        OnPropertyChanged(nameof(ContextLimitText));
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is not null) ThemeManager.Instance.Apply(value.Value);
    }

    partial void OnUiScalePercentChanged(double value) => ThemeManager.Instance.Scale = value / 100.0;

    /// <summary>저장하지 않고 창을 닫았을 때 미리보기로 바꿔 둔 언어/테마/배율을 되돌린다.</summary>
    public void RevertPreview()
    {
        LocalizationManager.Instance.Culture = _originalCulture;
        ThemeManager.Instance.Apply(_originalTheme);
        ThemeManager.Instance.Scale = _originalScale;
    }

    /// <summary>단축키 입력 칸에서 누른 조합을 받는다(HotkeyCaptureBehavior).</summary>
    [RelayCommand]
    private void CaptureHotkey(string? hotkey)
    {
        if (!string.IsNullOrWhiteSpace(hotkey)) GlobalHotkey = hotkey;
    }

    [RelayCommand]
    private void DisableHotkey() => GlobalHotkey = AppSettings.HotkeyOff;

    [RelayCommand]
    private void ToggleApiKeyVisibility() => IsApiKeyVisible = !IsApiKeyVisible;

    public AppSettings BuildSettings() => new()
    {
        ApiKey = ApiKey?.Trim(),
        BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? AppSettings.DefaultBaseUrl : BaseUrl.Trim(),
        DefaultModelId = (DefaultModelId ?? "").Trim(),
        DefaultSystemPrompt = DefaultSystemPrompt,
        SendOnEnter = SendOnEnter,
        GenerateTitles = GenerateTitles,
        AboutYou = AboutYou,
        ResponseStyle = ResponseStyle,
        GlobalHotkey = HotKeyService.Normalize(GlobalHotkey),
        MinimizeToTrayOnClose = MinimizeToTrayOnClose,
        StartWithWindows = StartWithWindows,
        NotifyOnComplete = NotifyOnComplete,
        AutoCheckUpdates = AutoCheckUpdates,
        ContextTokenLimit = Math.Max(0, ContextTokenLimit),
        Language = SelectedLanguage?.Culture ?? _originalCulture,
        Theme = SelectedTheme?.Value ?? _originalTheme,
        UiScale = ThemeManager.Instance.Scale,
        Presets = Presets
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new PromptPreset(p.Name.Trim(), p.Text))
            .ToList(),
        DefaultParameters = new GenerationParameters
        {
            Temperature = Temperature,
            TopP = TopP,
            MaxTokens = MaxTokens <= 0 ? 4096 : MaxTokens,
            FrequencyPenalty = FrequencyPenalty,
            PresencePenalty = PresencePenalty,
        },
    };

    private void ResetTestStatus()
    {
        TestStatus = null;
        TestSucceeded = false;
    }

    private bool CanTestConnection() => !IsTesting && !string.IsNullOrWhiteSpace(ApiKey);

    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestSucceeded = false;
        TestStatus = L["TestConnecting"];

        try
        {
            var probe = BuildSettings();
            using NvidiaClient client = new(() => probe);
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            var models = await client.GetModelsAsync(cts.Token);

            TestSucceeded = true;
            TestStatus = L.Tr("TestSuccess", models.Count);
        }
        catch (Exception ex)
        {
            TestSucceeded = false;
            TestStatus = L.Tr("TestFailed", ex.Message);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private bool CanCheckUpdate() => !IsCheckingUpdate;

    [RelayCommand(CanExecute = nameof(CanCheckUpdate))]
    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = L["CheckingUpdate"];
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
            FoundUpdate = await new UpdateService().CheckAsync(cts.Token);
            UpdateStatus = FoundUpdate is { } info
                ? L.Tr("UpdateFoundInSettings", info.Tag)
                : L.Tr("UpToDate", "v" + UpdateService.CurrentVersion.ToString(3));
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        Saved = true;
        RequestClose();
    }

    [RelayCommand]
    private void Cancel()
    {
        Saved = false;
        RequestClose();
    }

    [RelayCommand]
    private void AddPreset() => Presets.Add(new PromptPreset(L["NewPreset"], ""));

    [RelayCommand]
    private void RemovePreset(PromptPreset? preset)
    {
        if (preset is not null) Presets.Remove(preset);
    }

    [RelayCommand]
    private void OpenApiKeyPage() => OpenShell(ApiKeyPage);

    [RelayCommand]
    private void OpenGitHub() => OpenShell(GitHubPage);

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            OpenShell(AppPaths.DataDirectory);
        }
        catch { /* 폴더 열기 실패는 무시. */ }
    }

    private static void OpenShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* 브라우저/탐색기 실행 실패는 무시. */ }
    }
}
