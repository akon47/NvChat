using System.Windows;

namespace NvChat.Views
{
    /// <summary>
    /// 간단한 확인 대화상자(테마 적용). 정적 <see cref="Show"/> 로 사용한다.
    /// </summary>
    public partial class ConfirmDialog : WindowView
    {
        #region Constructors

        private ConfirmDialog()
        {
            InitializeComponent();
        }

        #endregion


        #region Properties

        public bool Result { get; private set; }

        #endregion


        #region Helpers

        public static bool Show(Window owner, string title, string message)
        {
            var dialog = new ConfirmDialog
            {
                Owner = owner,
                Title = title
            };
            dialog.MessageText.Text = message;

            dialog.ShowDialog();
            return dialog.Result;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        #endregion
    }
}
