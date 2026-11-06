using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace NvChat.Views;

/// <summary>
/// 전역 단축키로 뜨는 작은 플로팅 채팅창. MainViewModel 을 공유한다. 닫기는 실제 종료가 아니라 숨김(런처 특성).
/// </summary>
public partial class QuickChatWindow : WindowView
{
    private bool _forceClose;

    public QuickChatWindow()
    {
        InitializeComponent();
        OpenMainCommand = new RelayCommand(() =>
        {
            Hide();
            OpenMainRequested?.Invoke(this, EventArgs.Empty);
        });
        HideCommand = new RelayCommand(Hide);
    }

    public event EventHandler? OpenMainRequested;

    /// <summary>"메인 열기" 버튼: 이 창을 숨기고 메인 창을 요청한다.</summary>
    public ICommand OpenMainCommand { get; }

    /// <summary>닫기(X)/Esc: 종료가 아니라 숨김.</summary>
    public ICommand HideCommand { get; }

    // MainViewModel 을 메인 창과 공유하므로 VM 닫기 프로토콜에서 제외한다.
    protected override bool UsesViewModelCloseProtocol => false;

    public void ShowAndActivate()
    {
        PositionNearTop();

        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        Activate();
        Topmost = true;
    }

    public void CloseForReal()
    {
        _forceClose = true;
        Close();
    }

    private void PositionNearTop()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.14;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_forceClose) return;
        e.Cancel = true;
        Hide();
    }

    // Esc = 숨김. 창 수준 동작이라 InputBinding 대신 재정의로 둔다(런처 창은 DataContext 를 메인과 공유해 VM 명령으로 두면 메인 창까지 영향).
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
