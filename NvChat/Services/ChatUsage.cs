namespace NvChat.Services;

/// <summary>
/// 채팅 요청 한 건이 끝났음을 알리는 사용량 보고.
/// 서버가 토큰 수를 알려주지 않으면 <see cref="HasTokens"/> 가 false 이고 요청 수만 유효하다.
/// </summary>
public readonly record struct ChatUsage(int PromptTokens, int CompletionTokens, bool HasTokens);

/// <summary>
/// 스트리밍 응답의 한 조각. 일반 답변 텍스트(<see cref="Content"/>)와 추론 모델의 사고 과정(<see cref="Reasoning"/>)을 구분해 전달한다.
/// <see cref="FinishReason"/>(stop / length / content_filter …)은 마지막 청크에만 들어오며 본문이 비어 있을 때 원인을 설명하는 데 쓴다.
/// <see cref="Usage"/> 는 stream_options.include_usage 를 지원하는 서버에서 마지막에 한 번 들어온다.
/// </summary>
public readonly record struct ChatStreamDelta(string? Content, string? Reasoning, string? FinishReason = null, ChatUsage Usage = default);

/// <summary>비스트리밍 채팅 완성 결과. 본문과 토큰 사용량을 함께 돌려준다.</summary>
public readonly record struct ChatCompletionOutcome(string Content, ChatUsage Usage);

/// <summary>NVIDIA API 호출 실패 시 던져지는 예외.</summary>
public sealed class NvidiaApiException(string message, Exception? innerException = null) : Exception(message, innerException);
