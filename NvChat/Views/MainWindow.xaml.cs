namespace NvChat.Views;

/// <summary>
/// 메인 창. 상호작용은 전부 뷰모델 명령 + 첨부 동작(Behaviors)으로 처리하고, 닫기 정책(트레이로 숨김/종료)은 App 이 Closing 에서 결정한다.
/// </summary>
public partial class MainWindow : WindowView
{
    public MainWindow() => InitializeComponent();

    // 메인/빠른채팅 창은 MainViewModel 을 공유하므로 VM 닫기 프로토콜을 쓰지 않는다. (쓰면 한쪽을 닫을 때 다른 쪽도 닫힌다.)
    protected override bool UsesViewModelCloseProtocol => false;
}
