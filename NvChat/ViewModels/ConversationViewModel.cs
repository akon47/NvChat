using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NvChat.Localization;
using NvChat.Models;

namespace NvChat.ViewModels;

/// <summary>하나의 대화 세션.</summary>
public sealed partial class ConversationViewModel : ObservableObject
{
    private static LocalizationManager L => LocalizationManager.Instance;

    public ConversationViewModel()
    {
        Title = L["NewChat"];
        SystemPrompt = "";
        UpdatedAt = DateTime.Now;
        Messages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Subtitle));
        };
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    /// <summary>이 대화의 진행 중인 스트림을 취소하는 핸들. 대화마다 따로 가지므로 여러 대화가 동시에 응답을 받을 수 있다.</summary>
    public CancellationTokenSource? StreamCts { get; set; }

    /// <summary>보내지 않은 입력 초안과 첨부. 다른 대화로 갔다 와도 남는다.</summary>
    public string? Draft { get; set; }

    public List<AttachmentViewModel> DraftAttachments { get; } = [];

    private int _savedEditSignature;

    /// <summary>
    /// 초안·시스템 프롬프트·파라미터처럼 저장 시점이 따로 없는 편집이 마지막 저장 이후 바뀌었는지.
    /// (슬라이더는 POCO 에 직접 바인딩돼 변경 알림이 없으므로 값의 지문을 비교한다.)
    /// </summary>
    public bool HasUnsavedEdits => EditSignature() != _savedEditSignature;

    public void MarkSaved() => _savedEditSignature = EditSignature();

    private int EditSignature() => HashCode.Combine(
        SystemPrompt, Draft ?? "", Parameters.Temperature, Parameters.TopP, Parameters.MaxTokens, Parameters.FrequencyPenalty, Parameters.PresencePenalty);

    public GenerationParameters Parameters { get; set; } = new();

    /// <summary>제목이 확정(사용자 지정 또는 자동 생성 완료)되어 더 이상 자동 생성하지 않음.</summary>
    public bool TitleLocked { get; set; }

    public bool IsEmpty => Messages.Count == 0;

    /// <summary>이 대화에서 모델별로 쓴 요청/토큰. 대화 중 모델을 바꾸면 항목이 늘어난다.</summary>
    public ObservableCollection<ModelUsage> Usage { get; } = [];

    public bool HasUsage => Usage.Count > 0;

    /// <summary>"합계 12.4k 토큰 · 8회" 형태의 요약.</summary>
    public string? UsageSummary => Usage.Count == 0 ? null
        : L.Tr("UsageSummary", ModelUsage.FormatTokens(Usage.Sum(u => u.TotalTokens)), Usage.Sum(u => u.Requests).ToString("N0"));

    /// <summary>사이드바에 표시할 부제(모델명 · 메시지 수).</summary>
    public string Subtitle
    {
        get
        {
            string model = NvModel.FromId(ModelId).Name;
            if (model.Length == 0) model = L["ModelUnset"];
            return L.Tr("Subtitle", model, Messages.Count.ToString());
        }
    }

    /// <summary>사이드바 그룹 이름(고정/오늘/어제/…).</summary>
    public string GroupName => GroupOrder switch
    {
        0 => L["GroupPinned"],
        1 => L["GroupToday"],
        2 => L["GroupYesterday"],
        3 => L["GroupLast7"],
        4 => L["GroupLast30"],
        _ => L["GroupOlder"],
    };

    /// <summary>그룹 정렬 순서(0=고정, 1=오늘, 2=어제, 3=7일, 4=30일, 5=이전).</summary>
    public int GroupOrder
    {
        get
        {
            if (Pinned) return 0;

            var today = DateTime.Now.Date;
            var day = UpdatedAt.Date;
            return day >= today ? 1
                : day == today.AddDays(-1) ? 2
                : day > today.AddDays(-7) ? 3
                : day > today.AddDays(-30) ? 4
                : 5;
        }
    }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string? ModelId { get; set; }

    [ObservableProperty]
    public partial string SystemPrompt { get; set; }

    /// <summary>컨텍스트 한도 때문에 오래된 메시지를 빼고 보냈을 때의 안내(없으면 null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContextNote))]
    public partial string? ContextNote { get; set; }

    public bool HasContextNote => !string.IsNullOrEmpty(ContextNote);

    /// <summary>날짜가 바뀌었을 때 그룹(오늘/어제/…)을 다시 계산하게 한다. 앱이 트레이에 며칠씩 떠 있어도 "오늘"이 어제 대화를 붙들지 않도록.</summary>
    public void RefreshGroup()
    {
        OnPropertyChanged(nameof(GroupOrder));
        OnPropertyChanged(nameof(GroupName));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupName), nameof(GroupOrder))]
    public partial bool Pinned { get; set; }

    /// <summary>이 대화에서 응답을 생성 중인지(사이드바 로딩 표시용).</summary>
    [ObservableProperty]
    public partial bool IsStreaming { get; set; }

    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    [ObservableProperty]
    public partial string? RenameText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupName), nameof(GroupOrder))]
    public partial DateTime UpdatedAt { get; set; }

    /// <summary>이 대화의 사용량에 한 건을 더한다. 모델별로 나누어 쌓는다.</summary>
    public void AddUsage(string? modelId, int promptTokens, int completionTokens, bool hasTokens)
    {
        if (string.IsNullOrWhiteSpace(modelId)) modelId = L["Unknown"];

        var entry = Usage.FirstOrDefault(u => string.Equals(u.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = new ModelUsage { ModelId = modelId };
            Usage.Add(entry);
        }

        entry.Requests++;
        if (hasTokens)
        {
            entry.PromptTokens += promptTokens;
            entry.CompletionTokens += completionTokens;
        }

        // ModelUsage 는 POCO 라 개별 알림이 없으므로 목록을 다시 그리게 한다.
        Usage[Usage.IndexOf(entry)] = entry;

        OnPropertyChanged(nameof(HasUsage));
        OnPropertyChanged(nameof(UsageSummary));
    }

    public Conversation ToData() => new()
    {
        Id = Id,
        Title = Title,
        ModelId = ModelId,
        SystemPrompt = SystemPrompt,
        Pinned = Pinned,
        TitleLocked = TitleLocked,
        // 저장은 백그라운드 스레드에서 직렬화하므로, UI 스레드에서 계속 바뀌는 객체는 복사본을 넘긴다.
        Parameters = Parameters.Clone(),
        Messages = Messages.Select(m => m.ToData()).ToList(),
        Usage = Usage.Select(u => new ModelUsage { ModelId = u.ModelId, Requests = u.Requests, PromptTokens = u.PromptTokens, CompletionTokens = u.CompletionTokens }).ToList(),
        Draft = string.IsNullOrEmpty(Draft) ? null : Draft,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };

    public static ConversationViewModel FromData(Conversation data)
    {
        ConversationViewModel vm = new()
        {
            Id = string.IsNullOrEmpty(data.Id) ? Guid.NewGuid().ToString("N") : data.Id,
            Title = string.IsNullOrEmpty(data.Title) ? L["NewChat"] : data.Title,
            ModelId = data.ModelId,
            SystemPrompt = data.SystemPrompt ?? "",
            Pinned = data.Pinned,
            TitleLocked = data.TitleLocked,
            Parameters = data.Parameters ?? new GenerationParameters(),
            Draft = data.Draft,
            CreatedAt = data.CreatedAt == default ? DateTime.Now : data.CreatedAt,
            UpdatedAt = data.UpdatedAt == default ? DateTime.Now : data.UpdatedAt,
        };

        foreach (var message in data.Messages ?? []) vm.Messages.Add(ChatMessageViewModel.FromData(message));
        foreach (var usage in (data.Usage ?? []).Where(u => u is not null && !string.IsNullOrWhiteSpace(u.ModelId))) vm.Usage.Add(usage);
        vm.MarkSaved();
        return vm;
    }
}
