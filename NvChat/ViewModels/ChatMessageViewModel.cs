using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvChat.Models;
using NvChat.Services;

namespace NvChat.ViewModels;

/// <summary>채팅창에 표시되는 하나의 메시지.</summary>
public sealed partial class ChatMessageViewModel : ObservableObject
{
    private bool _collapsedReasoningOnContent;
    private List<string>? _images;
    private IReadOnlyList<ImageSource>? _imageSources;

    public ChatMessageViewModel(ChatRole role, string? content)
    {
        Role = role;
        Content = content ?? "";
        Timestamp = DateTime.Now;
    }

    public ChatRole Role { get; }

    public bool IsUser => Role == ChatRole.User;

    public bool IsAssistant => Role == ChatRole.Assistant;

    /// <summary>
    /// 마크다운으로 렌더링할지 여부. 스트리밍 중에도 렌더링해 처음부터 서식이 보이게 한다. (잦은 재구성은 MarkdownPresenter 가 묶어서 처리)
    /// </summary>
    public bool ShowMarkdown => IsAssistant && !HasError && !IsEditing;

    public bool HasReasoning => !string.IsNullOrWhiteSpace(Reasoning);

    /// <summary>스트리밍이 시작됐지만 아직 본문/추론이 하나도 안 온 대기 상태(타이핑 애니메이션용).</summary>
    public bool IsWaiting => IsStreaming && string.IsNullOrEmpty(Content) && string.IsNullOrEmpty(Reasoning);

    public string TimestampText => Timestamp.ToString("HH:mm");

    public bool HasImages => _images is { Count: > 0 };

    public IReadOnlyList<string> Images => _images ?? [];

    /// <summary>표시용으로 디코딩된 이미지 소스(지연 생성).</summary>
    public IReadOnlyList<ImageSource> ImageSources => _imageSources ??= Images.Select(ImageUtil.FromDataUri).OfType<ImageSource>().ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    public partial string Content { get; set; }

    /// <summary>추론(reasoning) 모델의 사고 과정.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReasoning), nameof(IsWaiting))]
    public partial string? Reasoning { get; set; }

    [ObservableProperty]
    public partial bool IsReasoningExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMarkdown), nameof(IsWaiting))]
    public partial bool IsStreaming { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMarkdown))]
    public partial bool HasError { get; set; }

    /// <summary>사용자가 생성을 중단한 메시지인지. (본문은 부분적으로 유효)</summary>
    [ObservableProperty]
    public partial bool Aborted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMarkdown))]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string? EditText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimestampText))]
    public partial DateTime Timestamp { get; set; }

    partial void OnIsStreamingChanged(bool value)
    {
        // 스트리밍이 끝나면 추론 섹션을 접는다.
        if (!value && HasReasoning) IsReasoningExpanded = false;
    }

    [RelayCommand]
    private void ToggleReasoning() => IsReasoningExpanded = !IsReasoningExpanded;

    [RelayCommand]
    private void BeginEdit()
    {
        EditText = Content;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    /// <summary>본문 스트리밍 델타를 이어붙인다.</summary>
    public void AppendContent(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;

        // 첫 본문 토큰이 오면 추론 섹션을 접는다.
        if (!_collapsedReasoningOnContent && HasReasoning)
        {
            _collapsedReasoningOnContent = true;
            IsReasoningExpanded = false;
        }

        Content += delta;
    }

    /// <summary>추론 스트리밍 델타를 이어붙인다.</summary>
    public void AppendReasoning(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;

        bool wasEmpty = string.IsNullOrEmpty(Reasoning);
        Reasoning += delta;

        // 아직 본문이 없으면 추론을 펼쳐서 실시간으로 보여준다.
        if (wasEmpty && string.IsNullOrEmpty(Content)) IsReasoningExpanded = true;
    }

    /// <summary>첨부 이미지(data URI)를 설정한다.</summary>
    public void SetImages(IEnumerable<string>? images)
    {
        _images = images?.Where(s => !string.IsNullOrEmpty(s)).ToList();
        _imageSources = null;
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(ImageSources));
    }

    public ChatMessage ToData() => new()
    {
        Role = Role,
        Content = Content,
        Reasoning = Reasoning,
        Images = _images,
        Timestamp = Timestamp,
    };

    public static ChatMessageViewModel FromData(ChatMessage data)
    {
        ChatMessageViewModel viewModel = new(data.Role, data.Content)
        {
            Reasoning = data.Reasoning,
            Timestamp = data.Timestamp == default ? DateTime.Now : data.Timestamp,
        };
        if (data.Images is { Count: > 0 }) viewModel.SetImages(data.Images);
        return viewModel;
    }
}
