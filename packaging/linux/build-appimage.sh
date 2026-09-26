#!/usr/bin/env bash
# Wraps the single-file publish in an AppImage: build-appimage.sh <published binary> <output .AppImage>
# The binary is already self-contained, so the AppImage only adds the launcher entry and icon, and
# makes the download one file that runs on double-click. appimagetool uses the static type 2
# runtime, so it runs without libfuse2 (not installed by default on Ubuntu 22.04+).
set -euo pipefail

binary=$(realpath "$1")
output=$(realpath -m "$2")
here=$(dirname "$(realpath "$0")")
repo=$(realpath "$here/../..")
appimagetool_version=1.9.1
# Pinned so a compromised/rewritten release download can't slip untrusted code into the AppImage
# build. Verified against GitHub's own asset digest: `gh api repos/AppImage/appimagetool/releases/tags/1.9.1`
# --jq '.assets[] | select(.name=="appimagetool-x86_64.AppImage") | .digest` reports the same sha256.
appimagetool_sha256=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

appdir="$work/AppDir"
mkdir -p "$appdir/usr/bin"
install -m 755 "$binary" "$appdir/usr/bin/heroesprofile-uploader"
ln -s usr/bin/heroesprofile-uploader "$appdir/AppRun"
cp "$here/heroesprofile-uploader.desktop" "$appdir/"
cp "$repo/Heroesprofile.Uploader.Linux/Gui/Assets/app-icon.png" "$appdir/heroesprofile-uploader.png"
ln -s heroesprofile-uploader.png "$appdir/.DirIcon"

tool="$work/appimagetool"
curl -fsSL -o "$tool" "https://github.com/AppImage/appimagetool/releases/download/$appimagetool_version/appimagetool-x86_64.AppImage"
echo "$appimagetool_sha256  $tool" | sha256sum -c -
chmod +x "$tool"
# Extract-and-run so building doesn't need FUSE either (CI runners don't have it).
APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64 "$tool" --no-appstream "$appdir" "$output"
