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
| `trash` cinza | Pode ser removido | candidato à limpeza |
| `git-pull-request` verde / `git-pull-request-draft` cinza / `git-merge` roxo / `git-pull-request-closed` vermelho | PR aberto / draft / mergeado / fechado | `state`, `isDraft` |
| `alert` âmbar | Precisa rebase | `mergeable=CONFLICTING` ou `mergeStateStatus=BEHIND` |
| `check` verde / `x` vermelho / `dot-fill` âmbar | Checks passando / falhando / rodando | `statusCheckRollup` |
| `check-circle` verde / `file-diff` vermelho / `code-review` cinza | Review aprovado / mudanças pedidas / aguardando | `reviewDecision` |

Checks, review e "precisa rebase" só aparecem enquanto o PR está aberto. Se o `gh` instalado não
aceitar os campos novos, o app refaz a consulta com o conjunto básico e avisa no rodapé — a coluna
PR continua funcionando, só sem esses três.

O botão **?** abre a legenda com os desenhos de verdade, gerados da mesma fonte que a lista.

## Etiquetas

Vêm de `git worktree list --porcelain` e aparecem abaixo do nome:

| Etiqueta | Significado |
|---|---|
| `principal` | Worktree original, onde fica o `.git`. Não é removível. |
| `travado` | `git worktree lock` foi aplicado. O prune ignora, e a limpeza também. |
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
| Abrir PR no navegador | `open <url do PR>` | a branch tem PR |
| Atualizar a branch | `git pull --ff-only` no worktree e relê o estado da linha | a branch tem upstream |
| Apagar o worktree… | `git worktree remove`, com confirmação | não é o principal nem bare |

O Claude Code guarda as conversas em `~/.claude/projects/`, numa pasta com o caminho absoluto
do worktree trocando todo caractere que não é letra nem dígito por `-`. É isso que o app
consulta para habilitar o "Retomar".

**Atualizar** é só fast-forward: se a branch divergiu do upstream, o git recusa, nada muda e o
motivo aparece no rodapé. Merge ou rebase ficam por sua conta.

**Apagar** usa `git worktree remove` sem `--force`. Se o git recusar por alteração não commitada
ou arquivo não versionado, o app mostra o motivo e pergunta, num segundo diálogo, se é para forçar
— o que descarta esse trabalho de vez. Worktree travado não ganha essa opção: destrave antes. Órfão
é resolvido com `git worktree prune`. A branch local nunca é tocada.

## Limpar concluídos

Entram na limpeza os worktrees com PR **merged** ou **closed**, mais os **órfãos**.
Nunca entram o principal, os bare e os travados.

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

## Como os dados são obtidos

- **Worktrees**: `git -C <repo> worktree list --porcelain`
- **PRs**: `gh pr list --state all --limit 200 --json number,headRefName,state,title,url,isDraft`,
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
    ├── TerminalLauncher.cs     AppleScript p/ iTerm2 (fallback Terminal.app) + open
    ├── ClaudeSessions.cs       detecta sessão do Claude Code para o "Retomar"
    ├── ProcessRunner.cs        execução de processos com timeout
    ├── ExecutableLocator.cs    resolução de PATH/binários
    └── SettingsStore.cs        preferências em JSON
```
