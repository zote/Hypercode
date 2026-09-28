# Hypercode

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
- **Novo worktree…** (`⌘N`) cria um worktree com branch nova, a partir de um PR ou a partir de uma issue. Detalhes abaixo.
- **Configurações…** (`⌘,`, no menu do app, ou o botão **⚙** ao lado do **?**) reúne as
  preferências: o **Comando** que o duplo-clique roda — troque `claude` por outra coisa
  (`claude --resume`, `zsh`, etc.) —, o perfil do monitoramento e a atribuição de issue. Cada
  campo vale na hora e fica salvo; não há Salvar.
- O botão **?** abre a legenda das etiquetas, da coluna PR e das regras da limpeza.
  Parar o mouse sobre a etiqueta de uma linha explica só as dela.
- **Limpar concluídos** remove os worktrees com PR `merged` ou `closed` e poda os órfãos,
  sempre com confirmação. Detalhes abaixo.
- A lista **se atualiza sozinha**: worktree criado, apagado ou travado fora do app, troca de
  branch, commit, fetch, push e rebase aparecem em menos de um segundo. **Atualizar** (`⌘R`)
  relê tudo, inclusive os PRs. Detalhes abaixo.
- O app **monitora o remoto**: faz `git fetch --prune` e relê os PRs com uma cadência por
  worktree — de 30 s com a CI rodando a 30 min na branch nunca pushada —, e avisa (notificação
  do macOS e sino na linha) quando checks, review, merge ou conflito mudam. O perfil fica nas
  configurações. Detalhes em [Monitoramento](#monitoramento).
- O caminho do repositório e o comando ficam salvos em
  `~/Library/Application Support/Hypercode/settings.json`. Quem vem da época em que o app se
  chamava Hypertree não perde nada: na primeira abertura, se essa pasta não existe, o app copia
  a antiga `~/Library/Application Support/Hypertree/` para ela (a antiga fica intacta).

## Requisitos

- macOS 11+
- [iTerm2](https://iterm2.com) — se não estiver instalado, o app cai para o `Terminal.app`
- [.NET SDK 8 ou superior](https://dotnet.microsoft.com/download) — só para compilar
- `git` (Xcode Command Line Tools já serve)
- [`gh`](https://cli.github.com) autenticado (`gh auth login`) — **opcional**, é o que preenche
  a coluna PR. Sem ele o app funciona normalmente, só deixa a coluna vazia.

## Rodar em modo desenvolvimento

```bash
cd hypercode
dotnet run
```

## Gerar o .app

```bash
cd hypercode
./build-app.sh              # detecta arm64/x64 automaticamente
open dist/Hypercode.app
cp -R dist/Hypercode.app /Applications/   # opcional
```

O bundle é autocontido: quem for usar não precisa ter o .NET instalado.

O ícone vem de `Assets/icon.icns`, que é gerado a partir de `Assets/icon.svg`. Depois de
editar o SVG, rode `Assets/make-icon.sh` e commite os dois arquivos — o script só usa o que
já vem com o macOS (`swift`, `sips` e `iconutil`).

## Permissão de automação

Na primeira vez que você der duplo-clique numa linha, o macOS pergunta se o Hypercode
pode controlar o iTerm2. É preciso aceitar. Se recusar sem querer:

**Ajustes do Sistema → Privacidade e Segurança → Automação → Hypercode → iTerm2.**

O bundle id é `app.zimps.hypercode`. A versão antiga (`app.zimps.hypertree`) é outro app para o
macOS, então quem atualiza vê o pedido de permissão de novo — e pode remover a entrada
"Hypertree" da mesma tela.

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
| Travar o worktree… | `git worktree lock`, com um motivo opcional | vinculado, destravado e com a pasta no lugar (só aparece nesse caso) |
| Destravar o worktree… | `git worktree unlock`, depois de uma confirmação que mostra o worktree e a trava | travado (só aparece nesse caso) e uma linha só selecionada |
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

**Travar** pede o motivo uma vez e o aplica a todas as linhas selecionadas que ainda não estão
travadas. **Destravar** fica desabilitado com mais de uma linha: cada destrava passa por uma
confirmação com o nome, a branch e o caminho do worktree e com o tipo da trava (manual ou de
ferramenta), o dono e o motivo. Nela o padrão é **Cancelar** — o Enter não destrava por engano.

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

O diálogo tem três modos:

- **Branch nova**: nome da branch + base (padrão: a branch default do remoto, `origin/HEAD`;
  o campo sugere as branches locais e remotas). Se a base é remota, o app faz `git fetch` só
  dela antes. Cria com `git worktree add --no-track -b <branch> <pasta> <base>` — sem upstream,
  para o primeiro push não ir para a `main` por engano.
- **A partir de um PR**: número, `#número` ou URL. O app consulta o PR com `gh pr view` e usa a
  branch dele (`headRefName`) com o mesmo nome:
  - PR do próprio repo → `fetch` da branch e `worktree add --track`, pronta para push;
  - PR de fork → `fetch <remoto> pull/<n>/head` direto para a branch local, sem upstream;
  - branch local já existente → usada como está (sem pull).
- **A partir de uma issue**: número, `#número` ou URL. O app consulta a issue com
  `gh issue view`, mostra título e estado e sugere a branch `claude/issue-<n>-<slug>` — o
  padrão do `AGENTS.md`, com o slug tirado das primeiras palavras do título (sem acento, sem
  artigos e preposições). Branch e base são editáveis; para o git é uma branch nova como no
  primeiro modo. Issue fechada é aceita com aviso; número de PR é recusado (use o modo PR).
  Se a branch já existe localmente o botão Criar fica desabilitado; se existe só no remoto,
  aparece um aviso. Com **Atribuir a issue a mim** ligado nas configurações (desligado por
  padrão), depois de criar o app confere os assignees e, se o usuário do `gh` não está entre
  eles, roda `gh issue edit <n> --add-assignee @me` — acrescenta, não substitui. Se o GitHub
  recusar (sem permissão de escrita no repo), o worktree fica criado e o motivo vai para o
  rodapé; já atribuído, nada acontece.

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

Com um repositório carregado, o app confere o remoto por conta própria. Cada worktree tem a sua
cadência, que sai do estado do PR dele; um agendador em memória acorda a cada 10 s, vê quem venceu
e manda **uma** consulta cobrindo todos eles — nunca uma por worktree.

| Estado do worktree | Cadência |
|---|---|
| PR aberto com Action rodando ou na fila | 30 s |
| PR aberto, CI parada, sem aprovação | 3 min |
| PR aberto aprovado, ou draft | 10 min |
| Branch pushada sem PR (o PR pode nascer pela web) | 5 min |
| PR mergeado ou fechado | 20 min |
| Branch nunca pushada, e o principal | 30 min |

O que roda:

1. **PRs**: uma consulta GraphQL (`gh api graphql`) com um alias por branch vencida:
   `pullRequests(headRefName: …)` traz estado, draft, `mergeable`, `mergeStateStatus`,
   `reviewDecision` e os checks do último commit. O GitHub cobra **1 ponto a cada ~10 branches**
   (medido com `rateLimit.cost`: 1 ponto até 10, 4 com 40), então quem vence nos próximos 25 % da
   sua cadência pega carona no lote, até 10 branches, sem custar mais. Se a consulta falhar, cai
   para o `gh pr list` dos 100 mais recentes.
2. **Fetch**: `git fetch --all --prune --no-write-fetch-head` no principal, a cada 10 min, sem gc
   automático e com `GIT_TERMINAL_PROMPT=0`. Não gasta cota da API. As refs remotas mudam e o
   watcher relê o `git status` das linhas: "falta pull" e "divergiu" passam a refletir o remoto, e
   a branch apagada depois do merge aparece com o `cloud-offline`.

**Push promove.** O `git push` escreve em `refs/remotes/`, o watcher relê o status, e o app vê o
push: branch que **ganhou upstream** agora passa 10 min sendo conferida a cada 30 s; push numa
branch que já tinha upstream e segue sem PR, 3 min. É o momento em que um PR está para nascer, e
não custa chamada a mais — a branch só entra em lotes mais cedo. PR criado **pela web** numa branch
pushada dias antes não deixa sinal local: é o que a faixa de 5 min cobre, e voltar o foco para a
janela confere na hora o que venceu.

**Perfil**, nas configurações (`⌘,`), multiplica todas as cadências: econômico 2×, equilibrado 1×,
agressivo ½, ou desligado.

| Janela | Cadências |
|---|---|
| ativa | as da tabela, vezes o perfil |
| em segundo plano | o triplo |
| minimizada | pausado; ao voltar o foco, o que venceu é conferido na hora |

**Cota.** A resposta do GraphQL traz o `rateLimit` junto, de graça. A cota é de 5000 pontos/h e é
**da conta**, não do app — outras ferramentas gastam dela. Passados 60 % de uso, o agendador recua:
2× até 80 %, 4× até 90 %, 10× acima disso, até o horário do reset. Enquanto recua, o rodapé diz
("Monitor em recuo · …"), para a coluna PR parada não parecer travamento. O fetch não recua.

O ciclo não começa com uma operação do app em andamento, e pull e "atualizar a partir da base"
esperam o fetch do monitoramento terminar — dois fetches no mesmo repositório disputam o lock das
refs. Um `git pull` seu, no terminal, bem na hora do fetch ainda pode esbarrar nele.

Sem `gh`, ou sem autenticação, a parte git continua; o problema aparece uma vez no rodapé
("Monitoramento: …"), não a cada ciclo, e a coluna PR fica como estava.

**Avisos.** O app guarda o último estado visto de cada PR em
`~/Library/Application Support/Hypercode/pull-requests.json` e só avisa na **transição**:

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
| `MonitorProfile` | `balanced` | `off`, `economical`, `balanced` ou `aggressive` — o mesmo do seletor das configurações |
| `NotifyPullRequestChanges` | `true` | `false` mantém o sino e o rodapé, sem notificação |

A chave antiga `MonitorIntervalMinutes`, do intervalo fixo, é migrada na leitura: `0` vira `off`,
qualquer outro valor vira `balanced`. Ela some do arquivo no próximo salvamento.

## Como os dados são obtidos

- **Worktrees**: `git -C <repo> worktree list --porcelain`
- **PRs**: `gh api graphql` com um `pullRequests(headRefName: …)` por branch de worktree (ver
  [Monitoramento](#monitoramento)); se falhar, `gh pr list --state all --limit 100 --json …`,
  casando `headRefName` com a branch do worktree. Havendo mais de um PR para a mesma branch,
  o app prefere o aberto; depois o merged; depois o fechado.
- **Terminal**: `osascript` com `tell application id "com.googlecode.iterm2"` → `create window with
  default profile` → `write text "cd '<path>' && claude"`. Sem iTerm2, usa `do script` no `Terminal.app`.

Um app aberto pelo Finder herda um `PATH` mínimo, então o Hypercode reconstrói o `PATH`
(incluindo `/opt/homebrew/bin` e `/usr/local/bin`) antes de chamar `git` e `gh`.

## Estrutura

```
hypercode/
├── Program.cs                  ponto de entrada Avalonia
├── App.axaml(.cs)              tema e janela principal
├── Views/MainWindow.axaml(.cs) UI e handlers (duplo-clique, menu, atalhos)
├── Views/CreateWorktreeWindow.axaml(.cs) diálogo "Novo worktree"
├── Views/SettingsWindow.axaml(.cs) tela de configurações (⌘,)
├── ViewModels/
│   ├── MainViewModel.cs        carregamento, filtro, ordenação, status, ações
│   ├── CreateWorktreeViewModel.cs  diálogo "Novo worktree"
│   └── WorktreeRow.cs          uma linha da lista
└── Services/
    ├── GitService.cs           parser do `worktree list --porcelain` e helpers de git
    ├── WorktreeCreator.cs      criação de worktree (branch nova / PR / issue) e cópia do .worktreeinclude
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
