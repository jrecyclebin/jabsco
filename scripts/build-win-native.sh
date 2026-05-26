#!/usr/bin/env bash
# Cross-compiles a minimal freerdp3.dll for Windows x64 using the mingw-w64 toolchain.
# Produces: native/win-x64/freerdp3.dll
#
# Dependencies: x86_64-w64-mingw32-gcc, cmake, make, perl (for OpenSSL)
#
# Usage:
#   ./scripts/build-win-native.sh            # build if not present
#   ./scripts/build-win-native.sh --force    # rebuild from scratch
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT="$REPO/native/win-x64"
SHIM="$REPO/native/freerdp-shim"
TOOLCHAIN="$REPO/cmake/toolchain-win-x64.cmake"
CACHE="$HOME/.cache/jabsco-win-native"
BUILD="$CACHE/build"
DEPS="$CACHE/deps"  # install prefix for zlib, openssl, cjson, uriparser
FREERDP_INSTALL="$CACHE/freerdp"

ZLIBNG_VERSION="2.2.4"
OPENSSL_VERSION="3.3.3"
CJSON_VERSION="1.7.18"
URIPARSER_VERSION="0.9.9"
FREERDP_VERSION="3.26.0"

CROSS="x86_64-w64-mingw32"
JOBS=$(nproc)

# ── Helpers ───────────────────────────────────────────────────────────────────

info() { echo "→ $*"; }

download() {
    local url="$1" dest="$2"
    [[ -f "$dest" ]] && return
    info "Downloading $(basename "$dest")..."
    mkdir -p "$(dirname "$dest")"
    curl -sL "$url" -o "$dest"
}

# ── Flags ─────────────────────────────────────────────────────────────────────

if [[ "${1:-}" == "--force" ]]; then
    rm -rf "$BUILD" "$DEPS" "$FREERDP_INSTALL"
fi

if [[ -f "$OUTPUT/freerdp3.dll" && "${1:-}" != "--force" ]]; then
    echo "✓ $OUTPUT/freerdp3.dll already exists. Pass --force to rebuild."
    exit 0
fi

mkdir -p "$CACHE/src" "$BUILD" "$DEPS" "$FREERDP_INSTALL" "$OUTPUT"

BASE_CMAKE_FLAGS=(
    -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN"
    -DCMAKE_INSTALL_PREFIX="$DEPS"
    -DCMAKE_BUILD_TYPE=Release
    -DBUILD_SHARED_LIBS=OFF
)

# ── zlib-ng (compat mode — installs as libz.a, found by FreeRDP as zlib) ──────

if [[ ! -f "$DEPS/lib/libz.a" ]]; then
    info "Building zlib-ng $ZLIBNG_VERSION (compat mode)..."
    SRC="$CACHE/src/zlib-ng-$ZLIBNG_VERSION"
    download "https://github.com/zlib-ng/zlib-ng/archive/$ZLIBNG_VERSION.tar.gz" \
             "$CACHE/src/zlib-ng-$ZLIBNG_VERSION.tar.gz"
    [[ -d "$SRC" ]] || tar xzf "$CACHE/src/zlib-ng-$ZLIBNG_VERSION.tar.gz" -C "$CACHE/src"

    cmake -S "$SRC" -B "$BUILD/zlib" \
        "${BASE_CMAKE_FLAGS[@]}" \
        -DZLIB_COMPAT=ON \
        -DZLIB_ENABLE_TESTS=OFF \
        -G "Unix Makefiles" -Wno-dev
    cmake --build "$BUILD/zlib" -j"$JOBS"
    cmake --install "$BUILD/zlib"
    [[ -f "$DEPS/lib/libz.a" ]] || { echo "libz.a not found after install:"; ls "$DEPS/lib/"; exit 1; }
fi

# ── OpenSSL ───────────────────────────────────────────────────────────────────

