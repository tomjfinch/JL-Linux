#!/usr/bin/env bash
# Install or update JL for Linux.
#
#   curl -fsSL https://raw.githubusercontent.com/tomjfinch/JL-Linux/master/install.sh | bash
#
# Uninstall:  ... | bash -s -- --uninstall   (your settings and dictionaries are kept)
set -euo pipefail

REPO="tomjfinch/JL-Linux"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
DEST="$DATA/JL"
APPS="$DATA/applications"
BIN="$HOME/.local/bin"

if [[ "${1:-}" == "--uninstall" ]]; then
    rm -f "$APPS/jl.desktop" "$BIN/JL"
    echo "Removed the menu entry and the JL command."
    echo "The program, settings and dictionaries are still in $DEST - delete that folder to remove everything."
    exit 0
fi

case "$(uname -m)" in
    x86_64) ARCH=x64 ;;
    aarch64|arm64) ARCH=arm64 ;;
    *) echo "Sorry, JL isn't built for $(uname -m)." >&2; exit 1 ;;
esac

for tool in curl tar; do
    command -v "$tool" >/dev/null 2>&1 || { echo "Please install $tool first." >&2; exit 1; }
done

URL="https://github.com/$REPO/releases/download/latest/JL-linux-$ARCH.tar.gz"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "Downloading JL for Linux ($ARCH)..."
curl -fL --progress-bar -o "$TMP/jl.tar.gz" "$URL"

# Overwrites only the program's own files; Config/, Profiles/ and downloaded
# dictionaries in $DEST are left as they are.
mkdir -p "$DEST" "$APPS" "$BIN"
tar -xzf "$TMP/jl.tar.gz" -C "$DEST" --strip-components=1
chmod +x "$DEST/JL"

sed -e "s|@EXEC@|$DEST/JL|g" -e "s|@ICON@|$DEST/jl.png|g" "$DEST/jl.desktop" > "$APPS/jl.desktop"
ln -sf "$DEST/JL" "$BIN/JL"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS" >/dev/null 2>&1 || true

echo
echo "JL is installed. Open it from your application menu, or run: JL"

missing=()
command -v mpv >/dev/null 2>&1 || command -v ffplay >/dev/null 2>&1 || missing+=("mpv (word audio)")
if [[ -n "${WAYLAND_DISPLAY:-}" ]] && ! command -v wl-paste >/dev/null 2>&1; then
    missing+=("wl-clipboard (clipboard capture on Wayland)")
fi
if command -v fc-list >/dev/null 2>&1 && ! fc-list :lang=ja 2>/dev/null | grep -q .; then
    missing+=("a Japanese font such as Noto Sans CJK")
fi

if (( ${#missing[@]} > 0 )); then
    echo
    echo "Recommended, not yet installed:"
    printf '  - %s\n' "${missing[@]}"
fi
