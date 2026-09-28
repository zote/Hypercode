# Hypertree

App de macOS (C# + Avalonia) que lista os **worktrees** de um repositório git e, com
duplo-clique, abre uma nova janela do **iTerm2** na pasta do worktree rodando `claude`.

## Como é a tela

| Worktree | Branch | PR | Caminho |
|---|---|---|---|
| `main` (principal) | `main` | — | `/Users/…/repo` |
| `feat-login` | `feature/login` | `#412` | `/Users/…/repo-feat-login` |
| `hotfix` | `hotfix/crash` | `#418` (merged) | `/Users/…/repo-hotfix` |

- **Duplo-clique** (ou `Enter`) em uma linha → nova janela do iTerm2 em `cd <worktree> && claude`.
- **Botão direito** → ações do worktree. Detalhes abaixo.
- O campo **Filtrar** (`⌘F`) mostra só as linhas cujo nome, branch, número ou título do PR
  contém cada palavra digitada — `login 412` casa com a branch `feature/login` do PR `#412`.
  Não diferencia maiúsculas nem acentos. `Esc` limpa; `↓`/`Enter` desce para a lista.
- **Clicar no cabeçalho** de uma coluna ordena por ela; clicar de novo inverte (▲/▼). O
  principal fica sempre no topo e, ordenando por PR, as linhas sem PR vão para o fim. A
  escolha fica salva.
- **Novo worktree…** (`⌘N`) cria um worktree com branch nova ou a partir de um PR. Detalhes abaixo.
- O campo **Comando** deixa você trocar `claude` por outra coisa (`claude --resume`, `zsh`, etc.).
- O botão **?** abre a legenda das etiquetas, da coluna PR e das regras da limpeza.
  Parar o mouse sobre a etiqueta de uma linha explica só as dela.
- **Limpar concluídos** remove os worktrees com PR `merged` ou `closed` e poda os órfãos,
  sempre com confirmação. Detalhes abaixo.
- A lista **se atualiza sozinha**: worktree criado, apagado ou travado fora do app, troca de
  branch, commit, fetch, push e rebase aparecem em menos de um segundo. **Atualizar** (`⌘R`)
  relê tudo, inclusive os PRs. Detalhes abaixo.
- O app **monitora o remoto**: faz `git fetch --prune` e relê os PRs a cada 5 minutos, e avisa
  (notificação do macOS e sino na linha) quando checks, review, merge ou conflito mudam.
  Detalhes em [Monitoramento](#monitoramento).
- O caminho do repositório e o comando ficam salvos em
  `~/Library/Application Support/Hypertree/settings.json`.

## Requisitos

- macOS 11+
- [iTerm2](https://iterm2.com) — se não estiver instalado, o app cai para o `Terminal.app`
- [.NET SDK 8 ou superior](https://dotnet.microsoft.com/download) — só para compilar
- `git` (Xcode Command Line Tools já serve)
- [`gh`](https://cli.github.com) autenticado (`gh auth login`) — **opcional**, é o que preenche
  a coluna PR. Sem ele o app funciona normalmente, só deixa a coluna vazia.

## Rodar em modo desenvolvimento

```bash
cd hypertree
dotnet run
```

## Gerar o .app

```bash
cd hypertree
./build-app.sh              # detecta arm64/x64 automaticamente
open dist/Hypertree.app
cp -R dist/Hypertree.app /Applications/   # opcional
```

O bundle é autocontido: quem for usar não precisa ter o .NET instalado.

O ícone vem de `Assets/icon.icns`, que é gerado a partir de `Assets/icon.svg`. Depois de
editar o SVG, rode `Assets/make-icon.sh` e commite os dois arquivos — o script só usa o que
já vem com o macOS (`swift`, `sips` e `iconutil`).

## Permissão de automação

Na primeira vez que você der duplo-clique numa linha, o macOS pergunta se o Hypertree
pode controlar o iTerm2. É preciso aceitar. Se recusar sem querer:

**Ajustes do Sistema → Privacidade e Segurança → Automação → Hypertree → iTerm2.**

## Ícones

Os ícones de estado ficam **imediatamente à esquerda do nome do worktree**, numa calha de largura
fixa alinhada à direita — assim eles colam no nome e os nomes continuam alinhados entre linhas com
quantidades diferentes de ícone. Vêm de `git status --porcelain=v2 --branch`, lido em paralelo
(8 worktrees por vez). Os do PR ficam ao lado do número, e quando não há PR a coluna fica vazia,
sem marcador. Todo ícone tem tooltip.

Os desenhos são os [Octicons](https://primer.style/octicons/) do GitHub e as cores seguem os papéis
do Primer, com um tom para o tema claro e outro para o escuro: onde o GitHub mostra a mesma
informação, o ícone e a cor são os mesmos que ele usa.

| Ícone (Octicon) | Estado | Origem |
|---|---|---|
| `diff-modified` âmbar | Alterações não commitadas | `status` tem linhas fora do cabeçalho |
| `alert` vermelho | Conflito não resolvido | linhas `u` no status |
| `stop` vermelho | Merge/rebase/cherry-pick/revert/bisect pausado | `MERGE_HEAD`, `rebase-merge/` etc. no git dir |
| `arrow-up` azul | Falta push | `branch.ab +N` |
| `arrow-down` azul | Falta pull | `branch.ab -N` |
| `git-compare` âmbar | Divergiu | `+N` e `-M` ao mesmo tempo |
| `upload` cinza | Nunca foi pushada | sem `branch.upstream` |
| `cloud-offline` cinza | Branch remota apagada | `branch.upstream` sem `branch.ab` (upstream `gone`) |
| `trash` cinza | Pode ser removido | candidato à limpeza |
| `git-pull-request` verde / `git-pull-request-draft` cinza / `git-merge` roxo / `git-pull-request-closed` vermelho | PR aberto / draft / mergeado / fechado | `state`, `isDraft` |
| `alert` âmbar | Precisa atualizar a partir da base | `mergeable=CONFLICTING` ou `mergeStateStatus=BEHIND` |
| `check` verde / `x` vermelho / `dot-fill` âmbar | Checks passando / falhando / rodando | `statusCheckRollup` |
| `check-circle` verde / `file-diff` vermelho / `code-review` cinza | Review aprovado / mudanças pedidas / aguardando | `reviewDecision` |
| `bell-fill` azul | O PR mudou desde a última olhada | transição vista pelo monitoramento |

Checks, review e "precisa rebase" só aparecem enquanto o PR está aberto. Se o `gh` instalado não
aceitar os campos novos, o app refaz a consulta com o conjunto básico e avisa no rodapé — a coluna
PR continua funcionando, só sem esses três.

O botão **?** abre a legenda com os desenhos de verdade, gerados da mesma fonte que a lista.

## Etiquetas

Vêm de `git worktree list --porcelain` e aparecem abaixo do nome:

| Etiqueta | Significado |
|---|---|
| `principal` | Worktree original, onde fica o `.git`. Não é removível. |
| `travado` | `git worktree lock`. Lock manual protege; lock de ferramenta (JSON com `owner`) não. |
| `órfão` | `prunable`: a pasta não existe mais. `git worktree prune` resolve. |
| `bare` | Repositório sem árvore de trabalho. |
| `draft` / `merged` / `closed` | Estado do PR, quando não está simplesmente aberto. |

## Menu de contexto

| Ação | O que faz | Habilitada quando |
|---|---|---|
| Abrir no iTerm2 rodando o comando | O mesmo que o duplo-clique | a pasta existe |
| Abrir o terminal | `cd` no worktree, sem rodar o **Comando** | a pasta existe |
| Retomar a sessão do claude | `claude --continue`: volta à última conversa daquele worktree | há sessão em `~/.claude/projects/<caminho>` |
| Revelar no Finder | `open <pasta>` | sempre |
| Abrir PR no navegador | `open <url do PR>` — e marca as mudanças do PR como vistas | a branch tem PR |
| Marcar como visto | tira o sino da linha | o PR mudou desde a última olhada (só aparece nesse caso) |
| Puxar do remoto (pull) | `git pull --ff-only` no worktree e relê o estado da linha | a branch tem upstream |
| Atualizar a partir da base… | `git fetch` da base do PR e `git merge` ou `git rebase` dela, à escolha | o PR aberto está atrás da base ou em conflito |
| Apagar o worktree… | `git worktree remove`, com confirmação | não é o principal nem bare |

### Em lote

A lista aceita seleção múltipla (⌘-clique e ⇧-clique), e o menu age sobre **todas as linhas
selecionadas**. Clicar com o botão direito numa linha fora da seleção seleciona só ela, como no
Finder. O duplo-clique, o Enter e o botão "Abrir no iTerm2" do rodapé seguem agindo numa linha só.

Com mais de uma linha, cada item diz em quantas vale — "Puxar do remoto (pull) (4)" — e fica
habilitado se ao menos uma o suporta; as demais ficam de fora e aparecem no relatório final
(funcionou / falhou / não se aplica). O progresso aparece no rodapé.

- **Puxar do remoto**: um `git fetch` por remoto e depois `git merge --ff-only @{upstream}` em
  cada worktree, 8 por vez. É o mesmo que um `git pull --ff-only` em cada um, mas sem os pulls
  paralelos disputando o lock das refs remotas.
- **Apagar os worktrees…**: uma confirmação só, listando todos. Um de cada vez, sem `--force`
  e sem destravar: o worktree sujo ou travado fica, e o relatório diz por quê.
- **Terminal, retomar o claude e PR no navegador**: uma janela por worktree. Acima de 3, pede
  confirmação antes de abrir.
- **Atualizar a partir da base…** não vira lote: merge ou rebase é uma escolha por worktree.

O Claude Code guarda as conversas em `~/.claude/projects/`, numa pasta com o caminho absoluto
do worktree trocando todo caractere que não é letra nem dígito por `-`. É isso que o app
consulta para habilitar o "Retomar".

**Puxar do remoto** é só fast-forward do upstream da própria branch: se ela divergiu, o git
recusa, nada muda e o motivo aparece no rodapé. Não resolve o `alert` âmbar — esse fala da
**base** do PR, e a branch normalmente já está em dia com o próprio remoto.

**Atualizar a partir da base** é o que resolve o `alert`. Usa a base real do PR (`baseRefName`),
não a `main` — PRs empilhados apontam para outra branch de feature —, comparada como
`<remoto>/<base>`, com o remoto do upstream da branch (ou `origin`). O fluxo:

1. Recusa se há alteração em arquivo versionado ou merge/rebase parado no meio. Nada de `--force`.
2. `git fetch` só da base, e conta quantos commits ela tem que a branch não tem.
3. Diálogo com a base, a distância e a escolha: **merge** (`git merge --no-edit`, preserva o
   histórico, gera commit de merge) ou **rebase** (`git rebase`, histórico linear, reescreve
   commits já publicados).
4. Em conflito o app não tenta resolver: o worktree fica onde o git parou e a linha ganha o
   `stop` de operação pausada. Depois de um rebase, avisa que o push precisa de
   `--force-with-lease`.

O tooltip do `alert` nomeia a base e quantos commits ela está à frente, medidos com as refs
locais (`git rev-list --left-right --count <remoto>/<base>...HEAD`) — sem fetch, então o número
pode estar defasado até o próximo fetch.

**Apagar** usa `git worktree remove` sem `--force`. Se o git recusar por alteração não commitada
ou arquivo não versionado, o app mostra o motivo e pergunta, num segundo diálogo, se é para forçar
— o que descarta esse trabalho de vez. Worktree travado não ganha essa opção: destrave antes. Órfão
é resolvido com `git worktree prune`. A branch local nunca é tocada.

## Limpar concluídos

Entram na limpeza os worktrees com PR **merged** ou **closed**, mais os **órfãos**.
Nunca entram o principal, os bare e os worktrees com **lock manual**.

Sobre o lock: ferramentas como o `supacode` gravam no motivo um JSON com `owner` e usam o
`git worktree lock` apenas como marcador de propriedade — não é um pedido seu de não mexer.
Esses entram na limpeza e o app roda `git worktree unlock` antes do `remove` (o git recusa
remover worktree travado). Lock manual — motivo em texto livre ou sem motivo — continua protegendo.

A remoção usa `git worktree remove` **sem `--force`**, de propósito: havendo alteração não
commitada o git recusa, e o app lista o que ficou de fora em vez de apagar trabalho.
A branch local não é tocada — só o worktree. Os órfãos são resolvidos com um único
`git worktree prune` ao final.

O app lista os candidatos com o motivo de cada um e pede confirmação antes de remover.

## Novo worktree

O diálogo tem dois modos:

- **Branch nova**: nome da branch + base (padrão: a branch default do remoto, `origin/HEAD`;
  o campo sugere as branches locais e remotas). Se a base é remota, o app faz `git fetch` só
  dela antes. Cria com `git worktree add --no-track -b <branch> <pasta> <base>` — sem upstream,
  para o primeiro push não ir para a `main` por engano.
- **A partir de um PR**: número, `#número` ou URL. O app consulta o PR com `gh pr view` e usa a
  branch dele (`headRefName`) com o mesmo nome:
  - PR do próprio repo → `fetch` da branch e `worktree add --track`, pronta para push;
  - PR de fork → `fetch <remoto> pull/<n>/head` direto para a branch local, sem upstream;
  - branch local já existente → usada como está (sem pull).

A pasta sugerida é `<pai do repo>/<repo>.worktrees/<branch>`, com `/` virando `-`
(`feature/login` → `meu-repo.worktrees/feature-login`). Dá para editar; apagar o campo volta
à sugestão.

**Arquivos não versionados** (`.env` e afins) são copiados do principal quando o repo tem um
`.worktreeinclude` na raiz — padrões no formato do `.gitignore`, o mesmo arquivo que o Claude
Code usa. Só é copiado o que casar com o `.worktreeinclude` **e** estiver ignorado pelo git.
Nada é sobrescrito. Sem o arquivo, nada é copiado.

```gitignore
# .worktreeinclude
.env*
appsettings.Development.json
```

Depois de criar, a lista recarrega com o novo selecionado e — se o checkbox estiver marcado,
o padrão — abre o terminal nele rodando o **Comando**. O checkbox fica salvo.

## Atualização automática

O app observa o git dir comum do repositório (o `.git` do principal) com `FileSystemWatcher`,
que no macOS usa o FSEvents: nada de polling, e o processador fica parado enquanto nada muda.
Os eventos são filtrados na chegada e agrupados por 800 ms, então um `git worktree add` ou um
`fetch` viram uma releitura só.

| O que mudou no git dir | O que o app relê |
|---|---|
| `worktrees/<nome>` criado ou apagado, `HEAD`, `locked`, `gitdir` dele, `HEAD` do principal | `git worktree list`; se a lista mudou, refaz as linhas e busca os PRs |
| `refs/heads/`, `refs/remotes/`, `packed-refs` (commit, fetch, push) | só o `git status` de cada linha |
| `MERGE_HEAD`, `rebase-merge/` e cia. | só o `git status` de cada linha |
| `objects/`, `logs/`, `index`, `FETCH_HEAD`, arquivos `.lock` | nada |

A releitura automática não trava os botões nem mexe no rodapé, e as linhas que não mudaram
guardam o PR e os ícones — a lista não pisca. Se o app está no meio de uma operação própria
(carregar, limpar, apagar), espera ela terminar.

Fica de fora, de propósito:

- **Edição de arquivo sem commit** (o ícone de "alterações não commitadas"). Detectar isso exigiria
  observar a árvore de trabalho inteira de cada worktree, e o `index` não serve de sinal: o próprio
  `git status` que o app roda pode regravá-lo, o que viraria um laço.
- **Mudança de PR no GitHub** (merge, checks, review): não passa pelo disco. Quem cobre é o
  [monitoramento](#monitoramento), por polling.

## Monitoramento

Com um repositório carregado, o app confere o remoto periodicamente — um ciclo por intervalo:

1. `git fetch --all --prune --no-write-fetch-head` no principal, sem gc automático e com
   `GIT_TERMINAL_PROMPT=0` (um app sem terminal não tem onde pedir senha). As refs remotas mudam
   e o watcher relê o `git status` das linhas: "falta pull" e "divergiu" passam a refletir o
   remoto, e a branch apagada depois do merge aparece com o `cloud-offline`.
2. Uma consulta GraphQL só (`gh api graphql`, 1 ponto do rate limit de 5000/h) com um alias por
   branch de worktree: `pullRequests(headRefName: …)` traz estado, draft, `mergeable`,
   `mergeStateStatus`, `reviewDecision` e os checks do último commit. Não depende de o PR estar
   entre os mais recentes do repositório. Se a consulta falhar, cai para o `gh pr list` dos 100
   mais recentes.

| Janela | Intervalo |
|---|---|
| ativa | `MonitorIntervalMinutes` (padrão 5) |
| em segundo plano | o triplo |
| minimizada | pausado; ao voltar, se o intervalo já passou, confere na hora |

O ciclo não começa com uma operação do app em andamento, e pull e "atualizar a partir da base"
esperam o fetch do monitoramento terminar — dois fetches no mesmo repositório disputam o lock das
refs. Um `git pull` seu, no terminal, bem na hora do fetch ainda pode esbarrar nele.

Sem `gh`, ou sem autenticação, a parte git continua; o problema aparece uma vez no rodapé
("Monitoramento: …"), não a cada ciclo, e a coluna PR fica como estava.

**Avisos.** O app guarda o último estado visto de cada PR em
`~/Library/Application Support/Hypertree/pull-requests.json` e só avisa na **transição**:

- checks passaram a falhar ou a passar;
- review aprovado ou pedindo mudanças;
- PR mergeado, fechado ou reaberto;
- conflito com a base apareceu, ou a base andou.

Cada transição vira uma notificação do macOS (acima de três PRs de uma vez, uma só), uma frase no
rodapé e o sino na linha, com a lista no tooltip. O sino fica até **Marcar como visto** ou abrir o
PR no navegador — e sobrevive a fechar o app. Como o estado fica gravado, o aviso não se repete ao
reabrir; o que mudou com o app fechado é avisado uma vez, no primeiro carregamento. PR visto pela
primeira vez não gera aviso. Enquanto o GitHub responde `UNKNOWN` na mergeabilidade (ele calcula
sob demanda), vale o último valor conhecido, para o conflito não "sumir e voltar".

A notificação sai por `display notification` do AppleScript, então o macOS a atribui ao Editor de
Script — é o que dá sem assinar o app.

Em `settings.json`:

| Chave | Padrão | |
|---|---|---|
| `MonitorIntervalMinutes` | `5` | `0` desliga o monitoramento |
| `NotifyPullRequestChanges` | `true` | `false` mantém o sino e o rodapé, sem notificação |

## Como os dados são obtidos

- **Worktrees**: `git -C <repo> worktree list --porcelain`
- **PRs**: `gh api graphql` com um `pullRequests(headRefName: …)` por branch de worktree (ver
  [Monitoramento](#monitoramento)); se falhar, `gh pr list --state all --limit 100 --json …`,
  casando `headRefName` com a branch do worktree. Havendo mais de um PR para a mesma branch,
  o app prefere o aberto; depois o merged; depois o fechado.
- **Terminal**: `osascript` com `tell application id "com.googlecode.iterm2"` → `create window with
  default profile` → `write text "cd '<path>' && claude"`. Sem iTerm2, usa `do script` no `Terminal.app`.

Um app aberto pelo Finder herda um `PATH` mínimo, então o Hypertree reconstrói o `PATH`
(incluindo `/opt/homebrew/bin` e `/usr/local/bin`) antes de chamar `git` e `gh`.

## Estrutura

```
hypertree/
├── Program.cs                  ponto de entrada Avalonia
├── App.axaml(.cs)              tema e janela principal
├── Views/MainWindow.axaml(.cs) UI e handlers (duplo-clique, menu, atalhos)
├── Views/CreateWorktreeWindow.axaml(.cs) diálogo "Novo worktree"
├── ViewModels/
│   ├── MainViewModel.cs        carregamento, filtro, ordenação, status, ações
│   ├── CreateWorktreeViewModel.cs  diálogo "Novo worktree"
│   └── WorktreeRow.cs          uma linha da lista
└── Services/
    ├── GitService.cs           parser do `worktree list --porcelain` e helpers de git
    ├── WorktreeCreator.cs      criação de worktree (branch nova / PR) e cópia do .worktreeinclude
    ├── GitHubService.cs        leitura dos PRs via gh
    ├── RepositoryWatcher.cs    observa o git dir e avisa o que precisa ser relido
    ├── PullRequestMemory.cs    último estado visto de cada PR e as transições
    ├── Notifier.cs             notificação do macOS via osascript
    ├── TerminalLauncher.cs     AppleScript p/ iTerm2 (fallback Terminal.app) + open
    ├── ClaudeSessions.cs       detecta sessão do Claude Code para o "Retomar"
    ├── ProcessRunner.cs        execução de processos com timeout
    ├── ExecutableLocator.cs    resolução de PATH/binários
    └── SettingsStore.cs        preferências em JSON
```
