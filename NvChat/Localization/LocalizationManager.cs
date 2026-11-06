using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace NvChat.Localization;

/// <summary>
/// 언어별 문자열을 제공한다.
/// 이 앱은 단일 exe 로 배포되므로 언어 JSON 을 어셈블리에 임베드(EmbeddedResource)해 둔다.
/// 각 json 은 자신의 메타데이터("_culture", "_name")를 담는 평평한 문자열 사전이다.
/// 인덱서(this[key])에 OneWay 바인딩하면(LocExtension) 언어 변경 시 실시간으로 갱신된다.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private const string Fallback = "en-US";
    private const string MetaCulture = "_culture";
    private const string MetaName = "_name";
    private const string ResourcePrefix = "NvChat.Localization."; // 예: NvChat.Localization.en-US.json

    public static LocalizationManager Instance { get; } = new();

    /// <summary>지원 언어: (컬처 코드, 표시 이름). 임베드된 *.json 스캔으로 구성된다.</summary>
    public static (string Culture, string Display)[] Available => Instance._available;

    private readonly Dictionary<string, Dictionary<string, string>> _all = new(StringComparer.OrdinalIgnoreCase);
    private readonly (string Culture, string Display)[] _available;
    private string _culture = Fallback;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>언어가 바뀌어 코드로 만든 문자열(트레이 메뉴/상태 등)도 다시 그려야 할 때 발생.</summary>
    public event Action? LanguageChanged;

    private LocalizationManager()
    {
        List<(string Culture, string Display)> found = [];
        foreach (var (key, dict) in DiscoverLocales())
        {
            string culture = dict.TryGetValue(MetaCulture, out var c) && !string.IsNullOrWhiteSpace(c) ? c.Trim() : key;
            if (culture.Length == 0) continue;

            string display = dict.TryGetValue(MetaName, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : culture;
            _all[culture] = dict;
            found.Add((culture, display));
        }

        _available = found.OrderBy(x => x.Display, StringComparer.CurrentCulture).ToArray();

        // 스캔이 실패하더라도 앱이 죽지 않도록 최소한의 폴백을 보장한다.
        if (!_all.ContainsKey(Fallback)) _all[Fallback] = [];
        if (_available.Length == 0) _available = [(Fallback, "English")];
    }

    /// <summary>현재 컬처. 설정 시 UI 스레드 컬처도 갱신하고 모든 바인딩을 새로고침한다.</summary>
    public string Culture
    {
        get => _culture;
        set
        {
            if (string.IsNullOrEmpty(value) || !_all.ContainsKey(value) || value == _culture) return;
            _culture = value;

            try
            {
                CultureInfo ci = new(value);
                CultureInfo.CurrentUICulture = ci;
                CultureInfo.DefaultThreadCurrentUICulture = ci;
            }
            catch { /* 표시 문자열만 바꾸면 되므로 스레드 컬처 실패는 무시. */ }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>키에 해당하는 번역. 없으면 영어 폴백, 그래도 없으면 키 자체.</summary>
    public string this[string key] =>
        _all.TryGetValue(_culture, out var d) && d.TryGetValue(key, out var v) ? v
        : _all.TryGetValue(Fallback, out var f) && f.TryGetValue(key, out var fv) ? fv
        : key;

    /// <summary>코드에서 즉시 조회(바인딩 아님). 포맷 인자 지원.</summary>
    public string Tr(string key, params object?[] args) => args.Length == 0 ? this[key] : string.Format(this[key], args);

    /// <summary>어셈블리에 임베드된 *.json 들을 (컬처 키, 파싱된 사전)으로 열거한다.</summary>
    private static IEnumerable<(string Key, Dictionary<string, string> Dict)> DiscoverLocales()
    {
        var assembly = typeof(LocalizationManager).Assembly;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

            Dictionary<string, string>? dict = null;
            try
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null) continue;
                using StreamReader reader = new(stream);
                dict = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
            }
            catch { dict = null; }

            if (dict is not null) yield return (name[ResourcePrefix.Length..^".json".Length], dict);
        }
    }

    /// <summary>첫 실행 기본 언어: 윈도우 UI 언어와 가장 잘 맞는 지원 언어. 없으면 en-US.</summary>
    public static string ResolveDefaultCulture()
    {
        try
        {
            var ci = CultureInfo.InstalledUICulture;
            if (Available.FirstOrDefault(a => a.Culture.Equals(ci.Name, StringComparison.OrdinalIgnoreCase)).Culture is { } exact) return exact;
            if (Available.FirstOrDefault(a => a.Culture.StartsWith(ci.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase)).Culture is { } lang) return lang;
        }
        catch { /* 아래 폴백 */ }
        return Fallback;
    }
}
