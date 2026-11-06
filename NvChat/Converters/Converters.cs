using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NvChat.Converters;

/// <summary>bool true → Collapsed, false → Visible. (정방향은 WPF 내장 BooleanToVisibilityConverter 를 쓴다)</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}
