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
contra a `main`, referenciando a issue.

```bash
git push -u origin claude/issue-<numero>-<slug>
gh pr create --base main --title "..." --body "... Closes #<numero>"
```

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
