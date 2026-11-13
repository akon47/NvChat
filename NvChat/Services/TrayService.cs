using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using NvChat.Localization;

namespace NvChat.Services;

/// <summary>
/// 시스템 트레이(알림 영역) 아이콘과 컨텍스트 메뉴.
///
/// WinForms 의 NotifyIcon 을 빌려 쓰지 않고 Shell_NotifyIcon 으로 아이콘을 직접 등록한다. 그래야 오른쪽 단추 메뉴로
/// WPF ContextMenu 를 띄울 수 있고, 메뉴가 앱의 다른 메뉴와 같은 스타일·테마(다크/라이트/고대비)로 그려진다.
/// (NotifyIcon 의 메뉴는 WinForms 가 그려서 색 표만 바꿔서는 호버 색 등이 테마를 따르지 않는다.)
/// 메뉴 문구는 번역 문자열에 바인딩돼 있어 언어를 바꾸면 바로 따라 바뀐다.
/// </summary>
public sealed class TrayService : IDisposable
{
    private const int WmTrayCallback = 0x0400 + 1024;
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int WmRightButtonUp = 0x0205;
    private const int NinBalloonUserClick = 0x0400 + 5;

    private readonly HwndSource _messageWindow;
    private readonly uint _taskbarCreatedMessage;
    private readonly uint _id = (uint)Guid.NewGuid().GetHashCode();
    private readonly ContextMenu _menu;

    private nint _iconHandle;
    private bool _isVisible;
    private bool _disposed;

    public TrayService()
    {
        // HWND_MESSAGE 의 자식으로 만들면 화면에 보이지 않는 메시지 전용 창이 된다. 트레이 아이콘의 마우스 알림을 이 창이 받는다.
        _messageWindow = new HwndSource(new HwndSourceParameters("NvChatTrayWindow") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _messageWindow.AddHook(OnMessage);

        // 탐색기가 다시 시작되면 트레이 아이콘이 사라지므로 이 메시지를 받아 다시 등록한다.
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        _iconHandle = LoadIcon();
        _menu = CreateMenu(
            ("TrayOpen", "IconChat", () => OpenRequested?.Invoke()),
            ("TrayQuickChat", "IconSend", () => QuickChatRequested?.Invoke()),
            ("TrayNewChat", "IconPlus", () => NewChatRequested?.Invoke()),
            null,
            ("Settings", "IconSettings", () => SettingsRequested?.Invoke()),
            null,
            ("TrayExit", "IconClose", () => ExitRequested?.Invoke()));

        Show();
    }

    public event Action? OpenRequested;
    public event Action? QuickChatRequested;
    public event Action? NewChatRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    /// <summary>트레이 알림(창을 닫아 트레이로 숨었을 때, 가려진 동안 응답이 끝났을 때 등). 윈도우 10/11 에서는 알림 센터 토스트로 뜬다.</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_isVisible) return;

        var data = CreateData();
        data.uFlags |= NifInfo;
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = text.Length > 255 ? text[..255] : text;
        data.dwInfoFlags = NiifUser; // 알림에 시스템 '정보' 아이콘 대신 앱 아이콘을 쓴다
        Shell_NotifyIcon(NimModify, ref data);
    }

    private void Show()
    {
        if (_isVisible || _disposed) return;

        var data = CreateData();
        _isVisible = Shell_NotifyIcon(NimAdd, ref data);
    }

    private void Hide()
    {
        if (!_isVisible) return;

        var data = CreateData();
        Shell_NotifyIcon(NimDelete, ref data);
        _isVisible = false;
    }

