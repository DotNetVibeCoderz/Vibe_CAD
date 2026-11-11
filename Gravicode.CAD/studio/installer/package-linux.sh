#!/usr/bin/env bash
# Packages a linux-x64 publish folder as a .tar.gz and a .deb.
#   package-linux.sh <publish-dir> <version> <out-dir>
set -euo pipefail
SRC=$1; VER=$2; OUT=$3
HERE=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$OUT"
chmod +x "$SRC/GravicodeCadStudio"

# tarball
STAGE=$(mktemp -d)
cp -r "$SRC" "$STAGE/gravicode-cad-studio-$VER"
tar -C "$STAGE" -czf "$OUT/GravicodeCadStudio-$VER-linux-x64.tar.gz" "gravicode-cad-studio-$VER"

# .deb: app in /opt, launcher in /usr/bin, desktop entry + icon + MIME type for .gcproj
DEB=$(mktemp -d)
mkdir -p "$DEB/DEBIAN" "$DEB/opt/gravicode-cad-studio" "$DEB/usr/bin" "$DEB/usr/share/applications" \
         "$DEB/usr/share/icons/hicolor/512x512/apps" "$DEB/usr/share/mime/packages"
cp -r "$SRC"/. "$DEB/opt/gravicode-cad-studio/"
ln -s /opt/gravicode-cad-studio/GravicodeCadStudio "$DEB/usr/bin/gravicode-cad-studio"
cp "$HERE/../../assets/icon.png" "$DEB/usr/share/icons/hicolor/512x512/apps/gravicode-cad-studio.png"

{
  echo "[Desktop Entry]"
  echo "Name=Gravicode CAD Studio"
  echo "Comment=2D drafting and 3D solid modelling"
  echo "Exec=/opt/gravicode-cad-studio/GravicodeCadStudio %f"
  echo "Icon=gravicode-cad-studio"
  echo "Terminal=false"
  echo "Type=Application"
  echo "Categories=Graphics;Engineering;"
  echo "MimeType=application/x-gravicode-project;"
} > "$DEB/usr/share/applications/gravicode-cad-studio.desktop"

{
  echo '<?xml version="1.0" encoding="UTF-8"?>'
  echo '<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">'
  echo '  <mime-type type="application/x-gravicode-project">'
  echo '    <comment>Gravicode CAD project</comment>'
  echo '    <glob pattern="*.gcproj"/>'
  echo '  </mime-type>'
  echo '</mime-info>'
} > "$DEB/usr/share/mime/packages/gravicode-cad-studio.xml"

SIZE=$(du -sk "$DEB/opt" | cut -f1)
{
  echo "Package: gravicode-cad-studio"
  echo "Version: $VER"
  echo "Section: graphics"
  echo "Priority: optional"
  echo "Architecture: amd64"
  echo "Installed-Size: $SIZE"
  echo "Depends: libc6, libfontconfig1, libx11-6, libice6, libsm6"
  echo "Recommends: libicu-dev"
  echo "Maintainer: Gravicode Studios <noreply@github.com>"
  echo "Homepage: https://github.com/DotNetVibeCoderz/Vibe_CAD"
  echo "Description: Gravicode CAD Studio - command-line driven CAD for 2D drafting and 3D solids"
  echo " Built on the open-source Gravicode.CAD library for .NET."
} > "$DEB/DEBIAN/control"

dpkg-deb --root-owner-group --build "$DEB" "$OUT/gravicode-cad-studio_${VER}_amd64.deb"
ls -la "$OUT"
