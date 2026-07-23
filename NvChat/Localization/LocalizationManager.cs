using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace NvChat.Localization
{
    /// <summary>
    /// 언어별 문자열을 제공한다.
    ///
    /// 이 앱은 단일 exe 로 배포되므로 언어 JSON 을 어셈블리에 임베드(EmbeddedResource)해 둔다.
    /// (참고: 느슨한 locale\ 폴더 방식은 파일이 하나 더 늘어 단일 파일 배포와 맞지 않는다)
    /// 각 json 은 자신의 메타데이터("_culture", "_name")를 담는 평평한 문자열 사전이다.
    /// 인덱서(this[key])에 OneWay 바인딩하면(LocExtension) 언어 변경 시 실시간으로 갱신된다.
    /// </summary>
    public sealed class LocalizationManager : INotifyPropertyChanged
    {
        #region Constants

        private const string Fallback = "en-US";
        private const string MetaCulture = "_culture";
        private const string MetaName = "_name";

        // 임베드 리소스 이름 접두사. (예: NvChat.Localization.en-US.json)
        private const string ResourcePrefix = "NvChat.Localization.";

        #endregion


        #region Singleton

        public static LocalizationManager Instance { get; } = new LocalizationManager();

        /// <summary>지원 언어: (컬처 코드, 표시 이름). 임베드된 *.json 스캔으로 구성된다.</summary>
        public static (string Culture, string Display)[] Available => Instance._available;

        #endregion


        #region Fields

        private readonly Dictionary<string, Dictionary<string, string>> _all =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private (string Culture, string Display)[] _available = Array.Empty<(string, string)>();
        private string _culture = Fallback;

        #endregion


        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>언어가 바뀌어 코드로 만든 문자열(트레이 메뉴/상태 등)도 다시 그려야 할 때 발생.</summary>
        public event Action LanguageChanged;

        #endregion


        #region Constructors

        private LocalizationManager()
        {
            var found = new List<(string Culture, string Display)>();

            foreach (var (key, dict) in DiscoverLocales())
            {
                var culture = dict.TryGetValue(MetaCulture, out var c) && string.IsNullOrWhiteSpace(c) == false
                    ? c.Trim()
                    : key;

                if (string.IsNullOrEmpty(culture))
                    continue;

                var display = dict.TryGetValue(MetaName, out var n) && string.IsNullOrWhiteSpace(n) == false
                    ? n.Trim()
                    : culture;

                _all[culture] = dict;
                found.Add((culture, display));
            }

            // 표시 이름 기준으로 결정적 정렬.
            _available = found.OrderBy(x => x.Display, StringComparer.CurrentCulture).ToArray();

            // 스캔이 실패하더라도 앱이 죽지 않도록 최소한의 폴백을 보장한다.
            if (_all.ContainsKey(Fallback) == false)
                _all[Fallback] = new Dictionary<string, string>();

            if (_available.Length == 0)
                _available = new[] { (Fallback, "English") };
        }

        #endregion


        #region Properties

        /// <summary>현재 컬처. 설정 시 UI 스레드 컬처도 갱신하고 모든 바인딩을 새로고침한다.</summary>
        public string Culture
        {
            get => _culture;
            set
            {
                if (string.IsNullOrEmpty(value) || _all.ContainsKey(value) == false || value == _culture)
                    return;

                _culture = value;

                try
                {
                    var ci = new CultureInfo(value);
                    CultureInfo.CurrentUICulture = ci;
                    CultureInfo.DefaultThreadCurrentUICulture = ci;
                }
                catch
                {
                    // 표시 문자열만 바꾸면 되므로 스레드 컬처 실패는 무시.
                }

                // 인덱서 바인딩 + 전체 갱신
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
                LanguageChanged?.Invoke();
            }
        }

        /// <summary>키에 해당하는 번역. 없으면 영어 폴백, 그래도 없으면 키 자체.</summary>
        public string this[string key]
        {
            get
            {
                if (_all.TryGetValue(_culture, out var d) && d.TryGetValue(key, out var v))
                    return v;

                if (_all.TryGetValue(Fallback, out var f) && f.TryGetValue(key, out var fv))
                    return fv;

                return key;
            }
        }

        #endregion


        #region Helpers

        /// <summary>코드에서 즉시 조회(바인딩 아님). 포맷 인자 지원.</summary>
        public string Tr(string key, params object[] args)
        {
            var s = this[key];
            return args == null || args.Length == 0 ? s : string.Format(s, args);
        }

        /// <summary>어셈블리에 임베드된 *.json 들을 (컬처 키, 파싱된 사전)으로 열거한다.</summary>
        private static IEnumerable<(string Key, Dictionary<string, string> Dict)> DiscoverLocales()
        {
            var assembly = typeof(LocalizationManager).Assembly;

            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) == false)
                    continue;

                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == false)
                    continue;

                Dictionary<string, string> dict = null;
                try
                {
                    using var stream = assembly.GetManifestResourceStream(name);
                    if (stream == null)
                        continue;

                    using var reader = new StreamReader(stream);
                    dict = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
                }
                catch
                {
                    dict = null;
                }

                if (dict != null)
                {
                    // 리소스 이름에서 컬처 키 복원: "NvChat.Localization.en-US.json" → "en-US"
                    var key = name.Substring(ResourcePrefix.Length);
                    key = key.Substring(0, key.Length - ".json".Length);
                    yield return (key, dict);
                }
            }
        }

        /// <summary>첫 실행 기본 언어: 윈도우 UI 언어와 가장 잘 맞는 지원 언어. 없으면 en-US.</summary>
        public static string ResolveDefaultCulture()
        {
            try
            {
                var ci = CultureInfo.InstalledUICulture;

                // 1) 정확히 일치하는 컬처(예: ko-KR)
                foreach (var (culture, _) in Available)
                {
                    if (culture.Equals(ci.Name, StringComparison.OrdinalIgnoreCase))
                        return culture;
                }

                // 2) 두 글자 언어 코드로 매칭(예: ko-KP → ko-KR, en-GB → en-US)
                foreach (var (culture, _) in Available)
                {
                    if (culture.StartsWith(ci.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase))
                        return culture;
                }
            }
            catch
            {
                // 아래 폴백.
            }

            return Fallback;
        }

        #endregion
    }
}
