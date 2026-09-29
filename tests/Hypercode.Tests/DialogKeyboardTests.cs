using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Hypercode.Services;
using Hypercode.ViewModels;
using Hypercode.Views;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// Return e Esc nos diálogos, na janela de verdade e sem tela (HIG — Alerts, Sheets): Return
/// aciona o botão padrão, Esc cancela; os painéis sem botão (Sobre, Ajustes) fecham no Esc.
/// </summary>
public sealed class DialogKeyboardTests
{
    /// <summary>Abre o diálogo sobre uma janela dona, aperta a tecla e devolve o resultado.</summary>
    private static async Task<T?> PressAsync<T>(Window dialog, PhysicalKey key)
    {
        var owner = new Window();
        owner.Show();
        var result = dialog.ShowDialog<T?>(owner);
        Dispatcher.UIThread.RunJobs();

        dialog.KeyPressQwerty(key, RawInputModifiers.None);
        await UntilAsync(() => result.IsCompleted, $"{key} não fechou o diálogo");

        owner.Close();
        return await result;
    }

    private static async Task UntilAsync(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(failure);
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task TravarReturnConfirmaComOMotivoDigitado()
    {
        var dialog = new LockWorktreeWindow("Travar o worktree x?", "Travar");
        dialog.FindControl<TextBox>("ReasonBox")!.Text = "  revisão em andamento ";

        Assert.Equal("revisão em andamento", await PressAsync<string>(dialog, PhysicalKey.Enter));
    }

    [AvaloniaFact]
    public async Task TravarEscCancela()
        => Assert.Null(await PressAsync<string>(new LockWorktreeWindow("Travar o worktree x?", "Travar"), PhysicalKey.Escape));

    [AvaloniaFact]
    public async Task AtualizarDaBaseReturnEscolheMerge()
        => Assert.Equal(BaseUpdateStrategy.Merge, await PressAsync<BaseUpdateStrategy?>(new UpdateFromBaseWindow(), PhysicalKey.Enter));

    [AvaloniaFact]
    public async Task AtualizarDaBaseEscCancela()
        => Assert.Null(await PressAsync<BaseUpdateStrategy?>(new UpdateFromBaseWindow(), PhysicalKey.Escape));

    [AvaloniaFact]
    public void AtualizarDaBaseRebaseFicaAEsquerdaDoCancelar()
    {
        var buttons = new UpdateFromBaseWindow().FindControl<DialogFrame>("Frame")!.Buttons;

        Assert.Equal(new[] { "Rebase", "Cancelar", "Merge" }, buttons.OfType<Button>().Select(b => b.Content as string));
    }

    [AvaloniaFact]
    public async Task NovoWorktreeEscCancela()
    {
        var dialog = new CreateWorktreeWindow
        {
            DataContext = new CreateWorktreeViewModel(Path.GetTempPath(), openTerminal: false, command: "claude", assignIssue: false),
        };

        Assert.Null(await PressAsync<object>(dialog, PhysicalKey.Escape));
    }

    [AvaloniaFact]
    public async Task AjudaFechaNoReturnENoEsc()
    {
        await PressAsync<object>(new HelpWindow(), PhysicalKey.Enter);
        await PressAsync<object>(new HelpWindow(), PhysicalKey.Escape);
    }

    [AvaloniaFact]
    public async Task SobreFechaNoEsc()
    {
        var about = new AboutWindow();
        about.Show();
        Dispatcher.UIThread.RunJobs();

        about.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        await UntilAsync(() => !about.IsVisible, "Esc não fechou o Sobre");
        Assert.DoesNotContain(about.GetLogicalDescendantsOfType<Button>(), b => b.Content is "Fechar");
    }

    [AvaloniaFact]
    public async Task AjustesFechamNoEscESemBotao()
    {
        var dir = Directory.CreateTempSubdirectory("hypercode-dialog-").FullName;
        var main = new MainViewModel(new Settings { MonitorProfile = "off" }, _ => { },
            PullRequestMemory.Load(Path.Combine(dir, "pull-requests.json")),
            new AutoCleanupStore(Path.Combine(dir, "autocleanup.json")));
        var settings = new SettingsWindow { DataContext = new SettingsViewModel(main) };
        settings.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(settings.GetLogicalDescendantsOfType<Button>(), b => b.Content is "Fechar");

        settings.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await UntilAsync(() => !settings.IsVisible, "Esc não fechou os Ajustes");
    }
}

internal static class LogicalTreeTestExtensions
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Avalonia.LogicalTree.ILogical root)
        => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<T>();
}
