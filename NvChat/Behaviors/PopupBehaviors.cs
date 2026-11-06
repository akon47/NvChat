using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NvChat.Behaviors;

/// <summary>
/// 팝업/선택기 첨부 동작.
/// - <see cref="FocusOnOpenProperty"/> (Popup): 팝업이 열리면 지정 요소에 키보드 포커스를 주고, Esc 로 닫는다. WPF Popup 은 자체 HWND 라서
///   네이티브 포커스를 받지 못하면 한글 IME 조합이 깨지므로 팝업 HWND 에 직접 포커스를 준 뒤 요소에 포커스를 준다.
/// - <see cref="ClickCommandProperty"/> (Selector): 항목을 마우스로 눌렀다 떼면 명령 실행(이미 선택된 항목을 다시 클릭해도 — 선택 변경 이벤트 없이 — 팝업을 닫을 수 있게).
/// - <see cref="EnterCommandProperty"/> (TextBox): Enter 를 누르면 명령 실행(검색 상자에서 Enter 로 첫 결과 선택).
/// </summary>
public static class PopupBehaviors
{
    public static readonly DependencyProperty FocusOnOpenProperty = DependencyProperty.RegisterAttached(
        "FocusOnOpen", typeof(UIElement), typeof(PopupBehaviors), new PropertyMetadata(null, OnFocusOnOpenChanged));

    public static UIElement? GetFocusOnOpen(DependencyObject o) => (UIElement?)o.GetValue(FocusOnOpenProperty);

    public static void SetFocusOnOpen(DependencyObject o, UIElement? value) => o.SetValue(FocusOnOpenProperty, value);

    public static readonly DependencyProperty ClickCommandProperty = DependencyProperty.RegisterAttached(
        "ClickCommand", typeof(ICommand), typeof(PopupBehaviors), new PropertyMetadata(null, OnClickCommandChanged));

    public static ICommand? GetClickCommand(DependencyObject o) => (ICommand?)o.GetValue(ClickCommandProperty);

    public static void SetClickCommand(DependencyObject o, ICommand? value) => o.SetValue(ClickCommandProperty, value);

    public static readonly DependencyProperty EnterCommandProperty = DependencyProperty.RegisterAttached(
        "EnterCommand", typeof(ICommand), typeof(PopupBehaviors), new PropertyMetadata(null, OnEnterCommandChanged));

    public static ICommand? GetEnterCommand(DependencyObject o) => (ICommand?)o.GetValue(EnterCommandProperty);

    public static void SetEnterCommand(DependencyObject o, ICommand? value) => o.SetValue(EnterCommandProperty, value);

    private static void OnFocusOnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Popup popup) return;
        popup.Opened -= OnPopupOpened;
        popup.PreviewKeyDown -= OnPopupKeyDown;
        if (e.NewValue is UIElement)
        {
            popup.Opened += OnPopupOpened;
            popup.PreviewKeyDown += OnPopupKeyDown;
        }
    }

    private static void OnPopupOpened(object? sender, EventArgs e)
    {
        if (sender is not Popup popup || GetFocusOnOpen(popup) is not { } target) return;
        popup.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (PresentationSource.FromVisual(target) is HwndSource { Handle: var handle } && handle != 0) SetFocus(handle);
            target.Focus();
            Keyboard.Focus(target);
        });
    }

    private static void OnPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not Popup popup) return;
        popup.IsOpen = false;
        e.Handled = true;
    }

    private static void OnClickCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Selector selector) return;
        selector.PreviewMouseLeftButtonUp -= OnSelectorMouseUp;
        if (e.NewValue is ICommand) selector.PreviewMouseLeftButtonUp += OnSelectorMouseUp;
    }

    private static void OnSelectorMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (GetClickCommand((DependencyObject)sender) is { } command && command.CanExecute(null)) command.Execute(null);
    }

    private static void OnEnterCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.PreviewKeyDown -= OnEnterKeyDown;
        if (e.NewValue is ICommand) box.PreviewKeyDown += OnEnterKeyDown;
    }

    private static void OnEnterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        if (GetEnterCommand((DependencyObject)sender) is { } command && command.CanExecute(null))
        {
            e.Handled = true;
            command.Execute(null);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hWnd);
}
