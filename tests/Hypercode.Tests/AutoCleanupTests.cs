using Hypercode.Services;
using Xunit;

namespace Hypercode.Tests;

public sealed class AutoCleanupTests : IDisposable
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Directory.CreateTempSubdirectory("hypercode-autocleanup-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static readonly Func<string, bool> Never = _ => false;
    private static readonly Func<string, bool> Always = _ => true;

    private string FilePath => Path.Combine(_directory, "autocleanup.json");

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;

    [Fact]
    public void CarenciaSobreviveAoFechamento()
    {
        var repository = Folder("repo");
        var worktree = Folder("repo-wt");
        var store = new AutoCleanupStore(FilePath);

        var before = new AutoCleanupTracker();
        before.Observe(new[] { worktree }, Never, Now);
        store.Save(repository, before.Snapshot());

        var after = new AutoCleanupTracker();
        after.Restore(new AutoCleanupStore(FilePath).Load(repository), Now + TimeSpan.FromMinutes(11));
        after.Observe(new[] { worktree }, Never, Now + TimeSpan.FromMinutes(11));

        Assert.True(after.IsDue(worktree, Grace, Now + TimeSpan.FromMinutes(11)));
    }

    [Fact]
    public void CarimboNoFuturoRecomecaACarencia()
    {
        var state = new AutoCleanupState();
        state.CompletedSince["/wt"] = Now + TimeSpan.FromDays(1);

        var tracker = new AutoCleanupTracker();
        tracker.Restore(state, Now);

        Assert.True(tracker.IsDirty);
        Assert.Equal(Grace, tracker.GraceRemaining("/wt", Grace, Now));
    }

    [Fact]
    public void SeparaPorRepositorio()
    {
        var first = Folder("a");
        var second = Folder("b");
        var worktree = Folder("a-wt");
        var store = new AutoCleanupStore(FilePath);

        var tracker = new AutoCleanupTracker();
        tracker.Observe(new[] { worktree }, Never, Now);
        tracker.RecordRemoval("/outro", "outro", "PR merged", Now);
        store.Save(first, tracker.Snapshot());

        Assert.True(store.Load(second).IsEmpty);
        Assert.Equal(new[] { worktree }, store.Load(first).CompletedSince.Keys);
        Assert.Single(store.Load(first).History);
    }

    [Fact]
    public void GravacaoPodaOQueSumiuDosOutrosRepositorios()
    {
        var kept = Folder("kept");
        var gone = Folder("gone");
        var alive = Folder("kept-wt");
        var store = new AutoCleanupStore(FilePath);

        var state = new AutoCleanupState();
        state.CompletedSince[alive] = Now;
        state.CompletedSince[Path.Combine(_directory, "apagado")] = Now;
        store.Save(kept, state);
        store.Save(gone, new AutoCleanupState { CompletedSince = { [alive] = Now } });

        Directory.Delete(gone);
        store.Save(Folder("current"), new AutoCleanupState { CompletedSince = { ["/x"] = Now } });

        Assert.Equal(new[] { alive }, store.Load(kept).CompletedSince.Keys);
        Assert.True(store.Load(gone).IsEmpty);
    }

    [Fact]
    public void ArquivoCorrompidoValeComoVazio()
    {
        File.WriteAllText(FilePath, "{ não é json");

        Assert.True(new AutoCleanupStore(FilePath).Load("/repo").IsEmpty);
    }

    [Fact]
    public void GravaEmUtc()
    {
        var tracker = new AutoCleanupTracker();
        tracker.Observe(new[] { "/wt" }, Never, Now.ToOffset(TimeSpan.FromHours(-3)));

        Assert.Equal(TimeSpan.Zero, tracker.Snapshot().CompletedSince["/wt"].Offset);
    }

    [Fact]
    public void SoFicaSujoQuandoACarenciaMuda()
    {
        var tracker = new AutoCleanupTracker();
        tracker.Observe(new[] { "/wt" }, Never, Now);
        tracker.Snapshot();

        tracker.Observe(new[] { "/wt" }, Never, Now + TimeSpan.FromMinutes(1));
        Assert.False(tracker.IsDirty);

        tracker.Observe(Array.Empty<string>(), Always, Now + TimeSpan.FromMinutes(2));
        Assert.True(tracker.IsDirty);
    }

    private static string Local(DateTimeOffset moment) => $"{moment.ToLocalTime():dd/MM HH:mm}";

    [Fact]
    public void DescreveOCarimboEOPrazoDaCarencia()
    {
        var tracker = new AutoCleanupTracker();
        tracker.Observe(new[] { "/wt" }, Never, Now);

        var note = tracker.Describe("/wt", Grace, Now + TimeSpan.FromMinutes(3), startsAt: Now);

        Assert.Equal(
            $"Visto concluído pela limpeza automática em {Local(Now)}.\n"
            + $"Carência de 10 min: sai a partir de {Local(Now + Grace)}.",
            note);
    }

    [Fact]
    public void CarimboSegueOMesmoEntreTiquesEMudaQuandoACarenciaRecomeca()
    {
        var tracker = new AutoCleanupTracker();
        tracker.Observe(new[] { "/wt" }, Never, Now);
        tracker.Observe(new[] { "/wt" }, Never, Now + TimeSpan.FromMinutes(1));
        Assert.Contains($"em {Local(Now)}.", tracker.Describe("/wt", Grace, Now + TimeSpan.FromMinutes(1), Now));

        // Um PR que voltou a estar aberto, ou uma pasta que saiu da lista, esquece o carimbo.
        tracker.Observe(Array.Empty<string>(), Always, Now + TimeSpan.FromMinutes(2));
        Assert.Null(tracker.Describe("/wt", Grace, Now + TimeSpan.FromMinutes(2), Now));

        tracker.Observe(new[] { "/wt" }, Never, Now + TimeSpan.FromMinutes(3));
        Assert.Contains($"em {Local(Now + TimeSpan.FromMinutes(3))}.", tracker.Describe("/wt", Grace, Now + TimeSpan.FromMinutes(3), Now));
    }

    [Fact]
    public void TiqueSemOPrAindaNaoEsqueceOCarimbo()
    {
        var tracker = new AutoCleanupTracker();
        tracker.Observe(new[] { "/wt" }, Never, Now);
        tracker.Snapshot();

        // Reabriu o app: a lista chegou, o PR ainda não — o worktree não aparece como concluído.
        tracker.Observe(Array.Empty<string>(), Never, Now + TimeSpan.FromMinutes(1));

        Assert.False(tracker.IsDirty);
        Assert.Contains($"em {Local(Now)}.", tracker.Describe("/wt", Grace, Now + TimeSpan.FromMinutes(1), Now));

        // O PR chegou e é o mesmo: a carência segue de onde estava.
        tracker.Observe(new[] { "/wt" }, Never, Now + TimeSpan.FromMinutes(2));
        Assert.True(tracker.IsDue("/wt", Grace, Now + Grace));
    }

    [Fact]
    public void PastaQueSaiuDaListaOuPrReabertoEsquecemOCarimbo()
    {
        var tracker = new AutoCleanupTracker();
        tracker.Observe(new[] { "/gone", "/reopened", "/waiting" }, Never, Now);

        tracker.Observe(Array.Empty<string>(), path => path != "/waiting", Now + TimeSpan.FromMinutes(1));

        Assert.Null(tracker.Describe("/gone", Grace, Now, Now));
        Assert.Null(tracker.Describe("/reopened", Grace, Now, Now));
        Assert.NotNull(tracker.Describe("/waiting", Grace, Now, Now));
    }

    [Fact]
    public void PrazoRespeitaAJanelaDeAberturaEOAdiamento()
    {
        var tracker = new AutoCleanupTracker();
        tracker.Observe(new[] { "/wt" }, Never, Now - TimeSpan.FromHours(1));

        var startsAt = Now + TimeSpan.FromMinutes(2);
        Assert.EndsWith($"sai a partir de {Local(startsAt)}.", tracker.Describe("/wt", Grace, Now, startsAt));

        Assert.EndsWith("sai no próximo tique do monitoramento, se a janela não estiver minimizada.",
            tracker.Describe("/wt", Grace, Now, startsAt: Now));
    }

    [Fact]
    public void PendenteMostraOMotivoEANovaTentativa()
    {
        var tracker = new AutoCleanupTracker();
        tracker.Observe(new[] { "/wt" }, Never, Now - TimeSpan.FromHours(1));
        tracker.Skip("/wt", "wt", "em uso: há terminal ou processo com a pasta aberta", AutoCleanupTracker.InUseRetry, Now);

        var note = tracker.Describe("/wt", Grace, Now, startsAt: Now);

        Assert.Contains("Mantido: em uso: há terminal ou processo com a pasta aberta.", note);
        Assert.EndsWith($"Nova tentativa a partir de {Local(Now + AutoCleanupTracker.InUseRetry)}.", note);
    }

    [Fact]
    public void SemCarimboNaoDescreve()
        => Assert.Null(new AutoCleanupTracker().Describe("/wt", Grace, Now, Now));
}
