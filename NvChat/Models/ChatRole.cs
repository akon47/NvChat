using System.Text.Json.Serialization;

namespace NvChat.Models
{
    /// <summary>
    /// 채팅 메시지의 역할. OpenAI 호환 API 의 role 필드에 대응한다.
    /// 저장 시 문자열로 직렬화하여 향후 멤버 순서 변경에 안전하게 한다.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChatRole
    {
        System,
        User,
        Assistant
    }
}
