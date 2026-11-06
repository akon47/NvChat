using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NvChat.Behaviors;

/// <summary>
/// 인라인 편집용 TextBox: Enter 또는 포커스 잃음 = <see cref="CommitCommandProperty"/>, Esc = <see cref="CancelCommandProperty"/>.
/// 두 명령 모두 <see cref="CommandParameterProperty"/> 를 인자로 받는다(보통 편집 중인 항목).
/// </summary>
public static class TextBoxCommitBehavior
{
    public static readonly DependencyProperty CommitCommandProperty = DependencyProperty.RegisterAttached(
        "CommitCommand", typeof(ICommand), typeof(TextBoxCommitBehavior), new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommitCommand(DependencyObject o) => (ICommand?)o.GetValue(CommitCommandProperty);

    public static void SetCommitCommand(DependencyObject o, ICommand? value) => o.SetValue(CommitCommandProperty, value);

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.RegisterAttached(
        "CancelCommand", typeof(ICommand), typeof(TextBoxCommitBehavior), new PropertyMetadata(null));

    public static ICommand? GetCancelCommand(DependencyObject o) => (ICommand?)o.GetValue(CancelCommandProperty);

    public static void SetCancelCommand(DependencyObject o, ICommand? value) => o.SetValue(CancelCommandProperty, value);

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
        "CommandParameter", typeof(object), typeof(TextBoxCommitBehavior), new PropertyMetadata(null));

    public static object? GetCommandParameter(DependencyObject o) => o.GetValue(CommandParameterProperty);

    public static void SetCommandParameter(DependencyObject o, object? value) => o.SetValue(CommandParameterProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.KeyDown -= OnKeyDown;
        box.LostFocus -= OnLostFocus;
        if (e.NewValue is ICommand)
        {
            box.KeyDown += OnKeyDown;
            box.LostFocus += OnLostFocus;
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        switch (e.Key)
        {
            case Key.Enter or Key.Return:
                e.Handled = true;
                Run(GetCommitCommand(box), GetCommandParameter(box));
                break;
            case Key.Escape:
                e.Handled = true;
                Run(GetCancelCommand(box), GetCommandParameter(box));
                break;
        }
    }

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        Run(GetCommitCommand(box), GetCommandParameter(box));
    }

    private static void Run(ICommand? command, object? parameter)
    {
        if (command is not null && command.CanExecute(parameter)) command.Execute(parameter);
    }
}
