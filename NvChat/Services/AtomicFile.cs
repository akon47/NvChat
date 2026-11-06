using System.IO;
using System.Text;

namespace NvChat.Services;

/// <summary>
/// 임시 파일에 먼저 쓰고 원자적으로 교체해, 쓰기 도중 크래시/전원차단에도 대상 파일이 반쯤 쓰인 상태로 손상되지 않게 한다.
/// </summary>
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        File.WriteAllText(temp, content, Utf8NoBom);
        File.Move(temp, path, overwrite: true); // 같은 볼륨에서의 rename 은 원자적
    }
}
