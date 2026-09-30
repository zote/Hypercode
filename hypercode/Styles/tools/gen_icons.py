"""Gera hypercode/Styles/Icons.axaml: Octicons de 16px + Phosphor regular.

Uso: gen_icons.py <build/svg do @primer/octicons> <assets/regular do @phosphor-icons/core> <saída>

Os dois diretórios vêm dos pacotes npm (regenerar.sh baixa os dois); a versão citada no
cabeçalho gerado sai do package.json de cada um. Para acrescentar um ícone, some uma linha
em OCTICONS ou em PHOSPHORS e gere de novo. Ver tools/README.md.
"""
import json
import re
import sys

if len(sys.argv) != 4:
    sys.exit(__doc__)
OCTICON_DIR, PHOSPHOR_DIR, OUT = sys.argv[1:4]


def package_version(asset_dir):
    """Versão do pacote npm do diretório de SVGs, que fica dois níveis abaixo da raiz."""
    return json.load(open(f"{asset_dir}/../../package.json", encoding="utf-8"))["version"]


OCTICONS_VERSION = package_version(OCTICON_DIR)
PHOSPHOR_VERSION = package_version(PHOSPHOR_DIR)

# Nome do recurso -> arquivo do Octicons (<nome>-16.svg)
OCTICONS = [
    ("Icon.DiffModified", "diff-modified"),
    ("Icon.Alert", "alert"),
    ("Icon.Stop", "stop"),
    ("Icon.GitCompare", "git-compare"),
    ("Icon.ArrowUp", "arrow-up"),
    ("Icon.ArrowDown", "arrow-down"),
    ("Icon.Upload", "upload"),
    ("Icon.CloudOffline", "cloud-offline"),
    ("Icon.BellFill", "bell-fill"),
    ("Icon.Terminal", "terminal"),
    ("Icon.Columns", "columns"),
    ("Icon.Trash", "trash"),
    ("Icon.GitPullRequest", "git-pull-request"),
    ("Icon.GitPullRequestDraft", "git-pull-request-draft"),
    ("Icon.GitMerge", "git-merge"),
    ("Icon.GitPullRequestClosed", "git-pull-request-closed"),
    ("Icon.Check", "check"),
    ("Icon.X", "x"),
    ("Icon.DotFill", "dot-fill"),
    ("Icon.CheckCircle", "check-circle"),
    ("Icon.FileDiff", "file-diff"),
    ("Icon.CodeReview", "code-review"),
]

# Nome do recurso -> arquivo do Phosphor (peso regular), com o uso
PHOSPHORS = [
    ("Icon.Plus", "plus", "nova aba"),
    ("Icon.Close", "x", "fechar aba"),
    ("Icon.ClearField", "x-circle", "limpar o campo"),
    ("Icon.Help", "question", "ajuda"),
    ("Icon.Settings", "gear-six", "configurações"),
    ("Icon.Info", "info", "explicação de um ajuste"),
    ("Icon.SortAscending", "caret-up", "coluna ordenada, crescente"),
    ("Icon.SortDescending", "caret-down", "coluna ordenada, decrescente"),
]


def svg_paths(path, viewbox):
    """Os `d` dos paths do SVG, que tem de ser só paths, sem fill-rule nem transform."""
    svg = open(path, encoding="utf-8").read()
    assert f'viewBox="{viewbox}"' in svg, path
    elements = re.findall(r"<(\w+)", svg)
    assert set(elements) <= {"svg", "path"}, (path, elements)
    assert "fill-rule" not in svg and "transform" not in svg, path
    paths = re.findall(r'<path d="([^"]+)"', svg)
    assert len(paths) == elements.count("path"), path
    return paths


def octicon_path(name):
    # Octicon com mais de um path (upload) vira uma figura só: com NonZero, dá no mesmo.
    return " ".join(svg_paths(f"{OCTICON_DIR}/{name}-16.svg", "0 0 16 16"))


def phosphor_path(name):
    paths = svg_paths(f"{PHOSPHOR_DIR}/{name}.svg", "0 0 256 256")
    assert len(paths) == 1, name
    return paths[0]


lines = []
lines.append(f'''<!--
  Ícones do Hypercode, todos no mesmo quadro de 16x16: o PathIcon (tema em Controls.axaml)
  desenha a geometria nesse quadro, sem esticar até as bordas do desenho, então ícones
  diferentes saem do mesmo tamanho e alinhados. Cor e tamanho vêm de quem usa: Foreground
  com um token de Brush e as classes de tamanho (README.md).

  Duas famílias, cada uma com o seu papel:
  - Octicons (GitHub, MIT): as etiquetas de estado de worktree, PR, checks e review. Sempre
    que o GitHub mostra a mesma informação, o app usa o mesmo ícone que ele (StatusBadge.cs).
    Copiados de @primer/octicons {OCTICONS_VERSION}, <nome>-16.svg.
  - Phosphor (MIT, peso regular): os comandos do próprio app. É o conjunto aberto mais perto
    do SF Symbols, cuja licença não permite copiar os desenhos para cá (issue #67).
    Copiados de @phosphor-icons/core {PHOSPHOR_VERSION}, assets/regular/<nome>.svg, e reduzidos de 256
    para 16 pelo Transform (matriz de escala 1/16).

  Licenças e avisos de copyright dos dois conjuntos: THIRD-PARTY-NOTICES.md, na raiz.

  "F1" = NonZero, a regra de preenchimento padrão do SVG: o Avalonia assume EvenOdd quando o
  prefixo não vem.

  Gerado por Styles/tools/gen_icons.py: para acrescentar ou trocar um ícone, mude a lista lá
  e gere de novo (Styles/tools/README.md), em vez de editar este arquivo à mão.
-->
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
''')
lines.append("  <!-- Octicons: etiquetas de estado -->")
for key, file in OCTICONS:
    lines.append(f"  <!-- {file}-16 -->")
    lines.append(f'  <StreamGeometry x:Key="{key}">F1 {octicon_path(file)}</StreamGeometry>')
lines.append("")
lines.append("  <!-- Phosphor: comandos do app -->")
for key, file, use in PHOSPHORS:
    lines.append(f"  <!-- {file}: {use} -->")
    lines.append(f'  <PathGeometry x:Key="{key}" Figures="F1 {phosphor_path(file)}" Transform="0.0625,0,0,0.0625,0,0" />')
lines.append("")
lines.append("</ResourceDictionary>")
open(OUT, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print(len(OCTICONS), "octicons,", len(PHOSPHORS), "phosphor")
