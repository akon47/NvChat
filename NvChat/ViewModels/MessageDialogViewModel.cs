using CommunityToolkit.Mvvm.Input;
using NvChat.Localization;

namespace NvChat.ViewModels;

/// <summary>확인/정보/오류 대화상자(MessageDialog)의 뷰모델. 확인 버튼을 누르면 <see cref="Result"/> 가 true 로 닫힌다.</summary>
public sealed partial class MessageDialogViewModel : WindowViewModel
{
    public required string Title { get; init; }

    public required string Message { get; init; }

    public string ConfirmText { get; init; } = LocalizationManager.Instance["Confirm"];

    public string CancelText { get; init; } = LocalizationManager.Instance["Cancel"];

    /// <summary>취소 버튼을 보여 주는 확인 질문인지(false 면 확인 버튼만 있는 안내).</summary>
    public bool IsConfirmation { get; init; }

    /// <summary>확인이 되돌릴 수 없는 동작(삭제 등)이면 확인 버튼을 경고색으로 그린다.</summary>
    public bool IsDestructive { get; init; }

    /// <summary>오류 안내면 경고 아이콘을 함께 보인다.</summary>
    public bool IsError { get; init; }

    public bool Result { get; private set; }

    [RelayCommand]
    private void Confirm()
    {
        Result = true;
        RequestClose();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = false;
        RequestClose();
    }
}
