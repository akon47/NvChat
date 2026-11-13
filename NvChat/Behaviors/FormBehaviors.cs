using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NvChat.Services;

namespace NvChat.Behaviors;

/// <summary>
/// 입력 폼용 첨부 동작.
/// - <see cref="PasswordProperty"/> (PasswordBox): PasswordBox.Password 는 바인딩할 수 없으므로 양방향으로 이어 준다(API 키 가리기).
/// - <see cref="HotkeyCommandProperty"/> (TextBox): 칸에 포커스를 두고 키 조합을 누르면 "Ctrl+Shift+K" 형태의 문자열로 명령을 실행한다(전역 단축키 지정).
/// </summary>
public static class FormBehaviors
{
    // ===== PasswordBox 바인딩 =====

    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(FormBehaviors), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    public static string? GetPassword(DependencyObject o) => (string?)o.GetValue(PasswordProperty);

    public static void SetPassword(DependencyObject o, string? value) => o.SetValue(PasswordProperty, value);

    /// <summary>
    /// PasswordBox 의 입력을 <see cref="PasswordProperty"/> 로 올려 보내도록 연결한다.
    /// (바인딩의 초기 값이 null 이면 속성 변경 콜백이 오지 않으므로, 값과 무관하게 연결되도록 스위치를 따로 둔다.)
    /// </summary>
    public static readonly DependencyProperty BindPasswordProperty = DependencyProperty.RegisterAttached(
        "BindPassword", typeof(bool), typeof(FormBehaviors), new PropertyMetadata(false, OnBindPasswordChanged));

    public static bool GetBindPassword(DependencyObject o) => (bool)o.GetValue(BindPasswordProperty);

    public static void SetBindPassword(DependencyObject o, bool value) => o.SetValue(BindPasswordProperty, value);

    private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        box.PasswordChanged -= OnPasswordBoxChanged;
        if (e.NewValue is true) box.PasswordChanged += OnPasswordBoxChanged;
    }

    private static readonly DependencyProperty PasswordSyncingProperty = DependencyProperty.RegisterAttached(
        "PasswordSyncing", typeof(bool), typeof(FormBehaviors), new PropertyMetadata(false));

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;

        // 상자에서 올라온 변경이면 다시 써 넣지 않는다(캐럿이 맨 앞으로 튄다).
        if ((bool)box.GetValue(PasswordSyncingProperty)) return;

        string value = e.NewValue as string ?? "";
        if (box.Password != value) box.Password = value;
    }

    private static void OnPasswordBoxChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;

        // 아무것도 입력하지 않은 상태(null)를 빈 문자열로 바꾸지 않는다. null 은 "키를 건드리지 않음"이라는 뜻으로 저장소가 구분한다.
        if (box.Password.Length == 0 && GetPassword(box) is null) return;

        box.SetValue(PasswordSyncingProperty, true);
        box.SetCurrentValue(PasswordProperty, box.Password);
        box.SetValue(PasswordSyncingProperty, false);
    }

    // ===== 단축키 입력 =====

    public static readonly DependencyProperty HotkeyCommandProperty = DependencyProperty.RegisterAttached(
        "HotkeyCommand", typeof(ICommand), typeof(FormBehaviors), new PropertyMetadata(null, OnHotkeyCommandChanged));

    public static ICommand? GetHotkeyCommand(DependencyObject o) => (ICommand?)o.GetValue(HotkeyCommandProperty);

    public static void SetHotkeyCommand(DependencyObject o, ICommand? value) => o.SetValue(HotkeyCommandProperty, value);

    private static void OnHotkeyCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.PreviewKeyDown -= OnHotkeyKeyDown;
        if (e.NewValue is ICommand) box.PreviewKeyDown += OnHotkeyKeyDown;
    }

    private static void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        // Alt 조합은 Key 가 System 으로 오고 실제 키는 SystemKey 에 있다.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Tab/Esc 는 포커스 이동·창 닫기에 그대로 쓰이게 둔다(수식키 없이 누른 경우).
        if (Keyboard.Modifiers == ModifierKeys.None && key is Key.Tab or Key.Escape) return;

        e.Handled = true;
        if (HotKeyService.Format(Keyboard.Modifiers, key) is not { } hotkey) return;
        if (GetHotkeyCommand((DependencyObject)sender) is { } command && command.CanExecute(hotkey)) command.Execute(hotkey);
    }
}
