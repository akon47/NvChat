using NvChat.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NvChat.Services
{
    /// <summary>
    /// 모든 대화를 하나의 JSON 파일로 저장/로드한다.
    /// 손상/잠금 상황에서 기존 데이터를 실수로 지우지 않도록 방어한다.
    /// </summary>
    public sealed class ConversationStore : IConversationStore
    {
        #region Fields

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        #endregion


        #region Properties

        /// <summary>
        /// 로드 시 파일이 존재하지만 읽기에 실패(잠금 등)한 경우 false.
        /// 이때는 저장을 막아 읽지 못한 기존 데이터를 덮어쓰지 않는다.
        /// </summary>
        public bool CanSave { get; private set; } = true;

        /// <summary>로드 중 발생한 문제를 사용자에게 알리기 위한 메시지(정상 시 null).</summary>
        public string LoadError { get; private set; }

        #endregion


        #region Helpers

        public List<Conversation> LoadAll()
        {
            CanSave = true;
            LoadError = null;

            // 파일이 아예 없는 것만 '대화 없음'으로 취급.
            if (File.Exists(AppPaths.ConversationsFile) == false)
                return new List<Conversation>();

            string json;
            try
            {
                json = File.ReadAllText(AppPaths.ConversationsFile);
            }
            catch (Exception ex)
            {
                // 읽기 실패(잠금/권한 등): 기존 파일을 덮어쓰지 않도록 저장을 막는다.
                CanSave = false;
                LoadError = "대화 기록 파일을 읽지 못했습니다. 이번 세션의 새 대화는 저장되지 않습니다. (" + ex.Message + ")";
                return new List<Conversation>();
            }

            try
            {
                var conversations = JsonSerializer.Deserialize<List<Conversation>>(json, _jsonOptions);
                return conversations ?? new List<Conversation>();
            }
            catch (JsonException)
            {
                // 손상된 JSON: 백업본을 남기고 빈 목록으로 시작(백업으로 수동 복구 가능).
                var backup = BackupCorrupt();
                LoadError = backup != null
                    ? "대화 기록 파일이 손상되어 백업했습니다: " + backup
                    : "대화 기록 파일이 손상되었습니다.";
                return new List<Conversation>();
            }
        }

        public void SaveAll(IEnumerable<Conversation> conversations)
        {
            if (CanSave == false)
                return;

            try
            {
                var list = conversations?.ToList() ?? new List<Conversation>();
                var json = JsonSerializer.Serialize(list, _jsonOptions);
                AtomicFile.WriteAllText(AppPaths.ConversationsFile, json);
            }
            catch
            {
                // 저장 실패가 흐름을 막지 않도록 무시(원자적 쓰기라 기존 파일은 온전).
            }
        }

        private static string BackupCorrupt()
        {
            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backup = Path.Combine(AppPaths.DataDirectory, $"conversations.corrupt-{stamp}.json");
                File.Copy(AppPaths.ConversationsFile, backup, overwrite: true);
                return backup;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
