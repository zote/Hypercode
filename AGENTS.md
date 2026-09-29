# AGENTS.md

Convenções para qualquer agente — ou pessoa — trabalhando neste repositório.

## Fluxo de trabalho

Todo trabalho segue o mesmo ciclo, sem exceção:

**1. Uma issue primeiro.** Nada começa sem uma issue descrevendo o problema ou a proposta.
Se ela ainda não existe, crie antes de escrever qualquer código:

```bash
gh issue create --title "..." --body "..."
```

**2. Branch e worktree próprios.** Nunca trabalhe direto na `main`, nem no worktree principal.
Cada issue ganha o seu, criados a partir da `main` atualizada:

```bash
git fetch origin
git worktree add -b claude/issue-<numero>-<slug> \
  .claude/worktrees/issue-<numero>-<slug> origin/main
```

**3. Encerre com um pull request.** O trabalho não termina no commit: termina com o PR aberto
contra a `main`, referenciando a issue. (Se a issue depende de um PR ainda aberto, veja a
seção seguinte.)

```bash
git push -u origin claude/issue-<numero>-<slug>
gh pr create --base main --title "..." --body "... Closes #<numero>"
```

## Issue que depende de um PR ainda aberto

Se a issue precisa de código de um PR que ainda não entrou na `main`, **não** crie a branch em
cima do commit dele e abra o PR contra a `main`: se o PR de cima for mergeado primeiro (com
squash), ele leva junto o commit do PR de baixo, que fica em conflito e redundante (#80/#81).
Nem abra o PR contra a branch do PR anterior à mão: ele acaba mergeado lá, e não na `main`
(#75).

Monte uma pilha com [`gh stack`](https://github.com/github/gh-stack), que encadeia as bases dos
PRs, rebaseia a pilha quando uma camada muda e faz o merge na ordem:

```bash
gh extension install github/gh-stack   # uma vez

# no worktree do PR de baixo, com a branch dele em checkout
gh stack init --adopt claude/issue-<anterior>-<slug>
gh stack add claude/issue-<numero>-<slug>     # nova camada, em cima da anterior
# ... commits da nova issue ...

gh stack submit --auto   # push e PRs encadeados; depois ajuste título, body e label
gh stack sync            # após mudar uma camada ou a main andar: rebase e push da pilha
gh stack merge <pr>      # mergeia o PR e todos os de baixo, na ordem
```

A pilha inteira vive num worktree só — o do PR de baixo; o `gh stack add` troca de branch
nele. Os nomes de branch continuam os da tabela abaixo, e cada PR segue referenciando a sua
issue.

## Nomes

| | Formato | Exemplo |
|---|---|---|
| Branch | `claude/issue-<numero>-<slug>` | `claude/issue-12-tool-lock` |
| Worktree | `.claude/worktrees/issue-<numero>-<slug>` | `.claude/worktrees/issue-12-tool-lock` |

O `<slug>` é curto e descreve o assunto, não a issue inteira.

## Commits

Conventional commits, em português:

```
fix(limpeza): distingue lock de ferramenta de lock manual

O corpo explica o porquê da mudança, não o que o diff já mostra.

Closes #12
```

Um commit por unidade de trabalho coerente. Não misture refatoração com correção.

## Pull request

O corpo do PR traz, nessa ordem: **o problema**, **a mudança** e **a verificação** — o que foi
testado e como. Se algo ficou de fora ou é uma decisão discutível, diga no PR em vez de deixar
para o review descobrir.

## Antes de abrir o PR

- `dotnet build` limpo, sem warning novo.
- A verificação que você descreveu no PR realmente rodou.
- O worktree não deixou lixo (`bin/`, `obj/`, arquivos temporários) no commit.

## Limpeza

Worktrees de trabalho concluído podem ser removidos depois do merge:

```bash
git worktree remove .claude/worktrees/issue-<numero>-<slug>
git worktree prune
```
