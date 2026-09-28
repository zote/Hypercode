# TODO

Lista de tarefas do que quero implementar no Hypertree.

## Pendências

- [ ] **Título do terminal aberto**: definir o título da janela/aba do terminal
      aberto a partir do worktree.
- [ ] **Menu de contexto nos itens da lista**: clique com o botão direito num
      item da lista exibe botões de ações. Ações iniciais:
  - [ ] Abrir o terminal
  - [ ] Apagar o worktree
  - [ ] Abrir terminal executando `claude` com resume da sessão anterior
  - [ ] Atualizar a branch do worktree
- [ ] **Atualização da lista de worktrees**: botão para recarregar a lista
      manualmente, ou atualização em background quase em tempo real — desde que
      não consuma muito processamento (avaliar file watcher em `.git/worktrees`
      em vez de polling).
- [ ] **Ícone do aplicativo**: criar um ícone para o app.
- [ ] **Nome no menu do macOS**: ajustar o nome exibido no menu do app
      (atualmente aparece `Avalonia Application`).
- [ ] **Ícones no padrão do GitHub**: sempre que possível, usar a mesma forma e
      cor que o GitHub usa para representar a mesma informação (ex.: estados de
      PR aberto/draft/merged/fechado, branch, checks — ver Octicons).

## Concluídas

- [x] **Filtro na lista de worktrees**: campo de texto que filtra a lista conforme
      digito, casando com o nome do worktree, a branch ou o PR.
- [x] **Ordenação da lista de worktrees**: opção para escolher o critério de
      ordenação da lista.
- [x] **Criar worktree**: opção para criar um worktree novo pela interface.
      ⚠️ Antes de implementar, fazer entrevista para definir o comportamento
      (origem da branch, nome, local, etc.).
      Definido na entrevista: branch nova a partir de base ou a partir de PR;
      pasta `<repo>.worktrees/<branch>`; recarrega e seleciona, abre o terminal
      (checkbox), copia arquivos do `.worktreeinclude`.
