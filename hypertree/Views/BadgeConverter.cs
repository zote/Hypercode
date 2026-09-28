using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Hypertree.Views;

/// <summary>
/// Converte as strings do BadgeVisuals nos tipos do Avalonia. Fica na camada de view
/// justamente para o view-model não depender do Avalonia.
/// </summary>
public sealed class BadgeConverter : IValueConverter
{
    public static readonly BadgeConverter ToGeometry = new(producesGeometry: true);
    public static readonly BadgeConverter ToBrush = new(producesGeometry: false);

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Geometry> Geometries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IBrush> Brushes = new(StringComparer.Ordinal);

    private readonly bool _producesGeometry;

    private BadgeConverter(bool producesGeometry) => _producesGeometry = producesGeometry;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text.Length == 0) return null;

        lock (CacheLock)
        {
            if (_producesGeometry)
            {
                if (!Geometries.TryGetValue(text, out var geometry))
                {
                    geometry = Geometry.Parse(text);
                    Geometries[text] = geometry;
                }

                return geometry;
            }

            if (!Brushes.TryGetValue(text, out var brush))
            {
                brush = new SolidColorBrush(Color.Parse(text));
                Brushes[text] = brush;
            }

            return brush;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
