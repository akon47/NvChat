using CommunityToolkit.Mvvm.ComponentModel;

namespace NvChat.ViewModels;

/// <summary>
/// 창(Window)과 결합되는 뷰모델의 기반 클래스. 닫기 요청 프로토콜(<see cref="RequestClose"/> → <see cref="CloseRequested"/>)을 제공한다.
/// WindowView 는 DataContext 가 이 타입이면 X 버튼/Alt+F4 를 RequestClose 로 돌리고, CloseRequested 가 오면 실제로 닫는다.
/// </summary>
public abstract class WindowViewModel : ObservableObject
{
    /// <summary>닫기 요청이 확정되었을 때 UI 스레드에서 발생하는 이벤트.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>닫기를 요청한다.</summary>
    public void RequestClose() => OnRequestClose();

    /// <summary>닫기를 요청한다. 파생 클래스에서 재정의하여 닫기 전 처리를 넣을 수 있다.</summary>
    protected virtual void OnRequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
