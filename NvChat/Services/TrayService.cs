using System.Windows;
using NvChat.Localization;
using WinForms = System.Windows.Forms;

namespace NvChat.Services;

/// <summary>시스템 트레이 아이콘과 컨텍스트 메뉴. 언어가 바뀌면 메뉴 라벨을 다시 그린다.</summary>
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
            ContextMenuStrip = new WinForms.ContextMenuStrip(),
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();

        BuildMenu();
        LocalizationManager.Instance.LanguageChanged += BuildMenu;
    }

    public event Action? OpenRequested;
    public event Action? QuickChatRequested;
    public event Action? NewChatRequested;
    public event Action? ExitRequested;

    /// <summary>트레이 풍선 알림(창을 닫아 트레이로 숨었을 때 등).</summary>
    public void ShowBalloon(string title, string text)
    {
        try { _icon.ShowBalloonTip(5000, title, text, WinForms.ToolTipIcon.Info); }
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
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            if (Application.GetResourceStream(new Uri("pack://application:,,,/NvChat;component/Resources/app.ico")) is { } info)
                return new System.Drawing.Icon(info.Stream);
        }
        catch { /* 아래 폴백 */ }
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        LocalizationManager.Instance.LanguageChanged -= BuildMenu;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
