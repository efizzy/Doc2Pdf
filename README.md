# Efizzy.Doc2Pdf

A high-performance, zero-dependency .NET library and NuGet package for converting Microsoft Word documents (.docx, .doc), HTML, Markdown, RTF, OpenDocument (.odt), LaTeX, XML, and plain text directly into publication-quality PDFs.

Powered by native in-process engine written in pure Rust extracted from [WordCraft](https://github.com/storytold/wordcraft) (`storytold/wordcraft`), **Efizzy.Doc2Pdf** produces real, searchable, selectable PDFs with embedded font subsets, layout preservation, hyperlinks, and document bookmarks—with zero dependency on Microsoft Office, LibreOffice, Puppeteer, Chromium, or external processes.

---

## Features

- **Multi-Format Input:**
  - **Microsoft Word:** `.docx`, `.docm`, `.dotx`, `.dotm`
  - **Legacy Word:** Word 97–2003 `.doc`, `.dot`
  - **Web Documents:** Semantic `.html`, `.htm`, `.xhtml` with automated in-process CSS inlining (`css-inline`) and high-fidelity print layout (`fulgur`)
  - **Markdown:** CommonMark with GitHub-flavored markdown (GFM) tables `.md`, `.markdown`
  - **Rich Text Format:** `.rtf`
  - **OpenDocument:** `.odt`
  - **LaTeX:** `.tex`, `.latex`, `.ltx`
  - **Plain Text:** `.txt`, `.text` (auto-detects UTF-8, UTF-16, Win-1252 encodings)
  - **Word XML:** Flat OPC and WordprocessingML 2003 `.xml`
  - **Efizzy AST:** `.json`, `.doc.json`
- **High-Quality PDF Output:**
  - Real, selectable text with embedded font subsets.
  - Full layout fidelity: tables, margins, alignments, headers, footers, footnotes, images, and vector drawings.
  - Interactive elements: Clickable URLs, `#bookmark` internal anchors, and document outline hierarchy.
  - Optional accessibility: Tagged PDF structure tree (PDF/UA best effort).
  - Review markup: Show tracked changes (insertions/deletions) and comment balloons in the margins.
  - Paper sizing: Scale to standard sizes (Letter, A4, Legal, A3, A5) or retain original document geometry.
- **Enterprise Features:**
  - **Dynamic Watermarking:** Diagonal or horizontal customizable watermarks (text, hex color, opacity).
  - **Page Range Filtering:** Selectively render specific pages (e.g. `"1-3, 5"`).
  - **Custom Fonts Directory:** Load and catalog custom `.ttf`, `.otf`, `.ttc` fonts on the fly.
  - **PDF Dublin Core / XMP Metadata:** Custom Title, Author, Subject, and Keywords.
- **Blazing Native Performance:**
  - Sub-50ms typical document conversion with in-process C-ABI / P/Invoke calls.
  - Multi-target .NET support: `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`.
  - Minimal memory footprint (~50 MB working set, 0.5 MB managed GC heap).

---

## Installation

Install the **Efizzy.Doc2Pdf** NuGet package:

```bash
dotnet add package Efizzy.Doc2Pdf
```

---

## Quick Start

### 1. Simple File Conversion

```csharp
using Efizzy.Doc2Pdf;

// Convert a Word DOCX document to PDF
Doc2PdfConverter.ConvertFile("Contract.docx", "Contract.pdf");

// Convert a Markdown file to PDF
Doc2PdfConverter.ConvertFile("README.md", "README.pdf");

// Convert an HTML file to PDF
Doc2PdfConverter.ConvertFile("invoice.html", "invoice.pdf");
```

### 2. In-Memory Byte Array & Stream Conversion

```csharp
using Efizzy.Doc2Pdf;

byte[] docxBytes = File.ReadAllBytes("Document.docx");

// Convert bytes directly in memory
byte[] pdfBytes = Doc2PdfConverter.Convert(docxBytes, DocumentFormat.Docx);
File.WriteAllBytes("Document.pdf", pdfBytes);

// Stream-to-stream conversion (ideal for ASP.NET Core Web APIs)
using var inputStream = File.OpenRead("Notes.md");
using var outputStream = File.Create("Notes.pdf");
Doc2PdfConverter.Convert(inputStream, outputStream, formatHint: "md");
```

### 3. Enterprise Features (Watermarking, Page Ranges, Metadata)

```csharp
using Efizzy.Doc2Pdf;

var options = new Doc2PdfOptions
{
    // Enterprise Watermark
    WatermarkText = "CONFIDENTIAL",
    WatermarkColor = "#C53030",
    WatermarkDiagonal = true,
    WatermarkOpacity = 0.2f,

    // Page Range Filter (1-based)
    PageRange = "1-3, 5",

    // Custom Fonts Cataloging
    CustomFontsDirectory = @"C:\CorporateFonts",

    // Document Metadata
    Title = "Q4 Financial Audit",
    Author = "Corporate Governance",
    Subject = "Audit Report",
    Keywords = "audit, finance, 2026, compliance",

    // Paper Scaling
    PaperSizePoints = StandardPaperSize.A4,

    // Review Options
    IncludeMarkup = true,      // Show tracked changes
    ShowComments = true,       // Render comment balloons in margin
    TaggedPdf = true,          // Accessible tagged PDF
    Compress = true            // Compress PDF streams
};

Doc2PdfConverter.ConvertFile("AnnualAudit.docx", "AnnualAudit.pdf", options);
```

---

## Interactive Web Testing App

An interactive, browser-based web testing tool is included in `samples/Efizzy.Doc2Pdf.Web`:

```bash
dotnet run --project samples/Efizzy.Doc2Pdf.Web
```

Open your browser at **`http://localhost:5248`**:

- **Drag & Drop:** Upload any supported file format.
- **One-Click Samples:** Test Markdown, HTML, and RTF samples instantly.
- **Enterprise Panel:** Test watermarking, page range filtering, paper size scaling, and metadata in real-time.
- **Live PDF Preview:** View generated PDFs directly in the embedded browser preview with millisecond performance timing.

---

## Command-Line Tool

A standalone CLI tool with built-in benchmarking is provided in `samples/Efizzy.Doc2Pdf.Cli`:

```bash
# Convert a document
dotnet run --project samples/Efizzy.Doc2Pdf.Cli -- input.docx output.pdf --watermark "SAMPLE" --paper A4

# Run performance and resource benchmark
dotnet run --project samples/Efizzy.Doc2Pdf.Cli -- -c Release -- --benchmark
```

---

## Cross-Platform Runtime Support

**Efizzy.Doc2Pdf** provides full cross-platform compatibility across operating systems, architectures, and .NET runtimes:

| Operating System                  | Architectures | Shared Library Name       | Runtime Identifier (RID)   |
| --------------------------------- | ------------- | ------------------------- | -------------------------- |
| **Windows**                       | x64, ARM64    | `doc2pdf_native.dll`      | `win-x64`, `win-arm64`     |
| **Linux (glibc)**                 | x64, ARM64    | `libdoc2pdf_native.so`    | `linux-x64`, `linux-arm64` |
| **Linux (musl / Alpine)**         | x64           | `libdoc2pdf_native.so`    | `linux-musl-x64`           |
| **macOS (Apple Silicon & Intel)** | ARM64, x64    | `libdoc2pdf_native.dylib` | `osx-arm64`, `osx-x64`     |

### Multi-Target .NET Support

- **.NET 8.0, 9.0, 10.0+:** Dynamically resolved at runtime via `NativeLibrary.SetDllImportResolver` with intelligent fallback probing.
- **.NET Standard 2.0 / .NET Framework:** Includes automated in-process dynamic loader shims (`LoadLibraryExW` on Windows, `dlopen` with `RTLD_GLOBAL` on Linux and macOS) to ensure zero-configuration loading.
- **Transitive MSBuild Assets:** `build/` and `buildTransitive/` targets automatically copy the matching native binary to the project output directory on all target platforms.

---

## Architecture & Project Structure

```
Efizzy.Doc2Pdf/
├── .github/
│   └── workflows/
│       └── build-and-release.yml      # Multi-OS CI/CD pipeline (Windows, Ubuntu, macOS)
│
├── scripts/
│   ├── build-native.ps1               # PowerShell native build script (Windows x64 / ARM64)
│   ├── build-native.sh                # Bash native build script (Linux & macOS)
│   └── Dockerfile.linux-build         # Docker container build for Linux shared objects
│
├── src/
│   ├── Efizzy.Doc2Pdf/                # Multi-target .NET library (netstandard2.0; net8.0; net9.0; net10.0)
│   │   ├── Doc2PdfConverter.cs        # Primary public API & backwards-compatible Word2PdfConverter
│   │   ├── Doc2PdfOptions.cs          # Configuration options (Watermark, Metadata, PaperSize)
│   │   ├── DocumentFormat.cs          # Document format enumeration & auto-detection
│   │   ├── Doc2PdfException.cs        # Typed exceptions with status codes
│   │   ├── Native/
│   │   │   ├── NativeMethods.cs       # P/Invoke C-ABI bindings (doc2pdf_native)
│   │   │   └── NativeLibraryResolver.cs # Cross-platform dynamic library resolver (Win/Linux/macOS)
│   │   ├── build/                     # MSBuild target copy hooks
│   │   ├── buildTransitive/           # Transitive project reference hooks
│   │   └── runtimes/                  # Multi-platform native binaries
│   │       ├── win-x64/native/doc2pdf_native.dll
│   │       ├── win-arm64/native/doc2pdf_native.dll
│   │       ├── linux-x64/native/libdoc2pdf_native.so
│   │       ├── linux-arm64/native/libdoc2pdf_native.so
│   │       ├── linux-musl-x64/native/libdoc2pdf_native.so
│   │       ├── osx-arm64/native/libdoc2pdf_native.dylib
│   │       └── osx-x64/native/libdoc2pdf_native.dylib
│   │
│   └── native/core/                   # High-performance headless Rust engine
│       └── crates/
│           ├── doc2pdf_native/        # C-ABI export crate (cdylib/staticlib)
│           ├── doc/                   # Document model AST
│           ├── docx/                  # OOXML reader & writer
│           ├── docbin/                # Word 97-2003 binary reader
│           ├── formats/               # HTML, Markdown, RTF, ODT, LaTeX parsers
│           ├── fonts/                 # Font database and HarfBuzz shaper
│           ├── layout/                # Line breaking, pagination, and tables
│           ├── pdf/                   # Vector PDF generator (craft-pdf / Krilla)
│           ├── geom/                  # Geometric units and transforms
│           ├── proof/                 # Language proofing and hyphenation
│           ├── metafile/              # Windows EMF/WMF record parser
│           └── engine/                # Core headless document pipeline
│
├── samples/
│   ├── Efizzy.Doc2Pdf.Cli/            # CLI utility and benchmarking suite
│   └── Efizzy.Doc2Pdf.Web/            # ASP.NET Core web application and interactive viewer
│
└── tests/
    └── Efizzy.Doc2Pdf.Tests/          # Comprehensive xUnit test suite
```

---

## Building from Source

### Prerequisites

- [.NET 8.0, 9.0, or 10.0 SDK](https://dotnet.microsoft.com/)
- [Rust toolchain](https://rustup.rs/) (Cargo)

### 1. Build Native Engine

```powershell
# Windows
.\scripts\build-native.ps1

# Linux / macOS
./scripts/build-native.sh
```

### 2. Run Tests

```powershell
dotnet test tests/Efizzy.Doc2Pdf.Tests/Efizzy.Doc2Pdf.Tests.csproj -c Release
```

### 3. Create Multi-Platform NuGet Package

```powershell
dotnet pack src/Efizzy.Doc2Pdf/Efizzy.Doc2Pdf.csproj -c Release -o ./artifacts
```

---

## Acknowledgments & Credits

**Efizzy.Doc2Pdf** stands on the shoulders of exceptional open-source projects across the Rust and document processing communities:

- **[WordCraft](https://github.com/storytold/wordcraft)** (`storytold/wordcraft`) – The foundational headless document model AST, OOXML (.docx) and legacy (.doc) binary format parsers, HarfBuzz/Skrifa font shaping pipeline, and document pagination/layout engine.
- **[Fulgur](https://github.com/duskmoon314/fulgur)** (`duskmoon314/fulgur`) – High-fidelity, modern HTML and CSS to PDF conversion engine, integrating Mozilla Servo's `stylo` CSS engine and `taffy` flexbox/grid layout.
- **[css-inline](https://github.com/Stranger6667/css-inline)** (`Stranger6667/css-inline`) – High-performance in-process CSS style inliner that compiles internal `<style>` rules directly into element `style` attributes prior to rendering.
- **[Krilla](https://github.com/storytold/craft-libs)** & **[craft-pdf](https://github.com/storytold/craft-libs)** – Compact pure-Rust vector PDF emission, dynamic font subsetting, and accessible document outline hierarchy.

---

## License

This project is licensed under either the **MIT License** or the **Apache License 2.0**.
