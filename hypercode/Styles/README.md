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
| `Typography.axaml` | classes de texto (`title1`, `body`, `headline`…) | `Application.Styles`, depois do `FluentTheme` |
| `Controls.axaml` | variantes e estados que o Fluent não tem (botão destrutivo, lista `table`, anel de foco…) | `Application.Styles`, depois do `FluentTheme` |

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
| `Brush.Accent` | destaque: borda do campo em foco, ícone ativo |
| `Brush.Accent.Pressed` | botão de destaque e checkbox marcado, pressionados |
| `Brush.Focus` | anel de foco do teclado (a accent a 50%) |

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

Raios: `Radius.Small` (4, etiqueta, tooltip, item de menu e linha de lista),
`Radius.Control` (6, botão e campo), `Radius.Group` (10, cartão, grupo e menu),
`Radius.Window` (12, sheet e popover) e `Radius.FocusRing` (9, o anel de foco em volta de
um controle).

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

Em janela fora de foco o botão padrão perde o destaque, como no macOS: o `App` põe a
classe `inactive` na janela que perde o foco, e `Controls.axaml` a usa.

**Lista estilo tabela** — `<ListBox Classes="table">`: fundo de conteúdo, linhas recuadas
com o destaque arredondado, sem destaque sob o mouse, seleção na accent com a lista em
foco e cinza quando o foco está em outro lugar ou a janela está inativa. É opcional porque
o `ListBox` também serve de barra de abas (`MainWindow`), que não deve ganhar esse visual.

**Foco** — Tab mostra um anel na accent a 50% por fora do controle. O campo de texto em
foco troca a borda pela accent (o Fluent não mostra anel nele).

### Como mexer em `FluentOverrides.axaml`

Os aliases se repetem em `Light` e `Dark`: um `StaticResource` dentro de um dicionário de
tema só encontra os tokens da mesma variante. O teste `FluentOverridesTests` garante que
as duas listas são iguais e que todo alias aponta para um token que existe. Para achar a
chave do Fluent que um controle usa, veja o template dele em
`src/Avalonia.Themes.Fluent/Controls/` e as chaves em
`src/Avalonia.Themes.Fluent/Accents/FluentControlResources.xaml`, no repositório do
Avalonia, na tag da versão em uso.
