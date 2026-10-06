using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Hypercode.ViewModels;

namespace Hypercode.Views;

/// <summary>
/// As arestas do grafo de issues (#144), desenhadas por baixo dos cartões. Não há biblioteca de
/// grafo no Avalonia: cada aresta é uma curva de quem bloqueia (saída à direita) para quem é
/// bloqueado (entrada à esquerda), com a ponta de seta na entrada — a direção se lê sem legenda.
/// A que pula colunas atravessa cada coluna do meio em linha reta, pelo vão que o layout
/// reservou, e nunca por trás de um cartão. A aresta dentro de um ciclo liga dois cartões da mesma coluna e contorna pela direita, na cor
/// de aviso; a de um bloqueador já fechado sai tracejada.
/// </summary>
public sealed class IssueGraphEdges : Control
{
    public static readonly StyledProperty<IReadOnlyList<GraphEdgeItem>?> EdgesProperty =
        AvaloniaProperty.Register<IssueGraphEdges, IReadOnlyList<GraphEdgeItem>?>(nameof(Edges));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<IssueGraphEdges, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> CycleStrokeProperty =
        AvaloniaProperty.Register<IssueGraphEdges, IBrush?>(nameof(CycleStroke));

    /// <summary>Quanto a curva do ciclo sai para a direita da coluna antes de voltar.</summary>
    private const double CycleBulge = 36;

    private const double ArrowLength = 7;
    private const double ArrowHalfWidth = 4;

    static IssueGraphEdges()
    {
        AffectsRender<IssueGraphEdges>(EdgesProperty, StrokeProperty, CycleStrokeProperty);
    }

    public IReadOnlyList<GraphEdgeItem>? Edges
    {
        get => GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? CycleStroke
    {
        get => GetValue(CycleStrokeProperty);
        set => SetValue(CycleStrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Edges is not { Count: > 0 } edges) return;

        var normal = new Pen(Stroke ?? Brushes.Gray, 1.25);
        var resolved = new Pen(Stroke ?? Brushes.Gray, 1, new DashStyle(new double[] { 3, 3 }, 0));
        var cycle = new Pen(CycleStroke ?? Brushes.Orange, 1.5);

        foreach (var edge in edges)
        {
            var pen = edge.IsCycle ? cycle : edge.IsResolved ? resolved : normal;
            var start = new Point(edge.X1, edge.Y1);
            var end = new Point(edge.X2, edge.Y2);

            // Ciclo: as duas pontas na direita da mesma coluna; a curva sai e volta por fora,
            // e a seta entra apontando para a esquerda.
            var direction = edge.IsCycle ? new Vector(-1, 0) : new Vector(1, 0);

            // A curva termina na base da seta, para a ponta não ficar coberta pelo traço.
            var tip = end;
            var baseCenter = tip - direction * ArrowLength;

            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(start, false);

                if (edge.IsCycle)
                {
                    stream.CubicBezierTo(
                        new Point(start.X + CycleBulge, start.Y), new Point(end.X + CycleBulge, end.Y), baseCenter);
                }
                else
                {
                    // Uma curva em S até cada vão, a reta por dentro dele, e a última curva até a seta.
                    var current = start;
                    foreach (var waypoint in edge.Via)
                    {
                        var entry = new Point(waypoint.Left, waypoint.Y);
                        Curve(stream, current, entry);
                        current = new Point(waypoint.Right, waypoint.Y);
                        stream.LineTo(current);
                    }

                    Curve(stream, current, baseCenter);
                }

                stream.EndFigure(false);
            }
            context.DrawGeometry(null, pen, geometry);

            var normalVector = new Vector(-direction.Y, direction.X) * ArrowHalfWidth;
            var arrow = new StreamGeometry();
            using (var stream = arrow.Open())
            {
                stream.BeginFigure(tip, true);
                stream.LineTo(baseCenter + normalVector);
                stream.LineTo(baseCenter - normalVector);
                stream.EndFigure(true);
            }
            context.DrawGeometry(pen.Brush, null, arrow);
        }
    }

    /// <summary>Curva em S horizontal: sai e chega na horizontal, com a virada no meio do caminho.</summary>
    private static void Curve(StreamGeometryContext stream, Point from, Point to)
    {
        var middle = (from.X + to.X) / 2;
        stream.CubicBezierTo(new Point(middle, from.Y), new Point(middle, to.Y), to);
    }
}

/// <summary>
/// "#rrggbb" (a cor do tipo ou da label, que vem do GitHub) para pincel. Fica na view para o
/// view-model não depender do Avalonia. Cor inválida ou ausente: nenhum pincel.
/// </summary>
public sealed class HexBrushConverter : IValueConverter
{
    public static readonly HexBrushConverter Instance = new();

    private HexBrushConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text && Color.TryParse(text, out var color) ? new SolidColorBrush(color) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
