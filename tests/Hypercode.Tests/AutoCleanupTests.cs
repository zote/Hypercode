using Hypercode.Services;
using Xunit;

namespace Hypercode.Tests;

public sealed class AutoCleanupTests : IDisposable
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Directory.CreateTempSubdirectory("hypercode-autocleanup-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string FilePath => Path.Combine(_directory, "autocleanup.json");

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;

    [Fact]
    public void CarenciaSobreviveAoFechamento()
    {
        var repository = Folder("repo");
        var worktree = Folder("repo-wt");
        var store = new AutoCleanupStore(FilePath);

        var before = new AutoCleanupTracker();
        before.Observe(new[] { worktree }, Now);
        store.Save(repository, before.Snapshot());

        var after = new AutoCleanupTracker();
        after.Restore(new AutoCleanupStore(FilePath).Load(repository), Now + TimeSpan.FromMinutes(11));
        after.Observe(new[] { worktree }, Now + TimeSpan.FromMinutes(11));

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
        tracker.Observe(new[] { worktree }, Now);
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
        tracker.Observe(new[] { "/wt" }, Now.ToOffset(TimeSpan.FromHours(-3)));

        Assert.Equal(TimeSpan.Zero, tracker.Snapshot().CompletedSince["/wt"].Offset);
    }

    [Fact]
    public void SoFicaSujoQuandoACarenciaMuda()
    {
        var tracker = new AutoCleanupTracker();
        tracker.Observe(new[] { "/wt" }, Now);
        tracker.Snapshot();

        tracker.Observe(new[] { "/wt" }, Now + TimeSpan.FromMinutes(1));
        Assert.False(tracker.IsDirty);

        tracker.Observe(Array.Empty<string>(), Now + TimeSpan.FromMinutes(2));
        Assert.True(tracker.IsDirty);
    }
}
