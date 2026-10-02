using System.Text.Json;
using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

/// <summary>A fila do GitHub Actions (#130): agrupamento, ordem, cruzamento com os runners e a leitura da API.</summary>
public sealed class ActionsQueueTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static ActionsJob Job(
        long id,
        string repository,
        string status,
        DateTimeOffset createdAt,
        params string[] labels)
        => new(id, id, repository, $"job {id}", "CI", status, labels, createdAt, null, null, null, "main", null, null);

    // ── Filas ───────────────────────────────────────────────────────────────

    [Fact]
    public void FilaSeparaPorConjuntoDeLabelsSemCaixaNemOrdem()
    {
        var lanes = ActionsQueue.BuildLanes(new[]
        {
            Job(1, "Zimps/locanota", "queued", Start, "self-hosted", "ARM64"),
            Job(2, "Zimps/outro", "queued", Start.AddSeconds(5), "arm64", "Self-Hosted"),
            Job(3, "Zimps/locanota", "queued", Start.AddSeconds(1), "self-hosted", "mac-studio"),
        });

        Assert.Equal(2, lanes.Count);
        Assert.Equal(new long[] { 1, 2 }, lanes[0].Jobs.Select(job => job.Id));
        Assert.Equal(new long[] { 3 }, lanes[1].Jobs.Select(job => job.Id));
    }

    [Fact]
    public void FilaOrdenaPorCreatedAtComReposMisturados()
    {
        var lanes = ActionsQueue.BuildLanes(new[]
        {
            Job(10, "Zimps/b", "queued", Start.AddMinutes(3), "mac-studio"),
            Job(11, "Zimps/a", "queued", Start.AddMinutes(1), "mac-studio"),
            Job(12, "Zimps/c", "queued", Start.AddMinutes(2), "mac-studio"),
            Job(13, "Zimps/a", "queued", Start.AddMinutes(1), "mac-studio"),
        });

        var lane = Assert.Single(lanes);
        // Empate no created_at: o id desempata, para a ordem não pular entre ciclos.
        Assert.Equal(new long[] { 11, 13, 12, 10 }, lane.Jobs.Select(job => job.Id));
        Assert.Equal(new[] { "Zimps/a", "Zimps/a", "Zimps/c", "Zimps/b" }, lane.Jobs.Select(job => job.Repository));
    }

    [Fact]
    public void SoJobQueuedEntraNaFila()
    {
        var lanes = ActionsQueue.BuildLanes(new[]
        {
            Job(1, "Zimps/a", "in_progress", Start, "x"),
            Job(2, "Zimps/a", "completed", Start, "x"),
            Job(3, "Zimps/a", "waiting", Start, "x"),
        });

        Assert.Empty(lanes);
    }

    [Fact]
    public void FilaMaiorVemPrimeiro()
    {
        var lanes = ActionsQueue.BuildLanes(new[]
        {
            Job(1, "Zimps/a", "queued", Start, "solo"),
            Job(2, "Zimps/a", "queued", Start.AddMinutes(1), "dupla"),
            Job(3, "Zimps/b", "queued", Start.AddMinutes(2), "dupla"),
        });

        Assert.Equal(new[] { "dupla", "solo" }, lanes.Select(lane => lane.Labels.Single()));
    }

    [Fact]
    public void PosicaoETempoDeEsperaNaTela()
    {
        var snapshot = Snapshot(jobs: new[]
        {
            Job(2, "Zimps/b", "queued", Start.AddMinutes(-2), "mac"),
            Job(1, "Zimps/a", "queued", Start.AddMinutes(-75), "mac"),
        });

        var lane = Assert.Single(ActionsQueueViewModel.BuildLanes(snapshot, Start));

        Assert.Equal(new[] { "1º", "2º" }, lane.Jobs.Select(job => job.Position));
        Assert.Equal(new[] { "espera há 1 h 15 min", "espera há 2 min" }, lane.Jobs.Select(job => job.Wait));
        Assert.Equal("a · main", lane.Jobs[0].Origin);
    }

    // ── Runners ─────────────────────────────────────────────────────────────

    private static ActionsRunner Runner(long id, string name, bool online = true, bool busy = false, string group = "Default")
        => new(id, name, "macOS", online, busy, new[] { "self-hosted" }, group);

    [Fact]
    public void RunnerOcupadoMostraOJobDoRepositorioAcompanhado()
    {
        var running = Job(7, "Zimps/locanota", "in_progress", Start) with { RunnerId = 4, RunnerName = "Runner4", PullRequest = 12 };
        var byName = Job(8, "Zimps/locanota", "in_progress", Start) with { RunnerName = "Runner5" };

        var map = ActionsQueue.JobsByRunner(
            new[] { Runner(4, "Runner4", busy: true), Runner(5, "Runner5", busy: true), Runner(6, "Runner6", busy: true) },
            new[] { running, byName });

        Assert.Equal(7, map[4].Id);
        Assert.Equal(8, map[5].Id);
        // Ocupado com job de repositório não acompanhado: sem job, sem erro.
        Assert.False(map.ContainsKey(6));
    }

    [Fact]
    public void RunnersAgrupadosPorGrupoComOcupadoPrimeiro()
    {
        var snapshot = Snapshot(runners: new[]
        {
            Runner(1, "b-livre"),
            Runner(2, "a-offline", online: false),
            Runner(3, "c-ocupado", busy: true),
            Runner(4, "aws", busy: true, group: "zimps-arm64"),
        });

        var groups = ActionsQueueViewModel.BuildRunnerGroups(snapshot, Start);

        Assert.Equal(new[] { "Default", "zimps-arm64" }, groups.Select(group => group.Name));
        Assert.Equal(new[] { RunnerState.Busy, RunnerState.Idle, RunnerState.Offline }, groups[0].Runners.Select(runner => runner.State));
        Assert.Equal("1 ocupado de 2 online · 1 offline", groups[0].Summary);
    }

    // ── Configuração ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Zimps/locanota", "Zimps/locanota")]
    [InlineData("  https://github.com/Zimps/locanota.git ", "Zimps/locanota")]
    [InlineData("github.com/zote/Hypercode/", "zote/Hypercode")]
    [InlineData("git@github.com:Zimps/Zimps.Infra.git", "Zimps/Zimps.Infra")]
    [InlineData("locanota", null)]
    [InlineData("Zimps/locanota/pulls", null)]
    [InlineData("", null)]
    public void RepositorioDaConfiguracao(string text, string? expected)
        => Assert.Equal(expected, ActionsQueue.NormalizeRepository(text));

    [Fact]
    public void ListaDaConfiguracaoSemRepetir()
        => Assert.Equal(
            new[] { "Zimps/a", "Zimps/b" },
            ActionsQueue.ParseRepositories("Zimps/a\nlixo\nzimps/A, Zimps/b\r\n"));

    [Fact]
    public void SettingsNormalizaOsRepositoriosAoLer()
    {
        var settings = SettingsStore.Parse("""{ "ActionsRepositories": ["https://github.com/Zimps/a", "Zimps/a", "x"], "ActionsPanelOpen": true }""");

        Assert.Equal(new[] { "Zimps/a" }, settings.ActionsRepositories);
        Assert.True(settings.ActionsPanelOpen);
        Assert.False(settings.ActionsWindowOpen);
    }

    [Fact]
    public void SettingsSemAChaveFicaComListaVazia()
        => Assert.Empty(SettingsStore.Parse("""{ "ActionsRepositories": null }""").ActionsRepositories);

    // ── Resposta do gh api -i ───────────────────────────────────────────────

    [Fact]
    public void RespostaTrazStatusCorpoECota()
    {
        var response = ApiResponse.Parse(
            "HTTP/2.0 200 OK\r\nContent-Type: application/json\r\nX-Ratelimit-Limit: 5000\r\nX-Ratelimit-Used: 4100\r\nX-Ratelimit-Reset: 1790859754\r\n\r\n{\"total_count\":0}",
            null);

        Assert.True(response.Success);
        Assert.Equal("{\"total_count\":0}", response.Body);
        Assert.Equal(5000, response.Budget!.Limit);
        Assert.Equal(0.82, response.Budget.UsedFraction, 2);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790859754), response.Budget.ResetAt);
    }

    [Fact]
    public void RespostaDeErroGuardaOStatus()
    {
        var response = ApiResponse.Parse("HTTP/2.0 404 Not Found\nX-Ratelimit-Limit: 5000\n\n{\"message\":\"Not Found\"}", "gh: Not Found (HTTP 404)");

        Assert.False(response.Success);
        Assert.Equal(404, response.Status);
        Assert.Equal("gh: Not Found (HTTP 404)", response.Error);
    }

    [Fact]
    public void SaidaSemStatusEhFalhaAntesDoGitHub()
    {
        var response = ApiResponse.Parse(string.Empty, "To get started with GitHub CLI, please run: gh auth login");

        Assert.Equal(0, response.Status);
        Assert.False(response.Success);
    }

    // ── Um ciclo, com a API de mentira ──────────────────────────────────────

    /// <summary>A API de mentira: caminho → (status, corpo). Guarda o que foi chamado.</summary>
    private sealed class FakeApi
    {
        public Dictionary<string, (int Status, object Body)> Routes { get; } = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = new();

        public Task<ApiResponse> GetAsync(string path, CancellationToken cancellationToken)
        {
            lock (Calls) Calls.Add(path);
            return Task.FromResult(Routes.TryGetValue(path, out var route)
                ? new ApiResponse(route.Status, JsonSerializer.Serialize(route.Body), null, route.Status is >= 200 and < 300 ? null : "erro")
                : new ApiResponse(404, "{\"message\":\"Not Found\"}", null, "gh: Not Found (HTTP 404)"));
        }

        public void Runs(string repository, string status, params object[] runs)
            => Routes[$"repos/{repository}/actions/runs?status={status}&per_page=100"] = (200, new { total_count = runs.Length, workflow_runs = runs });

        public void Jobs(string repository, long run, params object[] jobs)
            => Routes[$"repos/{repository}/actions/runs/{run}/jobs?per_page=100"] = (200, new { total_count = jobs.Length, jobs });

        public void Groups(string owner, params object[] groups)
            => Routes[$"orgs/{owner}/actions/runner-groups?per_page=100"] = (200, new { total_count = groups.Length, runner_groups = groups });

        public void GroupRunners(string owner, long group, params object[] runners)
            => Routes[$"orgs/{owner}/actions/runner-groups/{group}/runners?per_page=100"] = (200, new { total_count = runners.Length, runners });

        /// <summary>Os grupos de concurrency ativos do repositório, cada um com os membros dados.</summary>
        public void Concurrency(string repository, params (string Name, object[] Members)[] groups)
        {
            Routes[$"repos/{repository}/actions/concurrency_groups?per_page=100"] = (200, new
            {
                total_count = groups.Length,
                concurrency_groups = groups.Select(group => new
                {
                    group_name = group.Name,
                    group_url = $"https://api.github.com/repos/{repository}/actions/concurrency_groups/{Uri.EscapeDataString(group.Name)}",
                    last_acquired_at = Start.ToString("O"),
                }).ToArray(),
            });
            foreach (var (name, members) in groups)
                Routes[$"repos/{repository}/actions/concurrency_groups/{Uri.EscapeDataString(name)}"] =
                    (200, new { group_name = name, total_count = members.Length, group_members = members });
        }
    }

    private static object MemberJson(long run, string status, long? job = null)
        => new
        {
            run_id = run,
            run_name = "CI",
            run_html_url = $"https://github.com/o/r/actions/runs/{run}",
            job_id = job,
            job_name = job is null ? null : $"job {job}",
            status,
        };

    private static object RunJson(long id, string status, string branch = "feat/x", int? pullRequest = null)
        => new
        {
            id,
            name = "CI",
            status,
            head_branch = branch,
            created_at = Start.ToString("O"),
            html_url = $"https://github.com/o/r/actions/runs/{id}",
            pull_requests = pullRequest is { } number ? new object[] { new { number } } : Array.Empty<object>(),
        };

    private static object JobJson(long id, string status, string createdAt, string[] labels, long? runnerId = null, string? runnerName = null)
        => new { id, name = $"job {id}", workflow_name = "CI", status, labels, created_at = createdAt, runner_id = runnerId, runner_name = runnerName };

    private static object RunnerJson(long id, string name, bool busy, string status = "online")
        => new { id, name, os = "macOS", status, busy, labels = new[] { new { name = "self-hosted" }, new { name = "mac-studio" } } };

    [Fact]
    public async Task CicloJuntaFilaDeDoisReposERunnersPorGrupo()
    {
        var api = new FakeApi();
        api.Runs("Zimps/a", "queued", RunJson(1, "queued", pullRequest: 12));
        api.Runs("Zimps/a", "in_progress", RunJson(2, "in_progress"));
        api.Runs("Zimps/b", "queued", RunJson(3, "queued"));
        api.Runs("Zimps/b", "in_progress");
        api.Jobs("Zimps/a", 1, JobJson(10, "queued", "2026-10-01T12:00:05Z", new[] { "self-hosted", "mac-studio" }));
        api.Jobs("Zimps/a", 2, JobJson(20, "in_progress", "2026-10-01T11:59:00Z", new[] { "self-hosted", "mac-studio" }, 4, "Runner4"));
        api.Jobs("Zimps/b", 3, JobJson(30, "queued", "2026-10-01T12:00:01Z", new[] { "mac-studio", "self-hosted" }));
        api.Groups("Zimps", new { id = 1, name = "Default" });
        api.GroupRunners("Zimps", 1, RunnerJson(4, "Runner4", busy: true), RunnerJson(5, "Runner5", busy: false));

        var snapshot = await ActionsQueueService.LoadAsync(new[] { "Zimps/a", "Zimps/b" }, api.GetAsync, Start);

        Assert.Empty(snapshot.RunnerProblems);
        Assert.Empty(snapshot.QueueProblems);
        Assert.Equal(new[] { "Default", "Default" }, snapshot.Runners.Select(runner => runner.Group));

        var lane = Assert.Single(ActionsQueue.BuildLanes(snapshot.Jobs));
        Assert.Equal(new long[] { 30, 10 }, lane.Jobs.Select(job => job.Id));
        Assert.Equal(12, lane.Jobs[1].PullRequest);
        Assert.Equal(20, ActionsQueue.JobsByRunner(snapshot.Runners, snapshot.Jobs)[4].Id);

        // Uma chamada por grupo, uma de grupos, e por repositório 2 + 1 por run ativo + a
        // listagem dos grupos de concurrency, porque nos dois há job na fila.
        Assert.Equal(2 + 3 + 2 + 2 + 2, api.Calls.Count);
    }

    [Fact]
    public async Task SemAdminNaOrgOsRunnersAvisamEAFilaSegue()
    {
        var api = new FakeApi();
        api.Runs("Zimps/a", "queued", RunJson(1, "queued"));
        api.Runs("Zimps/a", "in_progress");
        api.Jobs("Zimps/a", 1, JobJson(10, "queued", "2026-10-01T12:00:05Z", new[] { "ARM64" }));
        api.Routes["orgs/Zimps/actions/runner-groups?per_page=100"] = (403, new { message = "Must have admin rights to Repository." });

        var snapshot = await ActionsQueueService.LoadAsync(new[] { "Zimps/a" }, api.GetAsync, Start);

        var problem = Assert.Single(snapshot.RunnerProblems);
        Assert.Contains("admin:org", problem);
        Assert.Empty(snapshot.QueueProblems);
        Assert.Single(ActionsQueue.BuildLanes(snapshot.Jobs));
    }

    [Fact]
    public async Task ContaPessoalUsaOsRunnersDoRepositorio()
    {
        var api = new FakeApi();
        api.Runs("zote/Hypercode", "queued");
        api.Runs("zote/Hypercode", "in_progress");
        api.Routes["repos/zote/Hypercode/actions/runners?per_page=100"] = (200, new { total_count = 1, runners = new[] { RunnerJson(9, "mini", busy: false) } });

        var snapshot = await ActionsQueueService.LoadAsync(new[] { "zote/Hypercode" }, api.GetAsync, Start);

        Assert.Empty(snapshot.RunnerProblems);
        Assert.Equal("zote/Hypercode", Assert.Single(snapshot.Runners).Group);
    }

    [Fact]
    public async Task RunNaFilaSemJobViraRunEmEspera()
    {
        var api = new FakeApi();
        api.Runs("Zimps/a", "queued", RunJson(1, "queued", branch: "main"));
        api.Runs("Zimps/a", "in_progress");
        api.Jobs("Zimps/a", 1);
        api.Groups("Zimps");

        var snapshot = await ActionsQueueService.LoadAsync(new[] { "Zimps/a" }, api.GetAsync, Start);

        var waiting = Assert.Single(snapshot.WaitingRuns);
        Assert.Equal("main", waiting.Branch);
        Assert.Empty(ActionsQueue.BuildLanes(snapshot.Jobs));
    }

    [Fact]
    public async Task RepositorioInexistenteViraAvisoDaFila()
    {
        var api = new FakeApi();
        api.Groups("Zimps");

        var snapshot = await ActionsQueueService.LoadAsync(new[] { "Zimps/nao-existe" }, api.GetAsync, Start);

        Assert.Contains("Zimps/nao-existe", Assert.Single(snapshot.QueueProblems));
    }

    [Fact]
    public async Task RunQueTerminouEntreAsChamadasNaoEhErro()
    {
        var api = new FakeApi();
        api.Runs("Zimps/a", "queued");
        api.Runs("Zimps/a", "in_progress", RunJson(2, "in_progress"));
        api.Groups("Zimps");
        // Sem rota para os jobs do run 2: 404, como o run que acabou de sumir.

        var snapshot = await ActionsQueueService.LoadAsync(new[] { "Zimps/a" }, api.GetAsync, Start);

        Assert.Empty(snapshot.QueueProblems);
        Assert.Empty(snapshot.Jobs);
    }

    // ── Concurrency (#131) ──────────────────────────────────────────────────

    private static ActionsConcurrencyMember Member(long run, string status, long? job = null)
        => new(run, "CI", $"https://github.com/o/r/actions/runs/{run}", job, job is null ? null : $"job {job}", status);

    [Fact]
    public void GrupoSoComQuemEsperaNaoSeguraNinguem()
        => Assert.Empty(ActionsQueue.Holds(new[]
        {
            new ActionsConcurrencyGroup("Zimps/a", "ci-1", new[] { Member(1, "pending"), Member(2, "pending") }),
        }));

    [Fact]
    public void GrupoComQuemRodaSeguraQuemEspera()
    {
        var hold = Assert.Single(ActionsQueue.Holds(new[]
        {
            new ActionsConcurrencyGroup("Zimps/a", "ci-1226", new[] { Member(1, "in_progress"), Member(2, "pending") }),
        }));

        Assert.Equal(2, hold.RunId);
        Assert.Null(hold.JobId);
        Assert.Equal("ci-1226", hold.Group);
        Assert.Equal(1, hold.Holder.RunId);
    }

    [Fact]
    public void GrupoVazioNaoSeguraNinguem()
        => Assert.Empty(ActionsQueue.Holds(new[]
        {
            new ActionsConcurrencyGroup("Zimps/a", "ci-1", Array.Empty<ActionsConcurrencyMember>()),
        }));

    [Fact]
    public void ConcurrencyDeJobSeguraOJobNaoORun()
    {
        var hold = Assert.Single(ActionsQueue.Holds(new[]
        {
            new ActionsConcurrencyGroup("Zimps/a", "deploy", new[] { Member(1, "in_progress", job: 10), Member(2, "pending", job: 20) }),
        }));

        Assert.Equal(20, hold.JobId);
        Assert.Equal(10, hold.Holder.JobId);
    }

    [Fact]
    public async Task RunSeguradoPorConcurrencyVemDaListagemDoRepositorio()
    {
        var api = new FakeApi();
        api.Runs("Zimps/a", "queued", RunJson(2, "queued"));
        api.Runs("Zimps/a", "in_progress", RunJson(1, "in_progress"));
        api.Jobs("Zimps/a", 1, JobJson(10, "in_progress", "2026-10-01T11:59:00Z", new[] { "ARM64" }, 4, "Runner4"));
        api.Jobs("Zimps/a", 2);
        api.Groups("Zimps");
        api.Concurrency("Zimps/a", ("ci-refs/heads/main", new[] { MemberJson(1, "in_progress"), MemberJson(2, "pending") }));

        var snapshot = await ActionsQueueService.LoadAsync(new[] { "Zimps/a" }, api.GetAsync, Start);

        var hold = Assert.Single(snapshot.Holds);
        Assert.Equal(2, hold.RunId);
        Assert.Equal(1, hold.Holder.RunId);
        Assert.Empty(snapshot.QueueProblems);

        // Uma listagem por repositório e o GET de cada grupo ativo — nunca o endpoint por run.
        Assert.Single(api.Calls, call => call.StartsWith("repos/Zimps/a/actions/concurrency_groups?", StringComparison.Ordinal));
        Assert.Single(api.Calls, call => call.StartsWith("repos/Zimps/a/actions/concurrency_groups/", StringComparison.Ordinal));
        Assert.DoesNotContain(api.Calls, call => call.Contains("/runs/") && call.Contains("concurrency"));
    }

    [Fact]
    public async Task RepositorioSemNadaEsperandoNaoLeOsGrupos()
    {
        var api = new FakeApi();
        api.Runs("Zimps/a", "queued");
        api.Runs("Zimps/a", "in_progress", RunJson(1, "in_progress"));
        api.Jobs("Zimps/a", 1, JobJson(10, "in_progress", "2026-10-01T11:59:00Z", new[] { "ARM64" }, 4, "Runner4"));
        api.Groups("Zimps");

        await ActionsQueueService.LoadAsync(new[] { "Zimps/a" }, api.GetAsync, Start);

        Assert.DoesNotContain(api.Calls, call => call.Contains("concurrency_groups"));
    }

    [Fact]
    public async Task SemAcessoAosGruposAFilaSegueSemDistinguir()
    {
        var api = new FakeApi();
        api.Runs("Zimps/a", "queued", RunJson(2, "queued"));
        api.Runs("Zimps/a", "in_progress");
        api.Jobs("Zimps/a", 2);
        api.Groups("Zimps");
        // Sem rota para concurrency_groups: 404.

        var snapshot = await ActionsQueueService.LoadAsync(new[] { "Zimps/a" }, api.GetAsync, Start);

        Assert.Empty(snapshot.Holds);
        Assert.Empty(snapshot.QueueProblems);
        Assert.Single(snapshot.WaitingRuns);
    }

    [Fact]
    public void JobSeguradoSaiDaFilaEDizQuemDestrava()
    {
        var waiting = Job(20, "Zimps/a", "queued", Start, "ARM64") with { RunId = 2 };
        var free = Job(30, "Zimps/a", "queued", Start.AddSeconds(1), "ARM64") with { RunId = 3 };
        var snapshot = Snapshot(jobs: new[] { waiting, free }) with
        {
            Holds = new[] { new ActionsHold("Zimps/a", "deploy", 2, 20, Member(1, "in_progress", job: 10)) },
        };

        var lane = Assert.Single(ActionsQueueViewModel.BuildLanes(snapshot, Start));
        Assert.Equal("CI › job 30", Assert.Single(lane.Jobs).Title);

        var held = Assert.Single(ActionsQueueViewModel.BuildHeld(snapshot, Start));
        Assert.Equal("CI › job 20", held.Title);
        Assert.Equal("deploy", held.Group);
        Assert.Equal("destrava quando CI › job 10 terminar ou for cancelado", held.Unlock);
        Assert.Equal("https://github.com/o/r/actions/runs/1", held.HolderUrl);
    }

    [Fact]
    public void RunSeguradoNomeiaOQueRodaPeloNumero()
    {
        var snapshot = new ActionsSnapshot(
            Array.Empty<ActionsRunner>(),
            Array.Empty<ActionsJob>(),
            new[] { new ActionsWaitingRun(2, "Zimps/a", "CI", "feat/x", 12, Start, null) },
            new[] { new ActionsRun(1, "Zimps/a", "CI", "feat: x", 482, "feat/x", 12, "https://github.com/o/r/actions/runs/1") },
            Array.Empty<string>(),
            Array.Empty<string>(),
            null,
            Start)
        {
            Holds = new[] { new ActionsHold("Zimps/a", "ci-12", 2, null, new ActionsConcurrencyMember(1, "CI", null, null, null, "in_progress")) },
        };
        var targets = (string repository, long id) => id == 1
            ? new RunTarget(repository, 1, "CI", "feat: x", 482, "feat/x", 12, "https://github.com/o/r/actions/runs/1", null, null)
            : null;

        var held = Assert.Single(ActionsQueueViewModel.BuildHeld(snapshot, Start.AddMinutes(3), targets));
        Assert.Equal("destrava quando CI #482 terminar ou for cancelado", held.Unlock);
        Assert.Equal("https://github.com/o/r/actions/runs/1", held.HolderUrl);
        Assert.Equal("espera há 3 min", held.Wait);
    }

    [Fact]
    public void MembroQueNaoEstaNaLeituraNaoViraItem()
    {
        var snapshot = Snapshot() with
        {
            Holds = new[] { new ActionsHold("Zimps/a", "ci-1", 99, null, Member(1, "in_progress")) },
        };

        Assert.Empty(ActionsQueueViewModel.BuildHeld(snapshot, Start));
    }

    // ── Cadência ────────────────────────────────────────────────────────────

    [Fact]
    public void CadenciaDaFilaSegueOPerfilEAJanela()
    {
        var scheduler = new MonitorScheduler();
        Assert.True(scheduler.IsActionsDue(Start));

        scheduler.MarkActionsChecked(Start);
        Assert.False(scheduler.IsActionsDue(Start.AddSeconds(29)));
        Assert.True(scheduler.IsActionsDue(Start.AddSeconds(30)));

        scheduler.IsWindowActive = false;
        Assert.False(scheduler.IsActionsDue(Start.AddSeconds(89)));
        Assert.True(scheduler.IsActionsDue(Start.AddSeconds(90)));

        scheduler.IsWindowMinimized = true;
        Assert.False(scheduler.IsActionsDue(Start.AddHours(1)));
    }

    [Fact]
    public void PerfilDesligadoNaoLeAFila()
        => Assert.False(new MonitorScheduler { Profile = MonitorProfile.Off }.IsActionsDue(Start));

    [Fact]
    public void CotaRestAltaEspacaAFila()
    {
        var scheduler = new MonitorScheduler();
        scheduler.RecordBudget(new ApiBudget(5000, 4600, 1, Start.AddMinutes(30)));
        scheduler.MarkActionsChecked(Start);

        // 92 % da cota: 10×.
        Assert.False(scheduler.IsActionsDue(Start.AddSeconds(299)));
        Assert.True(scheduler.IsActionsDue(Start.AddSeconds(300)));
    }

    // ── O view model: fechado não chama nada ────────────────────────────────

    private static ActionsSnapshot Snapshot(IEnumerable<ActionsRunner>? runners = null, IEnumerable<ActionsJob>? jobs = null)
        => new(
            (runners ?? Array.Empty<ActionsRunner>()).ToList(),
            (jobs ?? Array.Empty<ActionsJob>()).ToList(),
            Array.Empty<ActionsWaitingRun>(),
            Array.Empty<ActionsRun>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            null,
            Start);

    private static (ActionsQueueViewModel ViewModel, FakeApi Api, Settings Settings) ViewModel(bool panelOpen, params string[] repositories)
    {
        var settings = new Settings { ActionsPanelOpen = panelOpen };
        settings.ActionsRepositories.AddRange(repositories);
        var api = new FakeApi();
        api.Groups("Zimps");
        api.Runs("Zimps/a", "queued");
        api.Runs("Zimps/a", "in_progress");
        var now = Start;
        return (new ActionsQueueViewModel(settings, () => { }, api.GetAsync, () => now), api, settings);
    }

    [Fact]
    public async Task PainelRecolhidoNaoChamaNada()
    {
        var (viewModel, api, _) = ViewModel(panelOpen: false, "Zimps/a");

        await viewModel.CheckAsync();
        await viewModel.RefreshNowAsync();

        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task SemRepositorioNaoChamaNadaEPedeConfiguracao()
    {
        var (viewModel, api, _) = ViewModel(panelOpen: true);

        await viewModel.CheckAsync();

        Assert.Empty(api.Calls);
        Assert.True(viewModel.HasNoRepositories);
    }

    [Fact]
    public async Task PainelAbertoLeEDepoisEsperaOCiclo()
    {
        var (viewModel, api, _) = ViewModel(panelOpen: true, "Zimps/a");

        await viewModel.CheckAsync();
        var first = api.Calls.Count;
        await viewModel.CheckAsync();

        // Fila queued e in_progress, grupos de runner e — só no primeiro ciclo — a permissão de escrita (#133).
        Assert.Equal(4, first);
        Assert.Equal(first, api.Calls.Count);
        Assert.True(viewModel.HasNoQueue);
    }

    [Fact]
    public async Task RunnerEfemeroQueSomeSoSaiDaLista()
    {
        var settings = new Settings { ActionsPanelOpen = true };
        settings.ActionsRepositories.Add("Zimps/a");
        var api = new FakeApi();
        api.Runs("Zimps/a", "queued");
        api.Runs("Zimps/a", "in_progress");
        api.Groups("Zimps", new { id = 4, name = "zimps-arm64" });
        api.GroupRunners("Zimps", 4, RunnerJson(1, "aws-1790850232", busy: true), RunnerJson(2, "aws-1790850417", busy: true));

        var now = Start;
        var viewModel = new ActionsQueueViewModel(settings, () => { }, api.GetAsync, () => now);
        await viewModel.CheckAsync();
        Assert.Equal(2, viewModel.RunnerGroups.Single().Runners.Count);

        api.GroupRunners("Zimps", 4, RunnerJson(2, "aws-1790850417", busy: true));
        now = Start.AddMinutes(1);
        await viewModel.CheckAsync();

        Assert.Equal("aws-1790850417", viewModel.RunnerGroups.Single().Runners.Single().Name);
        Assert.False(viewModel.HasRunnerProblem);
    }

    [Fact]
    public async Task FecharOPainelEAJanelaParaDeChamar()
    {
        var (viewModel, api, settings) = ViewModel(panelOpen: true, "Zimps/a");
        await viewModel.CheckAsync();
        var calls = api.Calls.Count;

        viewModel.IsPanelOpen = false;
        await viewModel.RefreshNowAsync();

        Assert.Equal(calls, api.Calls.Count);
        Assert.False(settings.ActionsPanelOpen);
    }

    [Fact]
    public async Task JanelaPropriaSozinhaContinuaLendo()
    {
        var (viewModel, api, _) = ViewModel(panelOpen: false, "Zimps/a");

        // Principal minimizada, janela própria num segundo monitor: segue valendo.
        viewModel.SetMainWindowActivity(isActive: false, isMinimized: true);
        viewModel.IsWindowOpen = true;
        await viewModel.RefreshNowAsync();

        Assert.NotEmpty(api.Calls);
    }

    [Theory]
    [InlineData(5, "5 s")]
    [InlineData(125, "2 min")]
    [InlineData(3600, "1 h")]
    [InlineData(4500, "1 h 15 min")]
    public void Duracao(int seconds, string expected)
        => Assert.Equal(expected, ActionsQueueViewModel.Duration(TimeSpan.FromSeconds(seconds)));
}
