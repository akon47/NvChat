using System.IO;

namespace NvChat.Services;

/// <summary>앱 데이터 저장 경로. %APPDATA%\NvChat\ 아래에 설정/대화 파일을 둔다.</summary>
internal static class AppPaths
{
    /// <summary>
    /// 설정/대화가 저장되는 폴더. 기본은 %APPDATA%\NvChat 이며,
    /// 환경 변수 NVCHAT_DATA_DIR 로 덮어쓸 수 있다(테스트 격리 / 포터블 실행용).
    /// </summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string ConversationsFile => Path.Combine(DataDirectory, "conversations.json");

    public static string ErrorLogFile => Path.Combine(DataDirectory, "error.log");

    private static string ResolveDataDirectory()
    {
        string? custom = Environment.GetEnvironmentVariable("NVCHAT_DATA_DIR");
        return !string.IsNullOrWhiteSpace(custom)
            ? custom.Trim()
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NvChat");
    }
}
