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

    // Pseudo-classes que o tema usa para não deixar respiro sobrando: ":untitled" (sem título
    // nem mensagem) tira a margem do conteúdo, que só separa o conteúdo do texto de cima;
    // ":no-content" (conteúdo nulo ou escondido, como a caixa de detalhe vazio) esconde o
    // espaço do conteúdo inteiro.
    public DialogFrame()
    {
        PseudoClasses.Set(":untitled", true);
        PseudoClasses.Set(":no-content", true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HeadlineProperty || change.Property == MessageProperty)
        {
            PseudoClasses.Set(":untitled", string.IsNullOrEmpty(Headline) && string.IsNullOrEmpty(Message));
        }
        else if (change.Property == ContentProperty)
        {
            if (change.OldValue is Visual oldContent) oldContent.PropertyChanged -= OnContentPropertyChanged;
            if (change.NewValue is Visual newContent) newContent.PropertyChanged += OnContentPropertyChanged;
            UpdateNoContent();
        }
    }

    private void OnContentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty) UpdateNoContent();
    }

    private void UpdateNoContent()
        => PseudoClasses.Set(":no-content", Content is null || Content is Visual { IsVisible: false });
}
