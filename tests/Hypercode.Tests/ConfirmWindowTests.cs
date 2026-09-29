using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hypercode.Views;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// O diálogo de confirmação na janela de verdade, sem tela: o que o Return e o Esc fazem em
/// cada estilo (HIG — Alerts) e a ordem dos botões.
/// </summary>
public sealed class ConfirmWindowTests
{
    /// <summary>Abre o diálogo sobre uma janela dona, aperta a tecla e devolve o resultado.</summary>
    private static async Task<bool> PressAsync(ConfirmWindow dialog, PhysicalKey key)
    {
        var owner = new Window();
        owner.Show();
        var result = dialog.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();

        dialog.KeyPressQwerty(key, RawInputModifiers.None);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!result.IsCompleted)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"{key} não fechou o diálogo");
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        owner.Close();
        return await result;
    }

    private static ConfirmWindow Confirm(ConfirmStyle style = ConfirmStyle.Default)
        => new("Apagar o worktree", "Apagar o worktree x?", "detalhe", "Apagar", style);

    private static Button Named(Window window, string name) => window.FindControl<Button>(name)!;

    [AvaloniaFact]
    public async Task ReturnConfirmaNoEstiloPadrao()
        => Assert.True(await PressAsync(Confirm(), PhysicalKey.Enter));

    [AvaloniaFact]
    public async Task EscCancelaNoEstiloPadrao()
        => Assert.False(await PressAsync(Confirm(), PhysicalKey.Escape));

    [AvaloniaFact]
    public async Task ReturnCancelaQuandoCancelarEOPadrao()
        => Assert.False(await PressAsync(Confirm(ConfirmStyle.CancelIsDefault), PhysicalKey.Enter));

    [AvaloniaFact]
    public async Task AcaoDestrutivaNuncaEOPadrao()
    {
        var dialog = Confirm(ConfirmStyle.Destructive);

        Assert.Contains("destructive", Named(dialog, "ConfirmButton").Classes);
        Assert.False(Named(dialog, "ConfirmButton").IsDefault);
        Assert.False(await PressAsync(dialog, PhysicalKey.Enter));
    }

    [AvaloniaFact]
    public async Task EscCancelaAAcaoDestrutiva()
        => Assert.False(await PressAsync(Confirm(ConfirmStyle.Destructive), PhysicalKey.Escape));

    [AvaloniaFact]
    public void CancelarFicaAEsquerdaDoBotaoQueConfirma()
    {
        var buttons = Confirm().FindControl<DialogFrame>("Frame")!.Buttons;

        Assert.Equal(new[] { "CancelButton", "ConfirmButton" }, buttons.Select(b => b.Name));
    }

    [AvaloniaFact]
    public async Task AvisoTemUmBotaoSoQueReturnEEscAcionam()
    {
        var notice = ConfirmWindow.Notice("Relatório", "Tudo certo.", "detalhe");
        Assert.False(Named(notice, "CancelButton").IsVisible);

        Assert.True(await PressAsync(notice, PhysicalKey.Enter));
        Assert.True(await PressAsync(ConfirmWindow.Notice("Relatório", "Tudo certo.", "detalhe"), PhysicalKey.Escape));
    }

    [AvaloniaFact]
    public void DetalheVazioEscondeACaixa()
    {
        var dialog = ConfirmWindow.Notice("Atualizar", "Não dá para atualizar agora.", "");

        Assert.False(dialog.FindControl<Border>("BodyBox")!.IsVisible);
    }

    /// <summary>O espaço do conteúdo no template do DialogFrame, com a janela já desenhada.</summary>
    private static ContentPresenter ContentArea(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(p => p.Name == "PART_ContentPresenter" && p.TemplatedParent is DialogFrame);
    }

    [AvaloniaFact]
    public void DetalheVazioNaoDeixaAMargemDoConteudoAntesDosBotoes()
    {
        var dialog = ConfirmWindow.Notice("Atualizar", "Não dá para atualizar agora.", "");

        Assert.False(ContentArea(dialog).IsVisible);
        dialog.Close();
    }

    [AvaloniaFact]
    public void DetalheAparecePorBaixoDoTituloComAMargemDoConteudo()
    {
        var dialog = Confirm();
        var content = ContentArea(dialog);

        Assert.True(content.IsVisible);
        Assert.NotEqual(0, content.Margin.Top);
        dialog.Close();
    }

    [AvaloniaFact]
    public void SemTituloOConteudoComecaNaMargemDaJanela()
    {
        var dialog = new Window { Content = new DialogFrame { Content = new TextBlock { Text = "formulário" } } };
        var content = ContentArea(dialog);

        Assert.True(content.IsVisible);
        Assert.Equal(new Thickness(0), content.Margin);
        dialog.Close();
    }
}
