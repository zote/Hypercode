using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Reactive;

namespace Hypercode.Views;

/// <summary>
/// Deriva Brush.Accent.Pressed de Brush.Selection.Active (o fundo do botão de destaque solto),
/// em vez de usar o SystemAccentColorDark2 do Fluent (#135). O Fluent escurece baixando a
/// luminosidade em HSL, e numa cor com saturação 100% como a accent vermelha do macOS isso
/// quase não escurece: Dark1 #FF1920, Dark2 #EF0007, que na tela saem iguais. Escurecer em HSV
/// (multiplicar os canais) mantém matiz e saturação e sempre escurece.
/// </summary>
public static class AccentPressed
{
    // A razão entre o Dark2 e o Dark1 do Fluent nas accents em que ele escurece direito
    // (azul, roxo, verde, amarelo): o pressionado fica como era nelas.
    private const double Fator = 0.79;

    public static Color Escurecer(Color solto) => Color.FromArgb(
        solto.A,
        (byte)Math.Round(solto.R * Fator),
        (byte)Math.Round(solto.G * Fator),
        (byte)Math.Round(solto.B * Fator));

    /// <summary>
    /// Acompanha a cor do solto, que segue a accent do sistema ao vivo (#121), e muda a cor do
    /// pressionado no próprio brush: os aliases de FluentOverrides.axaml apontam para ele.
    /// </summary>
    public static void Acompanhar(IResourceHost recursos)
    {
        if (!recursos.TryGetResource("Brush.Selection.Active", null, out var s) || s is not SolidColorBrush solto ||
            !recursos.TryGetResource("Brush.Accent.Pressed", null, out var p) || p is not SolidColorBrush pressionado)
            return;

        solto.GetObservable(SolidColorBrush.ColorProperty)
            .Subscribe(new AnonymousObserver<Color>(cor => pressionado.Color = Escurecer(cor)));
    }
}