if [[ ! -f "$DEPS/lib/libssl.a" ]]; then
    info "Building OpenSSL $OPENSSL_VERSION..."
    SRC="$CACHE/src/openssl-$OPENSSL_VERSION"
    download "https://github.com/openssl/openssl/releases/download/openssl-$OPENSSL_VERSION/openssl-$OPENSSL_VERSION.tar.gz" \
             "$CACHE/src/openssl-$OPENSSL_VERSION.tar.gz"
    [[ -d "$SRC" ]] || tar xzf "$CACHE/src/openssl-$OPENSSL_VERSION.tar.gz" -C "$CACHE/src"

    pushd "$SRC" > /dev/null
    ./Configure mingw64 \
        --cross-compile-prefix="$CROSS-" \
        --prefix="$DEPS" \
        --libdir=lib \
        --openssldir="$DEPS/ssl" \
        no-shared no-tests no-apps \
        no-engine no-dynamic-engine \
        -static
    make -j"$JOBS" build_sw
    make install_sw
    popd > /dev/null
    # Some OpenSSL configs still install to lib64 — normalise
    if [[ -f "$DEPS/lib64/libssl.a" && ! -f "$DEPS/lib/libssl.a" ]]; then
        cp -a "$DEPS/lib64/." "$DEPS/lib/"
    fi
fi

# ── cJSON ─────────────────────────────────────────────────────────────────────

if [[ ! -f "$DEPS/lib/libcjson.a" ]]; then
    info "Building cJSON $CJSON_VERSION..."
    SRC="$CACHE/src/cJSON-$CJSON_VERSION"
    download "https://github.com/DaveGamble/cJSON/archive/v$CJSON_VERSION.tar.gz" \
             "$CACHE/src/cjson-$CJSON_VERSION.tar.gz"
    [[ -d "$SRC" ]] || tar xzf "$CACHE/src/cjson-$CJSON_VERSION.tar.gz" -C "$CACHE/src"

    cmake -S "$SRC" -B "$BUILD/cjson" \
        "${BASE_CMAKE_FLAGS[@]}" \
        -DENABLE_CJSON_TEST=OFF \
        -DENABLE_CJSON_UTILS=OFF \
        -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
        -G "Unix Makefiles" -Wno-dev
    cmake --build "$BUILD/cjson" -j"$JOBS"
    cmake --install "$BUILD/cjson"
fi

# ── uriparser ─────────────────────────────────────────────────────────────────

if [[ ! -f "$DEPS/lib/liburiparser.a" ]]; then
    info "Building uriparser $URIPARSER_VERSION..."
    SRC="$CACHE/src/uriparser-$URIPARSER_VERSION"
    download "https://github.com/uriparser/uriparser/releases/download/uriparser-$URIPARSER_VERSION/uriparser-$URIPARSER_VERSION.tar.bz2" \
             "$CACHE/src/uriparser-$URIPARSER_VERSION.tar.bz2"
    [[ -d "$SRC" ]] || tar xjf "$CACHE/src/uriparser-$URIPARSER_VERSION.tar.bz2" -C "$CACHE/src"

    cmake -S "$SRC" -B "$BUILD/uriparser" \
        "${BASE_CMAKE_FLAGS[@]}" \
        -DURIPARSER_BUILD_DOCS=OFF \
        -DURIPARSER_BUILD_TESTS=OFF \
        -DURIPARSER_BUILD_TOOLS=OFF \
        -G "Unix Makefiles" -Wno-dev
    cmake --build "$BUILD/uriparser" -j"$JOBS"
    cmake --install "$BUILD/uriparser"
fi

# ── FreeRDP (static) ──────────────────────────────────────────────────────────

if ! ls "$FREERDP_INSTALL/lib/libfreerdp"*.a &>/dev/null; then
    info "Building FreeRDP $FREERDP_VERSION (static, minimal features)..."
    SRC="$CACHE/src/FreeRDP-$FREERDP_VERSION"
    download "https://github.com/FreeRDP/FreeRDP/archive/$FREERDP_VERSION.tar.gz" \
             "$CACHE/src/freerdp-$FREERDP_VERSION.tar.gz"
    [[ -d "$SRC" ]] || tar xzf "$CACHE/src/freerdp-$FREERDP_VERSION.tar.gz" -C "$CACHE/src"

    # Pre-load cache so FindZLIB/FindOpenSSL pick up our static libs before
    # CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY can interfere.
    INIT_CACHE="$BUILD/freerdp-init.cmake"
    cat > "$INIT_CACHE" <<EOF
