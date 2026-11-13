using System.IO;
using System.Text;

namespace NvChat.Services;

/// <summary>
/// 파일을 원자적으로 쓴다. 임시 파일에 먼저 쓰고 디스크까지 내려 보낸 뒤 대상 파일로 교체하므로,
/// 쓰는 도중 앱이 죽거나 전원이 나가도 기존 파일이 반쯤 쓰인 상태로 남지 않는다.
/// </summary>
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content) => WriteAllBytes(path, Utf8NoBom.GetBytes(content));

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true); // rename 전에 내용이 실제로 디스크에 있어야 원자성이 의미가 있다
        }
        File.Move(temp, path, overwrite: true); // 같은 볼륨에서의 rename 은 원자적
    }
}
