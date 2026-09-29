#!/usr/bin/env bash
# Gera dist/Hypercode.app — um bundle macOS autocontido (não precisa do .NET instalado
# na máquina que for rodar).  Uso:  ./build-app.sh [osx-arm64|osx-x64]
set -euo pipefail

cd "$(dirname "$0")"

APP_NAME="Hypercode"
BUNDLE_ID="app.zimps.hypercode"
# A versão mora no Hypercode.csproj (é a mesma que a janela Sobre exibe).
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Hypercode.csproj)"

RID="${1:-}"
if [ -z "$RID" ]; then
  case "$(uname -m)" in
    arm64) RID="osx-arm64" ;;
    *)     RID="osx-x64" ;;
  esac
fi

DIST="dist"
APP="$DIST/$APP_NAME.app"

echo "▸ publicando para ${RID}…"
rm -rf "$DIST"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish Hypercode.csproj \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
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
  <key>CFBundleShortVersionString</key> <string>$VERSION</string>
  <key>CFBundleVersion</key>         <string>$VERSION</string>
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
