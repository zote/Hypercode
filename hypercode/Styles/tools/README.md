# Geradores do design system

`FluentOverrides.axaml` e `Icons.axaml` são gerados pelos scripts desta pasta; não edite
os dois à mão, mude o script e gere de novo. Precisa de `python3`, `curl` e `tar`.

```bash
hypercode/Styles/tools/regenerar.sh
git diff hypercode/Styles
```

O `regenerar.sh` baixa as fontes nas versões fixadas no topo dele (Avalonia, Octicons e
Phosphor) para uma pasta temporária e roda os dois scripts, que sobrescrevem os `.axaml`
em `Styles/`. Com as versões fixadas, rodar de novo não muda nada: o diff mostra só o que
você mudou.

## `gen_overrides.py` — `FluentOverrides.axaml`

Lê as chaves do dicionário `Default` do `FluentControlResources.xaml` do Avalonia e passa
cada uma pelas `RULES`, uma lista de `(regex, token)`: a primeira regra que casar vence, e
token `None` mantém o valor do Fluent. Chave que não casa com nenhuma regra também fica
com o valor do Fluent. As `GROUPS` só separam os aliases por controle no arquivo gerado, e
o texto fora de `ThemeDictionaries` (fonte, raios, paddings) está no modelo, no fim do
script.

**Ao atualizar o Avalonia:** troque `AVALONIA` no `regenerar.sh`, rode e leia o diff. Chave
nova do Fluent que casar com uma regra já sai mapeada; as que não casarem continuam com o
valor do Fluent. Para achar as que faltam, compare as chaves do `FluentControlResources.xaml`
das duas tags. Depois, `dotnet test` (o `FluentOverridesTests` confere Light e Dark e os
tokens) e a galeria (`dotnet run --project hypercode -- --galeria`).

## `gen_icons.py` — `Icons.axaml`

Monta cada ícone a partir do SVG do pacote: `<nome>-16.svg` do `@primer/octicons` e
`assets/regular/<nome>.svg` do `@phosphor-icons/core`. Os Octicons já vêm no quadro de 16;
os do Phosphor, em 256, levam o `Transform` de escala 1/16. Todos levam o prefixo `F1`
(NonZero, o padrão do SVG). O script para com erro se o SVG tiver algo além de `path`
(fill-rule, transform, outra forma), em vez de gerar um ícone errado.

**Para acrescentar um ícone:** some uma linha em `OCTICONS` (etiqueta de estado) ou em
`PHOSPHORS` (comando do app, com o uso, que vira o comentário) e rode o `regenerar.sh`. A
versão citada no cabeçalho do `Icons.axaml` sai do `package.json` de cada pacote.
