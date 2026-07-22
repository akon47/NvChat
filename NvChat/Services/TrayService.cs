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

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("열기", null, (_, __) => OpenRequested?.Invoke());
            menu.Items.Add("빠른 채팅", null, (_, __) => QuickChatRequested?.Invoke());
            menu.Items.Add("새 대화", null, (_, __) => NewChatRequested?.Invoke());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("종료", null, (_, __) => ExitRequested?.Invoke());
            _icon.ContextMenuStrip = menu;
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
            _icon.Visible = false;
            _icon.Dispose();
        }

        #endregion
    }
}
