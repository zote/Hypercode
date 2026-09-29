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

    // Return num campo de texto e no da base (AutoCompleteBox) cria o worktree de verdade, no git
    // da sandbox. Cobre o resultado, não quem o produz: aqui o IsDefault do Criar já basta, com ou
    // sem o OnFieldKeyDown.
    [AvaloniaTheory]
    [InlineData(typeof(TextBox))]
    [InlineData(typeof(AutoCompleteBox))]
    public async Task NovoWorktreeReturnNoCampoCria(Type field)
    {
        using var sandbox = new GitSandbox();
        var repository = sandbox.CreateRepository("repo");
        var dialog = new CreateWorktreeWindow
        {
            DataContext = new CreateWorktreeViewModel(repository, openTerminal: false, command: "claude", assignIssue: false)
            {
                IsNewBranchMode = true,
                BranchName = "feat/return",
                BaseRef = "main",
            },
        };

        var owner = new Window();
        owner.Show();
        var result = dialog.ShowDialog<WorktreeCreationResult?>(owner);
        Dispatcher.UIThread.RunJobs();

        var box = dialog.GetLogicalDescendantsOfType<Control>().First(c => c.GetType() == field && c.IsEffectivelyVisible);
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await UntilAsync(() => result.IsCompleted, $"Return no {field.Name} não criou o worktree");
        owner.Close();

        var created = await result;
        Assert.NotNull(created);
        Assert.Equal("feat/return", created.Branch);
        Assert.True(Directory.Exists(created.Path));
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
