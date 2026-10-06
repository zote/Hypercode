using System.Text.Json;
using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

/// <summary>Agir sobre a fila (#133): cancelar e forçar cancelamento de um run pelo painel.</summary>
public sealed class ActionsRunActionsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    // ── Permissão de escrita ────────────────────────────────────────────────

    private static WriteAccess Access(object repo, string? scopes)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(repo));
        return ActionsQueue.ParseWriteAccess(document.RootElement, "Zimps/a", scopes);
    }

    [Fact]
    public void ContaQueSoLeNaoCancela()
    {
        var access = Access(new { @private = true, permissions = new { admin = false, maintain = false, push = false, pull = true } }, "repo");

        Assert.False(access.CanWrite);
        Assert.Contains("só lê Zimps/a", access.Reason);
    }

    [Fact]
    public void TokenSemEscopoRepoNaoCancela()
    {
        var access = Access(new { @private = true, permissions = new { push = true } }, "gist, read:org");

        Assert.False(access.CanWrite);
        Assert.Contains("gh auth refresh -s repo", access.Reason);
    }

    [Fact]
    public void PublicRepoBastaEmRepositorioPublico()
    {
        Assert.True(Access(new { @private = false, permissions = new { push = true } }, "public_repo, read:org").CanWrite);
        Assert.False(Access(new { @private = true, permissions = new { push = true } }, "public_repo, read:org").CanWrite);
    }

    [Fact]
    public void TokenSemCabecalhoDeEscopoFicaSoComAPermissaoDaConta()
    {
        // Fine-grained não manda X-OAuth-Scopes: o que dá para saber de antemão é a conta.
        Assert.True(Access(new { @private = true, permissions = new { push = true } }, null).CanWrite);
        Assert.True(Access(new { @private = true, permissions = new { maintain = true } }, "repo").CanWrite);
    }

    [Fact]
    public void RespostaGuardaOsEscoposDoToken()
    {
        var withScopes = ApiResponse.Parse("HTTP/2.0 200 OK\nX-Oauth-Scopes: gist, read:org, repo\n\n{}", null);
        var without = ApiResponse.Parse("HTTP/2.0 200 OK\n\n{}", null);

        Assert.Equal("gist, read:org, repo", withScopes.Scopes);
        Assert.Null(without.Scopes);
    }

    // ── O que dizer da resposta ─────────────────────────────────────────────

    [Fact]
    public void CancelamentoAceitoNaoTemMensagem()
        => Assert.Null(ActionsQueueService.DescribeCancel(new ApiResponse(202, "{}", null, null), "Zimps/a"));

    [Fact]
    public void RunQueJaTerminouDizIssoEmVezDeFalharCalado()
        => Assert.Equal(
            "O run já terminou — não há mais o que cancelar.",
            ActionsQueueService.DescribeCancel(new ApiResponse(409, "{\"message\":\"Cannot cancel a workflow run that is completed.\"}", null, "HTTP 409"), "Zimps/a"));

    [Fact]
    public void SemPermissaoNoCliqueDizODoRepositorio()
        => Assert.Contains("Zimps/a", ActionsQueueService.DescribeCancel(new ApiResponse(403, "{}", null, "HTTP 403"), "Zimps/a"));

    // ── A confirmação ───────────────────────────────────────────────────────

    private static RunTarget Target(string repository = "Zimps/a", long id = 1)
        => new(repository, id, "CI", "feat: painel da fila", 482, "feat/x", 12, null, null, null);

    [Fact]
    public void ConfirmacaoNomeiaRunWorkflowRepositorioEPr()
    {
        var (_, headline, body, confirm) = ActionsQueueViewModel.CancelConfirmation(Target(), force: false);

        Assert.Equal("Cancelar o run CI #482?", headline);
        Assert.Contains("feat: painel da fila", body);
        Assert.Contains("CI #482 · Zimps/a", body);
        Assert.Contains("PR #12 (feat/x)", body);
        // O job não cancela sozinho: o texto diz que vai o run inteiro.
        Assert.Contains("run inteiro", body);
        Assert.Equal("Cancelar o run", confirm);
    }

    [Fact]
    public void ForcarAvisaQuePulaALimpeza()
    {
        var (_, headline, body, confirm) = ActionsQueueViewModel.CancelConfirmation(Target(), force: true);

        Assert.StartsWith("Forçar", headline);
        Assert.Contains("if: always()", body);
        Assert.Contains("limpeza", body);
        Assert.Equal("Forçar cancelamento", confirm);
    }

    // ── O view model, com a API de mentira ──────────────────────────────────

    /// <summary>GET e POST de mentira: caminho → (status, corpo). Guarda o que foi chamado.</summary>
    private sealed class FakeApi
    {
        public Dictionary<string, (int Status, object Body)> Routes { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> PostStatus { get; } = new(StringComparer.Ordinal);

        public List<string> Gets { get; } = new();

        public List<string> Posts { get; } = new();

        public Func<string, Task>? Gate { get; set; }

        public async Task<ApiResponse> GetAsync(string path, CancellationToken cancellationToken)
        {
            lock (Gets) Gets.Add(path);
            if (Gate is { } gate) await gate(path);
            return Routes.TryGetValue(path, out var route)
                ? new ApiResponse(route.Status, JsonSerializer.Serialize(route.Body), null, route.Status is >= 200 and < 300 ? null : "erro")
                : new ApiResponse(404, "{\"message\":\"Not Found\"}", null, "gh: Not Found (HTTP 404)");
        }

        public Task<ApiResponse> PostAsync(string path, CancellationToken cancellationToken)
        {
            Posts.Add(path);
            var status = PostStatus.TryGetValue(path, out var value) ? value : 202;
            return Task.FromResult(new ApiResponse(status, "{}", null, status is >= 200 and < 300 ? null : $"HTTP {status}"));
        }

        public void Repository(string repository, bool push = true)
            => Routes[$"repos/{repository}"] = (200, new { @private = true, permissions = new { admin = false, push, pull = true } });

        public void QueuedRun(string repository, long run, long job)
        {
            Routes[$"repos/{repository}/actions/runs?status=queued&per_page=100"] = (200, new
            {
                total_count = 1,
                workflow_runs = new[]
                {
                    new
                    {
                        id = run, name = "CI", display_title = "feat: painel", run_number = 7, status = "queued",
                        head_branch = "feat/x", created_at = "2026-10-01T11:58:00Z", html_url = $"https://github.com/{repository}/actions/runs/{run}",
                        pull_requests = Array.Empty<object>(),
                    },
                },
            });
            Routes[$"repos/{repository}/actions/runs?status=in_progress&per_page=100"] = (200, new { total_count = 0, workflow_runs = Array.Empty<object>() });
            Routes[$"repos/{repository}/actions/runs/{run}/jobs?per_page=100"] = (200, new
            {
                total_count = 1,
                jobs = new[] { new { id = job, name = "build", workflow_name = "CI", status = "queued", labels = new[] { "self-hosted" }, created_at = "2026-10-01T11:58:00Z" } },
            });
        }

        public void Finished(string repository)
        {
            Routes[$"repos/{repository}/actions/runs?status=queued&per_page=100"] = (200, new { total_count = 0, workflow_runs = Array.Empty<object>() });
            Routes[$"repos/{repository}/actions/runs?status=in_progress&per_page=100"] = (200, new { total_count = 0, workflow_runs = Array.Empty<object>() });
        }
    }

    private static (ActionsQueueViewModel ViewModel, FakeApi Api) ViewModel(params string[] repositories)
    {
        var settings = new Settings { ActionsPanelOpen = true };
        settings.ActionsRepositories.AddRange(repositories);
        var api = new FakeApi();
        api.Routes["orgs/Zimps/actions/runner-groups?per_page=100"] = (200, new { total_count = 0, runner_groups = Array.Empty<object>() });
        var viewModel = new ActionsQueueViewModel(settings, () => { }, api.GetAsync, () => Start, api.PostAsync, () => Task.CompletedTask);
        return (viewModel, api);
    }

    private static RunTarget QueuedTarget(ActionsQueueViewModel viewModel)
        => Assert.Single(Assert.Single(viewModel.Lanes).Jobs).Run!;

    [Fact]
    public void JobDaFilaApontaParaORunDele()
    {
        var job = new ActionsJob(10, 1, "Zimps/a", "build", "CI", "queued", new[] { "x" }, Start, null, null, null, "main", null, null);
        var snapshot = new ActionsSnapshot(
            Array.Empty<ActionsRunner>(), new[] { job }, Array.Empty<ActionsWaitingRun>(),
            new[] { new ActionsRun(1, "Zimps/a", "CI", "feat: x", 3, "main", null, null) },
            Array.Empty<string>(), Array.Empty<string>(), null, Start);

        var lane = Assert.Single(ActionsQueueViewModel.BuildLanes(snapshot, Start, (repository, id) =>
            new RunTarget(repository, id, "CI", "feat: x", 3, "main", null, null, null, null)));

        Assert.Equal(1, lane.Jobs.Single().Run!.Id);
    }

    [Fact]
    public async Task CicloLeTituloENumeroDoRun()
    {
        var (viewModel, api) = ViewModel("Zimps/a");
        api.Repository("Zimps/a");
        api.QueuedRun("Zimps/a", run: 1, job: 10);

        await viewModel.CheckAsync();

        var run = QueuedTarget(viewModel);
        Assert.Equal((1L, "feat: painel", 7, "CI #7"), (run.Id, run.Title, run.Number, run.Name));
        Assert.True(run.CanCancel);
        Assert.True(run.CanForceCancel);
    }

    [Fact]
    public async Task CancelarPedeORunDoRepositorioCertoERele()
    {
        // O painel é multi-repo: o run de Zimps/b se cancela em Zimps/b, qualquer que seja a aba aberta.
        var (viewModel, api) = ViewModel("Zimps/a", "Zimps/b");
        api.Repository("Zimps/a");
        api.Repository("Zimps/b");
        api.Finished("Zimps/a");
        api.QueuedRun("Zimps/b", run: 5, job: 50);
        await viewModel.CheckAsync();
        var reads = api.Gets.Count;

        var problem = await viewModel.CancelRunAsync(QueuedTarget(viewModel), force: false);
        await viewModel.Reread;

        Assert.Null(problem);
        Assert.Equal(new[] { "repos/Zimps/b/actions/runs/5/cancel" }, api.Posts);
        // Releu sem esperar o ciclo — e não perguntou a permissão de novo.
        Assert.True(api.Gets.Count > reads);
        Assert.DoesNotContain("repos/Zimps/b", api.Gets.Skip(reads));
    }

    [Fact]
    public async Task CancelamentoPedidoDeixaSoOForcar()
    {
        var (viewModel, api) = ViewModel("Zimps/a");
        api.Repository("Zimps/a");
        api.QueuedRun("Zimps/a", run: 1, job: 10);
        await viewModel.CheckAsync();

        await viewModel.CancelRunAsync(QueuedTarget(viewModel), force: false);
        await viewModel.Reread;

        // O GitHub ainda não parou o run: cancelar de novo não adianta, forçar é a saída.
        var run = QueuedTarget(viewModel);
        Assert.True(run.IsCancelRequested);
        Assert.False(run.CanCancel);
        Assert.True(run.CanForceCancel);

        await viewModel.CancelRunAsync(run, force: true);
        Assert.Equal("repos/Zimps/a/actions/runs/1/force-cancel", api.Posts[^1]);

        // Saiu da leitura: o pedido está cumprido.
        api.Finished("Zimps/a");
        await viewModel.RefreshNowAsync();
        Assert.Empty(viewModel.Lanes);
    }

    [Fact]
    public async Task RunQueTerminouNoMeioTempoDaErroClaroESomeDaLista()
    {
        var (viewModel, api) = ViewModel("Zimps/a");
        api.Repository("Zimps/a");
        api.QueuedRun("Zimps/a", run: 1, job: 10);
        await viewModel.CheckAsync();
        var run = QueuedTarget(viewModel);

        api.Finished("Zimps/a");
        api.PostStatus["repos/Zimps/a/actions/runs/1/cancel"] = 409;
        var problem = await viewModel.CancelRunAsync(run, force: false);
        await viewModel.Reread;

        Assert.Equal("O run já terminou — não há mais o que cancelar.", problem);
        Assert.Empty(viewModel.Lanes);
    }

    [Fact]
    public async Task SemEscritaOsItensSaemDesabilitadosComOMotivo()
    {
        var (viewModel, api) = ViewModel("Zimps/a");
        api.Repository("Zimps/a", push: false);
        api.QueuedRun("Zimps/a", run: 1, job: 10);

        await viewModel.CheckAsync();

        var run = QueuedTarget(viewModel);
        Assert.False(run.CanCancel);
        Assert.False(run.CanForceCancel);
        Assert.Contains("permissão de escrita", run.CancelBlocked);
    }

    [Fact]
    public async Task RecusaPorPermissaoNoCliqueFazRelerAPermissao()
    {
        var (viewModel, api) = ViewModel("Zimps/a");
        api.Repository("Zimps/a");
        api.QueuedRun("Zimps/a", run: 1, job: 10);
        await viewModel.CheckAsync();
        var reads = api.Gets.Count;

        api.PostStatus["repos/Zimps/a/actions/runs/1/cancel"] = 403;
        api.Repository("Zimps/a", push: false);
        var problem = await viewModel.CancelRunAsync(QueuedTarget(viewModel), force: false);
        await viewModel.Reread;

        Assert.NotNull(problem);
        Assert.Contains("repos/Zimps/a", api.Gets.Skip(reads));
        Assert.False(QueuedTarget(viewModel).CanCancel);
    }

    [Fact]
    public async Task AtualizarDuranteUmaLeituraLeDeNovoDepoisDela()
    {
        var (viewModel, api) = ViewModel("Zimps/a");
        api.Repository("Zimps/a");
        api.QueuedRun("Zimps/a", run: 1, job: 10);

        var release = new TaskCompletionSource();
        api.Gate = path => path.Contains("status=queued", StringComparison.Ordinal) ? release.Task : Task.CompletedTask;

        var first = viewModel.CheckAsync();
        await viewModel.RefreshNowAsync();
        Assert.True(viewModel.IsLoading);

        api.Gate = null;
        api.Finished("Zimps/a");
        release.SetResult();
        await first;

        // A leitura em curso via o run; a que veio depois, não.
        Assert.Equal(2, api.Gets.Count(path => path.Contains("status=queued", StringComparison.Ordinal)));
        Assert.Empty(viewModel.Lanes);
    }
}
