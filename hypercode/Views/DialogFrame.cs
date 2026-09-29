using Avalonia;
using Avalonia.Controls;

namespace Hypercode.Views;

/// <summary>
/// Layout padrão de diálogo e sheet do macOS (HIG — Alerts, Sheets): título em negrito,
/// mensagem, conteúdo opcional e a barra de botões alinhada à direita. Quem usa só põe os
/// textos, o conteúdo e os botões, na ordem da HIG: ações alternativas, Cancelar e, por
/// último, o botão padrão. Tema em Styles/Dialog.axaml; uso em Styles/README.md.
/// </summary>
public class DialogFrame : ContentControl
{
    public static readonly StyledProperty<string?> HeadlineProperty =
        AvaloniaProperty.Register<DialogFrame, string?>(nameof(Headline));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<DialogFrame, string?>(nameof(Message));

    /// <summary>
    /// A pergunta ou a afirmação principal, curta. Vai em negrito. Opcional: um formulário
    /// que já diz o que faz pelo título da janela pode ir sem.
    /// </summary>
    public string? Headline
    {
        get => GetValue(HeadlineProperty);
        set => SetValue(HeadlineProperty, value);
    }

    /// <summary>Texto de apoio abaixo do título: consequência, contexto. Opcional.</summary>
    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>
    /// Os botões, da esquerda para a direita — o padrão por último. Em XAML vão em
    /// &lt;DialogFrame.Buttons&gt;; o Content fica para o corpo do diálogo.
    /// </summary>
    public Avalonia.Controls.Controls Buttons { get; } = new();
}
