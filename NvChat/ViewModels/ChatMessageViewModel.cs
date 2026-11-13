using System.Text;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvChat.Models;
using NvChat.Services;

namespace NvChat.ViewModels;

/// <summary>채팅창에 표시되는 하나의 메시지.</summary>
public sealed partial class ChatMessageViewModel : ObservableObject
{
    private readonly StringBuilder _pendingContent = new();
    private readonly StringBuilder _pendingReasoning = new();
    private bool _collapsedReasoningOnContent;
    private int _variantBeforePush = -1;
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

    /// <summary>표시용으로 디코딩된 이미지 소스(지연 생성). 말풍선 안 최대 폭에 맞춰 줄여 디코딩한다.</summary>
    public IReadOnlyList<ImageSource> ImageSources => _imageSources ??= Images.Select(i => ImageUtil.FromDataUri(i, decodeWidth: 600)).OfType<ImageSource>().ToList();

    // ===== 버전(다시 생성/편집으로 생긴 갈래) =====

    /// <summary>이 자리의 모든 버전. 지금 보이는 버전의 자리는 null(내용은 이 뷰모델과 대화의 뒤따르는 메시지들). 버전이 하나뿐이면 비어 있다.</summary>
    public List<MessageVariant?> Variants { get; } = [];

    public int ActiveVariant { get; private set; }

    public bool HasVariants => Variants.Count > 1;

    /// <summary>"2 / 3" 형태의 버전 표시.</summary>
    public string VariantLabel => $"{ActiveVariant + 1} / {Math.Max(1, Variants.Count)}";

    public bool CanGoPreviousVariant => HasVariants && ActiveVariant > 0;

    public bool CanGoNextVariant => HasVariants && ActiveVariant < Variants.Count - 1;

    // ===== 바인딩 속성 =====

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
    [NotifyPropertyChangedFor(nameof(ShowMarkdown), nameof(IsWaiting), nameof(InfoText))]
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
    [NotifyPropertyChangedFor(nameof(TimestampText), nameof(InfoText))]
    public partial DateTime Timestamp { get; set; }

    /// <summary>이 답변을 만든 모델(사용자 메시지는 null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfoText))]
    public partial string? ModelId { get; set; }

    /// <summary>응답 생성에 걸린 시간(ms).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfoText))]
    public partial long DurationMs { get; set; }

    /// <summary>서버가 알려 준 출력 토큰 수.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfoText))]
    public partial int OutputTokens { get; set; }

    /// <summary>대화 내 찾기에서 지금 가리키는 메시지인지(테두리 강조용).</summary>
    [ObservableProperty]
    public partial bool IsFindCurrent { get; set; }

    /// <summary>이번 세션에서 새로 생긴 메시지라 등장 애니메이션을 한 번 보여 줄지. (불러온 기록은 false)</summary>
    public bool IsFresh { get; set; }

    /// <summary>"llama-3.3-70b · 14:32 · 3.2s · 41 tok/s" 형태의 메시지 정보. 있는 항목만 이어 붙인다.</summary>
    public string InfoText
    {
        get
        {
            List<string> parts = [];
            if (IsAssistant && NvModel.FromId(ModelId).Name is { Length: > 0 } model) parts.Add(model);
            parts.Add(TimestampText);

            if (!IsStreaming && DurationMs > 0)
            {
                double seconds = DurationMs / 1000.0;
                parts.Add(seconds < 10 ? $"{seconds:0.0}s" : $"{seconds:0}s");
                if (OutputTokens > 0 && seconds > 0.2) parts.Add($"{OutputTokens / seconds:0} tok/s");
            }

            return string.Join("  ·  ", parts);
        }
    }

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

    // ===== 스트리밍 =====

    /// <summary>
    /// 본문 스트리밍 델타를 모아 둔다. 화면 반영은 <see cref="FlushPending"/> 에서 한꺼번에 한다 —
    /// 토큰마다 문자열을 다시 만들고 변경 알림을 내면 긴 답변에서 할당량과 레이아웃 비용이 제곱으로 늘어난다.
    /// </summary>
    public void AppendContent(string delta)
    {
        if (!string.IsNullOrEmpty(delta)) _pendingContent.Append(delta);
    }

    /// <summary>추론 스트리밍 델타를 모아 둔다.</summary>
    public void AppendReasoning(string delta)
    {
        if (!string.IsNullOrEmpty(delta)) _pendingReasoning.Append(delta);
    }

    public bool HasPending => _pendingContent.Length > 0 || _pendingReasoning.Length > 0;

    /// <summary>모아 둔 델타를 본문/추론에 반영한다.</summary>
    public void FlushPending()
    {
        if (_pendingReasoning.Length > 0)
        {
            bool wasEmpty = string.IsNullOrEmpty(Reasoning);
            Reasoning += _pendingReasoning.ToString();
            _pendingReasoning.Clear();

            // 아직 본문이 없으면 추론을 펼쳐서 실시간으로 보여준다.
            if (wasEmpty && string.IsNullOrEmpty(Content) && _pendingContent.Length == 0) IsReasoningExpanded = true;
        }

        if (_pendingContent.Length > 0)
        {
            // 첫 본문 토큰이 오면 추론 섹션을 접는다.
            if (!_collapsedReasoningOnContent && HasReasoning)
            {
                _collapsedReasoningOnContent = true;
                IsReasoningExpanded = false;
            }

            Content += _pendingContent.ToString();
            _pendingContent.Clear();
        }
    }

    /// <summary>새 응답을 받기 위해 내용을 비운다(같은 자리에서 다시 생성할 때).</summary>
    public void ResetForRegeneration()
    {
        _pendingContent.Clear();
        _pendingReasoning.Clear();
        _collapsedReasoningOnContent = false;
        Content = "";
        Reasoning = null;
        HasError = false;
        Aborted = false;
        IsReasoningExpanded = false;
        DurationMs = 0;
        OutputTokens = 0;
        Timestamp = DateTime.Now;
    }

