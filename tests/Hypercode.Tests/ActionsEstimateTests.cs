using System.Text.Json;
using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

/// <summary>Quando a fila anda (#132): mediana por job, a conta com fila sintética, a coleta e o cache.</summary>
public sealed class ActionsEstimateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] Mac = { "self-hosted", "mac-studio" };

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hypercode-estimate-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static ActionsRunner Runner(long id, bool busy = false, bool online = true, params string[] labels)
        => new(id, $"Runner{id}", "macOS", online, busy, labels.Length == 0 ? Mac : labels, "Default");

    private static ActionsJob Queued(long id, string name, int createdMinutesAgo, params string[] labels)
        => new(id, id, "Zimps/locanota", name, "CI", "queued", labels.Length == 0 ? Mac : labels, Now.AddMinutes(-createdMinutesAgo),
            null, null, null, "main", null, null);

    private static ActionsJob Running(long id, string name, long runnerId, int startedMinutesAgo)
        => new(id, id, "Zimps/locanota", name, "CI", "in_progress", Mac, Now.AddMinutes(-startedMinutesAgo - 1),
            Now.AddMinutes(-startedMinutesAgo), runnerId, $"Runner{runnerId}", "main", null, null);

    /// <summary>Mediana por nome de job; o que não está aqui não tem histórico.</summary>
    private static Func<ActionsJob, TimeSpan?> Medians(params (string Name, int Minutes)[] medians)
        => job => medians.Where(median => median.Name == job.Name).Select(median => (TimeSpan?)TimeSpan.FromMinutes(median.Minutes)).FirstOrDefault();

    private static TimeSpan? Minutes(double minutes) => TimeSpan.FromMinutes(minutes);

    // ── Mediana ─────────────────────────────────────────────────────────────

    [Fact]
    public void MedianaIgnoraOsExtremos()
    {
        // Falhou em 20 s, travou até o timeout de 6 h: a média iria a ~1 h 20, a mediana fica em 5 min.
        var samples = new[] { 5, 4, 6, 0.33, 360 }.Select(TimeSpan.FromMinutes).ToList();

        Assert.Equal(TimeSpan.FromMinutes(5), ActionsQueue.Median(samples));
        Assert.Equal(TimeSpan.FromMinutes(5), ActionsQueue.Median(new[] { 4, 6 }.Select(m => TimeSpan.FromMinutes(m)).ToList()));
    }

    [Fact]
    public void MedianaComPoucasAmostrasNaoVale()
    {
        var durations = new ActionsDurations(new Dictionary<string, RepositoryDurations>
        {
            ["Zimps/locanota"] = new()
            {
                CollectedAt = Now,
                Jobs =
                {
                    new JobDuration { Workflow = "CI", Name = "muito", Labels = Mac.ToList(), MedianSeconds = 300, Samples = ActionsQueue.MinSamples },
                    new JobDuration { Workflow = "CI", Name = "pouco", Labels = Mac.ToList(), MedianSeconds = 300, Samples = ActionsQueue.MinSamples - 1 },
                },
            },
        });

        Assert.Equal(TimeSpan.FromMinutes(5), durations.Median(Queued(1, "muito", 0)));
        Assert.Null(durations.Median(Queued(2, "pouco", 0)));
        // Mesmo nome com outras labels é outro job: outro runner, outra duração.
        Assert.Null(durations.Median(Queued(3, "muito", 0, "ARM64")));
    }

    // ── A conta, com fila sintética ─────────────────────────────────────────

    [Fact]
    public void UmRunnerDescontaODecorridoESomaQuemEstaAFrente()
    {
        var starts = ActionsQueue.EstimateStarts(
            new[] { Runner(1, busy: true) },
            new[] { Running(10, "Backend (.NET)", 1, startedMinutesAgo: 4), Queued(20, "Formatação (.NET)", 3), Queued(21, "Backend (.NET)", 1) },
            Medians(("Backend (.NET)", 10), ("Formatação (.NET)", 2)),
            Now);

        // Faltam 6 do Backend que roda; a Formatação entra em 6 e ocupa 2.
        Assert.Equal(Minutes(6), starts[20]);
        Assert.Equal(Minutes(8), starts[21]);
    }

    [Fact]
    public void VariosRunnersCadaJobPegaOQueLiberaPrimeiro()
    {
        var starts = ActionsQueue.EstimateStarts(
            new[] { Runner(1, busy: true), Runner(2), Runner(3, online: false) },
            new[] { Running(10, "lento", 1, startedMinutesAgo: 7), Queued(20, "rapido", 3), Queued(21, "rapido", 2), Queued(22, "lento", 1) },
            Medians(("lento", 10), ("rapido", 2)),
            Now);

        // Runner2 livre: 20 entra já e libera em 2; o Runner1 libera em 3.
        Assert.Equal(Minutes(0), starts[20]);
        Assert.Equal(Minutes(2), starts[21]);
        // Runner2 libera de novo em 4, o Runner1 em 3: o lento vai para o Runner1. O offline não conta.
        Assert.Equal(Minutes(3), starts[22]);
    }

    [Fact]
    public void RunnerQueNaoTemAsLabelsNaoAtende()
    {
        var starts = ActionsQueue.EstimateStarts(
            new[] { Runner(1, labels: new[] { "self-hosted", "ARM64" }) },
            new[] { Queued(20, "rapido", 1) },
            Medians(("rapido", 2)),
            Now);

        Assert.Null(starts[20]);
    }

    [Fact]
    public void RunnerOcupadoSemHistoricoFicaForaDaConta()
    {
        var starts = ActionsQueue.EstimateStarts(
            new[] { Runner(1, busy: true), Runner(2, busy: true), Runner(3, busy: true) },
            new[] { Running(10, "novo", 1, 1), Running(11, "lento", 2, 2), Queued(20, "rapido", 1) },
            Medians(("lento", 10), ("rapido", 2)),
            Now);

        // Runner1 roda job sem histórico e Runner3, job de repositório não acompanhado: só o Runner2 conta.
        Assert.Equal(Minutes(8), starts[20]);

        var none = ActionsQueue.EstimateStarts(
            new[] { Runner(1, busy: true) },
            new[] { Running(10, "novo", 1, 1), Queued(20, "rapido", 1) },
            Medians(("rapido", 2)),
            Now);

        Assert.Null(none[20]);
    }

    [Fact]
    public void JobSemHistoricoAFrenteTiraAEstimativaDeQuemVemAtras()
    {
        var starts = ActionsQueue.EstimateStarts(
            new[] { Runner(1, busy: true) },
            new[] { Running(10, "lento", 1, 4), Queued(20, "novo", 3), Queued(21, "rapido", 1) },
            Medians(("lento", 10), ("rapido", 2)),
            Now);

        // O "novo" começa quando o Runner1 liberar — isso se sabe. Quanto ele ocupa, não.
        Assert.Equal(Minutes(6), starts[20]);
        Assert.Null(starts[21]);

        var lane = Assert.Single(ActionsQueueViewModel.BuildLanes(Snapshot(new[] { Runner(1, busy: true) },
            new[] { Running(10, "lento", 1, 4), Queued(20, "novo", 3), Queued(21, "rapido", 1) }), Now, durations: Durations(("lento", 10), ("rapido", 2))));
        Assert.Equal(new[] { "deve começar em ~6 min", "sem estimativa" }, lane.Jobs.Select(job => job.Estimate));
    }

    [Fact]
    public void JobQueEstourouAMedianaNaoViraEstimativaNegativa()
    {
        var starts = ActionsQueue.EstimateStarts(
            new[] { Runner(1, busy: true) },
            new[] { Running(10, "lento", 1, startedMinutesAgo: 45), Queued(20, "rapido", 3), Queued(21, "rapido", 1) },
            Medians(("lento", 10), ("rapido", 2)),
            Now);

        Assert.Equal(TimeSpan.Zero, starts[20]);
        Assert.Equal(Minutes(2), starts[21]);
        Assert.Equal("deve começar a qualquer momento", ActionsQueueViewModel.Estimate(starts[20]));
    }

    [Theory]
    [InlineData(null, "sem estimativa")]
    [InlineData(0.0, "deve começar a qualquer momento")]
    [InlineData(10.0, "deve começar em ~1 min")]
    [InlineData(90.0, "deve começar em ~2 min")]
    [InlineData(4500.0, "deve começar em ~1 h 15 min")]
    public void EstimativaNaTelaSempreComoAproximacao(double? seconds, string expected)
        => Assert.Equal(expected, ActionsQueueViewModel.Estimate(seconds is { } value ? TimeSpan.FromSeconds(value) : null));

    // ── Leitura do histórico ────────────────────────────────────────────────

    private static object JobJson(string name, string status, string? conclusion, string? started, string? completed)
        => new { id = 1, name, workflow_name = "CI", status, conclusion, labels = Mac, started_at = started, completed_at = completed };

    [Fact]
    public void DuracaoSoDeJobConcluidoQueRodou()
    {
        var json = JsonSerializer.Serialize(new
        {
            jobs = new[]
            {
                JobJson("ok", "completed", "success", "2026-10-01T10:00:00Z", "2026-10-01T10:05:00Z"),
                JobJson("falhou", "completed", "failure", "2026-10-01T10:00:00Z", "2026-10-01T10:00:20Z"),
                JobJson("pulado", "completed", "skipped", "2026-10-01T10:00:00Z", "2026-10-01T10:00:00Z"),
                JobJson("cancelado", "completed", "cancelled", "2026-10-01T10:00:00Z", "2026-10-01T10:03:00Z"),
                JobJson("rodando", "in_progress", null, "2026-10-01T10:00:00Z", null),
            },
        });

        using var document = JsonDocument.Parse(json);
        var durations = ActionsQueue.ParseJobDurations(document.RootElement, "CI");

        Assert.Equal(new[] { "ok", "falhou" }, durations.Select(item => item.Name));
        Assert.Equal(TimeSpan.FromMinutes(5), durations[0].Duration);
    }

    /// <summary>A API de mentira: caminho → corpo; o resto é 404. Guarda o que foi chamado.</summary>
    private sealed class FakeApi
    {
        public Dictionary<string, object> Routes { get; } = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = new();

        public Task<ApiResponse> GetAsync(string path, CancellationToken cancellationToken)
        {
            lock (Calls) Calls.Add(path);
            return Task.FromResult(Routes.TryGetValue(path, out var body)
                ? new ApiResponse(200, JsonSerializer.Serialize(body), null, null)
                : new ApiResponse(404, "{}", null, "gh: Not Found (HTTP 404)"));
        }

        /// <summary>Um workflow com runs concluídos; cada run tem um "build" com a duração dada, em minutos.</summary>
        public void History(string repository, long workflow, params int[] minutes)
        {
            Routes[$"repos/{repository}/actions/workflows?per_page=100"] = new
            {
                workflows = new object[] { new { id = workflow, name = "CI", state = "active" }, new { id = 99, name = "Velho", state = "disabled_manually" } },
            };
            Routes[$"repos/{repository}/actions/workflows/{workflow}/runs?status=completed&per_page={ActionsQueue.HistoryRuns}"] = new
            {
                workflow_runs = minutes.Select((_, index) => new { id = 1000 + index, name = "CI", status = "completed", created_at = "2026-10-01T09:00:00Z" }).ToArray(),
            };
            for (var index = 0; index < minutes.Length; index++)
                Routes[$"repos/{repository}/actions/runs/{1000 + index}/jobs?per_page=100"] = new
                {
                    jobs = new[] { JobJson("build", "completed", "success", "2026-10-01T09:00:00Z", Now.AddHours(-3).AddMinutes(minutes[index]).ToString("O")) },
                };
        }
    }

    [Fact]
    public async Task ColetaLeOsWorkflowsAtivosERunsConcluidos()
    {
        var api = new FakeApi();
        api.History("Zimps/locanota", 7, 4, 5, 6, 30);

        var durations = await ActionsQueueService.LoadDurationsAsync("Zimps/locanota", api.GetAsync, Now);

        Assert.NotNull(durations);
        Assert.Equal(Now, durations!.CollectedAt);
        var build = Assert.Single(durations.Jobs);
        Assert.Equal(("build", 4), (build.Name, build.Samples));
        Assert.Equal(TimeSpan.FromMinutes(5.5).TotalSeconds, build.MedianSeconds);
        // Workflow desativado não é lido.
        Assert.DoesNotContain(api.Calls, call => call.Contains("/workflows/99/"));
    }

    [Fact]
    public async Task ColetaSemWorkflowsEhFalha()
        => Assert.Null(await ActionsQueueService.LoadDurationsAsync("Zimps/nao-existe", new FakeApi().GetAsync, Now));

    // ── Cache em disco ──────────────────────────────────────────────────────

    [Fact]
    public void CacheGuardaAsMedianasComADataDaColeta()
    {
        var store = new ActionsDurationStore(Path.Combine(_directory, "actions-durations.json"));
        store.Save(new Dictionary<string, RepositoryDurations>
        {
            ["Zimps/locanota"] = new() { CollectedAt = Now, Jobs = { new JobDuration { Workflow = "CI", Name = "build", Labels = Mac.ToList(), MedianSeconds = 330, Samples = 4 } } },
        });

        var loaded = store.Load();

        Assert.Equal(Now, loaded["zimps/locanota"].CollectedAt);
        Assert.Equal(330, Assert.Single(loaded["Zimps/locanota"].Jobs).MedianSeconds);
    }

    [Fact]
    public void CacheCorrompidoValeComoVazio()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "actions-durations.json");
        File.WriteAllText(path, "{ não é json");

        Assert.Empty(new ActionsDurationStore(path).Load());
    }

    // ── Cadência da coleta no view model ────────────────────────────────────

    private (ActionsQueueViewModel ViewModel, FakeApi Api, Func<DateTimeOffset, DateTimeOffset> SetNow) ViewModel(ActionsDurationStore store)
    {
        var settings = new Settings { ActionsPanelOpen = true };
        settings.ActionsRepositories.Add("Zimps/locanota");
        var api = new FakeApi();
        api.Routes["orgs/Zimps/actions/runner-groups?per_page=100"] = new { runner_groups = Array.Empty<object>() };
        api.Routes["repos/Zimps/locanota/actions/runs?status=queued&per_page=100"] = new { workflow_runs = Array.Empty<object>() };
        api.Routes["repos/Zimps/locanota/actions/runs?status=in_progress&per_page=100"] = new { workflow_runs = Array.Empty<object>() };
        api.History("Zimps/locanota", 7, 4, 5, 6);

        var now = Now;
        var viewModel = new ActionsQueueViewModel(settings, () => { }, api.GetAsync, () => now, durations: store);
        return (viewModel, api, value => now = value);
    }

    private static int HistoryCalls(FakeApi api) => api.Calls.Count(call => call.Contains("/workflows"));

    [Fact]
    public async Task ColetaAoAbrirEDepoisSoDeHoraEmHora()
    {
        var store = new ActionsDurationStore(Path.Combine(_directory, "actions-durations.json"));
        var (viewModel, api, setNow) = ViewModel(store);

        await viewModel.CheckAsync();
        await viewModel.HistoryTask;
        var first = HistoryCalls(api);
        Assert.Equal(2, first);
        Assert.Contains("coletada às", viewModel.EstimateLine);
        Assert.Equal(3, Assert.Single(store.Load()["Zimps/locanota"].Jobs).Samples);

        // Ciclos do painel, inclusive o Atualizar: a fila é relida, o histórico não.
        for (var minute = 1; minute < 60; minute += 5)
        {
            setNow(Now.AddMinutes(minute));
            await viewModel.RefreshNowAsync();
            await viewModel.HistoryTask;
        }

        Assert.Equal(first, HistoryCalls(api));

        setNow(Now.AddHours(1));
        await viewModel.CheckAsync();
        await viewModel.HistoryTask;
        Assert.Equal(2 * first, HistoryCalls(api));
    }

    [Fact]
    public async Task CacheRecenteNoDiscoNaoColetaDeNovo()
    {
        var store = new ActionsDurationStore(Path.Combine(_directory, "actions-durations.json"));
        store.Save(new Dictionary<string, RepositoryDurations>
        {
            ["Zimps/locanota"] = new() { CollectedAt = Now.AddMinutes(-20) },
        });
        var (viewModel, api, _) = ViewModel(store);

        await viewModel.CheckAsync();
        await viewModel.HistoryTask;

        Assert.Equal(0, HistoryCalls(api));
        Assert.NotEmpty(api.Calls);
    }

    [Fact]
    public async Task PainelFechadoNaoColeta()
    {
        var store = new ActionsDurationStore(Path.Combine(_directory, "actions-durations.json"));
        var (viewModel, api, _) = ViewModel(store);
        viewModel.IsPanelOpen = false;

        await viewModel.CheckAsync();
        await viewModel.HistoryTask;

        Assert.Empty(api.Calls);
        Assert.Contains("sem estimativa", viewModel.EstimateLine);
    }

    private static ActionsSnapshot Snapshot(IEnumerable<ActionsRunner> runners, IEnumerable<ActionsJob> jobs)
        => new(runners.ToList(), jobs.ToList(), Array.Empty<ActionsWaitingRun>(), Array.Empty<ActionsRun>(), Array.Empty<string>(), Array.Empty<string>(), null, Now);

    private static ActionsDurations Durations(params (string Name, int Minutes)[] medians)
        => new(new Dictionary<string, RepositoryDurations>
        {
            ["Zimps/locanota"] = new()
            {
                CollectedAt = Now,
                Jobs = medians.Select(median => new JobDuration
                {
                    Workflow = "CI",
                    Name = median.Name,
                    Labels = Mac.ToList(),
                    MedianSeconds = median.Minutes * 60,
                    Samples = ActionsQueue.MinSamples,
                }).ToList(),
            },
        });
}
