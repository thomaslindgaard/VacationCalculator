using System.Windows.Data;
using System.Windows.Markup;

namespace VacationCalculator.App.Services;

/// <summary>
/// XAML text from the string tables: <c>Text="{svc:Str Sidebar.Year}"</c>. It binds to the <see cref="Loc"/> indexer, so the
/// text changes the moment the language does.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class StrExtension : MarkupExtension
{
    public StrExtension()
    {
    }

    public StrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding { Source = Loc.Instance, Path = new System.Windows.PropertyPath("[" + Key + "]"), Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