    private NotifyIconData CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _messageWindow.Handle,
        uID = _id,
        uFlags = NifMessage | NifIcon | NifTip | NifShowTip,
        uCallbackMessage = WmTrayCallback,
        hIcon = _iconHandle,
        szTip = "NvChat",
        szInfo = "",
        szInfoTitle = "",
    };

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            _isVisible = false;
            Show();
            return 0;
        }

        if (message != WmTrayCallback) return 0;

        switch ((int)lParam)
        {
            case WmLeftButtonDoubleClick:
            case NinBalloonUserClick:
                OpenRequested?.Invoke();
                break;

            case WmRightButtonUp:
                ShowMenu();
                break;
        }

        return 0;
    }

    private void ShowMenu()
    {
        // 메뉴 밖을 눌렀을 때 메뉴가 닫히려면 메뉴를 띄우기 전에 이 창이 활성 창이어야 한다.
        SetForegroundWindow(_messageWindow.Handle);

        _menu.Placement = PlacementMode.MousePoint;
        _menu.StaysOpen = false;
        _menu.IsOpen = true;
        _menu.Focus();
    }

    /// <summary>
    /// 트레이 메뉴를 만든다. 항목은 (문구의 번역 키, 아이콘 리소스 키, 동작)이고 null 은 구분선.
    /// 앱 리소스의 ContextMenu/MenuItem 스타일이 그대로 적용된다. (스크린샷 모드의 견본도 이 메서드를 쓴다)
    /// </summary>
    internal static ContextMenu CreateMenu(params (string TextKey, string IconKey, Action Run)?[] entries)
    {
        ContextMenu menu = new() { MinWidth = 190 };

        foreach (var entry in entries)
        {
            if (entry is not { } item)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            MenuItem menuItem = new() { Icon = Application.Current?.TryFindResource(item.IconKey) as Geometry };
            menuItem.SetBinding(HeaderedItemsControl.HeaderProperty, new Binding($"[{item.TextKey}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            menuItem.Click += (_, _) => item.Run();
            menu.Items.Add(menuItem);
        }

        return menu;
    }

    /// <summary>스크린샷 모드용: 트레이 아이콘 없이 같은 모양의 메뉴만 만든다.</summary>
    internal static ContextMenu CreatePreviewMenu() => CreateMenu(
        ("TrayOpen", "IconChat", () => { }),
        ("TrayQuickChat", "IconSend", () => { }),
        ("TrayNewChat", "IconPlus", () => { }),
        null,
        ("Settings", "IconSettings", () => { }),
        null,
        ("TrayExit", "IconClose", () => { }));

    /// <summary>
    /// 앱 아이콘(.ico 리소스)에서 트레이 크기에 가장 가까운 이미지를 골라 아이콘 핸들을 만든다.
    /// 단일 파일로 게시하면 실행 파일에서 아이콘을 꺼내기 어려우므로 리소스 바이트를 직접 해석한다.
    /// </summary>
    private static nint LoadIcon()
    {
        try
        {
            if (Application.GetResourceStream(new Uri("pack://application:,,,/NvChat;component/Resources/app.ico")) is not { } info) return 0;

            using MemoryStream buffer = new();
            using (info.Stream) info.Stream.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();

            int size = GetSystemMetrics(SmCxSmallIcon);
            return CreateIcon(bytes, size, size);
        }
        catch
        {
            return 0;
        }
    }

    private static nint CreateIcon(byte[] iconBytes, int width, int height)
    {
        const int headerSize = 6;
        const int entrySize = 16;
        if (iconBytes.Length < headerSize) return 0;

        var span = iconBytes.AsSpan();
        int count = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        if (count == 0 || iconBytes.Length < headerSize + count * entrySize) return 0;

        int bestOffset = 0;
        int bestLength = 0;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var entry = span[(headerSize + i * entrySize)..];

            // 아이콘 디렉터리는 크기를 1바이트로 적으며 0 은 256 을 뜻한다.
            int entryWidth = entry[0] == 0 ? 256 : entry[0];
            int entryHeight = entry[1] == 0 ? 256 : entry[1];
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (length <= 0 || offset < 0 || offset + length > iconBytes.Length) continue;

            // 같은 거리라면 큰 쪽(축소)이 작은 쪽(확대)보다 선명하다.
            int distance = Math.Abs(entryWidth - width) + Math.Abs(entryHeight - height);
            if (entryWidth < width) distance += 1;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestOffset = offset;
                bestLength = length;
            }
        }

        if (bestLength == 0) return 0;

        var pinned = GCHandle.Alloc(iconBytes, GCHandleType.Pinned);
        try
        {
            return CreateIconFromResourceEx(pinned.AddrOfPinnedObject() + bestOffset, (uint)bestLength, true, 0x00030000, width, height, 0);
        }
        finally
        {
            pinned.Free();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Hide();
        _menu.IsOpen = false;
        _messageWindow.RemoveHook(OnMessage);
        _messageWindow.Dispose();

        if (_iconHandle != 0) DestroyIcon(_iconHandle);
        _iconHandle = 0;
    }

    // ===== Win32 =====

    private const int SmCxSmallIcon = 49;
    private const int NimAdd = 0x00000000;
    private const int NimModify = 0x00000001;
    private const int NimDelete = 0x00000002;
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;
    private const int NifInfo = 0x00000010;
    private const int NifShowTip = 0x00000080;
    private const int NiifUser = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public int dwState;
        public int dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public int uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public int dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateIconFromResourceEx(nint bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool isIcon, uint version, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
