# Design system

Tokens e estilos visuais do Hypercode, baseados nas diretrizes da Apple para macOS
([HIG](https://developer.apple.com/design/human-interface-guidelines) e
[Design Resources](https://developer.apple.com/design/resources/#macos-apps)). Plano
geral na issue #63; as decisões de base (fonte Inter, `FluentTheme` compacto, accent do
sistema) e os motivos estão na #64.

| Arquivo | O que tem | Onde entra |
|---|---|---|
| `Tokens.axaml` | cores (claro/escuro), tamanhos de fonte, espaçamento, raios | `Application.Resources` |
| `Typography.axaml` | classes de texto (`title1`, `body`, `headline`…) | `Application.Styles`, depois do `FluentTheme` |

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
| `Brush.Background.Control` | fundo de controle (botão secundário) |
| `Brush.Background.Danger` / `Warning` | caixa de erro e de aviso (com o `Brush.Text.*` do mesmo status) |
| `Brush.Selection.Active` | linha selecionada com a janela em foco |
| `Brush.Selection.Inactive` | linha selecionada com a janela fora de foco |
| `Brush.Separator` | linha divisória e borda sutil |

**Status e destaque**

| Token | Uso |
|---|---|
| `Brush.Status.Danger` / `Warning` / `Success` | ícone, ponto e borda de status — **não para texto** |
| `Brush.Accent` | destaque: foco, controle marcado, ícone ativo |

`Brush.Accent` e `Brush.Selection.Active` seguem a accent color escolhida em Ajustes do
Sistema (o `FluentTheme` lê do macOS e expõe como `SystemAccentColor`). Por isso não têm
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

Raios: `Radius.Small` (4, etiqueta), `Radius.Control` (6, botão e campo),
`Radius.Group` (10, cartão e grupo), `Radius.Window` (12, sheet e popover).
