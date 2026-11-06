using System.Windows;
using NvChat.Localization;
using NvChat.ViewModels;

namespace NvChat.Views;

/// <summary>테마가 적용된 확인/정보/오류 대화상자. 정적 헬퍼로 띄운다.</summary>
public partial class MessageDialog : WindowView
{
    public MessageDialog() => InitializeComponent();

    /// <summary>확인 질문. 확인 버튼 문구를 동작에 맞게 넘긴다(삭제/비우기 등). 확인하면 true.</summary>
    public static bool Confirm(Window? owner, string title, string message, string confirmText, bool destructive = true) =>
        Show(owner, new MessageDialogViewModel { Title = title, Message = message, ConfirmText = confirmText, IsConfirmation = true, IsDestructive = destructive });

    public static void ShowError(Window? owner, string title, string message) =>
        Show(owner, new MessageDialogViewModel { Title = title, Message = message, ConfirmText = LocalizationManager.Instance["Confirm"], IsError = true });

    private static bool Show(Window? owner, MessageDialogViewModel viewModel)
    {
        MessageDialog dialog = new() { DataContext = viewModel };
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return viewModel.Result;
    }
}
