#!/usr/bin/env bash
set -euo pipefail

# Determine script directory and repo root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CORE_DIR="$REPO_ROOT/src/native/core"
RUNTIMES_DIR="$REPO_ROOT/src/Efizzy.Doc2Pdf/runtimes"

echo "========================================"
echo "Efizzy.Doc2Pdf Native Linux / macOS Build"
echo "========================================"

OS="$(uname -s)"
ARCH="$(uname -m)"

cd "$CORE_DIR"

if [ "$OS" = "Linux" ]; then
    echo ">>> Building Linux native library ($ARCH) <<<"
    cargo build --release -p doc2pdf_native

    if [ "$ARCH" = "x86_64" ]; then
        RID="linux-x64"
    elif [ "$ARCH" = "aarch64" ]; then
        RID="linux-arm64"
    else
        RID="linux-$ARCH"
    fi

    DEST_DIR="$RUNTIMES_DIR/$RID/native"
    mkdir -p "$DEST_DIR"
    cp "target/release/libdoc2pdf_native.so" "$DEST_DIR/libdoc2pdf_native.so"
    echo "Copied -> $DEST_DIR/libdoc2pdf_native.so"

elif [ "$OS" = "Darwin" ]; then
    echo ">>> Building macOS universal / native library ($ARCH) <<<"
    cargo build --release -p doc2pdf_native

    if [ "$ARCH" = "arm64" ]; then
        RID="osx-arm64"
    else
        RID="osx-x64"
    fi

    DEST_DIR="$RUNTIMES_DIR/$RID/native"
    mkdir -p "$DEST_DIR"
    cp "target/release/libdoc2pdf_native.dylib" "$DEST_DIR/libdoc2pdf_native.dylib"
    echo "Copied -> $DEST_DIR/libdoc2pdf_native.dylib"
else
    echo "Unsupported OS: $OS"
    exit 1
fi

echo ""
echo "Native build completed successfully!"