set(ZLIB_INCLUDE_DIR "$DEPS/include"    CACHE PATH     "" FORCE)
set(ZLIB_LIBRARY     "$DEPS/lib/libz.a" CACHE FILEPATH "" FORCE)
set(OPENSSL_ROOT_DIR    "$DEPS"           CACHE PATH "" FORCE)
set(OPENSSL_INCLUDE_DIR "$DEPS/include"   CACHE PATH "" FORCE)
set(OPENSSL_USE_STATIC_LIBS TRUE CACHE BOOL "" FORCE)
# FindOpenSSL.cmake MINGW branch uses LIB_EAY/SSL_EAY as intermediates;
# pre-seeding them bypasses the find_library search that can't see our deps dir.
set(LIB_EAY "$DEPS/lib/libcrypto.a" CACHE FILEPATH "" FORCE)
set(SSL_EAY "$DEPS/lib/libssl.a"    CACHE FILEPATH "" FORCE)
EOF

    cmake -S "$SRC" -B "$BUILD/freerdp" \
        -C "$INIT_CACHE" \
        -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" \
        -DCMAKE_INSTALL_PREFIX="$FREERDP_INSTALL" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_C_FLAGS="-D__STDC_NO_THREADS__ -Wno-error=incompatible-pointer-types" \
        -DCMAKE_CXX_FLAGS="-D__STDC_NO_THREADS__" \
        -DBUILD_SHARED_LIBS=OFF \
        -DWITH_CLIENT=ON \
        -DWITH_SERVER=OFF \
        -DWITH_SHADOW=OFF \
        -DWITH_SAMPLE=OFF \
        -DWITH_DEMO_CLIENT=OFF \
        -DWITH_PROXY=OFF \
        -DWITH_FFMPEG=OFF \
        -DWITH_DSP_FFMPEG=OFF \
        -DWITH_SWSCALE=OFF \
        -DWITH_SDL=OFF \
        -DWITH_CLIENT_SDL=OFF \
        -DWITH_CLIENT_SDL2=OFF \
        -DWITH_CLIENT_SDL3=OFF \
        -DWITH_CAIRO=OFF \
        -DWITH_CUPS=OFF \
        -DWITH_FUSE=OFF \
        -DWITH_USB=OFF \
        -DCHANNEL_URBDRC=OFF \
        -DWITH_LIBSYSTEMD=OFF \
        -DWITH_GSM=OFF \
        -DWITH_LAME=OFF \
        -DWITH_SOXR=OFF \
        -DWITH_FAAD2=OFF \
        -DWITH_FAAC=OFF \
        -DWITH_OPUS=OFF \
        -DWITH_AAD=OFF \
        -DWITH_PKCS11=OFF \
        -DWITH_SANITIZE_ADDRESS=OFF \
        -DBUILD_TESTING=OFF \
        -DWITH_JSON_DISABLED=ON \
        -DWITH_URIPARSER=OFF \
        -DWITH_PROXY_MODULES=OFF \
        -DUSE_UNWIND=OFF \
        "-DCMAKE_DISABLE_FIND_PACKAGE_json-c=ON" \
        "-DCMAKE_DISABLE_FIND_PACKAGE_jansson=ON" \
        -G "Unix Makefiles" -Wno-dev
    cmake --build "$BUILD/freerdp" -j"$JOBS" \
        --target freerdp freerdp-client winpr winpr-tools

    # Copy static libs and headers directly — skips cmake install rules
    # which try to link executables we don't need.
    mkdir -p "$FREERDP_INSTALL/lib" "$FREERDP_INSTALL/include"
    cp "$BUILD/freerdp/libfreerdp/libfreerdp3.a"          "$FREERDP_INSTALL/lib/"
    cp "$BUILD/freerdp/client/common/libfreerdp-client3.a" "$FREERDP_INSTALL/lib/"
    cp "$BUILD/freerdp/winpr/libwinpr/libwinpr3.a"         "$FREERDP_INSTALL/lib/"
    cp -r "$SRC/include/freerdp"  "$FREERDP_INSTALL/include/"
    cp -r "$SRC/winpr/include/winpr" "$FREERDP_INSTALL/include/"
    # Also copy generated config headers (freerdp/config.h must be under freerdp/)
    cp "$BUILD/freerdp/config.h"                                 "$FREERDP_INSTALL/include/freerdp/" 2>/dev/null || true
    mkdir -p "$FREERDP_INSTALL/include/winpr/config"
    cp "$BUILD/freerdp/winpr/include/winpr/config/winpr-config.h" \
       "$FREERDP_INSTALL/include/winpr/config/"                 2>/dev/null || true
