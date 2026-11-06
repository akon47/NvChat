using System.Windows.Media;

namespace NvChat.ViewModels;

/// <summary>전송 전 입력창에 대기 중인 첨부 이미지.</summary>
public sealed class AttachmentViewModel(string dataUri, ImageSource? preview)
{
    public string DataUri => dataUri;

    public ImageSource? Preview => preview;
}
