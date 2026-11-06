using System.IO;
using System.Text.Json;
using NvChat.Localization;
using NvChat.Models;

namespace NvChat.Services;

/// <summary>모든 대화를 하나의 JSON 파일로 저장/로드한다. 손상/잠금 상황에서 기존 데이터를 실수로 지우지 않도록 방어한다.</summary>
public sealed class ConversationStore : IConversationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // 로드 시 파일이 존재하지만 읽기에 실패(잠금 등)하면 false → 읽지 못한 기존 데이터를 덮어쓰지 않도록 저장을 막는다.
    private bool _canSave = true;

    public string? LoadError { get; private set; }

    public List<Conversation> LoadAll()
    {
        _canSave = true;
        LoadError = null;

        // 파일이 아예 없는 것만 '대화 없음'으로 취급.
        if (!File.Exists(AppPaths.ConversationsFile)) return [];

        string json;
        try
        {
            json = File.ReadAllText(AppPaths.ConversationsFile);
        }
        catch (Exception ex)
        {
            _canSave = false;
            LoadError = LocalizationManager.Instance.Tr("ConvLoadReadError", ex.Message);
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Conversation>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // 손상된 JSON: 백업본을 남기고 빈 목록으로 시작(백업으로 수동 복구 가능).
            string? backup = BackupCorrupt();
            LoadError = backup is not null ? LocalizationManager.Instance.Tr("ConvBackedUp", backup) : LocalizationManager.Instance["ConvCorrupt"];
            return [];
        }
    }

    public void SaveAll(IEnumerable<Conversation> conversations)
    {
        if (!_canSave) return;
        try { AtomicFile.WriteAllText(AppPaths.ConversationsFile, JsonSerializer.Serialize(conversations.ToList(), JsonOptions)); }
        catch { /* 저장 실패가 흐름을 막지 않도록 무시(원자적 쓰기라 기존 파일은 온전). */ }
    }

    private static string? BackupCorrupt()
    {
        try
        {
            string backup = Path.Combine(AppPaths.DataDirectory, $"conversations.corrupt-{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.Copy(AppPaths.ConversationsFile, backup, overwrite: true);
            return backup;
        }
        catch { return null; }
    }
}
