using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NvChat.Behaviors;

/// <summary>
/// 메뉴 첨부 동작. <see cref="IsDropDownProperty"/> 를 켠 Button 은 누르면 자기 ContextMenu 를 <see cref="DropDownPlacementProperty"/> 방향으로 연다.
/// </summary>
public static class MenuBehaviors
{
    public static readonly DependencyProperty IsDropDownProperty = DependencyProperty.RegisterAttached(
        "IsDropDown", typeof(bool), typeof(MenuBehaviors), new PropertyMetadata(false, OnIsDropDownChanged));

    public static bool GetIsDropDown(DependencyObject o) => (bool)o.GetValue(IsDropDownProperty);

    public static void SetIsDropDown(DependencyObject o, bool value) => o.SetValue(IsDropDownProperty, value);

    public static readonly DependencyProperty DropDownPlacementProperty = DependencyProperty.RegisterAttached(
        "DropDownPlacement", typeof(PlacementMode), typeof(MenuBehaviors), new PropertyMetadata(PlacementMode.Bottom));

    public static PlacementMode GetDropDownPlacement(DependencyObject o) => (PlacementMode)o.GetValue(DropDownPlacementProperty);

    public static void SetDropDownPlacement(DependencyObject o, PlacementMode value) => o.SetValue(DropDownPlacementProperty, value);

    private static void OnIsDropDownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;
        button.Click -= OnDropDownClick;
        if (e.NewValue is true) button.Click += OnDropDownClick;
    }

    private static void OnDropDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = GetDropDownPlacement(button);
        menu.IsOpen = true;
    }
}
