using NvChat.Localization;
using System;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace NvChat.Services
{
    /// <summary>
    /// 시스템 트레이 아이콘과 컨텍스트 메뉴.
    /// </summary>
    public sealed class TrayService : IDisposable
    {
        #region Fields

        private readonly WinForms.NotifyIcon _icon;

        #endregion


        #region Constructors

        public TrayService()
        {
            _icon = new WinForms.NotifyIcon
            {
                Text = "NvChat",
                Visible = true,
                Icon = LoadIcon()
            };

            _icon.DoubleClick += (_, __) => OpenRequested?.Invoke();

            _icon.ContextMenuStrip = new WinForms.ContextMenuStrip();
            BuildMenu();

            // 언어가 바뀌면 코드로 만든 메뉴 라벨을 다시 그린다.
            LocalizationManager.Instance.LanguageChanged += BuildMenu;
        }

        /// <summary>현재 언어로 트레이 메뉴를 다시 구성한다.</summary>
        private void BuildMenu()
        {
            var L = LocalizationManager.Instance;
            var menu = _icon.ContextMenuStrip;
            menu.Items.Clear();

            menu.Items.Add(L["TrayOpen"], null, (_, __) => OpenRequested?.Invoke());
            menu.Items.Add(L["TrayQuickChat"], null, (_, __) => QuickChatRequested?.Invoke());
            menu.Items.Add(L["TrayNewChat"], null, (_, __) => NewChatRequested?.Invoke());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(L["TrayExit"], null, (_, __) => ExitRequested?.Invoke());
        }

        #endregion


        #region Events

        public event Action OpenRequested;
        public event Action QuickChatRequested;
        public event Action NewChatRequested;
        public event Action ExitRequested;

        #endregion


        #region Helpers

        private static System.Drawing.Icon LoadIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/NvChat;component/Resources/app.ico");
                var info = Application.GetResourceStream(uri);
                if (info != null)
                    return new System.Drawing.Icon(info.Stream);
            }
            catch
            {
            }

            return System.Drawing.SystemIcons.Application;
        }

        public void Dispose()
        {
            LocalizationManager.Instance.LanguageChanged -= BuildMenu;
            _icon.Visible = false;
            _icon.Dispose();
        }

        #endregion
    }
}
