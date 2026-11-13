using System.Windows;
using NvChat.Localization;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;

namespace NvChat.Services;

/// <summary>시스템 트레이 아이콘과 컨텍스트 메뉴. 언어가 바뀌면 메뉴 라벨을, 테마가 바뀌면 색을 다시 맞춘다.</summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public TrayService()
    {
        _icon = new WinForms.NotifyIcon
        {
            Text = "NvChat",
            Visible = true,
            Icon = LoadIcon(),
            ContextMenuStrip = new WinForms.ContextMenuStrip { ShowImageMargin = false },
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();

        // 메뉴가 열릴 때마다 현재 테마 색으로 칠한다(WinForms 기본은 밝은 회색이라 앱의 테마와 어긋난다).
        _icon.ContextMenuStrip.Opening += (_, _) => ApplyTheme();

        BuildMenu();
        LocalizationManager.Instance.LanguageChanged += BuildMenu;
    }

    public event Action? OpenRequested;
    public event Action? QuickChatRequested;
    public event Action? NewChatRequested;
    public event Action? ExitRequested;

    /// <summary>트레이 알림(창을 닫아 트레이로 숨었을 때, 가려진 동안 응답이 끝났을 때 등). 윈도우 10/11 에서는 알림 센터 토스트로 뜬다.</summary>
    public void ShowBalloon(string title, string text)
    {
        try { _icon.ShowBalloonTip(5000, title, text, WinForms.ToolTipIcon.None); }
        catch { /* 알림 실패는 무시 */ }
    }

    private void BuildMenu()
    {
        var L = LocalizationManager.Instance;
        var items = _icon.ContextMenuStrip!.Items;
        items.Clear();
        items.Add(L["TrayOpen"], null, (_, _) => OpenRequested?.Invoke());
        items.Add(L["TrayQuickChat"], null, (_, _) => QuickChatRequested?.Invoke());
        items.Add(L["TrayNewChat"], null, (_, _) => NewChatRequested?.Invoke());
        items.Add(new WinForms.ToolStripSeparator());
        items.Add(L["TrayExit"], null, (_, _) => ExitRequested?.Invoke());

        foreach (WinForms.ToolStripItem item in items)
        {
            item.Padding = new WinForms.Padding(6, 4, 6, 4);
        }
    }

    private void ApplyTheme()
    {
        var menu = _icon.ContextMenuStrip!;
        ThemeColors colors = new(
            Background: Resolve("CardBackgroundColor", Drawing.Color.FromArgb(0x25, 0x25, 0x26)),
            Foreground: Resolve("ForegroundColor", Drawing.Color.FromArgb(0xF2, 0xF2, 0xF3)),
            Hover: Resolve("ControlHoverBackgroundColor", Drawing.Color.FromArgb(0x32, 0x32, 0x34)),
            Border: Resolve("BorderStrongColor", Drawing.Color.FromArgb(0x45, 0x45, 0x4A)));

        menu.Renderer = new WinForms.ToolStripProfessionalRenderer(new ThemedColorTable(colors)) { RoundedEdges = false };
        menu.BackColor = colors.Background;
        menu.ForeColor = colors.Foreground;
        foreach (WinForms.ToolStripItem item in menu.Items) item.ForeColor = colors.Foreground;
    }

    private static Drawing.Color Resolve(string key, Drawing.Color fallback) =>
        Application.Current?.TryFindResource(key) is WpfColor c ? Drawing.Color.FromArgb(c.R, c.G, c.B) : fallback;

    private readonly record struct ThemeColors(Drawing.Color Background, Drawing.Color Foreground, Drawing.Color Hover, Drawing.Color Border);

    /// <summary>WinForms 메뉴를 앱 테마 색으로 그리게 하는 색 표.</summary>
    private sealed class ThemedColorTable(ThemeColors colors) : WinForms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => colors.Background;
        public override Drawing.Color ImageMarginGradientBegin => colors.Background;
        public override Drawing.Color ImageMarginGradientMiddle => colors.Background;
        public override Drawing.Color ImageMarginGradientEnd => colors.Background;
        public override Drawing.Color MenuBorder => colors.Border;
        public override Drawing.Color MenuItemBorder => colors.Hover;
        public override Drawing.Color MenuItemSelected => colors.Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => colors.Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => colors.Hover;
        public override Drawing.Color MenuItemPressedGradientBegin => colors.Hover;
        public override Drawing.Color MenuItemPressedGradientEnd => colors.Hover;
        public override Drawing.Color SeparatorDark => colors.Border;
        public override Drawing.Color SeparatorLight => colors.Border;
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            if (Application.GetResourceStream(new Uri("pack://application:,,,/NvChat;component/Resources/app.ico")) is { } info)
                return new Drawing.Icon(info.Stream);
        }
        catch { /* 아래 폴백 */ }
        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        LocalizationManager.Instance.LanguageChanged -= BuildMenu;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
