using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NvChat.Behaviors;

/// <summary>
/// 입력창 첨부 동작 — 코드비하인드 없이 키/클립보드/드롭을 VM 명령으로 잇는다.
/// - <see cref="SendCommandProperty"/> + <see cref="SendOnEnterProperty"/>: SendOnEnter 면 Enter=전송·Shift+Enter=줄바꿈, 아니면 Ctrl+Enter=전송·Enter=줄바꿈.
/// - <see cref="PasteImageCommandProperty"/>: Ctrl+V 로 클립보드에 이미지(텍스트 없이)가 있으면 BitmapSource 를 명령에 넘긴다.
/// - <see cref="DropFilesCommandProperty"/>: 파일을 끌어다 놓으면 string[] 경로를 명령에 넘긴다(아무 UIElement 에 붙일 수 있다).
/// </summary>
public static class InputBehaviors
{
    public static readonly DependencyProperty SendCommandProperty = DependencyProperty.RegisterAttached(
        "SendCommand", typeof(ICommand), typeof(InputBehaviors), new PropertyMetadata(null, OnSendCommandChanged));

    public static ICommand? GetSendCommand(DependencyObject o) => (ICommand?)o.GetValue(SendCommandProperty);

    public static void SetSendCommand(DependencyObject o, ICommand? value) => o.SetValue(SendCommandProperty, value);

    public static readonly DependencyProperty SendOnEnterProperty = DependencyProperty.RegisterAttached(
        "SendOnEnter", typeof(bool), typeof(InputBehaviors), new PropertyMetadata(true));

    public static bool GetSendOnEnter(DependencyObject o) => (bool)o.GetValue(SendOnEnterProperty);

    public static void SetSendOnEnter(DependencyObject o, bool value) => o.SetValue(SendOnEnterProperty, value);

    public static readonly DependencyProperty PasteImageCommandProperty = DependencyProperty.RegisterAttached(
        "PasteImageCommand", typeof(ICommand), typeof(InputBehaviors), new PropertyMetadata(null, OnPasteImageCommandChanged));

    public static ICommand? GetPasteImageCommand(DependencyObject o) => (ICommand?)o.GetValue(PasteImageCommandProperty);

    public static void SetPasteImageCommand(DependencyObject o, ICommand? value) => o.SetValue(PasteImageCommandProperty, value);

    public static readonly DependencyProperty DropFilesCommandProperty = DependencyProperty.RegisterAttached(
        "DropFilesCommand", typeof(ICommand), typeof(InputBehaviors), new PropertyMetadata(null, OnDropFilesCommandChanged));

    public static ICommand? GetDropFilesCommand(DependencyObject o) => (ICommand?)o.GetValue(DropFilesCommandProperty);

    public static void SetDropFilesCommand(DependencyObject o, ICommand? value) => o.SetValue(DropFilesCommandProperty, value);

    /// <summary>파일을 끌어 올려놓은 동안 true. 놓을 수 있는 자리임을 알리는 오버레이를 띄우는 데 쓴다(읽기 전용처럼 쓴다).</summary>
    public static readonly DependencyProperty IsFileDragOverProperty = DependencyProperty.RegisterAttached(
        "IsFileDragOver", typeof(bool), typeof(InputBehaviors), new PropertyMetadata(false));

    public static bool GetIsFileDragOver(DependencyObject o) => (bool)o.GetValue(IsFileDragOverProperty);

    public static void SetIsFileDragOver(DependencyObject o, bool value) => o.SetValue(IsFileDragOverProperty, value);

    private static void OnSendCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.PreviewKeyDown -= OnSendKeyDown;
        if (e.NewValue is ICommand) box.PreviewKeyDown += OnSendKeyDown;
    }

    private static void OnSendKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || sender is not TextBox box) return;

        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shouldSend = GetSendOnEnter(box) ? !shift && !ctrl : ctrl;
        if (!shouldSend) return;

        e.Handled = true;
        if (GetSendCommand(box) is { } command && command.CanExecute(null)) command.Execute(null);
    }

    private static void OnPasteImageCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.PreviewKeyDown -= OnPasteKeyDown;
        if (e.NewValue is ICommand) box.PreviewKeyDown += OnPasteKeyDown;
    }

    private static void OnPasteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || sender is not TextBox box) return;
        if (!Clipboard.ContainsImage() || Clipboard.ContainsText()) return; // 텍스트 붙여넣기는 평소대로

        if (Clipboard.GetImage() is not BitmapSource image) return;
        if (GetPasteImageCommand(box) is { } command && command.CanExecute(image))
        {
            e.Handled = true;
            command.Execute(image);
        }
    }

    private static void OnDropFilesCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewDragOver -= OnDragOver;
        element.PreviewDrop -= OnDrop;
        element.PreviewDragLeave -= OnDragLeave;
        if (e.NewValue is ICommand)
        {
            element.AllowDrop = true;
            element.PreviewDragOver += OnDragOver;
            element.PreviewDrop += OnDrop;
            element.PreviewDragLeave += OnDragLeave;
        }
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        // 자식 요소 사이를 오갈 때도 DragLeave 가 올라오므로, 포인터가 실제로 영역 밖으로 나갔을 때만 끈다.
        if (sender is not FrameworkElement element) return;
        Point position = e.GetPosition(element);
        bool inside = position.X >= 0 && position.Y >= 0 && position.X < element.ActualWidth && position.Y < element.ActualHeight;
        if (!inside) element.SetCurrentValue(IsFileDragOverProperty, false);
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return; // 텍스트 드래그 등은 기본 동작에 맡긴다
        ((DependencyObject)sender).SetCurrentValue(IsFileDragOverProperty, true);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        ((DependencyObject)sender).SetCurrentValue(IsFileDragOverProperty, false);
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        if (GetDropFilesCommand((DependencyObject)sender) is { } command && command.CanExecute(files))
        {
            e.Handled = true;
            command.Execute(files);
        }
    }
}
