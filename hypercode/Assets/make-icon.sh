#!/usr/bin/env bash
# Gera, a partir de Assets/Hypercode.icon (o ícone em camadas do Icon Composer):
#   Assets/Assets.car  o ícone do macOS 26+, com as variantes claro, escuro, tinted e clear
#   Assets/icon.icns   a versão plana, para macOS anterior ao 26 (o actool gera junto)
#   Assets/icon.png    a imagem da janela Sobre
# Rode depois de editar o .icon e commite os três: o build-app.sh só copia, não precisa do
# Xcode. Este script precisa: Xcode 26 ou mais novo (actool e o ictool do Icon Composer).
# Para editar: Xcode → Open Developer Tool → Icon Composer → abrir Assets/Hypercode.icon.
set -euo pipefail

cd "$(dirname "$0")"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "▸ compilando Hypercode.icon…"
# Caminhos absolutos: o actool e o ictool não resolvem os relativos ao diretório atual.
# O nome do ícone (--app-icon) é o CFBundleIconName do Info.plist, no build-app.sh.
xcrun actool "$PWD/Hypercode.icon" --compile "$WORK" \
  --platform macosx --minimum-deployment-target 11.0 \
  --app-icon Hypercode --output-partial-info-plist "$WORK/partial.plist" >/dev/null
cp "$WORK/Assets.car" Assets.car
cp "$WORK/Hypercode.icns" icon.icns

echo "▸ exportando a imagem do Sobre…"
# O ictool que exporta imagem é o de dentro do Icon Composer; o de xcrun é outro.
ICTOOL="$(xcode-select -p)/../Applications/Icon Composer.app/Contents/Executables/ictool"
"$ICTOOL" "$PWD/Hypercode.icon" --export-image --output-file "$PWD/icon.png" \
  --platform macOS --rendition Default --width 256 --height 256 --scale 1 >/dev/null

echo "✓ pronto: Assets/Assets.car, Assets/icon.icns e Assets/icon.png"
