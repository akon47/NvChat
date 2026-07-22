using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NvChat.Services
{
    /// <summary>
    /// 전역 단축키를 등록하고 눌렸을 때 이벤트를 발생시킨다. (메시지 전용 창 사용)
    /// </summary>
    public sealed class HotKeyService : IDisposable
    {
        #region Constants

        private const int WM_HOTKEY = 0x0312;
        private const int HotKeyId = 0x4E56; // 'NV'
        private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;

        #endregion


        #region Fields

        private readonly HwndSource _source;
        private bool _registered;

        #endregion


        #region Constructors

        public HotKeyService()
        {
            var parameters = new HwndSourceParameters("NvChatHotKeyWindow")
            {
                ParentWindow = new IntPtr(-3) // HWND_MESSAGE (메시지 전용 창)
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        #endregion


        #region Events

        public event Action HotKeyPressed;

        #endregion


        #region Helpers

        /// <summary>단축키 문자열("Ctrl+Alt+Space")을 등록한다. 실패/끄기면 false.</summary>
        public bool Register(string hotkey)
        {
            Unregister();

            if (TryParse(hotkey, out var mods, out var vk) == false)
                return false;

            _registered = RegisterHotKey(_source.Handle, HotKeyId, mods | MOD_NOREPEAT, vk);
            return _registered;
        }

        public void Unregister()
        {
            if (_registered)
            {
                UnregisterHotKey(_source.Handle, HotKeyId);
                _registered = false;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotKeyId)
            {
                HotKeyPressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public static bool TryParse(string hotkey, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;

            if (string.IsNullOrWhiteSpace(hotkey))
                return false;

            var normalized = hotkey.Trim();
            if (normalized.Equals("끄기", StringComparison.OrdinalIgnoreCase) || normalized.Equals("off", StringComparison.OrdinalIgnoreCase) || normalized.Equals("none", StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = normalized.Split('+');
            if (parts.Length == 0)
                return false;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= MOD_CONTROL; break;
                    case "alt": modifiers |= MOD_ALT; break;
                    case "shift": modifiers |= MOD_SHIFT; break;
                    case "win": case "windows": modifiers |= MOD_WIN; break;
                    default: return false;
                }
            }

            var key = parts[parts.Length - 1].Trim();
            if (TryMapKey(key, out virtualKey) == false)
                return false;

            return modifiers != 0 && virtualKey != 0;
        }

        private static bool TryMapKey(string key, out uint vk)
        {
            vk = 0;
            if (string.IsNullOrEmpty(key))
                return false;

            if (key.Length == 1)
            {
                var c = char.ToUpperInvariant(key[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    vk = c;
                    return true;
                }
            }

            switch (key.ToLowerInvariant())
            {
                case "space": vk = 0x20; return true;
                case "enter": case "return": vk = 0x0D; return true;
                case "tab": vk = 0x09; return true;
                case "`": case "grave": vk = 0xC0; return true;
            }

            if ((key.StartsWith("F", StringComparison.OrdinalIgnoreCase) || key.StartsWith("f")) && int.TryParse(key.Substring(1), out var n) && n >= 1 && n <= 12)
            {
                vk = (uint)(0x70 + (n - 1));
                return true;
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
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        #endregion
    }
}
