using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Hypercode.Views;

/// <summary>
/// Converte as strings do BadgeVisuals nos tipos do Avalonia. Fica na camada de view
/// justamente para o view-model não depender do Avalonia.
/// </summary>
public sealed class BadgeConverter : IValueConverter
{
    public static readonly BadgeConverter ToGeometry = new();

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Geometry> Geometries = new(StringComparer.Ordinal);

    private BadgeConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text.Length == 0) return null;

        lock (CacheLock)
        {
            if (!Geometries.TryGetValue(text, out var geometry))
            {
                geometry = Geometry.Parse(text);
                Geometries[text] = geometry;
            }

            return geometry;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Escolhe a cor clara ou escura do ícone conforme o tema efetivo do controle.
/// Entradas do MultiBinding: cor do tema claro, cor do tema escuro e o
/// ActualThemeVariant — que, por ser binding, troca a cor na hora em que o macOS
/// muda de tema.
/// </summary>
public sealed class BadgeBrushConverter : IMultiValueConverter
{
    public static readonly BadgeBrushConverter Instance = new();

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, IBrush> Brushes = new(StringComparer.Ordinal);

    private BadgeBrushConverter() { }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3 || values[0] is not string light || values[1] is not string dark) return null;

        var text = values[2] is ThemeVariant variant && variant == ThemeVariant.Dark ? dark : light;

        lock (CacheLock)
        {
            if (!Brushes.TryGetValue(text, out var brush))
            {
                brush = new SolidColorBrush(Color.Parse(text));
                Brushes[text] = brush;
            }

            return brush;
        }
    }
}
