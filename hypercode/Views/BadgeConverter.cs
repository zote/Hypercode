using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Hypercode.Views;

/// <summary>
/// Converte a chave de ícone do BadgeVisuals (StatusBadge.IconKey) na geometria de
/// Styles/Icons.axaml. Fica na camada de view justamente para o view-model não depender
/// do Avalonia.
/// </summary>
public sealed class BadgeConverter : IValueConverter
{
    public static readonly BadgeConverter ToGeometry = new();

    private BadgeConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key && Application.Current?.TryGetResource(key, null, out var icon) == true
            ? icon as Geometry
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Busca o token de cor da etiqueta (StatusBadge.BrushKey, um Brush.Badge.*) na variante de
/// tema efetiva do controle. Entradas do MultiBinding: a chave do token e o
/// ActualThemeVariant — que, por ser binding, troca a cor na hora em que o macOS muda de
/// tema. Um DynamicResource não serve porque a chave vem do binding.
/// </summary>
public sealed class BadgeBrushConverter : IMultiValueConverter
{
    public static readonly BadgeBrushConverter Instance = new();

    private BadgeBrushConverter() { }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => values.Count >= 2 && values[0] is string key && values[1] is ThemeVariant variant
           && Application.Current?.TryGetResource(key, variant, out var brush) == true
            ? brush as IBrush
            : null;
}
