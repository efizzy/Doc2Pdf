<#
.SYNOPSIS
    Builds the native doc2pdf_native library on Windows for win-x64 and win-arm64.
#>
param(
    [string]$Configuration = "release",
    [switch]$Arm64 = $false
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path "$ScriptDir/.."
$CoreDir = Join-Path $RepoRoot "src/native/core"
$RuntimesDir = Join-Path $RepoRoot "src/Efizzy.Doc2Pdf/runtimes"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Efizzy.Doc2Pdf Native Windows Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Ensure cargo is on PATH
if (!(Get-Command cargo -ErrorAction SilentlyContinue)) {
    $CargoPath = "$env:USERPROFILE\.cargo\bin"
    if (Test-Path $CargoPath) {
        $env:PATH = "$CargoPath;$env:PATH"
    } else {
        throw "Cargo was not found. Please install Rust from https://rustup.rs/"
    }
}

Push-Location $CoreDir
try {
    # 1. Build x86_64
    Write-Host "`n>>> Building win-x64 ($Configuration) <<<" -ForegroundColor Yellow
    if ($Configuration -eq "release") {
        cargo build --release -p doc2pdf_native
        $srcPath = Join-Path $CoreDir "target/release/doc2pdf_native.dll"
    } else {
        cargo build -p doc2pdf_native
        $srcPath = Join-Path $CoreDir "target/debug/doc2pdf_native.dll"
    }

    $destDir = Join-Path $RuntimesDir "win-x64/native"
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    Copy-Item $srcPath (Join-Path $destDir "doc2pdf_native.dll") -Force
    Write-Host "Copied -> $destDir\doc2pdf_native.dll" -ForegroundColor Green

    # 2. Build ARM64 if requested
    if ($Arm64) {
        Write-Host "`n>>> Building win-arm64 ($Configuration) <<<" -ForegroundColor Yellow
        rustup target add aarch64-pc-windows-msvc
        if ($Configuration -eq "release") {
            cargo build --release -p doc2pdf_native --target aarch64-pc-windows-msvc
            $armSrc = Join-Path $CoreDir "target/aarch64-pc-windows-msvc/release/doc2pdf_native.dll"
        } else {
            cargo build -p doc2pdf_native --target aarch64-pc-windows-msvc
            $armSrc = Join-Path $CoreDir "target/aarch64-pc-windows-msvc/debug/doc2pdf_native.dll"
        }

        $armDest = Join-Path $RuntimesDir "win-arm64/native"
        New-Item -ItemType Directory -Force -Path $armDest | Out-Null
        Copy-Item $armSrc (Join-Path $armDest "doc2pdf_native.dll") -Force
        Write-Host "Copied -> $armDest\doc2pdf_native.dll" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}

Write-Host "`nNative Windows build completed successfully!" -ForegroundColor Green
