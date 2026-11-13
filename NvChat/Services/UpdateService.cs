using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using NvChat.Localization;

namespace NvChat.Services;

/// <summary>사용 가능한 새 버전 정보.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, string? ChecksumUrl, long Size)
{
    /// <summary>"12.3 MB" 형태의 크기 표시.</summary>
    public string? SizeText => Size > 0 ? $"{Size / 1024.0 / 1024.0:0.#} MB" : null;
}

/// <summary>
/// GitHub Releases 를 이용한 자동 업데이트.
///
/// 단일 exe 라 설치 관리자가 없지만, Windows 는 "실행 중인 exe 를 덮어쓰는 것"은 막아도 "이름을 바꾸는 것"은 허용한다.
/// 그래서 현재 exe 를 .old 로 밀어내고 새 파일을 제자리에 옮긴 뒤 재시작하면 별도 헬퍼 프로세스 없이 자기 자신을 교체할 수 있다.
/// 다음 실행 때 App 이 남은 .old 를 지운다.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/akon47/NvChat/releases/latest";
    private const string AssetName = "NvChat.exe";
    private const string ChecksumAssetName = "NvChat.exe.sha256";
    private const string RetiredPrefix = "NvChat.old.";

    /// <summary>업데이트 실패 시 안내할 릴리즈 페이지 주소.</summary>
    public const string ReleasePage = "https://github.com/akon47/NvChat/releases/latest";

    /// <summary>현재 실행 중인 버전.</summary>
    public static Version CurrentVersion { get; } = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>최신 릴리즈를 조회한다. 더 새로운 버전이 없거나 조회에 실패하면 null.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = CreateClient();
            using var response = await http.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = doc.RootElement;

            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

            string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (TryParseVersion(tag, out var latest) is false || latest <= CurrentVersion) return null;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

            string? downloadUrl = null;
            string? checksumUrl = null;
            long size = 0;
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                string? url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

                if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = url;
                    if (asset.TryGetProperty("size", out var s)) size = s.GetInt64();
                }
                else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    checksumUrl = url;
                }
            }

            return string.IsNullOrEmpty(downloadUrl) ? null : new UpdateInfo(latest, tag!, downloadUrl, checksumUrl, size);
        }
        catch
        {
            return null; // 네트워크 문제/응답 형식 변경 등은 조용히 무시한다. 업데이트는 부가 기능이다.
        }
    }

    /// <summary>새 exe 를 내려받아 검증한 뒤 현재 exe 와 교체하고 새 프로세스를 띄운다(호출측에서 종료해야 한다).</summary>
    public async Task DownloadAndApplyAsync(UpdateInfo info, IProgress<double> progress, CancellationToken cancellationToken)
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            throw new InvalidOperationException(LocalizationManager.Instance["UpdateNoExePath"]);

        string newPath = exePath + ".new";
        string oldPath = ResolveRetiredPath(exePath);

        using var http = CreateClient();

        // 1) 다운로드 (같은 폴더에 받아야 이후 이동이 같은 볼륨 내에서 원자적으로 끝난다)
        //    바탕화면 등에 두고 쓰는 경우가 많으므로 받는 동안에는 숨김 처리해 눈에 띄지 않게 한다.
        await DownloadAsync(http, info.DownloadUrl, newPath, info.Size, progress, cancellationToken).ConfigureAwait(false);
        TrySetHidden(newPath, true);

        // 2) 체크섬 검증. 릴리즈에 체크섬이 있는데 읽지 못했다면 "검증 안 함"이 아니라 "검증 실패"다 —
        //    받은 파일이 온전한지 알 수 없는 채로 실행 파일을 바꿔치기하지 않는다.
        if (!string.IsNullOrEmpty(info.ChecksumUrl))
        {
            string? expected = await TryReadChecksumAsync(http, info.ChecksumUrl, cancellationToken).ConfigureAwait(false);
            if (expected is null)
            {
                TryDelete(newPath);
                throw new InvalidOperationException(LocalizationManager.Instance["UpdateChecksumUnavailable"]);
            }
            if (!string.Equals(expected, ComputeSha256(newPath), StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(newPath);
                throw new InvalidOperationException(LocalizationManager.Instance["UpdateChecksumMismatch"]);
            }
        }

        // 3) 교체: 실행 중인 exe 는 덮어쓸 수 없지만 이름은 바꿀 수 있다.
        //    구 버전은 가능하면 %TEMP% 로 밀어내 exe 옆(바탕화면 등)에 잔여 파일이 보이지 않게 한다.
        TryDelete(oldPath);
        try
        {
            File.Move(exePath, oldPath);
        }
        catch (Exception ex)
        {
            TryDelete(newPath);
            throw new InvalidOperationException(LocalizationManager.Instance.Tr("UpdateReplaceFailed", ex.Message));
        }

        try
        {
            File.Move(newPath, exePath);
            TrySetHidden(exePath, false);
        }
        catch
        {
            // 되돌린다. 여기서 실패하면 앱이 사라지므로 롤백은 반드시 시도한다.
            try { File.Move(oldPath, exePath); } catch { /* 최선 */ }
            TryDelete(newPath);
            throw;
        }

        // 4) 새 버전으로 재시작
        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
    }

    /// <summary>
    /// 이전 업데이트가 남긴 파일을 정리한다. (앱 시작 시 호출)
    /// 업데이트 직후 재시작한 경우에는 방금 종료 중인 이전 프로세스가 아직 구 exe 이미지를 잠그고 있어 곧바로 지워지지 않는다.
    /// 그래서 백그라운드에서 잠시 재시도한다.
    /// </summary>
    public static void CleanupLeftovers()
    {
        if (Environment.ProcessPath is not { Length: > 0 } exePath) return;

        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                bool remaining = false;
                remaining |= !TryDelete(exePath + ".old") && File.Exists(exePath + ".old");
                remaining |= !TryDelete(exePath + ".new") && File.Exists(exePath + ".new");

                try
                {
                    foreach (string file in Directory.EnumerateFiles(Path.GetTempPath(), RetiredPrefix + "*.exe"))
                        remaining |= !TryDelete(file) && File.Exists(file);
                }
                catch { /* temp 열거 실패 */ }

                if (!remaining) return;
                await Task.Delay(2000).ConfigureAwait(false);
            }
        });
    }

    /// <summary>
    /// 구 버전을 밀어낼 경로를 정한다. 실행 중인 이미지는 같은 볼륨 안에서만 이동할 수 있으므로,
    /// %TEMP% 가 같은 드라이브일 때만 그쪽으로 보내고 아니면 exe 옆에 .old 로 둔다.
    /// </summary>
    private static string ResolveRetiredPath(string exePath)
    {
        try
        {
            string temp = Path.GetTempPath();
            if (string.Equals(Path.GetPathRoot(temp), Path.GetPathRoot(exePath), StringComparison.OrdinalIgnoreCase))
                return Path.Combine(temp, RetiredPrefix + Guid.NewGuid().ToString("N") + ".exe");
        }
        catch { /* 아래 폴백 */ }
        return exePath + ".old";
    }

    private static void TrySetHidden(string path, bool hidden)
    {
        try
        {
            if (!File.Exists(path)) return;
            var attributes = File.GetAttributes(path);
            File.SetAttributes(path, hidden ? attributes | FileAttributes.Hidden : attributes & ~FileAttributes.Hidden);
        }
        catch { /* 속성 변경 실패는 무시 */ }
    }

    private static HttpClient CreateClient()
    {
        HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub API 는 User-Agent 가 없으면 403 을 준다.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NvChat", CurrentVersion.ToString(3)));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static async Task DownloadAsync(HttpClient http, string url, string targetPath, long expectedSize, IProgress<double> progress, CancellationToken cancellationToken)
    {
        TryDelete(targetPath);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? expectedSize;
        using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using FileStream target = new(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        byte[] buffer = new byte[81920];
        long read = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            read += count;
            if (total > 0) progress.Report(Math.Min(100, read * 100.0 / total));
        }
    }

    private static async Task<string?> TryReadChecksumAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        try
        {
            string text = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            // "<sha256>  NvChat.exe" 형식의 첫 토큰만 쓴다.
            string? token = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return token is { Length: 64 } && token.All(Uri.IsHexDigit) ? token : null;
        }
        catch { return null; }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>파일을 지운다. 지웠거나 원래 없으면 true.</summary>
    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            File.SetAttributes(path, FileAttributes.Normal); // 숨김 속성이 붙어 있으면 먼저 떼어내야 지워진다.
            File.Delete(path);
            return true;
        }
        catch { return false; } // 아직 잠겨 있으면 호출측이 잠시 뒤 다시 시도한다.
    }

    /// <summary>"v1.4.0" / "1.4.0" / "1.4.0-beta" 태그를 버전으로(자리수는 4자리로 맞춰 비교를 안정화).</summary>
    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        string text = tag.Trim().TrimStart('v', 'V');
        if (text.IndexOf('-') is var dash && dash > 0) text = text[..dash];
        if (!Version.TryParse(text, out var parsed)) return false;

        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }
}
