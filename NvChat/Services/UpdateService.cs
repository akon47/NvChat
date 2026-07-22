using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NvChat.Services
{
    /// <summary>
    /// GitHub Releases 를 이용한 자동 업데이트.
    ///
    /// 단일 exe 라 설치 관리자가 없지만, Windows 는 "실행 중인 exe 를 덮어쓰는 것"은 막아도
    /// "이름을 바꾸는 것"은 허용한다. 그래서 현재 exe 를 .old 로 밀어내고 새 파일을 제자리에
    /// 옮긴 뒤 재시작하면 별도 헬퍼 프로세스 없이 자기 자신을 교체할 수 있다.
    /// 다음 실행 때 App 이 남은 .old 를 지운다.
    /// </summary>
    public sealed class UpdateService
    {
        #region Constants

        private const string LatestReleaseUrl = "https://api.github.com/repos/akon47/NvChat/releases/latest";
        private const string ReleasePageUrl = "https://github.com/akon47/NvChat/releases/latest";
        private const string AssetName = "NvChat.exe";
        private const string ChecksumAssetName = "NvChat.exe.sha256";

        #endregion


        #region Properties

        /// <summary>업데이트 실패 시 안내할 릴리즈 페이지 주소.</summary>
        public static string ReleasePage => ReleasePageUrl;

        /// <summary>현재 실행 중인 버전.</summary>
        public static Version CurrentVersion
        {
            get
            {
                var version = Assembly.GetEntryAssembly()?.GetName()?.Version;
                return version ?? new Version(0, 0, 0, 0);
            }
        }

        #endregion


        #region Helpers - 확인

        /// <summary>
        /// 최신 릴리즈를 조회한다. 더 새로운 버전이 없거나 조회에 실패하면 null.
        /// </summary>
        public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var http = CreateClient();
                using var response = await http.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode == false)
                    return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                    return null;

                if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                    return null;

                var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (TryParseVersion(tag, out var latest) == false)
                    return null;

                if (latest <= CurrentVersion)
                    return null;

                if (root.TryGetProperty("assets", out var assets) == false || assets.ValueKind != JsonValueKind.Array)
                    return null;

                string downloadUrl = null;
                string checksumUrl = null;
                long size = 0;

                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

                    if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = url;
                        if (asset.TryGetProperty("size", out var s))
                            size = s.GetInt64();
                    }
                    else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        checksumUrl = url;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                    return null;

                return new UpdateInfo
                {
                    Version = latest,
                    Tag = tag,
                    DownloadUrl = downloadUrl,
                    ChecksumUrl = checksumUrl,
                    Size = size,
                    Notes = root.TryGetProperty("body", out var b) ? b.GetString() : null
                };
            }
            catch (Exception)
            {
                // 네트워크 문제/응답 형식 변경 등은 조용히 무시한다. 업데이트는 부가 기능이다.
                return null;
            }
        }

        #endregion


        #region Helpers - 적용

        /// <summary>
        /// 새 exe 를 내려받아 검증한 뒤 현재 exe 와 교체한다.
        /// 성공하면 새 프로세스를 띄우고 true 를 반환한다(호출측에서 종료해야 한다).
        /// </summary>
        public async Task<bool> DownloadAndApplyAsync(UpdateInfo info, IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || File.Exists(exePath) == false)
                throw new InvalidOperationException("실행 파일 경로를 찾을 수 없어 업데이트할 수 없습니다.");

            var newPath = exePath + ".new";
            var oldPath = exePath + ".old";

            using var http = CreateClient();

            // 1) 다운로드 (같은 폴더에 받아야 이후 이동이 같은 볼륨 내에서 원자적으로 끝난다)
            await DownloadAsync(http, info.DownloadUrl, newPath, info.Size, progress, cancellationToken).ConfigureAwait(false);

            // 2) 체크섬 검증 (릴리즈에 sha256 이 함께 올라온 경우)
            if (string.IsNullOrEmpty(info.ChecksumUrl) == false)
            {
                var expected = await TryReadChecksumAsync(http, info.ChecksumUrl, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrEmpty(expected) == false)
                {
                    var actual = ComputeSha256(newPath);
                    if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) == false)
                    {
                        TryDelete(newPath);
                        throw new InvalidOperationException("내려받은 파일의 체크섬이 일치하지 않아 업데이트를 중단했습니다.");
                    }
                }
            }

            // 3) 교체: 실행 중인 exe 는 덮어쓸 수 없지만 이름은 바꿀 수 있다.
            TryDelete(oldPath);

            try
            {
                File.Move(exePath, oldPath);
            }
            catch (Exception ex)
            {
                TryDelete(newPath);
                throw new InvalidOperationException("실행 파일을 교체할 수 없습니다. 쓰기 권한이 있는 폴더로 옮긴 뒤 다시 시도하세요. (" + ex.Message + ")");
            }

            try
            {
                File.Move(newPath, exePath);
            }
            catch
            {
                // 되돌린다. 여기서 실패하면 앱이 사라지므로 롤백은 반드시 시도한다.
                try { File.Move(oldPath, exePath); } catch { }
                TryDelete(newPath);
                throw;
            }

            // 4) 새 버전으로 재시작
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            return true;
        }

        /// <summary>
        /// 이전 실행에서 남은 .old 파일을 정리한다. (앱 시작 시 호출)
        /// </summary>
        public static void CleanupLeftovers()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                    return;

                TryDelete(exePath + ".old");
                TryDelete(exePath + ".new");
            }
            catch
            {
            }
        }

        #endregion


        #region Helpers - 공통

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

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

            var total = response.Content.Headers.ContentLength ?? expectedSize;

            using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long read = 0;

            while (true)
            {
                var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    break;

                await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);

                read += count;
                if (total > 0)
                    progress?.Report(Math.Min(100, read * 100.0 / total));
            }
        }

        private static async Task<string> TryReadChecksumAsync(HttpClient http, string url, CancellationToken cancellationToken)
        {
            try
            {
                var text = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

                // "<sha256>  NvChat.exe" 형식의 첫 토큰만 쓴다.
                var token = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return token != null && token.Length == 64 ? token : null;
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // 아직 잠겨 있으면 다음 실행 때 다시 시도한다.
            }
        }

        /// <summary>"v1.4.0" / "1.4.0" 형태의 태그를 버전으로 바꾼다.</summary>
        private static bool TryParseVersion(string tag, out Version version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(tag))
                return false;

            var text = tag.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(1);

            // 프리릴리즈 접미사(-beta 등)는 잘라낸다.
            var dash = text.IndexOf('-');
            if (dash > 0)
                text = text.Substring(0, dash);

            if (Version.TryParse(text, out var parsed) == false)
                return false;

            // 1.4 → 1.4.0.0 처럼 자리수를 맞춰야 비교가 안정적이다.
            version = new Version(
                parsed.Major,
                parsed.Minor,
                parsed.Build < 0 ? 0 : parsed.Build,
                parsed.Revision < 0 ? 0 : parsed.Revision);

            return true;
        }

        #endregion
    }


    /// <summary>
    /// 사용 가능한 새 버전 정보.
    /// </summary>
    public sealed class UpdateInfo
    {
        public Version Version { get; set; }

        public string Tag { get; set; }

        public string DownloadUrl { get; set; }

        public string ChecksumUrl { get; set; }

        public long Size { get; set; }

        public string Notes { get; set; }

        /// <summary>"12.3 MB" 형태의 크기 표시.</summary>
        public string SizeText => Size > 0 ? $"{Size / 1024.0 / 1024.0:0.#} MB" : null;
    }
}
