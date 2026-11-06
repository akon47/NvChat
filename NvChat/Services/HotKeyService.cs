using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NvChat.Services;

/// <summary>전역 단축키를 등록하고 눌렸을 때 이벤트를 발생시킨다. (메시지 전용 창 사용)</summary>
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

    /// <summary>단축키 문자열("Ctrl+Alt+Space")을 등록한다. 실패/끄기면 false.</summary>
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
    /// 전역 단축키가 '끄기(비활성)' 상태인지. 언어별 "끄기" 라벨과 무관하게 인식하도록
    /// 알려진 off 라벨(영어 Off / 한국어 끄기 / off / none)과 빈 값을 모두 처리한다.
    /// </summary>
    public static bool IsDisabled(string? hotkey) =>
        string.IsNullOrWhiteSpace(hotkey)
        || hotkey.Trim() is var n && (n.Equals("Off", StringComparison.OrdinalIgnoreCase) || n.Equals("none", StringComparison.OrdinalIgnoreCase) || n == "끄기");

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
