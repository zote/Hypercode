#!/usr/bin/env bash
# Baixa as fontes nas versões fixadas abaixo e gera de novo FluentOverrides.axaml e
# Icons.axaml. Uso e como atualizar as versões: README.md, nesta pasta.
set -euo pipefail

AVALONIA=11.3.22
OCTICONS=19.15.0
PHOSPHOR=2.1.1

tools=$(cd "$(dirname "$0")" && pwd)
styles=$(dirname "$tools")
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

curl -fsSL -o "$tmp/FluentControlResources.xaml" \
  "https://raw.githubusercontent.com/AvaloniaUI/Avalonia/$AVALONIA/src/Avalonia.Themes.Fluent/Accents/FluentControlResources.xaml"
mkdir "$tmp/octicons" "$tmp/phosphor"
curl -fsSL "https://registry.npmjs.org/@primer/octicons/-/octicons-$OCTICONS.tgz" | tar xz -C "$tmp/octicons"
curl -fsSL "https://registry.npmjs.org/@phosphor-icons/core/-/core-$PHOSPHOR.tgz" | tar xz -C "$tmp/phosphor"

python3 "$tools/gen_overrides.py" "$tmp/FluentControlResources.xaml" "$styles/FluentOverrides.axaml"
python3 "$tools/gen_icons.py" "$tmp/octicons/package/build/svg" "$tmp/phosphor/package/assets/regular" "$styles/Icons.axaml"
