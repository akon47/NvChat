using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NvChat.Localization;
using NvChat.Models;
using NvChat.Services;

namespace NvChat.ViewModels;

/// <summary>프리셋 드롭다운 메뉴 한 줄. <see cref="Preset"/> 이 null 이면 "프리셋 없음" 안내(비활성).</summary>
public sealed record PresetMenuEntry(string Header, PromptPreset? Preset)
{
    public bool IsEnabled => Preset is not null;
}

/// <summary>메인 창의 뷰모델. 대화/모델/채팅 스트리밍/설정을 총괄한다. (빠른 채팅 창도 이 인스턴스를 공유한다)</summary>
public sealed partial class MainViewModel : WindowViewModel
{
    private const int TitleMaxLength = 30;

    /// <summary>스트리밍 델타를 화면에 반영하는 최소 간격. 토큰마다 갱신하지 않고 이 주기로 묶는다.</summary>
    private const int StreamFlushIntervalMs = 40;

    private const int SearchDebounceMs = 180;
    private const int ToastMs = 2200;
    private const int ActionToastMs = 6000;

    private static LocalizationManager L => LocalizationManager.Instance;

    private static readonly JsonSerializerOptions BackupJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] FallbackModelIds =
    [
        "meta/llama-3.3-70b-instruct",
        "meta/llama-3.1-405b-instruct",
        "meta/llama-3.1-70b-instruct",
        "meta/llama-3.1-8b-instruct",
        "nvidia/llama-3.1-nemotron-70b-instruct",
        "nvidia/nemotron-4-340b-instruct",
        "mistralai/mixtral-8x22b-instruct-v0.1",
        "mistralai/mistral-7b-instruct-v0.3",
        "google/gemma-2-27b-it",
        "google/gemma-2-9b-it",
        "qwen/qwen2.5-coder-32b-instruct",
        "microsoft/phi-3.5-moe-instruct",
        "deepseek-ai/deepseek-r1",
    ];

    private readonly ISettingsStore _settingsStore = new SettingsStore();
    private readonly IConversationStore _conversationStore = new ConversationStore();
    private readonly INvidiaClient _client;
    private readonly UpdateService _updateService = new();
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private readonly List<ChatMessageViewModel> _findMatches = [];

    private AppSettings _settings;
    private CancellationTokenSource? _toastCts;
    private Action? _toastAction;
    private bool _suppressModelSync;
    private bool _refreshingModels;
    private ICollectionView? _conversationsView;
    private ICollectionView? _modelsView;
    private DateTime _modelPickerClosedAt;
    private UpdateInfo? _update;
    private int _searchVersion;
    private int _findIndex = -1;
    private bool _settingsSaveScheduled;
    private bool _swappingDraft;
    private bool _expandedBeforeNarrow;
    private DateTime _lastGroupDate = DateTime.Now.Date;

    // 다시 시도: 마지막으로 실패한 응답과, (429 라면) 다시 보낼 수 있게 되는 시각.
    private ConversationViewModel? _retryConversation;
    private ChatMessageViewModel? _retryMessage;
    private int _retryVersion;

    public MainViewModel()
    {
        // 언어 부트스트랩: 설정을 읽기 전에 OS 언어로 먼저 맞춰, 첫 실행 시 만들어지는 기본값(프리셋 이름 등)과 로드 오류 메시지가 올바른 언어로 나오게 한다.
        LocalizationManager.Instance.Culture = LocalizationManager.ResolveDefaultCulture();
        _settings = _settingsStore.Load();
        InitLanguage();

        ThemeManager.Instance.Apply(_settings.Theme);
        ThemeManager.Instance.Scale = _settings.UiScale;
        IsSidebarCollapsed = _settings.SidebarCollapsed;

        _client = new NvidiaClient(() => _settings);
        _conversationStore.SaveFailed += OnStoreSaveFailed;

        Attachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAttachments));
            SendCommand.NotifyCanExecuteChanged();
        };
    }

    // ===== 이벤트(App/뷰가 구독) =====

    /// <summary>설정 창을 열어달라는 요청.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>설정이 적용되었을 때. App 이 전역 단축키 재등록 등에 사용.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>업데이트 적용 후 현재 인스턴스를 종료해 달라는 요청.</summary>
    public event EventHandler? RequestExitForUpdate;

    /// <summary>응답 생성이 정상적으로 끝났을 때 (대화 제목, 답변 앞부분). App 이 창이 가려져 있으면 알림을 띄운다.</summary>
    public event Action<string, string>? ResponseCompleted;

    /// <summary>확인 대화상자. (제목, 메시지, 확인 버튼 문구) → 확인 여부. App 이 설정한다.</summary>
    public Func<string, string, string, bool>? ConfirmCallback { get; set; }

    // ===== 컬렉션 =====

    public ObservableCollection<ConversationViewModel> Conversations { get; } = [];

    public ObservableCollection<NvModel> Models { get; } = [];

    /// <summary>전송 대기 중인 첨부 이미지.</summary>
    public ObservableCollection<AttachmentViewModel> Attachments { get; } = [];

    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>사이드바용 그룹/정렬/검색 뷰.</summary>
    public ICollectionView ConversationsView => _conversationsView ??= BuildConversationsView();

    /// <summary>모델 선택 팝업용 검색/그룹 뷰.</summary>
    public ICollectionView ModelsView => _modelsView ??= BuildModelsView();

    /// <summary>입력창 프리셋 드롭다운 항목. 프리셋이 없으면 안내 한 줄(비활성).</summary>
    public IReadOnlyList<PresetMenuEntry> PresetMenu => _settings.Presets is { Count: > 0 } presets
        ? presets.Select(p => new PresetMenuEntry(p.Name, p)).ToList()
        : [new PresetMenuEntry(L["PresetsEmpty"], null)];

    // 필드 초기화가 아니라 게터로 둔다. (생성자에서 언어가 정해진 뒤에 평가되도록)
    public IReadOnlyList<string> ExamplePrompts => [L["Example1"], L["Example2"], L["Example3"], L["Example4"]];

    // ===== 바인딩 속성 =====

    [ObservableProperty]
    public partial ConversationViewModel? SelectedConversation { get; set; }

    [ObservableProperty]
    public partial NvModel? SelectedModel { get; set; }

    [ObservableProperty]
    public partial string? ModelSearchText { get; set; }

    [ObservableProperty]
    public partial bool IsModelPickerOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string? InputText { get; set; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshModelsCommand))]
    public partial bool IsModelLoading { get; set; }

    /// <summary>경고/안내 배너 문구(null 이면 숨김).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsAdvancedOpen { get; set; }

    [ObservableProperty]
    public partial bool IsSidebarCollapsed { get; set; }

    /// <summary>창이 좁아 사이드바를 자동으로 접은 상태인지(뷰의 반응형 동작이 설정한다).</summary>
    [ObservableProperty]
    public partial bool IsWindowNarrow { get; set; }

    /// <summary>하단 토스트 문구(복사됨 등). 잠깐 보였다가 사라진다.</summary>
    [ObservableProperty]
    public partial string? ToastText { get; set; }

    [ObservableProperty]
    public partial bool IsToastVisible { get; set; }

    /// <summary>토스트의 동작 버튼 문구(실행 취소 등). 없으면 null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToastAction))]
    public partial string? ToastActionText { get; set; }

    public bool HasToastAction => !string.IsNullOrEmpty(ToastActionText);

    /// <summary>값이 바뀔 때마다 입력창에 포커스(FocusBehavior.FocusToken).</summary>
    [ObservableProperty]
    public partial int InputFocusToken { get; set; }

    /// <summary>값이 바뀔 때마다 대화 검색 상자에 포커스(Ctrl+Shift+F).</summary>
    [ObservableProperty]
    public partial int SearchFocusToken { get; set; }

    /// <summary>값이 바뀔 때마다 대화 내 찾기 상자에 포커스(Ctrl+F).</summary>
    [ObservableProperty]
    public partial int FindFocusToken { get; set; }

    /// <summary>대화 내 찾기 막대가 열려 있는지.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FindHighlight))]
    public partial bool IsFindOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FindHighlight))]
    public partial string? FindText { get; set; }

    /// <summary>"3 / 12" 또는 "결과 없음".</summary>
    [ObservableProperty]
    public partial string? FindStatus { get; set; }

    /// <summary>찾기에서 지금 가리키는 메시지. 뷰가 이 메시지를 화면 안으로 스크롤한다.</summary>
    [ObservableProperty]
    public partial ChatMessageViewModel? FindCurrent { get; set; }

    /// <summary>다시 시도 버튼 문구("다시 시도" 또는 "다시 시도 (12초)"). 다시 시도할 것이 없으면 null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRetry))]
    public partial string? RetryText { get; set; }

    public bool HasRetry => !string.IsNullOrEmpty(RetryText);

    /// <summary>업데이트를 내려받는 중인지.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateIdle))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand), nameof(CheckUpdateCommand))]
    public partial bool IsUpdating { get; private set; }

    /// <summary>다운로드 진행률(0~100).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateProgressText))]
    public partial double UpdateProgress { get; private set; }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>현재 선택한 대화가 스트리밍 중인지. (입력창의 전송/중단 전환에 사용)</summary>
    public bool IsSelectedStreaming => SelectedConversation?.IsStreaming == true;

    /// <summary>메시지 본문에서 강조할 찾기 문자열(찾기 막대가 닫혀 있으면 null).</summary>
    public string? FindHighlight => IsFindOpen && !string.IsNullOrWhiteSpace(FindText) ? FindText : null;

    /// <summary>사이드바 폭(DIP). 사용자가 끌어서 바꾸면 기억한다.</summary>
    public double SidebarWidth
    {
        get => _settings.SidebarWidth;
        set
        {
            double clamped = Math.Clamp(value, AppSettings.MinSidebarWidth, AppSettings.MaxSidebarWidth);
            if (Math.Abs(clamped - _settings.SidebarWidth) < 0.5) return;
            _settings.SidebarWidth = clamped;
            OnPropertyChanged();
            ScheduleSettingsSave();
        }
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public bool SendOnEnter => _settings.SendOnEnter;

    public bool MinimizeToTrayOnClose => _settings.MinimizeToTrayOnClose;

    /// <summary>메인 창 위치/크기 기억값(WindowBehaviors.Placement 가 읽고 쓴다).</summary>
    public WindowPlacement WindowPlacement => _settings.Window ??= new WindowPlacement();

    /// <summary>새 버전이 확인되어 안내 배너를 띄울지 여부.</summary>
    public bool HasUpdate => _update is not null;

    /// <summary>사이드바 하단에 표시할 현재 버전. 예: "v1.9.0".</summary>
    public string AppVersionText => "v" + UpdateService.CurrentVersion.ToString(3);

    /// <summary>"새 버전 v1.4.0 이 있습니다 (12.3 MB)" 형태의 안내.</summary>
    public string? UpdateText => _update is null ? null
        : _update.SizeText is { } size ? L.Tr("UpdateAvailableSize", _update.Tag, size)
        : L.Tr("UpdateAvailable", _update.Tag);

    public bool IsUpdateIdle => !IsUpdating;

    /// <summary>"내려받는 중 45%" 형태의 진행 표시.</summary>
    public string UpdateProgressText => UpdateProgress >= 99.5 ? L["InstallingRestart"] : L.Tr("Downloading", UpdateProgress.ToString("0"));

    partial void OnSelectedConversationChanged(ConversationViewModel? oldValue, ConversationViewModel? newValue)
    {
        SwapDraft(oldValue, newValue);

        // 떠나는 대화의 초안/시스템 프롬프트/파라미터 편집을 저장한다.
        if (oldValue is not null && Conversations.Contains(oldValue) && oldValue.HasUnsavedEdits) SaveConversation(oldValue);

        SyncSelectedModelToConversation();
        OnPropertyChanged(nameof(IsSelectedStreaming));
        RaiseCommandStates();
        if (IsFindOpen) RecomputeFind(keepPosition: false);
    }

    /// <summary>입력창 초안과 첨부를 대화별로 바꿔 끼운다.</summary>
    private void SwapDraft(ConversationViewModel? from, ConversationViewModel? to)
    {
        _swappingDraft = true;
        try
        {
            if (from is not null)
            {
                from.Draft = string.IsNullOrEmpty(InputText) ? null : InputText;
                from.DraftAttachments.Clear();
                from.DraftAttachments.AddRange(Attachments);
            }

            Attachments.Clear();
            InputText = to?.Draft ?? "";
            if (to is not null)
            {
                foreach (var attachment in to.DraftAttachments) Attachments.Add(attachment);
            }
        }
        finally
        {
            _swappingDraft = false;
        }
    }

    partial void OnInputTextChanged(string? value)
    {
        // 초안은 대화에 바로 반영해 둔다(창을 닫거나 앱이 죽어도 마지막 저장분까지는 남도록).
        if (!_swappingDraft && SelectedConversation is { } conversation) conversation.Draft = string.IsNullOrEmpty(value) ? null : value;
    }

    partial void OnSelectedModelChanged(NvModel? value)
    {
        if (_suppressModelSync || value is null) return;
        if (SelectedConversation is { } conversation) conversation.ModelId = value.Id;
        IsModelPickerOpen = false;
    }

    partial void OnModelSearchTextChanged(string? value) => ModelsView.Refresh();

    partial void OnIsModelPickerOpenChanged(bool value)
    {
        if (value) return;
        _modelPickerClosedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(ModelSearchText)) ModelSearchText = "";
    }

    // 본문까지 훑는 검색이라 글자마다 돌리지 않고 입력이 잠깐 멈췄을 때 한 번만 갱신한다.
    partial void OnSearchTextChanged(string? value) => _ = RefreshSearchLaterAsync();

    private async Task RefreshSearchLaterAsync()
    {
        int version = ++_searchVersion;
        await Task.Delay(SearchDebounceMs);
        if (version == _searchVersion) ConversationsView.Refresh();
    }

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        if (_settings.SidebarCollapsed == value || IsWindowNarrow) return;
        _settings.SidebarCollapsed = value;
        ScheduleSettingsSave();
    }

    partial void OnIsWindowNarrowChanged(bool value)
    {
        // 좁아지면 접고, 다시 넓어지면 좁아지기 전 상태로 돌린다. (좁은 상태에서의 수동 토글은 기억하지 않는다)
        if (value)
        {
            _expandedBeforeNarrow = !IsSidebarCollapsed;
            IsSidebarCollapsed = true;
        }
        else if (_expandedBeforeNarrow)
        {
            IsSidebarCollapsed = false;
        }
    }

    partial void OnFindTextChanged(string? value) => RecomputeFind(keepPosition: false);

    private void OnStreamingStateChanged()
    {
        OnPropertyChanged(nameof(IsSelectedStreaming));
        RaiseCommandStates();
    }

    // ===== 수명 주기 =====

    public async Task LoadAsync()
    {
        // 파일 읽기와 JSON 파싱은 UI 스레드 밖에서 한다(기록이 많아도 창이 먼저 뜨도록).
        var loaded = await Task.Run(_conversationStore.LoadAll);

        foreach (var data in loaded) Conversations.Add(ConversationViewModel.FromData(data));

        if (_settingsStore.LoadError is { } settingsError) StatusMessage = settingsError;
        else if (_conversationStore.LoadError is { } conversationError) StatusMessage = conversationError;

        if (Conversations.Count == 0) CreateConversation(select: true);
        else SelectedConversation = Conversations.OrderByDescending(c => c.UpdatedAt).First();

        _ = WatchDateChangeAsync();

        await RefreshModelsAsync();

        if (!HasApiKey)
        {
            StatusMessage = L["NeedApiKeyStart"];
            OpenSettings();
        }

        // 새 버전 확인은 채팅 준비가 끝난 뒤 조용히 진행한다.
        await CheckForUpdateAsync();
    }

    /// <summary>자정이 지나면 사이드바의 날짜 그룹을 다시 계산한다. (절전에서 깨어난 경우도 다음 확인 때 잡힌다)</summary>
    private async Task WatchDateChangeAsync()
    {
        while (true)
        {
            var now = DateTime.Now;
            var untilMidnight = now.Date.AddDays(1) - now + TimeSpan.FromSeconds(2);
            await Task.Delay(untilMidnight < TimeSpan.FromMinutes(30) ? untilMidnight : TimeSpan.FromMinutes(30));
            RefreshDateGroups();
        }
    }

    /// <summary>날짜가 바뀌었으면 모든 대화의 그룹을 다시 계산한다. 창을 다시 띄울 때도 호출한다.</summary>
    public void RefreshDateGroups()
    {
        if (DateTime.Now.Date == _lastGroupDate) return;
        _lastGroupDate = DateTime.Now.Date;
        foreach (var conversation in Conversations) conversation.RefreshGroup();
    }

    /// <summary>
    /// 바뀐 대화를 저장한다. (트레이 최소화 등에서도 호출되므로 스트림은 건드리지 않는다.)
    /// 응답을 받는 중인 대화도 그때까지 받은 만큼 저장해, 그대로 종료되더라도 부분 응답이 남게 한다.
    /// </summary>
    public void SaveState()
    {
        foreach (var conversation in Conversations.Where(c => c.HasUnsavedEdits || c.IsStreaming).ToList()) SaveConversation(conversation);
        if (_settingsSaveScheduled) _settingsStore.Save(_settings);
    }

    /// <summary>대기 중인 저장이 디스크에 내려갈 때까지 기다린다. 실제 앱 종료 경로에서만 호출.</summary>
    public void FlushStorage() => _conversationStore.Flush();

    /// <summary>진행 중인 모든 스트리밍을 취소한다. 실제 앱 종료 경로에서만 호출.</summary>
    public void CancelStreaming()
    {
        foreach (var conversation in Conversations) CancelStream(conversation);
    }

    private static void CancelStream(ConversationViewModel conversation)
    {
        try { conversation.StreamCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public AppSettings GetCurrentSettings() => _settings;

    /// <summary>설정 창의 기본 모델 선택에 쓸 모델 ID 목록.</summary>
    public IReadOnlyList<string> GetModelIds() => Models.Select(m => m.Id).ToList();

    /// <summary>저장된 언어를 적용한다. 첫 실행(빈 값)이면 OS 언어로 기본값을 정해 저장한다.</summary>
    private void InitLanguage()
    {
        if (string.IsNullOrWhiteSpace(_settings.Language))
        {
            _settings.Language = LocalizationManager.ResolveDefaultCulture();
            _settingsStore.Save(_settings);
        }
        LocalizationManager.Instance.Culture = _settings.Language;
    }

    public void ApplySettings(AppSettings settings)
    {
        // 설정 창이 편집하지 않는 값은 기존 것을 유지한다.
        settings.Window = _settings.Window;
        settings.CloseToTrayHintShown = _settings.CloseToTrayHintShown;
        settings.SidebarWidth = _settings.SidebarWidth;
        settings.SidebarCollapsed = _settings.SidebarCollapsed;

        bool connectionChanged = settings.ApiKey != _settings.ApiKey || !string.Equals(settings.BaseUrl, _settings.BaseUrl, StringComparison.OrdinalIgnoreCase);
        bool autoStartChanged = settings.StartWithWindows != _settings.StartWithWindows;

        _settings = settings;
        _settingsStore.Save(_settings);

        // 언어/테마/배율은 라이브 미리보기로 이미 적용돼 있지만, 저장 값 기준으로 한 번 더 확정한다.
        if (!string.IsNullOrWhiteSpace(_settings.Language)) LocalizationManager.Instance.Culture = _settings.Language;
        ThemeManager.Instance.Apply(_settings.Theme);
        ThemeManager.Instance.Scale = _settings.UiScale;

        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(SendOnEnter));
        OnPropertyChanged(nameof(MinimizeToTrayOnClose));
        OnPropertyChanged(nameof(PresetMenu));
        OnPropertyChanged(nameof(ExamplePrompts));
        OnPropertyChanged(nameof(WindowPlacement));
        RaiseCommandStates();

        if (HasStatusMessage && HasApiKey) StatusMessage = null;

        if (autoStartChanged && !AutoStartService.Apply(_settings.StartWithWindows)) StatusMessage = L["AutoStartFailed"];

        SettingsChanged?.Invoke(this, EventArgs.Empty);

        // 모델 목록은 연결 정보가 바뀌었을 때만 다시 받는다(설정을 저장할 때마다 요청을 쓰지 않는다).
        if (connectionChanged || Models.Count == 0) _ = RefreshModelsAsync();
    }

    /// <summary>"창을 닫아도 트레이에서 실행 중" 안내를 보였음을 기록한다(최초 1회만 보이도록).</summary>
    public void MarkCloseToTrayHintShown()
    {
        _settings.CloseToTrayHintShown = true;
        _settingsStore.Save(_settings);
    }

    [RelayCommand]
    private void SaveWindowPlacement() => _settingsStore.Save(_settings);

    /// <summary>사이드바 폭처럼 연속으로 바뀌는 값은 잠깐 모았다가 한 번만 저장한다.</summary>
    private void ScheduleSettingsSave()
    {
        if (_settingsSaveScheduled) return;
        _settingsSaveScheduled = true;
        _ = SaveSettingsLaterAsync();
    }

    private async Task SaveSettingsLaterAsync()
    {
        await Task.Delay(800);
        if (!_settingsSaveScheduled) return;
        _settingsSaveScheduled = false;
        _settingsStore.Save(_settings);
    }

    private void OnStoreSaveFailed(string message)
    {
        void Report() => StatusMessage = L.Tr("ConvSaveFailed", message);

        if (_uiContext is not null) _uiContext.Post(_ => Report(), null);
        else Report();
    }

    // ===== 확대/축소 =====

    [RelayCommand]
    private void ZoomIn() => SetScale(ThemeManager.Instance.Scale + 0.1);

    [RelayCommand]
    private void ZoomOut() => SetScale(ThemeManager.Instance.Scale - 0.1);

    [RelayCommand]
    private void ZoomReset() => SetScale(1.0);

    private void SetScale(double scale)
    {
        ThemeManager.Instance.Scale = scale;
        _settings.UiScale = ThemeManager.Instance.Scale;
        ScheduleSettingsSave();
        ShowToast(L.Tr("ZoomLevel", (ThemeManager.Instance.Scale * 100).ToString("0")));
    }

    // ===== 토스트 =====

    /// <summary>
    /// 하단에 잠깐 떠서 사라지는 안내(복사됨 등). 배너(StatusMessage)는 사용자가 닫아야 하는 경고용이라 구분한다.
    /// <paramref name="action"/> 을 주면 동작 버튼(실행 취소 등)이 함께 뜨고 조금 더 오래 머문다.
    /// </summary>
    public void ShowToast(string text, string? actionText = null, Action? action = null)
    {
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        _toastAction = action;
        ToastActionText = action is null ? null : actionText;
        ToastText = text;
        IsToastVisible = true;
        _ = HideToastLaterAsync(cts, action is null ? ToastMs : ActionToastMs);
    }

    private async Task HideToastLaterAsync(CancellationTokenSource cts, int delayMs)
    {
        try
        {
            await Task.Delay(delayMs, cts.Token);
            IsToastVisible = false;
            _toastAction = null;
        }
        catch (OperationCanceledException) { /* 새 토스트가 이어받았다 */ }
        finally
        {
            if (ReferenceEquals(_toastCts, cts)) _toastCts = null;
            cts.Dispose();
        }
    }

    [RelayCommand]
    private void InvokeToastAction()
    {
        var action = _toastAction;
        _toastAction = null;
        _toastCts?.Cancel();
        IsToastVisible = false;
        action?.Invoke();
    }

    [RelayCommand]
    private void DismissStatus()
    {
        StatusMessage = null;
        ClearRetry();
    }

    // ===== 업데이트 =====

    /// <summary>새 버전이 있는지 조용히 확인한다. (시작 시 1회 + 6시간마다)</summary>
    public async Task CheckForUpdateAsync()
    {
        if (!_settings.AutoCheckUpdates || _update is not null) return;

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
        if (await _updateService.CheckAsync(cts.Token) is { } info) OfferUpdate(info);
    }

    /// <summary>확인된 새 버전을 배너로 안내한다(설정 창의 "지금 확인"에서도 호출).</summary>
    public void OfferUpdate(UpdateInfo info)
    {
        _update = info;
        RaiseUpdateChanged();
    }

    /// <summary>버전 표시를 눌렀을 때 수동으로 새 버전을 확인한다.</summary>
    [RelayCommand(CanExecute = nameof(IsUpdateIdle))]
    private async Task CheckUpdateAsync()
    {
        ShowToast(L["CheckingUpdate"]);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
        var info = await _updateService.CheckAsync(cts.Token);
        if (info is null)
        {
            ShowToast(L.Tr("UpToDate", AppVersionText));
            return;
        }

        OfferUpdate(info);
    }

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (_update is not { } info) return;

        IsUpdating = true;
        UpdateProgress = 0;
        RaiseUpdateChanged();

        try
        {
            Progress<double> progress = new(p => UpdateProgress = p);
            using CancellationTokenSource cts = new(TimeSpan.FromMinutes(15));
            await _updateService.DownloadAndApplyAsync(info, progress, cts.Token);

            // 새 프로세스가 떴으므로 현재 인스턴스는 종료한다.
            RequestExitForUpdate?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = L.Tr("UpdateFailed", ex.Message);
            OpenUrl(UpdateService.ReleasePage);
        }
        finally
        {
            IsUpdating = false;
            RaiseUpdateChanged();
        }
    }

    private bool CanInstallUpdate() => _update is not null && !IsUpdating;

    [RelayCommand]
    private void DismissUpdate()
    {
        _update = null;
        RaiseUpdateChanged();
    }

    /// <summary>새 버전의 릴리스 노트(무엇이 바뀌었는지)를 브라우저로 연다.</summary>
    [RelayCommand]
    private void OpenReleaseNotes() => OpenUrl(UpdateService.ReleasePage);

    private void RaiseUpdateChanged()
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateText));
        InstallUpdateCommand.NotifyCanExecuteChanged();
        CheckUpdateCommand.NotifyCanExecuteChanged();
    }

    // ===== 대화 =====

    private ICollectionView BuildConversationsView()
    {
        var view = CollectionViewSource.GetDefaultView(Conversations);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConversationViewModel.GroupName)));
        view.SortDescriptions.Add(new SortDescription(nameof(ConversationViewModel.GroupOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(ConversationViewModel.UpdatedAt), ListSortDirection.Descending));
        view.Filter = FilterConversation;

        if (view is ICollectionViewLiveShaping live)
        {
            live.IsLiveGrouping = true;
            live.IsLiveSorting = true;
            live.IsLiveFiltering = true;
            foreach (string property in (string[])[nameof(ConversationViewModel.GroupName), nameof(ConversationViewModel.GroupOrder), nameof(ConversationViewModel.UpdatedAt), nameof(ConversationViewModel.Pinned), nameof(ConversationViewModel.Title)])
            {
                live.LiveGroupingProperties.Add(property);
                live.LiveSortingProperties.Add(property);
                live.LiveFilteringProperties.Add(property);
            }
        }

        return view;
    }

    private bool FilterConversation(object item)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        if (item is not ConversationViewModel c) return false;

        string query = SearchText.Trim();
        return c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || c.Messages.Any(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void NewConversation()
    {
        // 이미 빈 대화를 보고 있으면 또 만들지 않는다(빈 "새 대화"가 쌓이지 않도록).
        if (SelectedConversation is not { IsEmpty: true }) CreateConversation(select: true);
        InputFocusToken++;
    }

    private ConversationViewModel CreateConversation(bool select)
    {
        ConversationViewModel conversation = new()
        {
            ModelId = SelectedModel?.Id ?? _settings.DefaultModelId,
            SystemPrompt = _settings.DefaultSystemPrompt,
            Parameters = (_settings.DefaultParameters ?? new GenerationParameters()).Clone(),
        };
        conversation.MarkSaved();

        Conversations.Add(conversation);
        if (select) SelectedConversation = conversation;
        return conversation;
    }

    [RelayCommand(CanExecute = nameof(HasConversation))]
    private void DeleteConversation(ConversationViewModel? conversation)
    {
        if (conversation is null) return;

        // 스트리밍 중인 대화를 지우면 스트림도 함께 취소한다.
        CancelStream(conversation);

        bool wasSelected = ReferenceEquals(conversation, SelectedConversation);
        Conversations.Remove(conversation);
        _conversationStore.Delete(conversation.Id);

        if (Conversations.Count == 0) CreateConversation(select: true);
        else if (wasSelected) SelectedConversation = Conversations.OrderByDescending(c => c.UpdatedAt).First();

        // 확인 대화상자로 막는 대신 지운 뒤 되돌릴 기회를 준다. 내용이 없는 대화는 되돌릴 것도 없다.
        if (conversation.IsEmpty && string.IsNullOrEmpty(conversation.Draft)) return;

        ShowToast(L.Tr("ConversationDeleted", conversation.Title), L["Undo"], () =>
        {
            if (Conversations.Contains(conversation)) return;
            Conversations.Add(conversation);
            SelectedConversation = conversation;
            SaveConversation(conversation);
        });
    }

    [RelayCommand(CanExecute = nameof(HasConversation))]
    private void PinConversation(ConversationViewModel? conversation)
    {
        if (conversation is null) return;
        conversation.Pinned = !conversation.Pinned;
        SaveConversation(conversation);
    }

    [RelayCommand(CanExecute = nameof(HasConversation))]
    private void BeginRename(ConversationViewModel? conversation)
    {
        if (conversation is null) return;
        conversation.RenameText = conversation.Title;
        conversation.IsRenaming = true;
    }

    /// <summary>F2: 선택한 대화의 이름 변경을 시작한다.</summary>
    [RelayCommand]
    private void RenameSelected()
    {
        if (IsSidebarCollapsed) IsSidebarCollapsed = false;
        BeginRename(SelectedConversation);
    }

    [RelayCommand(CanExecute = nameof(HasConversation))]
    private void CommitRename(ConversationViewModel? conversation)
    {
        if (conversation is not { IsRenaming: true }) return;

        string text = (conversation.RenameText ?? "").Trim();
        if (text.Length > 0)
        {
            conversation.Title = text;
            conversation.TitleLocked = true; // 사용자가 지정한 제목은 자동 생성이 덮지 않는다.
        }

        conversation.IsRenaming = false;
        SaveConversation(conversation);
    }

    [RelayCommand(CanExecute = nameof(HasConversation))]
    private void CancelRename(ConversationViewModel? conversation)
    {
        if (conversation is not null) conversation.IsRenaming = false;
    }

    private static bool HasConversation(ConversationViewModel? conversation) => conversation is not null;

    [RelayCommand(CanExecute = nameof(CanClearMessages))]
    private void ClearMessages()
    {
        if (SelectedConversation is not { } conversation) return;
        if (ConfirmCallback is { } confirm && !confirm(L["ClearMessagesTitle"], L["ClearMessagesMsg"], L["Clear"])) return;

        conversation.Messages.Clear();
        conversation.ContextNote = null;
        conversation.UpdatedAt = DateTime.Now;
        RaiseCommandStates();
        SaveConversation(conversation);
    }

    private bool CanClearMessages() => SelectedConversation is { Messages.Count: > 0, IsStreaming: false };

    /// <summary>Ctrl+Shift+F: 사이드바를 펼치고 대화 검색 상자에 포커스.</summary>
    [RelayCommand]
    private void FocusSearch()
    {
        IsSidebarCollapsed = false;
        SearchFocusToken++;
    }

    /// <summary>
    /// 대화 하나를 저장한다. 직렬화용 스냅샷만 여기서(UI 스레드) 만들고 실제 쓰기는 저장소가 백그라운드에서 한다.
    /// 지워진 대화와, 내용도 초안도 없는 빈 대화는 쓰지 않는다.
    /// </summary>
    private void SaveConversation(ConversationViewModel conversation)
    {
        if (!Conversations.Contains(conversation)) return;

        conversation.MarkSaved();
        if (conversation.IsEmpty && string.IsNullOrEmpty(conversation.Draft)) return;

        try
        {
            // 받는 중인 응답은 화면에 아직 반영하지 않은 조각까지 포함해 저장한다.
            if (conversation.IsStreaming && conversation.Messages.LastOrDefault() is { IsStreaming: true } streaming) streaming.FlushPending();

            var data = conversation.ToData();

            // 받는 중이던 마지막 메시지는, 이 저장본이 마지막이 될 경우(종료)를 대비해 '중단됨'으로 적어 둔다. 아직 아무것도 못 받았으면 뺀다.
            // (스트림이 정상적으로 끝나면 그때 다시 저장되어 덮어쓴다.)
            if (conversation.IsStreaming && data.Messages is [.., { Role: ChatRole.Assistant } last])
            {
                if (string.IsNullOrEmpty(last.Content)) data.Messages.RemoveAt(data.Messages.Count - 1);
                else last.Aborted = true;
            }

            _conversationStore.Save(data);
        }
        catch { /* 저장 실패가 흐름을 막지 않도록 무시. */ }
    }

    // ===== 대화 내 찾기 =====

    /// <summary>Ctrl+F: 지금 대화에서 찾기.</summary>
    [RelayCommand]
    private void OpenFind()
    {
        IsFindOpen = true;
        FindFocusToken++;
        RecomputeFind(keepPosition: true);
    }

    [RelayCommand]
    private void CloseFind()
    {
        IsFindOpen = false;
        SetFindCurrent(null);
        _findMatches.Clear();
        _findIndex = -1;
        FindStatus = null;
        InputFocusToken++;
    }

    [RelayCommand]
    private void FindNext() => MoveFind(+1);

    [RelayCommand]
    private void FindPrevious() => MoveFind(-1);

    private void MoveFind(int step)
    {
        if (_findMatches.Count == 0) return;
        _findIndex = ((_findIndex + step) % _findMatches.Count + _findMatches.Count) % _findMatches.Count;
        UpdateFindCurrent();
    }

    private void RecomputeFind(bool keepPosition)
    {
        var previous = keepPosition ? FindCurrent : null;
        _findMatches.Clear();
        _findIndex = -1;

        string query = (FindText ?? "").Trim();
        if (!IsFindOpen || query.Length == 0 || SelectedConversation is not { } conversation)
        {
            SetFindCurrent(null);
            FindStatus = null;
            return;
        }

        _findMatches.AddRange(conversation.Messages.Where(m =>
            m.Content.Contains(query, StringComparison.OrdinalIgnoreCase) || (m.Reasoning?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));

        if (_findMatches.Count == 0)
        {
            SetFindCurrent(null);
            FindStatus = L["FindNoResults"];
            return;
        }

        // 처음 찾을 때는 가장 최근 일치(맨 아래)부터 보여 준다.
        int kept = previous is null ? -1 : _findMatches.IndexOf(previous);
        _findIndex = kept >= 0 ? kept : _findMatches.Count - 1;
        UpdateFindCurrent();
    }

    private void UpdateFindCurrent()
    {
        SetFindCurrent(_findMatches[_findIndex]);
        FindStatus = $"{_findIndex + 1} / {_findMatches.Count}";
    }

    private void SetFindCurrent(ChatMessageViewModel? message)
    {
        if (FindCurrent is { } old) old.IsFindCurrent = false;
        FindCurrent = message;
        if (message is not null) message.IsFindCurrent = true;
    }

    // ===== 모델 =====

    [RelayCommand(CanExecute = nameof(CanRefreshModels))]
    private async Task RefreshModelsAsync()
    {
        if (_refreshingModels) return;

        if (!HasApiKey)
        {
            ReplaceModels(BuildFallbackModels());
            SyncSelectedModelToConversation();
            return;
        }

        _refreshingModels = true;
        IsModelLoading = true;
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            var models = await _client.GetModelsAsync(cts.Token);
            ReplaceModels(models.Count > 0 ? models : BuildFallbackModels());
        }
        catch (Exception ex)
        {
            ReplaceModels(BuildFallbackModels());
            StatusMessage = L.Tr("ModelsLoadFailed", ex.Message);
        }
        finally
        {
            IsModelLoading = false;
            _refreshingModels = false;
            SyncSelectedModelToConversation();
        }
    }

    private bool CanRefreshModels() => !IsModelLoading;

    private static List<NvModel> BuildFallbackModels() => FallbackModelIds.Select(NvModel.FromId).ToList();

    private void ReplaceModels(IReadOnlyList<NvModel> models)
    {
        // 목록을 비우는 순간 ListBox 가 선택을 null 로 되돌려 쓰므로, 그 사이의 선택 변경은 대화에 반영하지 않는다.
        _suppressModelSync = true;
        try
        {
            Models.Clear();
            foreach (var model in models) Models.Add(model);
        }
        finally
        {
            _suppressModelSync = false;
        }
    }

    private void SyncSelectedModelToConversation()
    {
        string? desiredId = SelectedConversation?.ModelId;
        if (string.IsNullOrWhiteSpace(desiredId)) desiredId = _settings.DefaultModelId;

        NvModel? target = null;
        if (!string.IsNullOrWhiteSpace(desiredId))
        {
            target = Models.FirstOrDefault(m => string.Equals(m.Id, desiredId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                target = NvModel.FromId(desiredId);
                Models.Insert(0, target);
            }
        }
        else if (Models.Count > 0)
        {
            target = Models[0];
        }

        _suppressModelSync = true;
        SelectedModel = target;
        _suppressModelSync = false;

        if (SelectedConversation is { } conversation && string.IsNullOrWhiteSpace(conversation.ModelId) && target is not null) conversation.ModelId = target.Id;
    }

    private ICollectionView BuildModelsView()
    {
        var view = CollectionViewSource.GetDefaultView(Models);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NvModel.Publisher)));
        view.SortDescriptions.Add(new SortDescription(nameof(NvModel.Publisher), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(NvModel.Name), ListSortDirection.Ascending));
        view.Filter = item => string.IsNullOrWhiteSpace(ModelSearchText) || (item is NvModel model && model.Id.Contains(ModelSearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        return view;
    }

    [RelayCommand]
    private void ToggleModelPicker()
    {
        // Popup(StaysOpen=False)이 바깥 클릭으로 방금 닫힌 직후의 버튼 클릭은 무시한다. (아니면 닫혔다가 곧바로 다시 열려 버튼으로는 닫을 수 없다.)
        if (!IsModelPickerOpen && (DateTime.UtcNow - _modelPickerClosedAt).TotalMilliseconds < 300) return;
        IsModelPickerOpen = !IsModelPickerOpen;
    }

    /// <summary>이미 선택된 모델을 다시 클릭해도(선택 변경 이벤트가 없어도) 팝업이 닫히게 한다.</summary>
    [RelayCommand]
    private void CloseModelPicker() => IsModelPickerOpen = false;

    /// <summary>검색 상자에서 Enter: 필터 결과의 첫 모델을 선택한다.</summary>
    [RelayCommand]
    private void SelectFirstModel()
    {
        if (ModelsView.Cast<NvModel>().FirstOrDefault() is { } first) SelectedModel = first;
        IsModelPickerOpen = false;
    }

    // ===== 채팅 =====

    private bool CanSend() => SelectedConversation is { IsStreaming: false } && (!string.IsNullOrWhiteSpace(InputText) || Attachments.Count > 0);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        string text = (InputText ?? "").Trim();
        bool hasImages = Attachments.Count > 0;
        if ((text.Length == 0 && !hasImages) || SelectedConversation is not { IsStreaming: false } conversation) return;

        if (!HasApiKey)
        {
            StatusMessage = L["NeedApiKey"];
            OpenSettings();
            return;
        }

        ChatMessageViewModel userMessage = new(ChatRole.User, text) { IsFresh = true };
        if (hasImages) userMessage.SetImages(Attachments.Select(a => a.DataUri).ToList());

        conversation.Messages.Add(userMessage);
        InputText = "";
        Attachments.Clear();
        conversation.Draft = null;
        conversation.DraftAttachments.Clear();

        if (conversation.Messages.Count(m => m.IsUser) == 1 && !conversation.TitleLocked) conversation.Title = text.Length > 0 ? MakeTitle(text) : L["ImageConversation"];

        await GenerateResponseAsync(conversation);
    }

    [RelayCommand(CanExecute = nameof(IsSelectedStreaming))]
    private void Stop()
    {
        if (SelectedConversation is { } conversation) CancelStream(conversation);
    }

    [RelayCommand(CanExecute = nameof(CanAttach))]
    private void AttachImage()
    {
        string patterns = string.Join(";", ImageUtil.SupportedExtensions.Select(e => "*" + e));
        OpenFileDialog dialog = new()
        {
            Multiselect = true,
            Filter = $"{L["FilterImages"]} ({patterns})|{patterns}|{L["FilterAllFiles"]} (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        foreach (string file in dialog.FileNames) AttachFile(file);
    }

    /// <summary>파일 드롭: 이미지 파일만 첨부한다.</summary>
    [RelayCommand(CanExecute = nameof(CanAttach))]
    private void AttachFiles(string[]? files)
    {
        if (files is null) return;

        var images = files.Where(ImageUtil.IsSupportedFile).ToList();
        if (images.Count == 0)
        {
            ShowToast(L.Tr("UnsupportedImageFiles", string.Join(" ", ImageUtil.SupportedExtensions)));
            return;
        }

        foreach (string file in images) AttachFile(file);
    }

    /// <summary>클립보드 이미지 붙여넣기(Ctrl+V).</summary>
    [RelayCommand(CanExecute = nameof(CanAttach))]
    private void PasteImage(BitmapSource? image)
    {
        if (image is null) return;
        try { AddAttachment(ImageUtil.BitmapToDataUri(image)); }
        catch (Exception ex) { StatusMessage = L.Tr("ImageLoadFailed", ex.Message); }
    }

    private bool CanAttach() => SelectedConversation is not null;

    private void AttachFile(string path)
    {
        try { AddAttachment(ImageUtil.FileToDataUri(path)); }
        catch (Exception ex) { StatusMessage = L.Tr("ImageLoadFailed", ex.Message); }
    }

    private void AddAttachment(string dataUri)
    {
        Attachments.Add(new AttachmentViewModel(dataUri, ImageUtil.FromDataUri(dataUri, decodeWidth: 160)));
        InputFocusToken++;
    }

    [RelayCommand]
    private void RemoveAttachment(AttachmentViewModel? attachment)
    {
        if (attachment is not null) Attachments.Remove(attachment);
    }

    [RelayCommand]
    private void UseExamplePrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return;
        InputText = prompt;
        if (SendCommand.CanExecute(null)) SendCommand.Execute(null);
    }

    /// <summary>index 뒤의 메시지들을 대화에서 떼어 내 돌려준다(버전의 "뒤쪽 대화"로 보관하기 위해).</summary>
    private static List<ChatMessage> TakeTail(ConversationViewModel conversation, int index)
    {
        List<ChatMessage> tail = conversation.Messages.Skip(index + 1).Select(m => m.ToData()).ToList();
        for (int i = conversation.Messages.Count - 1; i > index; i--) conversation.Messages.RemoveAt(i);
        return tail;
    }

    [RelayCommand(CanExecute = nameof(CanActOnMessage))]
    private void RegenerateMessage(ChatMessageViewModel? message)
    {
        if (message is not { IsAssistant: true } || SelectedConversation is not { IsStreaming: false } conversation) return;

        int index = conversation.Messages.IndexOf(message);
        if (index < 0) return;

        // 이전 답변과 그 뒤로 이어진 대화는 지우지 않고 이 자리의 다른 버전으로 보관한다(‹ 1/2 › 로 오갈 수 있다).
        // 오류 안내뿐이던 자리는 보관할 답이 없으므로 그 자리에서 다시 받는다.
        var tail = TakeTail(conversation, index);
        if (!message.HasError || tail.Count > 0) message.PushNewVariant(tail);

        message.ResetForRegeneration();
        ClearRetry();
        _ = GenerateResponseAsync(conversation, message);
    }

    [RelayCommand(CanExecute = nameof(CanRegenerateLast))]
    private void RegenerateLast()
    {
        if (SelectedConversation?.Messages is { Count: > 0 } messages && messages[^1] is { IsAssistant: true } last) RegenerateMessage(last);
    }

    private bool CanRegenerateLast() => SelectedConversation is { IsStreaming: false, Messages: { Count: > 0 } messages } && messages[^1].IsAssistant;

    [RelayCommand(CanExecute = nameof(CanActOnMessage))]
    private void DeleteMessage(ChatMessageViewModel? message)
    {
        if (message is null || SelectedConversation is not { IsStreaming: false } conversation) return;

        int index = conversation.Messages.IndexOf(message);
        if (index < 0) return;

        conversation.Messages.RemoveAt(index);
        conversation.UpdatedAt = DateTime.Now;
        RaiseCommandStates();
        SaveConversation(conversation);

        ShowToast(L["MessageDeleted"], L["Undo"], () =>
        {
            if (!Conversations.Contains(conversation) || conversation.Messages.Contains(message)) return;
            conversation.Messages.Insert(Math.Min(index, conversation.Messages.Count), message);
            RaiseCommandStates();
            SaveConversation(conversation);
        });
    }

    [RelayCommand]
    private void CopyMessage(ChatMessageViewModel? message)
    {
        if (message is null) return;
        if (TrySetClipboard(message.Content)) ShowToast(L["CopiedMessage"]);
    }

    [RelayCommand(CanExecute = nameof(CanActOnMessage))]
    private void SubmitEdit(ChatMessageViewModel? message)
    {
        if (message is null || SelectedConversation is not { IsStreaming: false } conversation) return;

        int index = conversation.Messages.IndexOf(message);
        if (index < 0) return;

        string text = (message.EditText ?? "").Trim();
        if (text.Length == 0)
        {
            message.IsEditing = false;
            return;
        }

        message.IsEditing = false;

        // 고치기 전의 메시지와 그 뒤의 대화는 이 자리의 이전 버전으로 남긴다.
        var tail = TakeTail(conversation, index);
        if (tail.Count > 0 || !string.Equals(text, message.Content, StringComparison.Ordinal)) message.PushNewVariant(tail);

        message.Content = text;
        message.Timestamp = DateTime.Now;
        _ = GenerateResponseAsync(conversation);
    }

    [RelayCommand(CanExecute = nameof(CanActOnMessage))]
    private void PreviousVariant(ChatMessageViewModel? message) => SwitchVariant(message, -1);

    [RelayCommand(CanExecute = nameof(CanActOnMessage))]
    private void NextVariant(ChatMessageViewModel? message) => SwitchVariant(message, +1);

    /// <summary>메시지의 다른 버전으로 전환한다. 그 버전에서 이어졌던 뒤쪽 대화도 함께 바뀐다.</summary>
    private void SwitchVariant(ChatMessageViewModel? message, int step)
    {
        if (message is null || SelectedConversation is not { IsStreaming: false } conversation) return;

        int index = conversation.Messages.IndexOf(message);
        int target = message.ActiveVariant + step;
        if (index < 0 || target < 0 || target >= message.Variants.Count) return;

        var newTail = message.SwitchVariant(target, TakeTail(conversation, index));
        foreach (var data in newTail) conversation.Messages.Add(ChatMessageViewModel.FromData(data));

        RaiseCommandStates();
        SaveConversation(conversation);
        if (IsFindOpen) RecomputeFind(keepPosition: false);
    }

    private bool CanActOnMessage(ChatMessageViewModel? message) => message is not null && SelectedConversation is { IsStreaming: false };

    /// <summary>
    /// 대화의 현재 메시지들에 대해 어시스턴트 응답을 스트리밍한다.
    /// <paramref name="target"/> 이 있으면 새 메시지를 덧붙이는 대신 그 메시지 자리에 받는다(다시 생성).
    /// 스트림 상태는 대화마다 따로 가지므로, 다른 대화가 생성 중이어도 시작할 수 있다.
    /// </summary>
    private async Task GenerateResponseAsync(ConversationViewModel conversation, ChatMessageViewModel? target = null)
    {
        if (conversation.IsStreaming) return;

        if (!HasApiKey)
        {
            StatusMessage = L["NeedApiKey"];
            OpenSettings();
            return;
        }

        bool isSelected = ReferenceEquals(conversation, SelectedConversation);
        string modelId = (isSelected ? SelectedModel?.Id : null) ?? conversation.ModelId ?? _settings.DefaultModelId;
        conversation.ModelId = modelId;

        var history = target is null ? conversation.Messages.ToList() : conversation.Messages.TakeWhile(m => !ReferenceEquals(m, target)).ToList();
        var apiMessages = BuildApiMessages(conversation, history);

        ChatMessageViewModel assistant = target ?? new ChatMessageViewModel(ChatRole.Assistant, "") { IsFresh = true };
        assistant.ModelId = modelId;
        assistant.IsStreaming = true;
        if (target is null) conversation.Messages.Add(assistant);

        CancellationTokenSource cts = new();
        conversation.StreamCts = cts;
        conversation.IsStreaming = true;
        OnStreamingStateChanged();
        if (isSelected)
        {
            StatusMessage = null;
            ClearRetry();
        }

        ThinkTagSplitter splitter = new();
        Stopwatch stopwatch = Stopwatch.StartNew();
        long lastFlush = 0;
        bool aborted = false;
        bool usageRecorded = false;
        bool received = false;
        Exception? failure = null;

        try
        {
            string? finishReason = null;

            await foreach (var delta in _client.StreamChatAsync(modelId, apiMessages, conversation.Parameters.Clone(), cts.Token))
            {
                if (delta.Usage.HasTokens)
                {
                    usageRecorded = true;
                    conversation.AddUsage(modelId, delta.Usage.PromptTokens, delta.Usage.CompletionTokens, true);
                    assistant.OutputTokens = delta.Usage.CompletionTokens;
                }

                if (!string.IsNullOrEmpty(delta.FinishReason)) finishReason = delta.FinishReason;

                if (!string.IsNullOrEmpty(delta.Reasoning))
                {
                    received = true;
                    assistant.AppendReasoning(delta.Reasoning);
                }

                if (!string.IsNullOrEmpty(delta.Content))
                {
                    // 본문에 인라인으로 섞여 오는 <think> 블록은 도착하는 대로 추론 쪽으로 가른다.
                    var (content, reasoning) = splitter.Push(delta.Content);
                    if (reasoning.Length > 0) assistant.AppendReasoning(reasoning);
                    if (content.Length > 0) assistant.AppendContent(content);
                    received |= content.Length > 0 || reasoning.Length > 0;
                }

                // 화면 갱신은 일정 주기로 묶는다. 느린 스트림은 델타마다, 빠른 스트림은 주기마다 한 번.
                if (stopwatch.ElapsedMilliseconds - lastFlush >= StreamFlushIntervalMs)
                {
                    assistant.FlushPending();
                    lastFlush = stopwatch.ElapsedMilliseconds;
                }
            }

            FinishStreamText(assistant, splitter);

            // 내용이 하나도 오지 않은 경우. 자동 재전송은 하지 않는다(사용자의 무료 호출 한도를 말없이 소모하므로).
            // 대신 사유를 밝히고, '다시 시도' 로 사용자가 직접 재시도하게 안내한다.
            if (string.IsNullOrEmpty(assistant.Content))
            {
                assistant.HasError = true;
                assistant.Content = DescribeEmptyResponse(finishReason, assistant.HasReasoning);
                OfferRetry(conversation, assistant, L["EmptyResponseHint"], retryAfterSeconds: null);
            }
        }
        catch (OperationCanceledException)
        {
            aborted = true;
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            aborted = true; // 취소 도중의 소켓 오류 등도 사용자의 중단으로 본다
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            // 서버가 토큰 수를 안 주거나 중간에 끊겨도 요청 한 건은 실제로 나갔으므로 횟수는 남긴다.
            if (!usageRecorded) conversation.AddUsage(modelId, 0, 0, false);

            if (aborted || failure is not null) FinishStreamText(assistant, splitter);

            if (failure is not null)
            {
                // 받다가 끊긴 본문은 버리지 않는다. 받은 만큼 남기고 '중단됨'으로 표시한 뒤 사유는 배너로 알린다.
                if (string.IsNullOrEmpty(assistant.Content))
                {
                    assistant.HasError = true;
                    assistant.Content = L.Tr("ErrorPrefix", failure.Message);
                }
                else
                {
                    assistant.Aborted = true;
                }

                var apiError = failure as NvidiaApiException;
                string message = isSelected ? failure.Message : L.Tr("ErrorInConversation", conversation.Title, failure.Message);
                OfferRetry(conversation, assistant, message, apiError is { IsRateLimited: true } ? apiError.RetryAfterSeconds ?? 0 : null);
            }

            if (aborted)
            {
                if (!string.IsNullOrEmpty(assistant.Content))
                {
                    // 부분 응답이 있으면 유지하되 '중단됨'만 표시.
                    assistant.Aborted = true;
                }
                else if (target is null)
                {
                    conversation.Messages.Remove(assistant);
                }
                else if (assistant.DiscardActiveVariant() is { } restoredTail)
                {
                    // 빈 채로 중단된 다시 생성은 없던 일로 하고 직전 버전(과 그 뒤의 대화)을 되돌린다.
                    foreach (var data in restoredTail) conversation.Messages.Add(ChatMessageViewModel.FromData(data));
                }
                else
                {
                    assistant.HasError = true;
                    assistant.Content = L["RegenCancelled"];
                }
            }

            assistant.DurationMs = stopwatch.ElapsedMilliseconds;
            assistant.IsStreaming = false;
            assistant.Timestamp = DateTime.Now;

            conversation.IsStreaming = false;
            conversation.StreamCts = null;
            cts.Dispose();

            conversation.UpdatedAt = DateTime.Now;
            OnStreamingStateChanged();
            SaveConversation(conversation);
            if (IsFindOpen && ReferenceEquals(conversation, SelectedConversation)) RecomputeFind(keepPosition: true);
        }

        if (aborted || failure is not null || assistant.HasError || !received) return;

        if (_settings.NotifyOnComplete) ResponseCompleted?.Invoke(conversation.Title, Preview(assistant.Content));

        // 첫 응답이 정상 완료되면 모델로 제목 자동 생성(비차단).
        _ = MaybeGenerateTitleAsync(conversation, modelId);
    }

    /// <summary>스트림이 끝났을 때(정상/중단/오류 공통) 남은 조각을 반영하고 추론 태그를 마무리한다.</summary>
    private static void FinishStreamText(ChatMessageViewModel assistant, ThinkTagSplitter splitter)
    {
        var (content, reasoning) = splitter.Flush();
        if (reasoning.Length > 0) assistant.AppendReasoning(reasoning);
        if (content.Length > 0) assistant.AppendContent(content);
        assistant.FlushPending();

        // 여는 태그 없이 </think> 만 보내는 모델: 닫는 태그 앞쪽을 통째로 추론으로 옮긴다.
        if (ThinkTagSplitter.SplitOrphanClose(assistant.Content) is { } split)
        {
            assistant.Reasoning = string.IsNullOrEmpty(assistant.Reasoning) ? split.Reasoning : assistant.Reasoning + "\n" + split.Reasoning;
            assistant.Content = split.Content;
        }
    }

    private static string Preview(string text)
    {
        string flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length > 120 ? flat[..120].TrimEnd() + "…" : flat;
    }

    /// <summary>본문이 비어 있는 응답의 사유를 finish_reason 으로 설명한다.</summary>
    private static string DescribeEmptyResponse(string? finishReason, bool hadReasoning) => finishReason?.ToLowerInvariant() switch
    {
        "content_filter" => L["EmptyContentFilter"],
        "length" => L["EmptyLength"],
        _ when hadReasoning => L["EmptyLength"], // 추론만 하다 끝난 경우는 사실상 출력 토큰 부족이다
        _ => L["EmptyGeneric"],
    };

    // ===== 다시 시도 =====

    /// <summary>실패한 응답에 대해 배너에 '다시 시도' 버튼을 건다. 429 면 서버가 알려 준 시간만큼 카운트다운한 뒤 활성화한다.</summary>
    private void OfferRetry(ConversationViewModel conversation, ChatMessageViewModel message, string statusMessage, int? retryAfterSeconds)
    {
        StatusMessage = statusMessage;

        // 본문이 일부라도 남은 메시지는 '다시 생성' 버튼으로 버전을 만들면 된다. 배너의 다시 시도는 오류 자리만 대상으로 한다.
        if (!message.HasError)
        {
            ClearRetry();
            return;
        }

        _retryConversation = conversation;
        _retryMessage = message;
        int version = ++_retryVersion;

        if (retryAfterSeconds is > 0 and var wait) _ = RunRetryCountdownAsync(Math.Min(wait, 600), version);
        else RetryText = L["Retry"];

        RetryCommand.NotifyCanExecuteChanged();
    }

    private async Task RunRetryCountdownAsync(int seconds, int version)
    {
        for (int remaining = seconds; remaining > 0; remaining--)
        {
            if (version != _retryVersion) return;
            RetryText = L.Tr("RetryIn", remaining);
            RetryCommand.NotifyCanExecuteChanged();
            await Task.Delay(1000);
        }

        if (version != _retryVersion) return;
        RetryText = L["Retry"];
        RetryCommand.NotifyCanExecuteChanged();
    }

    private void ClearRetry()
    {
        _retryVersion++;
        _retryConversation = null;
        _retryMessage = null;
        RetryText = null;
        RetryCommand.NotifyCanExecuteChanged();
    }

    private bool CanRetry() =>
        _retryConversation is { IsStreaming: false } conversation && _retryMessage is { } message && conversation.Messages.Contains(message) && RetryText == L["Retry"];

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry()
    {
        if (_retryConversation is not { } conversation || _retryMessage is not { } message) return;

        ClearRetry();
        StatusMessage = null;
        if (!ReferenceEquals(conversation, SelectedConversation)) SelectedConversation = conversation;
        RegenerateMessage(message);
    }

    // ===== 제목 =====

    private async Task MaybeGenerateTitleAsync(ConversationViewModel conversation, string modelId)
    {
        if (!_settings.GenerateTitles) return;

        // 사용자가 직접 정했거나 이미 자동 생성이 끝난 제목은 다시 덮어쓰지 않는다(재생성 포함).
        if (conversation.TitleLocked) return;
        if (conversation.Messages.Count(m => m.IsAssistant) != 1) return;

        string? firstUser = conversation.Messages.FirstOrDefault(m => m.IsUser)?.Content;
        if (string.IsNullOrWhiteSpace(firstUser)) return;

        try
        {
            // 메시지를 그대로 user 턴으로 주면 작은 모델은 제목 대신 그 질문에 답해 버린다.
            // 구분자로 감싸 "이것은 답할 대상이 아니라 제목을 붙일 자료"임을 분명히 한다.
            string excerpt = firstUser.Length > 500 ? firstUser[..500] : firstUser;
            List<ChatMessage> prompt =
            [
                new() { Role = ChatRole.System, Content = L["AutoTitlePrompt"] },
                new() { Role = ChatRole.User, Content = L.Tr("AutoTitleUser", excerpt) },
            ];

            // 추론 모델은 사고 과정에 토큰을 먼저 쓰므로, 제목 몇 단어라도 한도는 넉넉히 준다.
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            var outcome = await _client.CompleteChatAsync(modelId, prompt, new GenerationParameters { MaxTokens = 256, Temperature = 0.2, TopP = 1.0 }, cts.Token);

            // 제목 생성도 실제로 나간 요청이므로 이 대화의 사용량에 포함한다.
            conversation.AddUsage(modelId, outcome.Usage.PromptTokens, outcome.Usage.CompletionTokens, outcome.Usage.HasTokens);

            // 쓸 만한 제목을 얻지 못했어도(null) 같은 요청을 되풀이하지 않도록 확정한다. 이미 첫 메시지 요약 제목이 있다.
            if (!conversation.TitleLocked && TitleCleaner.Clean(outcome.Content) is { } title) conversation.Title = title;
            conversation.TitleLocked = true;
            SaveConversation(conversation);
        }
        catch { /* 제목 생성 실패는 무시(이미 요약 제목이 있음). */ }
    }

    private List<ChatMessage> BuildApiMessages(ConversationViewModel conversation, IReadOnlyList<ChatMessageViewModel> history)
    {
        List<ChatMessage> messages = [];

        string system = ComposeSystemPrompt(conversation);
        if (!string.IsNullOrWhiteSpace(system)) messages.Add(new ChatMessage { Role = ChatRole.System, Content = system });

        foreach (var message in history)
        {
            if (message.IsStreaming || message.HasError) continue;
            if (string.IsNullOrEmpty(message.Content) && !message.HasImages) continue;

            messages.Add(new ChatMessage
            {
                Role = message.Role,
                Content = message.Content,
                Images = message.HasImages ? message.Images.ToList() : null,
            });
        }

        // 컨텍스트 한도: 출력에 쓸 몫을 남기고, 넘치면 오래된 메시지부터 전송에서 뺀다.
        int limit = _settings.ContextTokenLimit;
        int budget = limit <= 0 ? 0 : Math.Max(limit / 2, limit - conversation.Parameters.MaxTokens);
        var (kept, dropped, _) = ContextWindow.Trim(messages, budget);
        conversation.ContextNote = dropped > 0 ? L.Tr("ContextTrimmed", dropped) : null;
        return kept;
    }

    /// <summary>커스텀 지침(나에 대해 + 응답 방식) + 대화별 시스템 프롬프트를 합친다.</summary>
    private string ComposeSystemPrompt(ConversationViewModel conversation)
    {
        List<string> parts = [];

        if (_settings.AboutYou.Trim() is { Length: > 0 } about) parts.Add(L["SysAboutYou"] + "\n" + about);
        if (_settings.ResponseStyle.Trim() is { Length: > 0 } style) parts.Add(L["SysResponseStyle"] + "\n" + style);
        if (conversation.SystemPrompt.Trim() is { Length: > 0 } system) parts.Add(system);

        return string.Join("\n\n", parts);
    }

    [RelayCommand]
    private void InsertPreset(PromptPreset? preset)
    {
        if (preset is null) return;
        InputText = string.IsNullOrEmpty(InputText) ? preset.Text : InputText + "\n" + preset.Text;
        InputFocusToken++;
    }

    private static string MakeTitle(string text)
    {
        string firstLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (firstLine.Length == 0) return L["NewChat"];
        return firstLine.Length > TitleMaxLength ? firstLine[..TitleMaxLength].TrimEnd() + "…" : firstLine;
    }

    // ===== 내보내기 / 백업 =====

    [RelayCommand(CanExecute = nameof(HasMessages))]
    private void CopyConversation()
    {
        if (SelectedConversation is not { } conversation) return;
        if (TrySetClipboard(BuildConversationMarkdown(conversation, includeImages: false))) ShowToast(L["CopiedConversation"]);
    }

    [RelayCommand(CanExecute = nameof(HasMessages))]
    private void ExportConversation()
    {
        if (SelectedConversation is not { } conversation) return;

        SaveFileDialog dialog = new()
        {
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            FileName = SanitizeFileName(conversation.Title) + ".md",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, BuildConversationMarkdown(conversation, includeImages: true), Encoding.UTF8);
            ShowToast(L.Tr("ExportedTo", Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex)
        {
            StatusMessage = L.Tr("ExportFailed", ex.Message);
        }
    }

    private bool HasMessages() => SelectedConversation is { Messages.Count: > 0 };

    /// <summary>모든 대화를 JSON 파일 하나로 백업한다(다른 PC 로 옮기거나 보관용).</summary>
    [RelayCommand]
    private void BackupAll()
    {
        var conversations = Conversations.Where(c => !c.IsEmpty).Select(c => c.ToData()).ToList();
        if (conversations.Count == 0)
        {
            ShowToast(L["BackupNothing"]);
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "JSON (*.json)|*.json",
            FileName = $"NvChat-backup-{DateTime.Now:yyyyMMdd}.json",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            AtomicFile.WriteAllBytes(dialog.FileName, JsonSerializer.SerializeToUtf8Bytes(conversations, BackupJsonOptions));
            ShowToast(L.Tr("BackupSaved", conversations.Count));
        }
        catch (Exception ex)
        {
            StatusMessage = L.Tr("ExportFailed", ex.Message);
        }
    }

    /// <summary>백업 JSON 에서 대화를 가져온다. 이미 있는 대화(같은 ID)는 건너뛴다.</summary>
    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        OpenFileDialog dialog = new() { Filter = $"JSON (*.json)|*.json|{L["FilterAllFiles"]} (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            string path = dialog.FileName;
            var imported = await Task.Run(() => ReadBackup(path));

            HashSet<string> existing = new(Conversations.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            int skipped = 0;

            foreach (var data in imported)
            {
                data.Id = ConversationStore.NormalizeId(data.Id);
                if (data.Messages is not { Count: > 0 } || !existing.Add(data.Id))
                {
                    skipped++;
                    continue;
                }

                var conversation = ConversationViewModel.FromData(data);
                Conversations.Add(conversation);
                SaveConversation(conversation);
                added++;
            }

            ShowToast(skipped > 0 ? L.Tr("ImportDoneSkipped", added, skipped) : L.Tr("ImportDone", added));
        }
        catch (Exception ex)
        {
            StatusMessage = L.Tr("ImportFailed", ex.Message);
        }
    }

    /// <summary>백업 파일을 읽는다. 대화 배열(이 앱의 백업/1.8 의 conversations.json)과 대화 하나짜리 파일을 모두 받는다.</summary>
    private static List<Conversation> ReadBackup(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => document.RootElement.Deserialize<List<Conversation>>(BackupJsonOptions) ?? [],
            JsonValueKind.Object when document.RootElement.Deserialize<Conversation>(BackupJsonOptions) is { } single => [single],
            _ => [],
        };
    }

    private static string BuildConversationMarkdown(ConversationViewModel conversation, bool includeImages)
    {
        StringBuilder sb = new();
        sb.AppendLine("# " + conversation.Title).AppendLine();

        foreach (var m in conversation.Messages)
        {
            string who = m.IsUser ? L["RoleUser"] : m.IsAssistant ? L["RoleAssistant"] : L["RoleSystem"];
            if (m.IsAssistant && NvModel.FromId(m.ModelId).Name is { Length: > 0 } model) who += $" ({model})";
            sb.AppendLine("### " + who).AppendLine();

            if (m.HasImages)
            {
                if (includeImages)
                {
                    foreach (string image in m.Images) sb.AppendLine($"![image]({image})").AppendLine();
                }
                else
                {
                    sb.AppendLine(L.Tr("ExportImageCount", m.Images.Count)).AppendLine();
                }
            }

            if (m.HasReasoning)
            {
                sb.AppendLine("<details>").AppendLine($"<summary>{L["Reasoning"]}</summary>").AppendLine();
                sb.AppendLine(m.Reasoning!.Trim()).AppendLine();
                sb.AppendLine("</details>").AppendLine();
            }

            sb.AppendLine(m.Content).AppendLine();
        }

        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "conversation" : name;
    }

    private static bool TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false; // 다른 앱이 클립보드를 잠근 경우 등
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* 브라우저 실행 실패는 무시. */ }
    }

    // ===== 설정 / 명령 상태 =====

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedOpen = !IsAdvancedOpen;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    private void RaiseCommandStates()
    {
        SendCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ClearMessagesCommand.NotifyCanExecuteChanged();
        DeleteConversationCommand.NotifyCanExecuteChanged();
        RefreshModelsCommand.NotifyCanExecuteChanged();
        RegenerateMessageCommand.NotifyCanExecuteChanged();
        RegenerateLastCommand.NotifyCanExecuteChanged();
        DeleteMessageCommand.NotifyCanExecuteChanged();
        SubmitEditCommand.NotifyCanExecuteChanged();
        PreviousVariantCommand.NotifyCanExecuteChanged();
        NextVariantCommand.NotifyCanExecuteChanged();
        CopyConversationCommand.NotifyCanExecuteChanged();
        ExportConversationCommand.NotifyCanExecuteChanged();
        AttachImageCommand.NotifyCanExecuteChanged();
        AttachFilesCommand.NotifyCanExecuteChanged();
        PasteImageCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }
}
