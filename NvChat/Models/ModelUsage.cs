using NvChat.Localization;

namespace NvChat.Models;

/// <summary>
/// 한 대화 안에서 특정 모델이 쓴 양. 대화 도중 모델을 바꿔도 모델별로 따로 쌓이도록 대화마다 목록으로 보관한다.
/// </summary>
public sealed class ModelUsage
{
    /// <summary>모델 식별자(예: meta/llama-3.1-8b-instruct).</summary>
    public string ModelId { get; set; } = "";

    /// <summary>이 모델로 보낸 요청 수.</summary>
    public int Requests { get; set; }

    /// <summary>입력(프롬프트) 토큰 수.</summary>
    public long PromptTokens { get; set; }

    /// <summary>출력(생성) 토큰 수.</summary>
    public long CompletionTokens { get; set; }

    public long TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>모델 이름만 짧게(퍼블리셔 접두사 제거).</summary>
    public string ShortName =>
        string.IsNullOrEmpty(ModelId) ? LocalizationManager.Instance["Unknown"]
        : ModelId.LastIndexOf('/') is var slash && slash >= 0 && slash < ModelId.Length - 1 ? ModelId[(slash + 1)..]
        : ModelId;

    public string TotalText => FormatTokens(TotalTokens);

    public string DetailText => LocalizationManager.Instance.Tr(
        "UsageDetail", Requests.ToString("N0"), FormatTokens(PromptTokens), FormatTokens(CompletionTokens));

    /// <summary>"1.2k" 처럼 읽기 좋은 토큰 수.</summary>
    public static string FormatTokens(long tokens) => tokens switch
    {
        <= 0 => "0",
        < 10000 => tokens.ToString("N0"),
        _ => (tokens / 1000.0).ToString("0.#") + "k",
    };
}
