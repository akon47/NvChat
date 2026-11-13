using NvChat.Models;

namespace NvChat.Services;

public interface IConversationStore
{
    /// <summary>로드 중 문제가 있었을 경우의 안내 메시지(정상 시 null).</summary>
    string? LoadError { get; }

    /// <summary>백그라운드 저장이 실패했을 때(디스크 가득 참 등) 한 번 알린다. 임의의 스레드에서 발생한다.</summary>
    event Action<string>? SaveFailed;

    /// <summary>저장된 모든 대화를 읽는다. 디스크 I/O 와 파싱을 하므로 UI 스레드 밖에서 호출해도 된다.</summary>
    List<Conversation> LoadAll();

    /// <summary>대화 하나를 저장 대기열에 넣는다(곧바로 돌아오고, 쓰기는 백그라운드에서 순서대로 한다). 같은 대화의 연속 저장은 마지막 것만 쓴다.</summary>
    void Save(Conversation conversation);

    /// <summary>대화 파일을 지운다(백그라운드).</summary>
    void Delete(string id);

    /// <summary>대기 중인 쓰기가 모두 끝날 때까지 기다린다. 앱 종료 직전에 호출한다.</summary>
    void Flush();
}
