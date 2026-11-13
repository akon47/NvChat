using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NvChat.Localization;
using NvChat.Models;

namespace NvChat.Services;

/// <summary>
/// 대화를 대화마다 한 파일(conversations\{Id}.json)로 저장/로드한다.
///
/// 예전에는 모든 대화를 파일 하나에 담아, 무엇 하나 바뀔 때마다(고정·이름 변경·응답 완료) 전체를 UI 스레드에서 다시 썼다.
/// 첨부 이미지가 base64 로 함께 들어가므로 기록이 쌓일수록 한 번의 저장이 수 MB 가 되었다.
/// 지금은 바뀐 대화만, 백그라운드 스레드 하나가 순서대로 쓴다. 같은 대화에 대한 연속 요청은 마지막 것만 남긴다.
///
/// 방어 원칙은 그대로다: 원자적 쓰기, 손상 파일은 지우지 않고 이름을 바꿔 보존, 읽지 못한 기존 데이터를 덮어쓰지 않는다.
/// </summary>
public sealed class ConversationStore : IConversationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, Conversation?> _pending = new(StringComparer.OrdinalIgnoreCase); // null = 삭제
    private bool _draining;
    private Task _worker = Task.CompletedTask;
    private bool _saveFailureReported;

    // 기존 데이터를 읽지 못했으면(잠금 등) false → 그 데이터를 덮어쓰지 않도록 저장을 막는다.
    private bool _canSave = true;

    public string? LoadError { get; private set; }

    public event Action<string>? SaveFailed;

    public List<Conversation> LoadAll()
    {
        _canSave = true;
        LoadError = null;

        if (Directory.Exists(AppPaths.ConversationsDirectory)) return LoadDirectory();
        if (File.Exists(AppPaths.LegacyConversationsFile)) return MigrateLegacy();
        return [];
    }

    private List<Conversation> LoadDirectory()
    {
        List<Conversation> result = [];
        int corrupt = 0;
        string? readError = null;

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(AppPaths.ConversationsDirectory, "*.json").ToList();
        }
        catch (Exception ex)
        {
            _canSave = false;
            LoadError = LocalizationManager.Instance.Tr("ConvLoadReadError", ex.Message);
            return [];
        }

        foreach (string file in files)
        {
            try
            {
                using var stream = File.OpenRead(file);
                if (JsonSerializer.Deserialize<Conversation>(stream, JsonOptions) is { } conversation) result.Add(conversation);
            }
            catch (JsonException)
            {
                // 손상된 대화 하나 때문에 나머지를 잃지 않는다. 파일은 지우지 않고 이름만 바꿔 둔다(수동 복구 가능).
                corrupt++;
                try { File.Move(file, file + ".corrupt", overwrite: true); } catch { /* 이름 변경 실패는 무시 */ }
            }
            catch (Exception ex)
            {
                // 읽기 실패(잠금 등)한 파일은 그대로 두고 건너뛴다. 이번 세션에서 그 대화는 보이지 않지만 파일은 온전하다.
                readError ??= ex.Message;
            }
        }

        if (corrupt > 0) LoadError = LocalizationManager.Instance.Tr("ConvSomeCorrupt", corrupt);
        else if (readError is not null) LoadError = LocalizationManager.Instance.Tr("ConvSomeUnreadable", readError);
        return result;
    }

    /// <summary>단일 파일(conversations.json)을 읽어 대화별 파일로 옮긴다. 원본은 지우지 않고 .legacy 로 이름만 바꿔 남긴다.</summary>
    private List<Conversation> MigrateLegacy()
    {
        string json;
        try
        {
            json = File.ReadAllText(AppPaths.LegacyConversationsFile);
        }
        catch (Exception ex)
        {
            _canSave = false;
            LoadError = LocalizationManager.Instance.Tr("ConvLoadReadError", ex.Message);
            return [];
        }

        List<Conversation> conversations;
        try
        {
            conversations = JsonSerializer.Deserialize<List<Conversation>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // 손상된 JSON: 백업본을 남기고 빈 목록으로 시작(백업으로 수동 복구 가능).
            string? backup = BackupCorruptLegacy();
            LoadError = backup is not null ? LocalizationManager.Instance.Tr("ConvBackedUp", backup) : LocalizationManager.Instance["ConvCorrupt"];
            return [];
        }

        try
        {
            Directory.CreateDirectory(AppPaths.ConversationsDirectory);
            foreach (var conversation in conversations)
            {
                conversation.Id = NormalizeId(conversation.Id);
                Write(conversation);
            }
            // 모두 옮겨 쓴 뒤에만 원본 이름을 바꾼다. 중간에 실패하면 다음 실행에서 처음부터 다시 옮긴다.
            File.Move(AppPaths.LegacyConversationsFile, AppPaths.LegacyConversationsBackupFile, overwrite: true);
        }
        catch (Exception ex)
        {
            // 옮기지 못했으면 폴더를 되돌려 다음 실행에서 원본으로 다시 시도하게 한다. 이번 세션은 저장하지 않는다.
            try { Directory.Delete(AppPaths.ConversationsDirectory, recursive: true); } catch { /* 최선 */ }
            _canSave = false;
            LoadError = LocalizationManager.Instance.Tr("ConvLoadReadError", ex.Message);
        }

        return conversations;
    }

    public void Save(Conversation conversation)
    {
        conversation.Id = NormalizeId(conversation.Id);
        Enqueue(conversation.Id, conversation);
    }

    public void Delete(string id) => Enqueue(NormalizeId(id), null);

    private void Enqueue(string id, Conversation? conversation)
    {
        if (!_canSave) return;

        lock (_gate)
        {
            _pending[id] = conversation;
            if (_draining) return;
            _draining = true;
            _worker = Task.Run(Drain);
        }
    }

    private void Drain()
    {
        while (true)
        {
            string id;
            Conversation? conversation;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _draining = false;
                    return;
                }
                (id, conversation) = _pending.First();
                _pending.Remove(id);
            }

            try
            {
                if (conversation is null) File.Delete(PathFor(id));
                else Write(conversation);
            }
            catch (Exception ex)
            {
                // 원자적 쓰기라 기존 파일은 온전하다. 계속 실패하면(디스크 가득 참 등) 사용자가 모른 채 기록을 잃으므로 한 번은 알린다.
                if (!_saveFailureReported)
                {
                    _saveFailureReported = true;
                    SaveFailed?.Invoke(ex.Message);
                }
            }
        }
    }

    public void Flush()
    {
        while (true)
        {
            Task worker;
            lock (_gate)
            {
                if (!_draining) return;
                worker = _worker;
            }
            try { worker.Wait(TimeSpan.FromSeconds(15)); }
            catch { /* 쓰기 실패는 Drain 이 이미 처리했다 */ }
            if (!worker.IsCompleted) return; // 종료를 무한정 붙잡지 않는다
        }
    }

    private static void Write(Conversation conversation) =>
        AtomicFile.WriteAllBytes(PathFor(conversation.Id!), JsonSerializer.SerializeToUtf8Bytes(conversation, JsonOptions));

    private static string PathFor(string id) => Path.Combine(AppPaths.ConversationsDirectory, id + ".json");

    /// <summary>파일 이름으로 써도 안전한 ID. 비었거나 경로 문자가 섞여 있으면(가져온 백업 등) 새로 만든다.</summary>
    internal static string NormalizeId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : Guid.NewGuid().ToString("N");

    private static string? BackupCorruptLegacy()
    {
        try
        {
            string backup = Path.Combine(AppPaths.DataDirectory, $"conversations.corrupt-{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.Copy(AppPaths.LegacyConversationsFile, backup, overwrite: true);
            return backup;
        }
        catch { return null; }
    }
}
