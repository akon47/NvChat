using System;
using System.IO;

namespace NvChat.Services
{
    /// <summary>
    /// 앱 데이터 저장 경로. %APPDATA%\NvChat\ 아래에 설정/대화 파일을 둔다.
    /// </summary>
    internal static class AppPaths
    {
        public static string DataDirectory
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(root, "NvChat");
            }
        }

        public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

        public static string ConversationsFile => Path.Combine(DataDirectory, "conversations.json");

        public static void EnsureDataDirectory()
        {
            Directory.CreateDirectory(DataDirectory);
        }
    }
}
