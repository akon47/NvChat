using Microsoft.Win32;

namespace NvChat.Services;

/// <summary>
/// 윈도우 로그인 시 자동 시작(HKCU\...\Run). 관리자 권한이 필요 없고 현재 사용자에게만 적용된다.
/// 자동 시작된 인스턴스는 <see cref="StartHiddenArgument"/> 인자를 받아 창 없이 트레이로만 뜬다.
/// </summary>
internal static class AutoStartService
{
    public const string StartHiddenArgument = "--tray";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NvChat";

    /// <summary>설정값에 맞게 등록/해제한다. 실행 파일을 옮긴 뒤에도 경로가 맞도록 켜져 있으면 매번 다시 쓴다. 실패하면 false.</summary>
    public static bool Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return false;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            if (Environment.ProcessPath is not { Length: > 0 } exePath) return false;
            string command = $"\"{exePath}\" {StartHiddenArgument}";
            if (key.GetValue(ValueName) as string != command) key.SetValue(ValueName, command, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false; // 정책으로 레지스트리 쓰기가 막힌 환경 등
        }
    }
}
