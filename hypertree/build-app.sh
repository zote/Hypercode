#!/usr/bin/env bash
# Gera dist/Hypertree.app — um bundle macOS autocontido (não precisa do .NET instalado
# na máquina que for rodar). Uso:
#
#   ./build-app.sh [osx-arm64|osx-x64] [versão]
#
# A versão (SemVer: 1.2.0, 1.2.0-beta.1, com ou sem "v") também pode vir de $VERSION;
# sem nenhuma das duas, é 0.0.0-dev. O Info.plist só aceita números, então
# CFBundleShortVersionString e CFBundleVersion recebem a versão sem o sufixo de
# pré-release; o assembly (-p:Version) leva a completa.
set -euo pipefail

cd "$(dirname "$0")"

APP_NAME="Hypertree"
BUNDLE_ID="app.zimps.hypertree"
RID="${1:-}"
VERSION="${2:-${VERSION:-0.0.0-dev}}"
VERSION="${VERSION#v}"

if ! [[ "$VERSION" =~ ^([0-9]+\.[0-9]+\.[0-9]+)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$ ]]; then
  echo "✗ versão inválida: $VERSION (esperado SemVer, ex.: 1.2.0 ou 1.2.0-beta.1)" >&2
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

echo "▸ publicando $VERSION para $RID…"
rm -rf "$DIST"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish Hypertree.csproj \
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
  <key>CFBundlePackageType</key>     <string>APPL</string>
  <key>CFBundleShortVersionString</key> <string>$BUNDLE_VERSION</string>
  <key>CFBundleVersion</key>         <string>$BUNDLE_VERSION</string>
  <key>CFBundleInfoDictionaryVersion</key> <string>6.0</string>
  <key>LSMinimumSystemVersion</key>  <string>11.0</string>
  <key>NSHighResolutionCapable</key> <true/>
  <key>NSAppleEventsUsageDescription</key>
  <string>O Hypertree precisa controlar o iTerm2 para abrir uma janela na pasta do worktree.</string>
</dict>
</plist>
PLIST

chmod +x "$APP/Contents/MacOS/$APP_NAME"

# Assinatura ad-hoc: sem isso o macOS (Apple Silicon) mata o app ao abrir.
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || \
  echo "⚠ codesign falhou — se o app não abrir, rode: codesign --force --deep --sign - $APP"

echo "✓ pronto: $APP"
echo "  abrir:  open $APP"
echo "  instalar: cp -R $APP /Applications/"
