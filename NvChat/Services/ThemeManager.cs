using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace NvChat.Services;

/// <summary>사용자가 고르는 테마 설정값.</summary>
public static class ThemeNames
{
    public const string System = "System";
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string HighContrast = "HighContrast";

    public static readonly string[] All = [System, Dark, Light, HighContrast];
}

/// <summary>
/// 테마(다크/라이트/고대비)와 본문 확대 배율을 관리한다.
///
/// 브러시는 Brushes.xaml 에 다크 팔레트로 정의돼 있고, XAML 은 모두 DynamicResource 로 참조한다.
/// 테마를 바꾸면 선택한 팔레트(Colors*.xaml)의 색으로 같은 키의 브러시를 새로 만들어 앱 리소스 최상위에 덮어쓴다.
/// 최상위 항목이 병합된 사전보다 먼저 검색되므로 모든 DynamicResource 참조가 즉시 새 브러시로 바뀐다.
/// (앱 리소스의 브러시는 WPF 가 자동으로 동결하므로 색만 바꿔치기할 수는 없다.)
/// </summary>
public sealed class ThemeManager : INotifyPropertyChanged
{
    public const double MinScale = 0.8;
    public const double MaxScale = 1.6;

    private const string DictionaryRoot = "pack://application:,,,/NvChat;component/Resources/Dictionaries/";

    private string _setting = ThemeNames.System;
    private double _scale = 1.0;
    private bool _watching;

    public static ThemeManager Instance { get; } = new();

    private ThemeManager()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>실제 적용된 테마가 바뀌었을 때(코드로 만든 요소·DWM 프레임이 다시 맞춰야 할 때) 발생한다.</summary>
    public event Action? ThemeChanged;

    /// <summary>지금 실제로 적용된 테마(System 이 풀린 결과): Dark / Light / HighContrast.</summary>
    public string Effective { get; private set; } = ThemeNames.Dark;

    public bool IsDark => Effective != ThemeNames.Light;

    /// <summary>본문 확대 배율. 창 템플릿이 본문에 LayoutTransform 으로 건다.</summary>
    public double Scale
    {
        get => _scale;
        set
        {
            double clamped = Math.Round(Math.Clamp(double.IsFinite(value) ? value : 1.0, MinScale, MaxScale), 2);
            if (Math.Abs(clamped - _scale) < 0.001) return;
            _scale = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Scale)));
        }
    }

    /// <summary>테마 설정값을 적용한다. System 이면 윈도우의 앱 모드(밝게/어둡게)와 고대비 여부를 따르고 이후 변경도 따라간다.</summary>
    public void Apply(string? setting)
    {
        _setting = ThemeNames.All.Contains(setting) ? setting! : ThemeNames.System;
        WatchSystem();
        ApplyEffective(Resolve(_setting));
    }

    private static string Resolve(string setting)
    {
        if (setting != ThemeNames.System) return setting;
        if (SystemParameters.HighContrast) return ThemeNames.HighContrast;
        return SystemUsesLightTheme() ? ThemeNames.Light : ThemeNames.Dark;
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    private void WatchSystem()
    {
        if (_watching) return;
        _watching = true;

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)) return;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_setting == ThemeNames.System) ApplyEffective(Resolve(_setting));
            });
        };
    }

    private void ApplyEffective(string theme)
    {
        if (Application.Current is not { } app) return;

        string file = theme switch
        {
            ThemeNames.Light => "Colors.Light.xaml",
            ThemeNames.HighContrast => "Colors.HighContrast.xaml",
            _ => "Colors.xaml",
        };

        try
        {
            ResourceDictionary palette = new() { Source = new Uri(DictionaryRoot + file) };
            ResourceDictionary brushes = new() { Source = new Uri(DictionaryRoot + "Brushes.xaml") };

            foreach (object key in palette.Keys)
            {
                if (palette[key] is Color color) app.Resources[key] = color;
            }

            foreach (object key in brushes.Keys)
            {
                if (key is not string name || brushes[key] is not SolidColorBrush template) continue;
                if (palette[ColorKeyFor(name)] is not Color color) continue;

                SolidColorBrush brush = new(color) { Opacity = template.Opacity };
                brush.Freeze();
                app.Resources[name] = brush;
            }
        }
        catch
        {
            return; // 팔레트 로드 실패 시 지금 테마를 유지한다
        }

        bool changed = Effective != theme;
        Effective = theme;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Effective)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDark)));
        if (changed) ThemeChanged?.Invoke();
    }

    /// <summary>브러시 키에 대응하는 팔레트 색 키. 기본은 접미사만 바꾼다(XBrush → XColor).</summary>
    internal static string ColorKeyFor(string brushKey) => brushKey switch
    {
        "AccentSoftBrush" => "AccentColor",
        "OverlayHoverBrush" or "OverlayPressedBrush" => "OverlayColor",
        _ when brushKey.EndsWith("Brush", StringComparison.Ordinal) => brushKey[..^"Brush".Length] + "Color",
        _ => brushKey,
    };

    /// <summary>현재 테마의 색을 COLORREF(0x00BBGGRR)로. DWM 테두리 색 등에 쓴다.</summary>
    public static int ColorRef(string colorKey, int fallback)
    {
        if (Application.Current?.TryFindResource(colorKey) is not Color c) return fallback;
        return c.R | (c.G << 8) | (c.B << 16);
    }
}
