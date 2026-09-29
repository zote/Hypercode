using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Hypercode.Views;

public partial class ControlGallerySample : UserControl
{
    public ControlGallerySample() => AvaloniaXamlLoader.Load(this);

    /// <summary>Dá o foco à lista, para mostrar a seleção no estado ativo.</summary>
    public void FocusTable()
    {
        // O ListBox do Fluent não é focável: o foco vai para o item.
        if (this.FindControl<ListBox>("Table") is { } list)
            list.ContainerFromIndex(list.SelectedIndex)?.Focus();
    }

    /// <summary>Foca o botão de amostra como se fosse pelo Tab, para mostrar o anel de foco.</summary>
    public void FocusButtonByKeyboard() => this.FindControl<Button>("TipButton")?.Focus(NavigationMethod.Tab);

    /// <summary>Abre o tooltip de amostra sem precisar do mouse (para capturar a tela).</summary>
    public void OpenToolTip()
    {
        if (this.FindControl<Button>("TipButton") is { } button)
            ToolTip.SetIsOpen(button, true);
    }
}
