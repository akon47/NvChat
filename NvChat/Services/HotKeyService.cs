using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using NvChat.Models;

namespace NvChat.Services;

/// <summary>
/// 전역(시스템 전체) 단축키 등록. "Ctrl+Shift+Space" 같은 문자열을 받아 RegisterHotKey 로 건다.
/// 메시지 전용 창(HwndSource)으로 WM_HOTKEY 를 받아 이벤트로 올린다.
/// </summary>
public sealed class HotKeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int HotKeyId = 0x4E56; // 'NV'
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModWin = 0x0008, ModNoRepeat = 0x4000;

    private readonly HwndSource _source;
    private bool _registered;

    public HotKeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("NvChatHotKeyWindow") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ });
        _source.AddHook(WndProc);
    }

    public event Action? HotKeyPressed;

    /// <summary>단축키를 (다시) 등록한다. 꺼짐이거나 다른 앱이 이미 쓰는 조합이면 false.</summary>
    public bool Register(string? hotkey)
    {
        Unregister();
        if (!TryParse(hotkey, out uint mods, out uint vk)) return false;

        _registered = RegisterHotKey(_source.Handle, HotKeyId, mods | ModNoRepeat, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_source.Handle, HotKeyId);
        _registered = false;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            HotKeyPressed?.Invoke();
            handled = true;
        }
        return 0;
    }

    /// <summary>
    /// 단축키가 꺼진 값인지. 저장값은 언어와 무관한 <see cref="AppSettings.HotkeyOff"/> 이지만,
    /// 1.8 까지는 번역된 표시 문구("끄기")를 그대로 저장했으므로 그것도 꺼짐으로 읽는다.
    /// </summary>
    public static bool IsDisabled(string? hotkey) =>
        string.IsNullOrWhiteSpace(hotkey)
        || hotkey.Trim() is var n && (n.Equals(AppSettings.HotkeyOff, StringComparison.OrdinalIgnoreCase) || n.Equals("none", StringComparison.OrdinalIgnoreCase) || n == "끄기");

    /// <summary>저장할 값으로 정규화한다. 꺼짐은 항상 <see cref="AppSettings.HotkeyOff"/>.</summary>
    public static string Normalize(string? hotkey) => IsDisabled(hotkey) ? AppSettings.HotkeyOff : hotkey!.Trim();

    /// <summary>"Ctrl+Shift+Space" → (수식키 플래그, 가상 키). 수식키가 하나도 없으면 전역 단축키로 쓰지 않는다(일반 타이핑을 가로채므로).</summary>
    public static bool TryParse(string? hotkey, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (IsDisabled(hotkey)) return false;

        string[] parts = hotkey!.Trim().Split('+');
        foreach (string part in parts[..^1])
        {
            switch (part.Trim().ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win" or "windows": modifiers |= ModWin; break;
                default: return false;
            }
        }

        return TryMapKey(parts[^1].Trim(), out virtualKey) && modifiers != 0 && virtualKey != 0;
    }

    /// <summary>사용자가 누른 키 조합을 저장용 문자열로. 수식키만 눌렀거나 수식키가 없으면 null.</summary>
    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None) return null;
        if (modifiers == ModifierKeys.None) return null;
        if (KeyName(key) is not { } name) return null;

        List<string> parts = [];
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        return string.Join("+", parts);
    }

    private static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.F1 and <= Key.F12 => key.ToString(),
        Key.Space => "Space",
        Key.Enter => "Enter",
        Key.Tab => "Tab",
        Key.Oem3 => "`",
        _ => null,
    };

    private static bool TryMapKey(string key, out uint vk)
    {
        vk = 0;
        if (key.Length == 0) return false;

        if (key.Length == 1 && char.ToUpperInvariant(key[0]) is var c && (c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
        {
            vk = c;
            return true;
        }

        switch (key.ToLowerInvariant())
        {
            case "space": vk = 0x20; return true;
            case "enter" or "return": vk = 0x0D; return true;
            case "tab": vk = 0x09; return true;
            case "`" or "grave": vk = 0xC0; return true;
        }

        if (key.StartsWith('F') || key.StartsWith('f'))
        {
            if (int.TryParse(key.AsSpan(1), out int n) && n is >= 1 and <= 12)
            {
                vk = (uint)(0x70 + (n - 1));
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
