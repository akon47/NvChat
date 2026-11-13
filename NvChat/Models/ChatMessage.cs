namespace NvChat.Models;

/// <summary>대화에 저장되는 하나의 메시지(직렬화용 POCO).</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; set; }

    public string? Content { get; set; }

    /// <summary>첨부 이미지(data URI, base64). 비전 모델에 전달된다. 없으면 null.</summary>
    public List<string>? Images { get; set; }

    /// <summary>추론(reasoning) 모델의 사고 과정. 일반 모델은 비어 있음.</summary>
    public string? Reasoning { get; set; }

    public DateTime Timestamp { get; set; }

    /// <summary>이 답변을 만든 모델. 대화 도중 모델을 바꿔도 어느 답이 어느 모델 것인지 남는다. (사용자 메시지는 null)</summary>
    public string? ModelId { get; set; }

    /// <summary>오류 안내로 채워진 메시지(본문은 모델의 답이 아니다). 다음 요청의 컨텍스트에서 제외된다.</summary>
    public bool IsError { get; set; }

    /// <summary>사용자가 생성을 중단해 본문이 중간까지만 있는 메시지.</summary>
    public bool Aborted { get; set; }

    /// <summary>응답 생성에 걸린 시간(ms). 없으면 0.</summary>
    public long DurationMs { get; set; }

    /// <summary>서버가 알려 준 출력 토큰 수. 없으면 0.</summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// 이 자리의 다른 버전들(다시 생성/편집으로 생긴 갈래). 지금 보이는 버전의 자리는 null 이고, 그 내용은 이 메시지 자신과 대화의 뒤따르는 메시지들이다.
    /// 버전이 하나뿐이면 null.
    /// </summary>
    public List<MessageVariant?>? Variants { get; set; }

    /// <summary><see cref="Variants"/> 중 지금 보이는 버전의 인덱스.</summary>
    public int ActiveVariant { get; set; }
}

/// <summary>
/// 보이지 않는 버전 하나. 그 버전의 메시지 내용과, 그 버전에서 이어졌던 뒤쪽 대화(<see cref="Tail"/>)를 통째로 보관한다.
/// 버전을 전환하면 지금 보이는 내용/뒤쪽 대화와 맞바꾼다.
/// </summary>
public sealed class MessageVariant
{
    public ChatMessage Message { get; set; } = new();

    public List<ChatMessage> Tail { get; set; } = [];
}