fi

# ── Link freerdp3.dll ─────────────────────────────────────────────────────────

info "Linking freerdp3.dll..."

# Collect static libs — order matters for GNU ld
FREERDP_LIBS=(
    "$FREERDP_INSTALL/lib/libfreerdp-client3.a"
    "$FREERDP_INSTALL/lib/libfreerdp3.a"
    "$FREERDP_INSTALL/lib/libwinpr3.a"
)

DEP_LIBS=(
    "$DEPS/lib/libssl.a"
    "$DEPS/lib/libcrypto.a"
    "$DEPS/lib/libz.a"
)

WIN_LIBS=(
    -lws2_32 -lcrypt32 -lncrypt -ladvapi32 -luser32
    -lkernel32 -lshlwapi -lrpcrt4 -lbcrypt -ldbghelp
    -liphlpapi -lole32 -lshell32 -luuid -lgdi32
    -lsecur32 -lwinmm -lwinspool -lcredui -lntdll
    -lcomctl32 -lcomdlg32 -lversion -lmswsock
)

# Ensure generated config headers are present (cmake puts them in the build include tree).
mkdir -p "$FREERDP_INSTALL/include/freerdp" "$FREERDP_INSTALL/include/winpr/config"
cp "$BUILD/freerdp/include/freerdp/config.h" "$FREERDP_INSTALL/include/freerdp/" 2>/dev/null || \
    cp "$BUILD/freerdp/config.h"             "$FREERDP_INSTALL/include/freerdp/" 2>/dev/null || true
cp "$BUILD/freerdp/winpr/include/winpr/config/winpr-config.h" \
   "$FREERDP_INSTALL/include/winpr/config/" 2>/dev/null || true

# No --whole-archive: the DEF file forces resolution of the 19 exported
# symbols, and the linker pulls in only the transitively required objects.
# --whole-archive caused the linker to include ALL channel plugin code
# (audio, printer, etc.), triggering thousands of unneeded Windows API deps
# and ultimately a linker crash on duplicate .rsrc sections.
"$CROSS-gcc" -shared \
    -o "$OUTPUT/freerdp3.dll" \
    -I"$FREERDP_INSTALL/include" \
    -I"$BUILD/freerdp/winpr/include" \
    -I"$BUILD/freerdp/include" \
    -I"$DEPS/include" \
    -D__STDC_NO_THREADS__ \
    "$SHIM/dllmain.c" \
    "$SHIM/freerdp3.def" \
    "${FREERDP_LIBS[@]}" \
    "${DEP_LIBS[@]}" \
    "${WIN_LIBS[@]}" \
    -static-libgcc \
    -Wl,-Bstatic -lwinpthread -Wl,-Bdynamic \
    -Wl,--allow-multiple-definition

# libwinpthread-1.dll is baked into WinPR's compiled object files as a
# dynamic import — ship it alongside freerdp3.dll.
cp "/usr/$CROSS/bin/libwinpthread-1.dll" "$OUTPUT/"

info "Verifying exports..."
"$CROSS-objdump" -p "$OUTPUT/freerdp3.dll" | grep "DLL Name:" | sort

echo ""
echo "✓ freerdp3.dll + libwinpthread-1.dll → $OUTPUT/"
echo "  freerdp3.dll:       $(du -sh "$OUTPUT/freerdp3.dll" | cut -f1)"
echo "  libwinpthread-1.dll: $(du -sh "$OUTPUT/libwinpthread-1.dll" | cut -f1)"
