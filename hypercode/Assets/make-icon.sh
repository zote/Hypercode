#!/usr/bin/env bash
# Gera Assets/icon.icns (ícone do bundle) e Assets/icon.png (exibido na janela Sobre)
# a partir de Assets/icon.svg.  Rode depois de editar o SVG e commite os três: o
# build-app.sh só copia o .icns, não precisa gerar nada.
# Usa só o que vem com o macOS (swift/AppKit, sips, iconutil).
set -euo pipefail

cd "$(dirname "$0")"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
ICONSET="$WORK/icon.iconset"
mkdir "$ICONSET"

echo "▸ rasterizando icon.svg…"
swift render-svg.swift icon.svg "$WORK/icon-1024.png" 1024

# Os tamanhos e nomes que o iconutil espera num .iconset.
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$WORK/icon-1024.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$WORK/icon-1024.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done

iconutil -c icns "$ICONSET" -o icon.icns
cp "$ICONSET/icon_256x256.png" icon.png
echo "✓ pronto: Assets/icon.icns e Assets/icon.png"
