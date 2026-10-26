using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Pakban.Localization;

/// <summary>
/// XAML helper: Text="{l:Trans nav_dashboard}" — creates a live binding into the
/// language dictionary so the text follows the selected language automatically.
/// </summary>
public class TransExtension : MarkupExtension
{
    public string Key { get; set; }

    public TransExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding("[" + Key + "]") { Source = Loc.I, Mode = BindingMode.OneWay };
}

/// <summary>Translates a key coming from the DataContext (e.g. a category name key).</summary>
public class LocKeyConverter : IValueConverter
{
    public static readonly LocKeyConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is string key ? Loc.I[key] : "";

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>true → RightToLeft, false → LeftToRight.</summary>
public class BoolToFlowDirectionConverter : IValueConverter
{
    public static readonly BoolToFlowDirectionConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is true ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Standard visibility toggle: true → Visible, false → Collapsed.
/// Pass ConverterParameter="invert" to flip (true → Collapsed).
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool flag = value is true;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverts a bool (for IsEnabled bindings).</summary>
public class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>Shows an element only while the bound enum equals the parameter's name.</summary>
public class EnumToVisibilityConverter : IValueConverter
{
    public static readonly EnumToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is null || parameter is null) return Visibility.Collapsed;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
