#!/usr/bin/env bash
# Build JL for Linux.
#
#   ./JL.Linux/build.sh              build into ./out/JL-linux
#   ./JL.Linux/build.sh --install    build, copy to ~/.local/share/JL and add a menu entry
#   SELF_CONTAINED=1 ./JL.Linux/build.sh   bundle the .NET runtime (no runtime needed on the target)
set -euo pipefail

cd "$(dirname "$0")/.."
REPO_ROOT="$(pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
    cat >&2 <<'EOF'
The .NET SDK (9.0 or newer) is required to build JL.
  Debian/Ubuntu:  sudo apt install dotnet-sdk-9.0
  Fedora:         sudo dnf install dotnet-sdk-9.0
  Arch:           sudo pacman -S dotnet-sdk
  Other:          https://dotnet.microsoft.com/download
EOF
    exit 1
fi

case "$(uname -m)" in
    x86_64)  RID=linux-x64;   PLATFORM=x64 ;;
    aarch64|arm64) RID=linux-arm64; PLATFORM=ARM64 ;;
    *) echo "Unsupported CPU architecture: $(uname -m)" >&2; exit 1 ;;
esac

SELF_CONTAINED_FLAG="--self-contained false"
if [[ "${SELF_CONTAINED:-0}" == "1" ]]; then
    SELF_CONTAINED_FLAG="--self-contained true"
fi

OUT="$REPO_ROOT/out/JL-linux"
echo "Building JL for $RID → $OUT"

# shellcheck disable=SC2086
dotnet publish JL.Linux/JL.Linux.csproj \
    -c Release \
    -r "$RID" \
    -p:Platform="$PLATFORM" \
    $SELF_CONTAINED_FLAG \
    -p:TreatWarningsAsErrors=false \
    -o "$OUT"

chmod +x "$OUT/JL"
echo
echo "Built: $OUT/JL"

if [[ "${1:-}" == "--install" ]]; then
    DEST="${XDG_DATA_HOME:-$HOME/.local/share}/JL"
    APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
    mkdir -p "$DEST" "$APPS"

    # Keep the user's Config/, Profiles/ and downloaded dictionaries in Resources/.
    if command -v rsync >/dev/null 2>&1; then
        rsync -a --exclude 'Config/' --exclude 'Profiles/' --exclude 'Logs/' "$OUT/" "$DEST/"
    else
        cp -a "$OUT/." "$DEST/"
    fi

    sed -e "s|@EXEC@|$DEST/JL|g" -e "s|@ICON@|$DEST/jl.png|g" "$REPO_ROOT/JL.Linux/jl.desktop" > "$APPS/jl.desktop"
    chmod +x "$DEST/JL"
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS" || true

    echo "Installed to $DEST"
    echo "JL is now in your application menu (or run $DEST/JL)."
fi
