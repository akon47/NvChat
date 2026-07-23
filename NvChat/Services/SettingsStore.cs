using NvChat.Localization;
using NvChat.Models;
using System;
using System.IO;
using System.Text.Json;

namespace NvChat.Services
{
    /// <summary>
    /// 앱 설정을 JSON 파일로 저장/로드한다. API 키는 DPAPI 로 암호화되어 저장된다.
    ///
    /// 키 소실 방지 원칙:
    ///  - 파일은 있는데 읽기에 실패하면(잠금 등) 저장을 막아 기존 파일을 덮어쓰지 않는다.
    ///  - 복호화에 실패하면 원본 암호문을 보존했다가 저장 시 그대로 되돌려 쓴다.
    ///    (사용자가 키를 명시적으로 비운 경우[빈 문자열]와 복호화 실패[null]를 구분한다.)
    /// </summary>
    public sealed class SettingsStore : ISettingsStore
    {
        #region Fields

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private string _preservedCipher;
        private bool _blockSave;

        #endregion


        #region Properties

        /// <summary>로드 중 문제가 있었을 경우의 안내 메시지(정상 시 null).</summary>
        public string LoadError { get; private set; }

        #endregion


        #region Helpers

        public AppSettings Load()
        {
            _preservedCipher = null;
            _blockSave = false;
            LoadError = null;

            if (File.Exists(AppPaths.SettingsFile) == false)
                return new AppSettings();

            string json;
            try
            {
                json = File.ReadAllText(AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                // 읽기 실패: 기존 설정(암호화된 키 포함)을 덮어쓰지 않도록 저장을 막는다.
                _blockSave = true;
                LoadError = LocalizationManager.Instance.Tr("SetLoadReadError", ex.Message);
                return new AppSettings();
            }

            AppSettings settings;
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            }
            catch (JsonException)
            {
                // 손상: 백업 후 기본값으로 시작(백업으로 수동 복구 가능).
                var backup = BackupCorrupt();
                LoadError = backup != null
                    ? LocalizationManager.Instance.Tr("SetBackedUp", backup)
                    : LocalizationManager.Instance.Tr("SetCorrupt");
                return new AppSettings();
            }

            var cipher = settings.ApiKey;
            settings.ApiKey = SecureText.Unprotect(cipher);

            // 복호화 실패(다른 PC/계정에서 만든 파일 등) → 원본 암호문을 보존해 두고 덮어쓰지 않는다.
            if (settings.ApiKey == null && string.IsNullOrEmpty(cipher) == false)
            {
                _preservedCipher = cipher;
                LoadError = LocalizationManager.Instance.Tr("SetKeyDecryptFail");
            }

            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
                settings.BaseUrl = new AppSettings().BaseUrl;

            if (settings.DefaultParameters == null)
                settings.DefaultParameters = new GenerationParameters();

            if (settings.Presets == null)
                settings.Presets = AppSettings.DefaultPresets();

            return settings;
        }

        public void Save(AppSettings settings)
        {
            if (settings == null || _blockSave)
                return;

            string encryptedKey;

            if (settings.ApiKey == null && _preservedCipher != null)
            {
                // 메모리에 평문 키가 없고(복호화 실패) 사용자가 새로 입력하지도 않았다면 원본 암호문을 유지한다.
                encryptedKey = _preservedCipher;
            }
            else
            {
                encryptedKey = SecureText.Protect(settings.ApiKey);

                // 키는 있는데 암호화에 실패했다면 기존(유효한) 키를 지우지 않도록 저장 중단.
                if (string.IsNullOrEmpty(settings.ApiKey) == false && encryptedKey == null)
                    return;
            }

            var toStore = new AppSettings
            {
                ApiKey = encryptedKey,
                BaseUrl = settings.BaseUrl,
                DefaultModelId = settings.DefaultModelId,
                DefaultSystemPrompt = settings.DefaultSystemPrompt,
                DefaultParameters = settings.DefaultParameters,
                SendOnEnter = settings.SendOnEnter,
                GenerateTitles = settings.GenerateTitles,
                AboutYou = settings.AboutYou,
                ResponseStyle = settings.ResponseStyle,
                Presets = settings.Presets,
                GlobalHotkey = settings.GlobalHotkey,
                MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose,
                WindowLeft = settings.WindowLeft,
                WindowTop = settings.WindowTop,
                WindowWidth = settings.WindowWidth,
                WindowHeight = settings.WindowHeight,
                WindowMaximized = settings.WindowMaximized,
                AutoCheckUpdates = settings.AutoCheckUpdates,
                Language = settings.Language
            };

            try
            {
                var json = JsonSerializer.Serialize(toStore, _jsonOptions);
                AtomicFile.WriteAllText(AppPaths.SettingsFile, json);

                // 새 키가 정상 저장되었으면 보존본은 더 이상 필요 없다.
                if (string.IsNullOrEmpty(settings.ApiKey) == false)
                    _preservedCipher = null;
            }
            catch
            {
                // 저장 실패는 무시(원자적 쓰기라 기존 파일은 온전).
            }
        }

        private static string BackupCorrupt()
        {
            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backup = Path.Combine(AppPaths.DataDirectory, $"settings.corrupt-{stamp}.json");
                File.Copy(AppPaths.SettingsFile, backup, overwrite: true);
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