    /// <summary>첨부 이미지(data URI)를 설정한다.</summary>
    public void SetImages(IEnumerable<string>? images)
    {
        _images = images?.Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (_images is { Count: 0 }) _images = null;
        _imageSources = null;
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(ImageSources));
    }

    // ===== 버전 관리 =====

    /// <summary>
    /// 지금 보이는 내용(과 넘겨받은 뒤쪽 대화)을 버전 목록에 보관하고, 새 빈 버전을 활성으로 만든다.
    /// 다시 생성/편집 직전에 호출한다.
    /// </summary>
    public void PushNewVariant(IEnumerable<ChatMessage> tail)
    {
        if (Variants.Count == 0) Variants.Add(null);
        Variants[ActiveVariant] = new MessageVariant { Message = ToData(includeVariants: false), Tail = tail.ToList() };
        _variantBeforePush = ActiveVariant;
        Variants.Add(null);
        ActiveVariant = Variants.Count - 1;
        RaiseVariantChanged();
    }

    /// <summary>
    /// 다른 버전으로 전환한다. 지금 보이는 내용/뒤쪽 대화를 보관하고, 대상 버전의 내용을 이 메시지에 싣는다.
    /// 대상 버전의 뒤쪽 대화를 돌려주므로 호출측이 대화 목록에 이어 붙인다.
    /// </summary>
    public List<ChatMessage> SwitchVariant(int index, IEnumerable<ChatMessage> currentTail)
    {
        if (index < 0 || index >= Variants.Count || index == ActiveVariant || Variants[index] is not { } target) return currentTail.ToList();

        Variants[ActiveVariant] = new MessageVariant { Message = ToData(includeVariants: false), Tail = currentTail.ToList() };
        Variants[index] = null;
        ActiveVariant = index;
        Apply(target.Message);
        RaiseVariantChanged();
        return target.Tail;
    }

    /// <summary>방금 만든(활성) 버전을 버리고 직전 버전으로 되돌린다. 빈 채로 중단된 다시 생성을 없던 일로 할 때 쓴다. 되돌린 버전의 뒤쪽 대화를 돌려준다.</summary>
    public List<ChatMessage>? DiscardActiveVariant()
    {
        // 새 버전은 항상 맨 끝에 추가되므로, 맨 끝이 활성일 때만 버릴 수 있다.
        if (Variants.Count < 2 || ActiveVariant != Variants.Count - 1) return null;

        int restore = _variantBeforePush >= 0 && _variantBeforePush < Variants.Count - 1 ? _variantBeforePush : Variants.Count - 2;
        if (Variants[restore] is not { } target) return null;

        Apply(target.Message);
        Variants[restore] = null;
        Variants.RemoveAt(Variants.Count - 1);
        ActiveVariant = restore;
        if (Variants.Count == 1)
        {
            Variants.Clear();
            ActiveVariant = 0;
        }
        RaiseVariantChanged();
        return target.Tail;
    }

    private void RaiseVariantChanged()
    {
        OnPropertyChanged(nameof(HasVariants));
        OnPropertyChanged(nameof(VariantLabel));
        OnPropertyChanged(nameof(CanGoPreviousVariant));
        OnPropertyChanged(nameof(CanGoNextVariant));
    }

    /// <summary>저장된 메시지 내용을 이 뷰모델에 싣는다(버전 목록은 건드리지 않는다).</summary>
    private void Apply(ChatMessage data)
    {
        _pendingContent.Clear();
        _pendingReasoning.Clear();
        IsEditing = false;
        Content = data.Content ?? "";
        Reasoning = data.Reasoning;
        HasError = data.IsError;
        Aborted = data.Aborted;
        ModelId = data.ModelId;
        DurationMs = data.DurationMs;
        OutputTokens = data.OutputTokens;
        Timestamp = data.Timestamp == default ? DateTime.Now : data.Timestamp;
        IsReasoningExpanded = false;
        SetImages(data.Images);
    }

    public ChatMessage ToData(bool includeVariants = true) => new()
    {
        Role = Role,
        Content = Content,
        Reasoning = Reasoning,
        Images = _images,
        Timestamp = Timestamp,
        ModelId = ModelId,
        IsError = HasError,
        Aborted = Aborted,
        DurationMs = DurationMs,
        OutputTokens = OutputTokens,
        Variants = includeVariants && Variants.Count > 1 ? Variants.ToList() : null,
        ActiveVariant = includeVariants && Variants.Count > 1 ? ActiveVariant : 0,
    };

    public static ChatMessageViewModel FromData(ChatMessage data)
    {
        ChatMessageViewModel viewModel = new(data.Role, data.Content)
        {
            Reasoning = data.Reasoning,
            Timestamp = data.Timestamp == default ? DateTime.Now : data.Timestamp,
            ModelId = data.ModelId,
            HasError = data.IsError,
            Aborted = data.Aborted,
            DurationMs = data.DurationMs,
            OutputTokens = data.OutputTokens,
        };
        if (data.Images is { Count: > 0 }) viewModel.SetImages(data.Images);

        // 버전 목록은 "활성 자리만 null" 이어야 한다. 손상된 데이터면 버전을 버리고 지금 내용만 살린다.
        if (data.Variants is { Count: > 1 } variants && data.ActiveVariant >= 0 && data.ActiveVariant < variants.Count
            && variants[data.ActiveVariant] is null && variants.Count(v => v is null) == 1)
        {
            viewModel.Variants.AddRange(variants);
            viewModel.ActiveVariant = data.ActiveVariant;
        }

        return viewModel;
    }
}
