using System.Windows.Media;

namespace NvChat.ViewModels
{
    /// <summary>
    /// 전송 전 입력창에 대기 중인 첨부 이미지.
    /// </summary>
    public class AttachmentViewModel : ViewModel
    {
        public AttachmentViewModel(string dataUri, ImageSource preview)
        {
            DataUri = dataUri;
            Preview = preview;
        }

        public string DataUri { get; }

        public ImageSource Preview { get; }
    }
}
