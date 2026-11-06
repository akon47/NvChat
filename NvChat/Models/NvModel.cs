namespace NvChat.Models;

/// <summary>build.nvidia.com 에서 제공하는 하나의 모델. Id 는 "publisher/name" 꼴이며 대소문자 무관하게 같은 모델로 본다.</summary>
public sealed class NvModel : IEquatable<NvModel>
{
    private NvModel(string id, string publisher, string name)
    {
        Id = id;
        Publisher = publisher;
        Name = name;
    }

    /// <summary>모델 ID(예: "meta/llama-3.3-70b-instruct"). API 호출에 사용한다.</summary>
    public string Id { get; }

    /// <summary>게시자(ID 의 '/' 앞부분, 예: "meta"). 없으면 빈 문자열.</summary>
    public string Publisher { get; }

    /// <summary>모델 이름(ID 의 '/' 뒷부분, 예: "llama-3.3-70b-instruct").</summary>
    public string Name { get; }

    public static NvModel FromId(string? id)
    {
        string trimmed = (id ?? "").Trim();
        int slash = trimmed.IndexOf('/');
        return slash > 0
            ? new NvModel(trimmed, trimmed[..slash], trimmed[(slash + 1)..])
            : new NvModel(trimmed, "", trimmed);
    }

    public bool Equals(NvModel? other) => other is not null && string.Equals(other.Id, Id, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as NvModel);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);

    public override string ToString() => Id;
}
