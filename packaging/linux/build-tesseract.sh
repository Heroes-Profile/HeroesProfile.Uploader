#!/usr/bin/env bash
# Builds the Tesseract the Linux Ranks build reads player names with, so users don't have to install it:
#   packaging/linux/build-tesseract.sh <out-dir>
# <out-dir> gets tesseract (one program: Leptonica, libpng and zlib built in, PNG input only, no OpenMP),
# tessdata/eng.traineddata and licenses/. release-desktop.yml puts it in publish/tesseract, where
# TesseractTextReader looks for it.
#
# Built in a manylinux_2_28 container (glibc 2.28, a newer GCC with its C++ runtime linked in), so the
# program runs on any distro the app does - it needs nothing but glibc. Needs Docker; every download is
# pinned and checked against its sha256.
set -euo pipefail

TESSERACT_VERSION=5.5.3
TESSERACT_SHA256=9218e62793116d42a9f6d14cd9348518b27f382096eea3d0f2d1a24616bb5884
LEPTONICA_VERSION=1.87.0
LEPTONICA_SHA256=c73363397f96eb1295602bf44d708a994ad42046c791bf03ea0505d829bdb6a7
LIBPNG_VERSION=1.6.59
LIBPNG_SHA256=2540302a1844ad2b2b501977abecfa850f265f97b78f065a712ab4074a89f5b5
ZLIB_VERSION=1.3.2
ZLIB_SHA256=bb329a0a2cd0274d05519d61c667c062e06990d72e125ee2dfa8de64f0119d16
# tessdata_fast: the small, fast models (eng is ~4 MB). Other languages come from the system's Tesseract.
TESSDATA_COMMIT=87416418657359cb625c412a48b6e1d6d41c29bd
ENG_SHA256=7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2
IMAGE=quay.io/pypa/manylinux_2_28_x86_64@sha256:1e7b8460ad8fb3fb0fbe1cd818845159ac896b4fd17452d56e892c291f3b97a8

[ $# -eq 1 ] || { echo "usage: $0 <out-dir>" >&2; exit 2; }
mkdir -p "$1"
out=$(cd "$1" && pwd)

# Outside the container: run this same script inside it.
if [ -z "${IN_TESSERACT_BUILD:-}" ]; then
  script=$(cd "$(dirname "$0")" && pwd)/$(basename "$0")
  exec docker run --rm -e IN_TESSERACT_BUILD=1 -e HOME=/tmp -u "$(id -u):$(id -g)" \
    -v "$script:/build-tesseract.sh:ro" -v "$out:/out" "$IMAGE" bash /build-tesseract.sh /out
fi

work=$(mktemp -d)
prefix=$work/prefix
jobs=$(nproc)

fetch() { # url sha256 file
  curl -fsSL --retry 3 -o "$work/$3" "$1"
  echo "$2  $work/$3" | sha256sum -c --quiet
}
fetch "https://github.com/madler/zlib/releases/download/v$ZLIB_VERSION/zlib-$ZLIB_VERSION.tar.gz" $ZLIB_SHA256 zlib.tar.gz
fetch "https://github.com/pnggroup/libpng/archive/refs/tags/v$LIBPNG_VERSION.tar.gz" $LIBPNG_SHA256 libpng.tar.gz
fetch "https://github.com/DanBloomberg/leptonica/releases/download/$LEPTONICA_VERSION/leptonica-$LEPTONICA_VERSION.tar.gz" $LEPTONICA_SHA256 leptonica.tar.gz
fetch "https://github.com/tesseract-ocr/tesseract/archive/refs/tags/$TESSERACT_VERSION.tar.gz" $TESSERACT_SHA256 tesseract.tar.gz
fetch "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$TESSDATA_COMMIT/eng.traineddata" $ENG_SHA256 eng.traineddata
cd "$work"
for f in zlib libpng leptonica tesseract; do tar -xzf $f.tar.gz; done

cmake_static=(-DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$prefix" -DCMAKE_PREFIX_PATH="$prefix"
              -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DBUILD_SHARED_LIBS=OFF)

(cd zlib-$ZLIB_VERSION && ./configure --static --prefix="$prefix" > /dev/null && make -j"$jobs" install > /dev/null)

cmake -S libpng-$LIBPNG_VERSION -B libpng-build "${cmake_static[@]}" \
  -DPNG_SHARED=OFF -DPNG_STATIC=ON -DPNG_TESTS=OFF -DPNG_TOOLS=OFF -DZLIB_ROOT="$prefix"
cmake --build libpng-build -j"$jobs" --target install

# PNG is all it's ever given (TesseractTextReader sends each line as a PNG on stdin). Without TIFF,
# tesseract prints "Error in pixReadMemTiff ... bmfCreate: font pixa not made" on every run: that's the
# font for its debug images, which it never draws here - the text it reads is unaffected.
cmake -S leptonica-$LEPTONICA_VERSION -B leptonica-build "${cmake_static[@]}" -DSW_BUILD=OFF \
  -DENABLE_ZLIB=ON -DENABLE_PNG=ON -DENABLE_GIF=OFF -DENABLE_JPEG=OFF -DENABLE_TIFF=OFF -DENABLE_WEBP=OFF -DENABLE_OPENJPEG=OFF
cmake --build leptonica-build -j"$jobs" --target install

# The tessdata_fast models are LSTM only, so the legacy engine isn't needed. OpenMP off: one line at a
# time, and it's slower that way anyway (tesseract issue #1662).
cmake -S tesseract-$TESSERACT_VERSION -B tesseract-build "${cmake_static[@]}" \
  -DBUILD_TRAINING_TOOLS=OFF -DDISABLED_LEGACY_ENGINE=ON -DOPENMP_BUILD=OFF -DGRAPHICS_DISABLED=ON \
  -DDISABLE_ARCHIVE=ON -DDISABLE_CURL=ON -DDISABLE_TIFF=ON -DENABLE_CCACHE=OFF -DINSTALL_CONFIGS=OFF \
  -DCMAKE_EXE_LINKER_FLAGS="-static-libstdc++ -static-libgcc"
cmake --build tesseract-build -j"$jobs" --target tesseract

mkdir -p "$out/tessdata" "$out/licenses"
install -m 755 tesseract-build/bin/tesseract "$out/tesseract" 2>/dev/null || install -m 755 tesseract-build/tesseract "$out/tesseract"
strip "$out/tesseract"
cp eng.traineddata "$out/tessdata/"
cp tesseract-$TESSERACT_VERSION/LICENSE "$out/licenses/tesseract.txt"
cp leptonica-$LEPTONICA_VERSION/leptonica-license.txt "$out/licenses/leptonica.txt"
cp libpng-$LIBPNG_VERSION/LICENSE "$out/licenses/libpng.txt"
cp zlib-$ZLIB_VERSION/LICENSE "$out/licenses/zlib.txt"

# Nothing but glibc: anything else here would have to be on the user's system.
deps=$(objdump -p "$out/tesseract" | awk '/NEEDED/ {print $2}' | grep -vE '^(libc|libm|libpthread|libdl|librt|ld-linux-x86-64)\.so' || true)
[ -z "$deps" ] || { echo "tesseract needs more than glibc: $deps" >&2; exit 1; }
"$out/tesseract" --version | head -1
"$out/tesseract" --tessdata-dir "$out/tessdata" --list-langs
