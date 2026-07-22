using NvChat.Models;
using System;
using System.IO;
using System.Text.Json;

namespace NvChat.Services
{
    /// <summary>
    /// 앱 설정을 JSON 파일로 저장/로드한다. API 키는 DPAPI 로 암호화되어 저장된다.
    /// 손상 시 백업하고, 암호화 실패 시 기존 파일을 덮어쓰지 않는다.
    /// </summary>
    public sealed class SettingsStore : ISettingsStore
    {
        #region Fields

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        #endregion


        #region Helpers

        public AppSettings Load()
        {
            if (File.Exists(AppPaths.SettingsFile) == false)
                return new AppSettings();

            string json;
            try
            {
                json = File.ReadAllText(AppPaths.SettingsFile);
            }
            catch
            {
                // 읽기 실패: 기존 파일(암호화된 키 포함)을 보존하기 위해 기본값으로 시작.
                return new AppSettings();
            }

            AppSettings settings;
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            }
            catch (JsonException)
            {
                // 손상: 백업 후 기본값. 백업으로 이전 암호화 키를 수동 복구할 수 있다.
                BackupCorrupt();
                return new AppSettings();
            }

            settings.ApiKey = SecureText.Unprotect(settings.ApiKey);

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
            if (settings == null)
                return;

            var encryptedKey = SecureText.Protect(settings.ApiKey);

            // 키는 있는데 암호화가 실패(null)했다면, 디스크의 기존(유효한) 키를 지우지 않도록 저장 중단.
            if (string.IsNullOrEmpty(settings.ApiKey) == false && encryptedKey == null)
                return;

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
                WindowMaximized = settings.WindowMaximized
            };

            try
            {
                var json = JsonSerializer.Serialize(toStore, _jsonOptions);
                AtomicFile.WriteAllText(AppPaths.SettingsFile, json);
            }
            catch
            {
                // 저장 실패는 무시(원자적 쓰기라 기존 파일은 온전).
            }
        }

        private static void BackupCorrupt()
        {
            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backup = Path.Combine(AppPaths.DataDirectory, $"settings.corrupt-{stamp}.json");
                File.Copy(AppPaths.SettingsFile, backup, overwrite: true);
            }
            catch
            {
            }
        }

        #endregion
    }
}
