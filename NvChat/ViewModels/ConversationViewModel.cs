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
        Parameters = Parameters,
        Messages = Messages.Select(m => m.ToData()).ToList(),
        Usage = Usage.ToList(),
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
            CreatedAt = data.CreatedAt == default ? DateTime.Now : data.CreatedAt,
            UpdatedAt = data.UpdatedAt == default ? DateTime.Now : data.UpdatedAt,
        };

        foreach (var message in data.Messages) vm.Messages.Add(ChatMessageViewModel.FromData(message));
        foreach (var usage in data.Usage.Where(u => u is not null && !string.IsNullOrWhiteSpace(u.ModelId))) vm.Usage.Add(usage);
        return vm;
    }
}
