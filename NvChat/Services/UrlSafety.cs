namespace NvChat.Services;

/// <summary>
/// 모델이 출력한 링크를 열어도 되는지 판별한다.
/// 모델 출력은 신뢰할 수 없는 입력이다. 스킴을 가리지 않고 셸에 넘기면 <c>file://</c>·UNC·사용자 지정 프로토콜 링크 한 번의 클릭으로
/// 로컬 실행 파일이 뜰 수 있으므로, 웹/메일 링크만 허용한다.
/// </summary>
internal static class UrlSafety
{
    /// <summary>열어도 안전한 절대 URI(http/https/mailto)면 그 URI, 아니면 null. 스킴이 없는 "example.com/x" 는 https 로 본다.</summary>
    public static Uri? ToSafeUri(string? url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) return null;

        // 스킴이 없으면 https 로 보정한다. (드라이브 문자 "C:\..." 나 UNC "\\host\share" 는 보정하지 않고 아래에서 걸러진다)
        if (!url.Contains(':') && !url.StartsWith('\\') && !url.StartsWith('/') && url.Contains('.')) url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.IsFile || uri.IsUnc) return null;

        return uri.Scheme is "http" or "https" or "mailto" ? uri : null;
    }
}
