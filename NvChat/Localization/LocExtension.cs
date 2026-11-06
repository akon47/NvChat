using System.Windows.Data;
using System.Windows.Markup;

namespace NvChat.Localization;

/// <summary>XAML 에서 {loc:Loc Key} 형태로 번역 문자열을 OneWay 바인딩한다. 언어 변경 시 실시간으로 갱신된다.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
