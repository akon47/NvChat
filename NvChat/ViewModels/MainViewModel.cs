using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
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

    private static LocalizationManager L => LocalizationManager.Instance;

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

    private AppSettings _settings;
    private CancellationTokenSource? _streamCts;
    private CancellationTokenSource? _toastCts;
    private ConversationViewModel? _streamingConversation;
    private bool _suppressModelSync;
    private bool _refreshingModels;
    private ICollectionView? _conversationsView;
    private ICollectionView? _modelsView;
    private DateTime _modelPickerClosedAt;
    private UpdateInfo? _update;

    public MainViewModel()
    {
        // 언어 부트스트랩: 설정을 읽기 전에 OS 언어로 먼저 맞춰, 첫 실행 시 만들어지는 기본값(프리셋 이름 등)과 로드 오류 메시지가 올바른 언어로 나오게 한다.
        LocalizationManager.Instance.Culture = LocalizationManager.ResolveDefaultCulture();
        _settings = _settingsStore.Load();
        InitLanguage();

        _client = new NvidiaClient(() => _settings);

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
    public partial bool IsStreaming { get; set; }

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
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    public partial bool IsSidebarCollapsed { get; set; }

    /// <summary>하단 토스트 문구(복사됨 등). 잠깐 보였다가 사라진다.</summary>
    [ObservableProperty]
    public partial string? ToastText { get; set; }

    [ObservableProperty]
    public partial bool IsToastVisible { get; set; }

    /// <summary>값이 바뀔 때마다 입력창에 포커스(FocusBehavior.FocusToken).</summary>
    [ObservableProperty]
    public partial int InputFocusToken { get; set; }

    /// <summary>값이 바뀔 때마다 대화 검색 상자에 포커스(Ctrl+F).</summary>
    [ObservableProperty]
    public partial int SearchFocusToken { get; set; }

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
    public bool IsSelectedStreaming => IsStreaming && ReferenceEquals(_streamingConversation, SelectedConversation);

    /// <summary>다른 대화가 응답 생성 중이라 지금 대화에서 전송이 막혀 있는지.</summary>
    public bool IsSendBlockedByOtherConversation => IsStreaming && !ReferenceEquals(_streamingConversation, SelectedConversation);

    /// <summary>전송 차단 안내 문구(완성된 지역화 문자열). StringFormat 은 바인딩을 못 받으므로 여기서 조립한다.</summary>
    public string SendBlockedByOtherText => L.Tr("SendBlockedByOther", _streamingConversation?.Title ?? "");

    public GridLength SidebarWidth => IsSidebarCollapsed ? new GridLength(0) : new GridLength(280);

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public bool SendOnEnter => _settings.SendOnEnter;

    public bool MinimizeToTrayOnClose => _settings.MinimizeToTrayOnClose;

    /// <summary>메인 창 위치/크기 기억값(WindowBehaviors.Placement 가 읽고 쓴다).</summary>
    public WindowPlacement WindowPlacement => _settings.Window ??= new WindowPlacement();

    /// <summary>새 버전이 확인되어 안내 배너를 띄울지 여부.</summary>
    public bool HasUpdate => _update is not null;

    /// <summary>사이드바 하단에 표시할 현재 버전. 예: "v1.8.0".</summary>
    public string AppVersionText => "v" + UpdateService.CurrentVersion.ToString(3);

    /// <summary>"새 버전 v1.4.0 이 있습니다 (12.3 MB)" 형태의 안내.</summary>
    public string? UpdateText => _update is null ? null
        : _update.SizeText is { } size ? L.Tr("UpdateAvailableSize", _update.Tag, size)
        : L.Tr("UpdateAvailable", _update.Tag);

    public bool IsUpdateIdle => !IsUpdating;

    /// <summary>"내려받는 중 45%" 형태의 진행 표시.</summary>
    public string UpdateProgressText => UpdateProgress >= 99.5 ? L["InstallingRestart"] : L.Tr("Downloading", UpdateProgress.ToString("0"));

    partial void OnSelectedConversationChanged(ConversationViewModel? value)
    {
        SyncSelectedModelToConversation();
        NotifyStreamingStateChanged();
        RaiseCommandStates();
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

    partial void OnSearchTextChanged(string? value) => ConversationsView.Refresh();

    partial void OnIsStreamingChanged(bool value)
    {
        NotifyStreamingStateChanged();
        RaiseCommandStates();
    }

    private void NotifyStreamingStateChanged()
    {
        OnPropertyChanged(nameof(IsSelectedStreaming));
        OnPropertyChanged(nameof(IsSendBlockedByOtherConversation));
        OnPropertyChanged(nameof(SendBlockedByOtherText));
    }

    // ===== 수명 주기 =====

    public async Task LoadAsync()
    {
        foreach (var data in _conversationStore.LoadAll()) Conversations.Add(ConversationViewModel.FromData(data));

        if (_settingsStore.LoadError is { } settingsError) StatusMessage = settingsError;
        else if (_conversationStore.LoadError is { } conversationError) StatusMessage = conversationError;

        if (Conversations.Count == 0) CreateConversation(select: true);
        else SelectedConversation = Conversations.OrderByDescending(c => c.UpdatedAt).First();

        await RefreshModelsAsync();

        if (!HasApiKey)
        {
            StatusMessage = L["NeedApiKeyStart"];
            OpenSettings();
        }

        // 새 버전 확인은 채팅 준비가 끝난 뒤 조용히 진행한다.
        await CheckForUpdateAsync();
    }

    /// <summary>대화를 저장한다. (트레이 최소화 등에서도 호출되므로 스트림은 건드리지 않는다.)</summary>
    public void SaveState() => SaveConversations();

    /// <summary>진행 중인 스트리밍을 취소한다. 실제 앱 종료 경로에서만 호출.</summary>
    public void CancelStreaming()
    {
        try { _streamCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public AppSettings GetCurrentSettings() => _settings;

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

        _settings = settings;
        _settingsStore.Save(_settings);

        // 언어는 라이브 미리보기로 이미 적용돼 있지만, 저장 값 기준으로 한 번 더 확정한다.
        if (!string.IsNullOrWhiteSpace(_settings.Language)) LocalizationManager.Instance.Culture = _settings.Language;

        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(SendOnEnter));
        OnPropertyChanged(nameof(MinimizeToTrayOnClose));
        OnPropertyChanged(nameof(PresetMenu));
        OnPropertyChanged(nameof(WindowPlacement));
        RaiseCommandStates();

        if (HasStatusMessage && HasApiKey) StatusMessage = null;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ = RefreshModelsAsync();
    }

    /// <summary>"창을 닫아도 트레이에서 실행 중" 안내를 보였음을 기록한다(최초 1회만 보이도록).</summary>
    public void MarkCloseToTrayHintShown()
    {
        _settings.CloseToTrayHintShown = true;
        _settingsStore.Save(_settings);
    }

    [RelayCommand]
    private void SaveWindowPlacement() => _settingsStore.Save(_settings);

    // ===== 토스트 =====

    /// <summary>하단에 잠깐 떠서 사라지는 안내(복사됨 등). 배너(StatusMessage)는 사용자가 닫아야 하는 경고용이라 구분한다.</summary>
    public void ShowToast(string text)
    {
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        ToastText = text;
        IsToastVisible = true;
        _ = HideToastLaterAsync(cts);
    }

    private async Task HideToastLaterAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(2200, cts.Token);
            IsToastVisible = false;
        }
        catch (OperationCanceledException) { /* 새 토스트가 이어받았다 */ }
        finally
        {
            if (ReferenceEquals(_toastCts, cts)) _toastCts = null;
            cts.Dispose();
        }
    }

    [RelayCommand]
    private void DismissStatus() => StatusMessage = null;

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
        CreateConversation(select: true);
        SaveConversations();
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

        Conversations.Add(conversation);
        if (select) SelectedConversation = conversation;
        return conversation;
    }

    [RelayCommand(CanExecute = nameof(HasConversation))]
    private void DeleteConversation(ConversationViewModel? conversation)
    {
        if (conversation is null) return;
        if (ConfirmCallback is { } confirm && !confirm(L["DeleteConversationTitle"], L.Tr("DeleteConversationMsg", conversation.Title), L["Delete"])) return;

        // 스트리밍 중인 대화를 지우면 스트림도 함께 취소해야 IsStreaming 이 풀린다.
        if (ReferenceEquals(conversation, _streamingConversation)) CancelStreaming();

        bool wasSelected = ReferenceEquals(conversation, SelectedConversation);
        Conversations.Remove(conversation);

        if (Conversations.Count == 0) CreateConversation(select: true);
        else if (wasSelected) SelectedConversation = Conversations.OrderByDescending(c => c.UpdatedAt).First();

        SaveConversations();
    }

    [RelayCommand(CanExecute = nameof(HasConversation))]
    private void PinConversation(ConversationViewModel? conversation)
    {
        if (conversation is null) return;
        conversation.Pinned = !conversation.Pinned;
        SaveConversations();
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
    private void RenameSelected() => BeginRename(SelectedConversation);

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
        SaveConversations();
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
        conversation.UpdatedAt = DateTime.Now;
        RaiseCommandStates();
        SaveConversations();
    }

    private bool CanClearMessages() => SelectedConversation is { Messages.Count: > 0 } && !IsStreaming;

    /// <summary>Ctrl+F: 사이드바를 펼치고 대화 검색 상자에 포커스.</summary>
    [RelayCommand]
    private void FocusSearch()
    {
        IsSidebarCollapsed = false;
        SearchFocusToken++;
    }

    private void SaveConversations()
    {
        try { _conversationStore.SaveAll(Conversations.Select(c => c.ToData())); }
        catch { /* 저장 실패가 흐름을 막지 않도록 무시. */ }
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
        Models.Clear();
        foreach (var model in models) Models.Add(model);
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

    private bool CanSend() => !IsStreaming && SelectedConversation is not null && (!string.IsNullOrWhiteSpace(InputText) || Attachments.Count > 0);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        string text = (InputText ?? "").Trim();
        bool hasImages = Attachments.Count > 0;
        if ((text.Length == 0 && !hasImages) || SelectedConversation is not { } conversation || IsStreaming) return;

        if (!HasApiKey)
        {
            StatusMessage = L["NeedApiKey"];
            OpenSettings();
            return;
        }

        ChatMessageViewModel userMessage = new(ChatRole.User, text);
        if (hasImages) userMessage.SetImages(Attachments.Select(a => a.DataUri).ToList());

        conversation.Messages.Add(userMessage);
        InputText = "";
        Attachments.Clear();

        if (conversation.Messages.Count(m => m.IsUser) == 1) conversation.Title = text.Length > 0 ? MakeTitle(text) : L["ImageConversation"];

        await GenerateResponseAsync(conversation);
    }

    [RelayCommand(CanExecute = nameof(IsSelectedStreaming))]
    private void Stop() => CancelStreaming();

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

    private bool CanAttach() => !IsStreaming;

    private void AttachFile(string path)
    {
        try { AddAttachment(ImageUtil.FileToDataUri(path)); }
        catch (Exception ex) { StatusMessage = L.Tr("ImageLoadFailed", ex.Message); }
    }

    private void AddAttachment(string dataUri)
    {
        Attachments.Add(new AttachmentViewModel(dataUri, ImageUtil.FromDataUri(dataUri)));
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

    [RelayCommand(CanExecute = nameof(CanActOnMessage))]
    private void RegenerateMessage(ChatMessageViewModel? message)
    {
        if (message is null || SelectedConversation is not { } conversation || IsStreaming) return;

        int index = conversation.Messages.IndexOf(message);
        if (index < 0) return;

        // 이 어시스턴트 메시지부터 끝까지 제거 후 재생성.
        for (int i = conversation.Messages.Count - 1; i >= index; i--) conversation.Messages.RemoveAt(i);
        _ = GenerateResponseAsync(conversation);
    }

    [RelayCommand(CanExecute = nameof(CanRegenerateLast))]
    private void RegenerateLast()
    {
        if (SelectedConversation?.Messages is { Count: > 0 } messages && messages[^1] is { IsAssistant: true } last) RegenerateMessage(last);
    }

    private bool CanRegenerateLast() => !IsStreaming && SelectedConversation?.Messages is { Count: > 0 } messages && messages[^1].IsAssistant;

    [RelayCommand(CanExecute = nameof(CanActOnMessage))]
    private void DeleteMessage(ChatMessageViewModel? message)
    {
        if (message is null || SelectedConversation is not { } conversation || IsStreaming) return;

        conversation.Messages.Remove(message);
        conversation.UpdatedAt = DateTime.Now;
        RaiseCommandStates();
        SaveConversations();
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
        if (message is null || SelectedConversation is not { } conversation || IsStreaming) return;

        int index = conversation.Messages.IndexOf(message);
        if (index < 0) return;

        string text = (message.EditText ?? "").Trim();
        if (text.Length == 0)
        {
            message.IsEditing = false;
            return;
        }

        message.Content = text;
        message.IsEditing = false;

        // 편집한 사용자 메시지 다음의 모든 메시지를 제거하고 재생성.
        for (int i = conversation.Messages.Count - 1; i > index; i--) conversation.Messages.RemoveAt(i);
        _ = GenerateResponseAsync(conversation);
    }

    private bool CanActOnMessage(ChatMessageViewModel? message) => message is not null && !IsStreaming;

    /// <summary>대화의 현재 메시지들(마지막이 사용자 메시지)에 대해 어시스턴트 응답을 스트리밍한다.</summary>
    private async Task GenerateResponseAsync(ConversationViewModel conversation)
    {
        if (IsStreaming) return;

        if (!HasApiKey)
        {
            StatusMessage = L["NeedApiKey"];
            OpenSettings();
            return;
        }

        string modelId = SelectedModel?.Id ?? conversation.ModelId ?? _settings.DefaultModelId;
        conversation.ModelId = modelId;

        var apiMessages = BuildApiMessages(conversation);

        ChatMessageViewModel assistant = new(ChatRole.Assistant, "") { IsStreaming = true };
        conversation.Messages.Add(assistant);

        _streamCts = new CancellationTokenSource();
        _streamingConversation = conversation;
        conversation.IsStreaming = true;
        IsStreaming = true;
        StatusMessage = null;

        bool aborted = false;
        bool usageRecorded = false;

        try
        {
            bool received = false;
            string? finishReason = null;

            await foreach (var delta in _client.StreamChatAsync(modelId, apiMessages, conversation.Parameters, _streamCts.Token))
            {
                if (delta.Usage.HasTokens)
                {
                    usageRecorded = true;
                    conversation.AddUsage(modelId, delta.Usage.PromptTokens, delta.Usage.CompletionTokens, true);
                }

                if (!string.IsNullOrEmpty(delta.FinishReason)) finishReason = delta.FinishReason;

                if (!string.IsNullOrEmpty(delta.Reasoning))
                {
                    received = true;
                    assistant.AppendReasoning(delta.Reasoning);
                }

                if (!string.IsNullOrEmpty(delta.Content))
                {
                    received = true;
                    assistant.AppendContent(delta.Content);
                }
            }

            ExtractInlineThinking(assistant);

            // 내용이 하나도 오지 않은 경우. 자동 재전송은 하지 않는다(사용자의 무료 호출 한도를 말없이 소모하므로).
            // 대신 사유를 밝히고, 메시지 아래 '다시 생성' 버튼으로 사용자가 직접 재시도하게 안내한다.
            if (!received && string.IsNullOrEmpty(assistant.Content))
            {
                assistant.HasError = true;
                assistant.Content = DescribeEmptyResponse(finishReason);
                StatusMessage = L["EmptyResponseHint"];
            }
        }
        catch (OperationCanceledException)
        {
            ExtractInlineThinking(assistant);
            aborted = true;
        }
        catch (Exception ex)
        {
            assistant.HasError = true;
            assistant.Content = L.Tr("ErrorPrefix", ex.Message);
            StatusMessage = ex.Message;
        }
        finally
        {
            // 서버가 토큰 수를 안 주거나 중간에 끊겨도 요청 한 건은 실제로 나갔으므로 횟수는 남긴다.
            if (!usageRecorded) conversation.AddUsage(modelId, 0, 0, false);

            if (aborted)
            {
                // 빈 채로 중단되면 자리표시자를 제거하고, 부분 응답이 있으면 유지하되 '중단됨'만 표시.
                if (string.IsNullOrEmpty(assistant.Content)) conversation.Messages.Remove(assistant);
                else assistant.Aborted = true;
            }

            assistant.IsStreaming = false;
            assistant.Timestamp = DateTime.Now;

            conversation.IsStreaming = false;
            _streamingConversation = null;
            IsStreaming = false;
            _streamCts?.Dispose();
            _streamCts = null;

            conversation.UpdatedAt = DateTime.Now;
            RaiseCommandStates();
            SaveConversations();
        }

        // 첫 응답이 정상 완료되면 모델로 제목 자동 생성(비차단).
        if (!assistant.HasError) _ = MaybeGenerateTitleAsync(conversation, modelId);
    }

    /// <summary>본문이 비어 있는 응답의 사유를 finish_reason 으로 설명한다.</summary>
    private static string DescribeEmptyResponse(string? finishReason) => finishReason?.ToLowerInvariant() switch
    {
        "content_filter" => L["EmptyContentFilter"],
        "length" => L["EmptyLength"],
        _ => L["EmptyGeneric"],
    };

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
            List<ChatMessage> prompt =
            [
                new() { Role = ChatRole.System, Content = L["AutoTitlePrompt"] },
                new() { Role = ChatRole.User, Content = firstUser.Length > 500 ? firstUser[..500] : firstUser },
            ];

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(25));
            var outcome = await _client.CompleteChatAsync(modelId, prompt, new GenerationParameters { MaxTokens = 24, Temperature = 0.3, TopP = 1.0 }, cts.Token);

            // 제목 생성도 실제로 나간 요청이므로 이 대화의 사용량에 포함한다.
            conversation.AddUsage(modelId, outcome.Usage.PromptTokens, outcome.Usage.CompletionTokens, outcome.Usage.HasTokens);

            if (CleanTitle(outcome.Content) is { } title)
            {
                conversation.Title = title;
                conversation.TitleLocked = true;
                SaveConversations();
            }
        }
        catch { /* 제목 생성 실패는 무시(이미 요약 제목이 있음). */ }
    }

    private static string? CleanTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string title = raw.Trim();

        // <think> 흔적 제거
        int close = title.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (close >= 0) title = title[(close + "</think>".Length)..].Trim();

        title = title.Replace('\r', ' ').Replace('\n', ' ').Trim();

        // 모델이 마크다운으로 답할 때가 있어 서식 기호를 걷어낸다. (예: "## 제목", "**제목**")
        title = title.TrimStart('#', ' ').Trim('*', '_', '`', ' ').Trim('"', '\'', '「', '」', '“', '”', '.', ' ');

        if (title.Length > 40) title = title[..40].TrimEnd() + "…";
        return title.Length == 0 ? null : title;
    }

    private static void ExtractInlineThinking(ChatMessageViewModel message)
    {
        string content = message.Content;
        if (string.IsNullOrEmpty(content)) return;

        const string open = "<think>";
        const string close = "</think>";

        int start = content.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        int end = content.IndexOf(close, StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end <= start) return;

        string think = content[(start + open.Length)..end].Trim();
        string rest = (content[..start] + content[(end + close.Length)..]).Trim();

        if (think.Length > 0) message.Reasoning = string.IsNullOrEmpty(message.Reasoning) ? think : message.Reasoning + "\n" + think;
        message.Content = rest;
    }

    private List<ChatMessage> BuildApiMessages(ConversationViewModel conversation)
    {
        List<ChatMessage> messages = [];

        string system = ComposeSystemPrompt(conversation);
        if (!string.IsNullOrWhiteSpace(system)) messages.Add(new ChatMessage { Role = ChatRole.System, Content = system });

        foreach (var message in conversation.Messages)
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

        return messages;
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

    // ===== 내보내기 =====

    [RelayCommand(CanExecute = nameof(HasMessages))]
    private void CopyConversation()
    {
        if (SelectedConversation is not { } conversation) return;
        if (TrySetClipboard(BuildConversationMarkdown(conversation))) ShowToast(L["CopiedConversation"]);
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
            File.WriteAllText(dialog.FileName, BuildConversationMarkdown(conversation), Encoding.UTF8);
            ShowToast(L.Tr("ExportedTo", Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex)
        {
            StatusMessage = L.Tr("ExportFailed", ex.Message);
        }
    }

    private bool HasMessages() => SelectedConversation is { Messages.Count: > 0 };

    private static string BuildConversationMarkdown(ConversationViewModel conversation)
    {
        StringBuilder sb = new();
        sb.AppendLine("# " + conversation.Title).AppendLine();

        foreach (var m in conversation.Messages)
        {
            string who = m.IsUser ? L["RoleUser"] : m.IsAssistant ? L["RoleAssistant"] : L["RoleSystem"];
            sb.AppendLine("### " + who).AppendLine();
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
        CopyConversationCommand.NotifyCanExecuteChanged();
        ExportConversationCommand.NotifyCanExecuteChanged();
        AttachImageCommand.NotifyCanExecuteChanged();
        AttachFilesCommand.NotifyCanExecuteChanged();
        PasteImageCommand.NotifyCanExecuteChanged();
    }
}
