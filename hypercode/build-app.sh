#!/usr/bin/env bash
# Gera dist/Hypercode.app — um bundle macOS autocontido (não precisa do .NET instalado
# na máquina que for rodar). Uso:
#
#   ./build-app.sh [osx-arm64|osx-x64] [versão]
#
# A versão (SemVer: 1.2.0, 1.2.0-beta.1, com ou sem "v") vem do argumento, de $VERSION ou,
# sem nenhum dos dois, do <Version> do Hypercode.csproj. O assembly (-p:Version) leva a
# versão completa, que é a que a janela Sobre exibe; o Info.plist só aceita números, então
# CFBundleShortVersionString e CFBundleVersion recebem X.Y.Z sem o sufixo de pré-release.
set -euo pipefail

cd "$(dirname "$0")"

APP_NAME="Hypercode"
BUNDLE_ID="app.zimps.hypercode"

RID="${1:-}"
VERSION="${2:-${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Hypercode.csproj)}}"
VERSION="${VERSION#v}"

if ! [[ "$VERSION" =~ ^([0-9]+\.[0-9]+\.[0-9]+)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$ ]]; then
  echo "✗ versão inválida: '$VERSION' (esperado SemVer, ex.: 1.2.0 ou 1.2.0-beta.1)" >&2
  exit 1
fi
BUNDLE_VERSION="${BASH_REMATCH[1]}"

if [ -z "$RID" ]; then
  case "$(uname -m)" in
    arm64) RID="osx-arm64" ;;
    *)     RID="osx-x64" ;;
  esac
fi

DIST="dist"
APP="$DIST/$APP_NAME.app"

echo "▸ publicando ${VERSION} para ${RID}…"
rm -rf "$DIST"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish Hypercode.csproj \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:Version="$VERSION" \
  -o "$APP/Contents/MacOS"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>            <string>$APP_NAME</string>
  <key>CFBundleDisplayName</key>     <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>      <string>$BUNDLE_ID</string>
  <key>CFBundleExecutable</key>      <string>$APP_NAME</string>
  <key>CFBundleIconName</key>        <string>Hypercode</string>
  <key>CFBundleIconFile</key>        <string>icon</string>
  <key>CFBundlePackageType</key>     <string>APPL</string>
  <key>CFBundleShortVersionString</key> <string>$BUNDLE_VERSION</string>
  <key>CFBundleVersion</key>         <string>$BUNDLE_VERSION</string>
  <key>CFBundleInfoDictionaryVersion</key> <string>6.0</string>
  <key>LSMinimumSystemVersion</key>  <string>11.0</string>
  <key>NSHighResolutionCapable</key> <true/>
  <key>NSAppleEventsUsageDescription</key>
  <string>O Hypercode precisa controlar o iTerm2 para abrir uma janela na pasta do worktree.</string>
</dict>
</plist>
PLIST

chmod +x "$APP/Contents/MacOS/$APP_NAME"

# Gerados a partir de Assets/Hypercode.icon por Assets/make-icon.sh. O macOS 26+ usa o
# Assets.car (CFBundleIconName), com as variantes claro, escuro e tinted; os anteriores, o
# .icns (CFBundleIconFile).
cp Assets/Assets.car "$APP/Contents/Resources/Assets.car"
cp Assets/icon.icns "$APP/Contents/Resources/icon.icns"

# Licença do app e avisos dos ícones de terceiros (MIT pede que acompanhem as cópias).
cp ../LICENSE "$APP/Contents/Resources/LICENSE"
cp ../THIRD-PARTY-NOTICES.md "$APP/Contents/Resources/THIRD-PARTY-NOTICES.md"

# Assinatura ad-hoc: sem isso o macOS (Apple Silicon) mata o app ao abrir.
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || \
  echo "⚠ codesign falhou — se o app não abrir, rode: codesign --force --deep --sign - $APP"

echo "✓ pronto: $APP"
echo "  abrir:  open $APP"
echo "  instalar: cp -R $APP /Applications/"
