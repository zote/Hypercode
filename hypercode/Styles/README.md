# Design system

Tokens e estilos visuais do Hypercode, baseados nas diretrizes da Apple para macOS
([HIG](https://developer.apple.com/design/human-interface-guidelines) e
[Design Resources](https://developer.apple.com/design/resources/#macos-apps)). Plano
geral na issue #63; as decisões de base (fonte Inter, `FluentTheme` compacto, accent do
sistema) e os motivos estão na #64.

| Arquivo | O que tem | Onde entra |
|---|---|---|
| `Tokens.axaml` | cores (claro/escuro), tamanhos de fonte, espaçamento, raios | `Application.Resources` |
| `FluentOverrides.axaml` | as chaves de recurso do Fluent apontando para os tokens | `Application.Resources`, depois dos tokens |
| `Icons.axaml` | os ícones (`Icon.*`), como geometria no quadro de 16x16 | `Application.Resources` |
| `Dialog.axaml` | o tema do `DialogFrame`, o layout padrão de diálogo | `Application.Resources` |
| `Typography.axaml` | classes de texto (`title1`, `body`, `headline`…) | `Application.Styles`, depois do `FluentTheme` |
| `Controls.axaml` | variantes e estados que o Fluent não tem (botão destrutivo, lista `table`, anel de foco…) | `Application.Styles`, depois do `FluentTheme` |

`FluentOverrides.axaml` e `Icons.axaml` não se editam à mão: são gerados pelos scripts de
`tools/` (uso em `tools/README.md`).

Para ver tudo em claro e escuro lado a lado, abra a galeria de controles:

```bash
dotnet run --project hypercode -- --galeria
```

Os argumentos `escuro` (o foco vai para a lista escura), `foco` (anel de foco num botão) e
`tooltip` mudam o estado inicial, para capturar a tela sem usar mouse e teclado.

## Regra geral

View não usa valor visual literal: cor, `FontSize`, `CornerRadius`, margem e padding vêm
daqui. Sempre `DynamicResource` — é o que faz a cor trocar quando o macOS muda entre
claro e escuro. Quando um valor não existir, crie o token aqui em vez de escrever o
número na view; se for mesmo um caso único, deixe um comentário explicando.

```xml
<Border Background="{DynamicResource Brush.Background.Danger}"
        CornerRadius="{DynamicResource Radius.Control}"
        Padding="{DynamicResource Padding.Box}">
  <TextBlock Classes="body" Foreground="{DynamicResource Brush.Text.Danger}"
             Text="{Binding ErrorMessage}" />
</Border>
```

## Cores

Todas são `SolidColorBrush`, com uma versão em `Light` e outra em `Dark`. O teste
`TokensTests` garante que as duas variantes têm as mesmas chaves e que todo token de
texto passa em WCAG AA (4,5:1) sobre os fundos.

**Texto**

| Token | Uso |
|---|---|
| `Brush.Label.Primary` | texto principal |
| `Brush.Label.Secondary` | texto de apoio: subtítulo, metadado, legenda |
| `Brush.Label.Tertiary` | só texto desabilitado e placeholder — **não passa em AA** |
| `Brush.Label.Quaternary` | só decoração — **não é para texto** |
| `Brush.Text.Link` | link e ação em texto |
| `Brush.Text.Danger` / `Warning` / `Success` | mensagem de erro, aviso e sucesso |
| `Brush.Text.OnAccent` | texto sobre `Brush.Selection.Active` |

**Fundos e superfícies**

| Token | Uso |
|---|---|
| `Brush.Background.Window` | fundo da janela |
| `Brush.Background.Content` | fundo de lista, tabela e campo de texto |
| `Brush.Background.ContentAlternate` | linha alternada de lista |
| `Brush.Background.Control` | fundo de controle (botão, combo, checkbox) |
| `Brush.Background.ControlPressed` | controle pressionado |
| `Brush.Background.Overlay` | menu, lista do combo e tooltip |
| `Brush.Background.Danger` / `Warning` | caixa de erro e de aviso (com o `Brush.Text.*` do mesmo status) |
| `Brush.Selection.Active` | linha selecionada com a janela em foco |
| `Brush.Selection.Inactive` | linha selecionada com a janela fora de foco |
| `Brush.Separator` | linha divisória, borda de controle desabilitado |
| `Brush.Border.Control` | borda de botão, campo, combo e checkbox |
| `Brush.Border.Overlay` | borda de menu e tooltip |

**Status e destaque**

| Token | Uso |
|---|---|
| `Brush.Status.Danger` / `Warning` / `Success` | ícone, ponto e borda de status — **não para texto** |
| `Brush.Badge.Success` / `Danger` / `Done` / `Attention` / `Accent` / `Muted` | ícone das etiquetas de estado de worktree, PR, checks e review — **não para texto** |
| `Brush.Accent` | a cor de destaque do sistema |
| `Brush.Accent.Pressed` | botão de destaque e checkbox marcado, pressionados |
| `Brush.Focus` | anel de foco — Tab, e clique no campo de texto (a accent a 50%) |

`Brush.Accent`, `Brush.Accent.Pressed`, `Brush.Focus` e `Brush.Selection.Active` seguem
a accent color escolhida em Ajustes do Sistema (o `FluentTheme` lê do macOS e expõe como `SystemAccentColor`). Por isso não têm
versão clara/escura nem entram no teste. Com o azul padrão, o branco dá 6,1:1 sobre
`Brush.Selection.Active` mas só 4,0:1 sobre `Brush.Accent`: texto sobre destaque vai
sobre `Selection.Active`.

### Onde os valores diferem dos da Apple

As cores de sistema do macOS foram pensadas para o fundo delas e nem todas passam em AA
como texto. Nesses casos o token de texto usa um tom ajustado e a cor original da Apple
fica no token de status (ícone, ponto):

| Token | Apple | Aqui | Motivo |
|---|---|---|---|
| `Brush.Label.Secondary` (claro) | preto 50% | preto 60% | 50% dá 3,8:1 sobre o fundo da janela |
| `Brush.Text.Link` (claro) | `#0068DA` | `#0064D6` | 4,4:1 sobre o fundo da janela |
| `Brush.Text.Danger` | `#FF3B30` / `#FF453A` | `#B3221A` / `#FF8A84` | legível também dentro da caixa de erro |
| `Brush.Text.Warning` (claro) | `#FF9500` | `#8A5A00` | laranja do sistema dá 1,9:1 |
| `Brush.Text.Success` (claro) | `#28CD41` | `#157031` | verde do sistema dá 1,8:1 |

### Etiquetas de estado: a paleta do GitHub

As etiquetas de estado mostram informação do GitHub, e a regra do app é mostrá-la com o
ícone e a cor que o GitHub usa. Por isso os `Brush.Badge.*` não usam as cores do macOS:
são os `fg.*` do [Primer](https://primer.style/foundations/color) em claro e escuro
(`fg.success`, `fg.danger`, `fg.done`, `fg.attention`, `fg.accent`, `fg.muted`). Ficam nos
tokens, e não no código, para ter uma fonte só de cor e para o `TokensTests` conferir o
contraste delas (#103). O papel de cada etiqueta fica em `BadgeVisuals.Tone`, que devolve
a chave do token, e o `BadgeBrushConverter` busca o pincel na variante de tema do controle.

Como etiqueta é ícone, o mínimo é 3:1 (WCAG 1.4.11), não o 4,5:1 de texto. Todas passam
sobre os fundos de janela, conteúdo, linha alternada e overlay. Sobre
`Brush.Selection.Inactive`, o `Danger` escuro dá 2,8:1 — pendência na #108.

## Tipografia

A fonte é a Inter (padrão do app); a monoespaçada é `Font.Mono` (Menlo), para caminho,
branch, hash e saída de comando. Os tamanhos seguem a escala da HIG do macOS e ficam
em `Font.Size.*`, mas na view use a classe, que já traz peso e altura de linha:

| Classe | Tamanho | Peso | Uso |
|---|---|---|---|
| `large-title` | 26 | Regular | título de tela vazia, boas-vindas |
| `title1` | 22 | Regular | título de janela Sobre |
| `title2` | 17 | Regular | título de seção grande |
| `title3` | 15 | Regular | título de diálogo |
| `headline` | 13 | Bold | título de grupo, nome em destaque |
| `body` | 13 | Regular | texto comum |
| `callout` | 12 | Regular | texto explicativo abaixo de um controle |
| `subheadline` | 11 | Regular | metadado de linha de lista |
| `footnote` | 10 | Regular | nota de rodapé |
| `caption1` | 10 | Regular | legenda |
| `caption2` | 10 | Medium | rótulo curto, cabeçalho de coluna |

Modificadores combináveis: `emphasized` (SemiBold) e `mono` (`Font.Mono`) —
`Classes="body emphasized"`, `Classes="subheadline mono"`.

## Espaçamento e raios

Grade de 4pt: `Space.XXS` (2), `XS` (4), `S` (8), `M` (12), `L` (16), `XL` (20),
`XXL` (24), `XXXL` (32). Na view, prefira os nomes semânticos:

| Token | Valor | Uso |
|---|---|---|
| `Margin.Window` | 20 | da borda da janela ao conteúdo |
| `Spacing.Related` | 8 | entre controles relacionados (campo e botão, botões de uma barra) |
| `Spacing.Group` | 20 | entre grupos de controles |
| `Spacing.Label` | 6 | entre um rótulo e o controle que ele descreve |
| `Padding.Box` | 12 | dentro de caixa de aviso ou de detalhe |
| `Padding.Borderless` | 6,3 | botão sem borda |
| `Margin.MenuItem` / `Margin.TableRow` | 5,0 / 6,0 | recuo do destaque de item de menu e de linha de lista |
| `Margin.BeforeColumn` / `Margin.AroundColumn` | 0,0,12,0 / 12,0 | separação entre colunas de uma linha |
| `Margin.BeforeButton` / `Margin.AfterField` | 0,0,8,0 / 10,0,0,0 | antes de um botão numa barra; texto logo depois de um campo |
| `Margin.Badge` | 0,0,6,0 | entre uma etiqueta de estado e a seguinte |

Da janela principal (faixa de abas, barras e tabela): `Padding.TabStrip`, `Padding.Tab`,
`Margin.Tab`, `Size.Tab.MinHeight` e `Radius.Tab` (só os cantos de cima) para as abas;
`Padding.Bar` para a barra do filtro e o rodapé; `Padding.TableHeader`,
`Padding.TableCell` e `Size.TableRow.TwoLine` para a tabela. `Padding.TableCell` soma
com `Margin.TableRow` para o conteúdo da linha cair no mesmo recuo (16) do cabeçalho.

Raios: `Radius.Small` (4, etiqueta, tooltip e linha de lista), `Radius.Control` (6, botão
e campo), `Radius.Group` (12, cartão, grupo e menu), `Radius.MenuItem` (7, destaque do item
de menu), `Radius.Window` (16, sheet) e `Radius.FocusRing` (9, o anel de foco em volta de
um controle). `Radius.FocusRing` é `Radius.Control` + 3 (a espessura do anel) e
`Radius.MenuItem` é `Radius.Group` − 5 (o recuo do item), para as curvas ficarem
concêntricas; o `RadiusTests` confere as duas contas.

### De onde vêm as medidas

Raios, alturas e recuos foram medidos nos controles do próprio AppKit no macOS 27 (#97),
instanciados num script Swift e renderizados em bitmap 2x — não no UI Kit do Figma. Do
AppKit: botão, campo, pop-up e segmentado com 24pt de altura e raio 6; botão com 12pt de
padding lateral; `NSBox` e menu com raio 12; menu com 5pt em cima e embaixo, itens de 24pt,
texto a 14pt da borda e separador de 11pt com a linha recuada 16pt; destaque do item recuado
5pt, raio 7; janela e sheet com raio 16; anel de foco de 3pt na accent a 50%. Do lado do
Avalonia, a galeria renderizada sem tela (Skia, com a Inter) dá as mesmas medidas.

## Controles

Os controles do Fluent (botão, campo, checkbox, radio, combo, `NumericUpDown`, menu,
tooltip) já saem com as cores, a fonte (13pt) e os raios daqui, sem classe nenhuma:
`FluentOverrides.axaml` liga cada chave de recurso do Fluent a um token. Chave que não
aparece lá continua com o valor do Fluent.

**Botões**

| Como | Visual | Uso |
|---|---|---|
| `<Button>` | fundo de controle com borda | ação comum |
| `IsDefault="True"` | preenchido na accent, texto branco | a ação que o Return aciona — uma por janela |
| `Classes="accent"` | igual ao padrão | destaque sem ser o botão padrão |
| `Classes="destructive"` | botão comum com o rótulo em vermelho | remover, descartar; vence o `IsDefault` |
| `Classes="borderless"` | só o conteúdo; fundo discreto sob o mouse | barra de ferramentas, ação dentro de linha |
| `Classes="clear"` | só o ícone, terciário; escurece sob o mouse; sem foco nem Tab | o X de limpar dentro do campo (`InnerRightContent`), com `Icon.ClearField` |

Em janela fora de foco o botão padrão perde o destaque, como no macOS: o `App` põe a
classe `inactive` na janela que perde o foco, e `Controls.axaml` a usa.

**Lista estilo tabela** — `<ListBox Classes="table">`: fundo de conteúdo, linhas recuadas
com o destaque arredondado, sem destaque sob o mouse, seleção na accent com a lista em
foco e cinza quando o foco está em outro lugar ou a janela está inativa. É opcional porque
o `ListBox` também serve de barra de abas (`MainWindow`), que não deve ganhar esse visual.

**Foco** — Tab mostra um anel na accent a 50% por fora do controle. O campo de texto mostra
o mesmo anel também no foco por clique, como o `NSTextField`, e mantém a borda de repouso
(decisão de #97: o Fluent trocava a borda pela accent em 2px e não mostrava anel). No
`NumericUpDown` o anel envolve só a parte de texto, não as setas — como o campo e o
`NSStepper` separados do macOS.

### Como mexer em `FluentOverrides.axaml`

O arquivo é gerado por `tools/gen_overrides.py`, a partir das chaves do Fluent e de uma
lista de regras (chave → token); não edite à mão: mude a regra e rode
`tools/regenerar.sh`, que também é o caminho ao atualizar o Avalonia (`tools/README.md`).

Os aliases se repetem em `Light` e `Dark`: um `StaticResource` dentro de um dicionário de
tema só encontra os tokens da mesma variante. O teste `FluentOverridesTests` garante que
as duas listas são iguais e que todo alias aponta para um token que existe. Para achar a
chave do Fluent que um controle usa, veja o template dele em
`src/Avalonia.Themes.Fluent/Controls/` e as chaves em
`src/Avalonia.Themes.Fluent/Accents/FluentControlResources.xaml`, no repositório do
Avalonia, na tag da versão em uso.

## Diálogos

Todo diálogo usa o `DialogFrame` (`Views/DialogFrame.cs`), o layout de alerta e sheet da
HIG ([Alerts](https://developer.apple.com/design/human-interface-guidelines/alerts),
[Sheets](https://developer.apple.com/design/human-interface-guidelines/sheets)): título em
negrito (`Headline`), mensagem de apoio na cor secundária (`Message`, opcional), o
conteúdo e, embaixo, os botões alinhados à direita, com `Margin.Window` em volta. A janela
usa `Brush.Background.Window` de fundo.

O espaço entre as partes se ajusta ao que existe: sem título nem mensagem (um formulário),
o conteúdo começa direto na `Margin.Window`, sem a `Margin.DialogContent`; com o conteúdo
nulo ou escondido (`IsVisible="False"`), os botões ficam a `Margin.DialogButtons` do texto.

O diálogo não é redimensionável: a altura acompanha o conteúdo (`SizeToContent="Height"`),
e o que pode crescer sem limite rola dentro de uma caixa de altura máxima, como o detalhe
do `ConfirmWindow`.

```xml
<Window … Width="540" SizeToContent="Height" CanResize="False"
        Background="{DynamicResource Brush.Background.Window}">
  <views:DialogFrame Headline="Travar o worktree?" Message="Ninguém mais consegue removê-lo.">
    <TextBox Watermark="Motivo (opcional)" />
    <views:DialogFrame.Buttons>
      <Button Content="Cancelar" IsCancel="True" />
      <Button Content="Travar" IsDefault="True" />
    </views:DialogFrame.Buttons>
  </views:DialogFrame>
</Window>
```

Regras dos botões:

- **Ordem**, da esquerda para a direita: ações alternativas, **Cancelar** e, na ponta
  direita, o botão padrão.
- **Return** aciona o botão com `IsDefault` (em destaque, na accent); **Esc** aciona o com
  `IsCancel`. Um de cada por janela.
- **Ação destrutiva** (apagar, descartar, forçar) **nunca é a padrão**: o botão leva
  `Classes="destructive"` (rótulo vermelho) e o `IsDefault` vai para o Cancelar, para um
  Return distraído não apagar nada.
- **Aviso sem escolha** (relatório, erro) tem um botão só, que é `IsDefault` e `IsCancel`
  ao mesmo tempo; não há um "Cancelar" que faz o mesmo que o "OK".

Para confirmar uma ação, use o `ConfirmWindow`, que já segue essas regras:

| Chamada | Botões | Return |
|---|---|---|
| `new ConfirmWindow(título, pergunta, detalhe, rótulo)` | Cancelar, **rótulo** | confirma |
| … `, ConfirmStyle.CancelIsDefault)` | **Cancelar**, rótulo | cancela |
| … `, ConfirmStyle.Destructive)` | **Cancelar**, rótulo em vermelho | cancela |
| `ConfirmWindow.Notice(título, afirmação, detalhe)` | **Entendi** | fecha |

**Janela sem escolha a fazer** não é diálogo e não tem barra de botões:

- **Ajustes** (`SettingsWindow`): cada campo vale na hora, como nos Ajustes do macOS; não
  há Salvar, Cancelar nem Fechar. Fecha pelo botão da janela, ⌘W ou Esc.
- **Sobre** (`AboutWindow`): o painel Sobre do macOS, com ícone, nome, versão e créditos
  centralizados, sem botão; Esc fecha.

Formulário dentro do diálogo (`CreateWorktreeWindow`): rótulo à esquerda numa coluna de
largura fixa, campo à direita e a dica embaixo do campo, em `subheadline` secundário. Com
dica embaixo, o rótulo alinha ao topo com `Margin.FieldLabel`.

O detalhe aparece numa caixa em `Font.Mono` que rola a partir de `Size.DialogDetail.MaxHeight`;
detalhe vazio esconde a caixa e o espaço dela.

## Ícones

Cada ícone é uma geometria em `Icons.axaml`, com nome pelo que representa (`Icon.Settings`,
`Icon.GitPullRequest`…), sempre no mesmo quadro de 16x16. Na view, use o `PathIcon`, que
aqui desenha a geometria nesse quadro em vez de esticá-la até as bordas do desenho (tema
em `Controls.axaml`) — assim ícones diferentes saem do mesmo tamanho e um ponto continua
ponto. A cor vem do `Foreground` (por padrão, a do texto em volta); o tamanho, da classe:

| Classe | Tamanho | Ao lado de |
|---|---|---|
| `small` | `Size.Icon.Small` (12) | `subheadline`, `caption*` |
| (nenhuma) | `Size.Icon.Medium` (14) | `body` |
| `large` | `Size.Icon.Large` (18) | `title3` |

```xml
<Button Classes="borderless" ToolTip.Tip="Configurações">
  <PathIcon Data="{StaticResource Icon.Settings}" />
</Button>
```

Duas famílias, com papéis diferentes:

- **Octicons** (GitHub, MIT) nas etiquetas de estado de worktree, PR, checks e review:
  sempre que o GitHub mostra a mesma informação, o app usa o mesmo ícone e a mesma cor
  que ele. O view-model escolhe a etiqueta pela chave do ícone (`StatusBadge.IconKey`), e
  o `BadgeConverter.ToGeometry` busca o recurso; a cor vem dos `Brush.Badge.*`.
- **Phosphor** (MIT, peso regular) nos comandos do próprio app (nova aba, fechar, ajuda,
  configurações, informação, ordenação, limpar o campo). É o conjunto aberto mais parecido com o SF
  Symbols.

**Por que não o SF Symbols:** a licença só permite usá-los em interfaces de apps para
plataformas Apple, e a Apple os trata como imagens fornecidas pelo sistema; copiar os
desenhos para este repositório, público e com licença própria, seria redistribuir arte da
Apple. Decisão registrada na issue #67.

`Icons.axaml` é gerado por `tools/gen_icons.py`. Para acrescentar um ícone, some uma
linha na lista `OCTICONS` ou `PHOSPHORS` do script e rode `tools/regenerar.sh`
(`tools/README.md`). O script copia o `d` do SVG para a geometria com o prefixo `F1` — a
regra de preenchimento NonZero do SVG; sem ele o Avalonia usa EvenOdd e alguns desenhos
saem vazados — e, no Phosphor, põe o `Transform="0.0625,0,0,0.0625,0,0"` que reduz de 256
para 16 (o `Transform` não aceita a sintaxe `scale()` do CSS).
