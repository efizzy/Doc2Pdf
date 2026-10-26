using System;
using System.IO;
using System.Text;

namespace Efizzy.Doc2Pdf;

/// <summary>
/// Document formats supported by Efizzy.Doc2Pdf for conversion to PDF.
/// </summary>
public enum DocumentFormat
{
    /// <summary>
    /// Automatic detection from file content signature and magic header bytes.
    /// </summary>
    Auto,

    /// <summary>
    /// Microsoft Word 2007+ OpenXML Document (.docx, .docm, .dotx, .dotm).
    /// </summary>
    Docx,

    /// <summary>
    /// Legacy Microsoft Word 97-2003 Binary Document (.doc, .dot).
    /// </summary>
    Doc,

    /// <summary>
    /// Markdown document (.md, .markdown) - CommonMark with GFM tables.
    /// </summary>
    Markdown,

    /// <summary>
    /// HyperText Markup Language (.html, .htm, .xhtml).
    /// </summary>
    Html,

    /// <summary>
    /// Rich Text Format (.rtf).
    /// </summary>
    Rtf,

    /// <summary>
    /// OpenDocument Text (.odt).
    /// </summary>
    Odt,

    /// <summary>
    /// LaTeX document (.tex, .latex, .ltx).
    /// </summary>
    Latex,

    /// <summary>
    /// Plain text document (.txt, .text).
    /// </summary>
    PlainText,

    /// <summary>
    /// Word XML Document (Flat OPC or WordprocessingML 2003).
    /// </summary>
    Xml,

    /// <summary>
    /// Efizzy Document JSON AST (.json, .doc.json).
    /// </summary>
    Json
}

/// <summary>
/// Secure, zero-allocation header inspection and format resolution utilities for <see cref="DocumentFormat"/>.
/// Inspects file magic bytes and header structures to identify authentic document types
/// and prevent accidental execution or processing of disguised malicious binaries.
/// </summary>
public static class DocumentFormatExtensions
{
    // Well-known magic signatures
    private static readonly byte[] RtfSignature = new byte[] { 0x7B, 0x5C, 0x72, 0x74, 0x66 }; // {\rtf
    private static readonly byte[] OleDocSignature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }; // CFBF OLE2
    private static readonly byte[] ZipSignature = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // PK\x03\x04
    private static readonly byte[] ZipSpanned = new byte[] { 0x50, 0x4B, 0x07, 0x08 }; // PK\x07\x08
    private static readonly byte[] ZipEmpty = new byte[] { 0x50, 0x4B, 0x05, 0x06 }; // PK\x05\x06
    private static readonly byte[] ElfSignature = new byte[] { 0x7F, 0x45, 0x4C, 0x46 }; // \x7FELF
    private static readonly byte[] WindowsShortcutSignature = new byte[] { 0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00 }; // Windows LNK
    private static readonly byte[] ChmSignature = new byte[] { 0x49, 0x54, 0x53, 0x46 }; // ITSF (Compiled HTML Help)

    /// <summary>
    /// Returns the standard format hint string passed to the native converter.
    /// </summary>
    public static string ToFormatHint(this DocumentFormat format) => format switch
    {
        DocumentFormat.Docx => "docx",
        DocumentFormat.Doc => "doc",
        DocumentFormat.Markdown => "md",
        DocumentFormat.Html => "html",
        DocumentFormat.Rtf => "rtf",
        DocumentFormat.Odt => "odt",
        DocumentFormat.Latex => "tex",
        DocumentFormat.PlainText => "txt",
        DocumentFormat.Xml => "xml",
        DocumentFormat.Json => "json",
        DocumentFormat.Auto => "auto",
        _ => "docx"
    };

    /// <summary>
    /// Validates that the input bytes do not contain any known dangerous or executable header signatures.
    /// Throws a <see cref="Doc2PdfException"/> with status -403 if a malicious or executable signature is detected.
    /// </summary>
    /// <param name="bytes">Raw file or buffer bytes.</param>
    /// <param name="sourceIdentifier">Optional file name or stream identifier for diagnostic error reporting.</param>
    /// <exception cref="Doc2PdfException">Thrown if an executable or malicious signature is detected.</exception>
    public static void ValidateSafety(byte[]? bytes, string? sourceIdentifier = null)
    {
        if (bytes == null || bytes.Length == 0) return;

        if (IsDangerousOrExecutable(bytes, out string? reason))
        {
            string target = string.IsNullOrEmpty(sourceIdentifier) ? "Input document" : $"File '{sourceIdentifier}'";
            throw new Doc2PdfException($"Security validation rejected {target}: {reason}", -403);
        }
    }

    /// <summary>
    /// Inspects the file header bytes to determine if the content contains dangerous or executable binary signatures.
    /// Detects Windows PE (EXE/DLL), Linux ELF, macOS Mach-O, Java bytecode, Windows LNK shortcuts, and shell scripts.
    /// </summary>
    /// <param name="bytes">File or buffer bytes to inspect.</param>
    /// <param name="reason">Human-readable description of the detected threat.</param>
    /// <returns>True if the file header matches a known executable or malicious format, false otherwise.</returns>
    public static bool IsDangerousOrExecutable(byte[]? bytes, out string? reason)
    {
        reason = null;
        if (bytes == null || bytes.Length == 0) return false;

        // 1. Windows PE / DOS Executable (MZ or ZM signature)
        if (bytes.Length >= 2 && ((bytes[0] == 0x4D && bytes[1] == 0x5A) || (bytes[0] == 0x5A && bytes[1] == 0x4D)))
        {
            reason = "Potentially malicious executable detected (DOS/Windows PE 'MZ' header). Executable binaries cannot be converted to PDF.";
            return true;
        }

        // 2. Linux / UNIX ELF binary (\x7FELF)
        if (bytes.Length >= 4 && Matches(bytes, ElfSignature))
        {
            reason = "Potentially malicious executable detected (Linux/Unix ELF binary header). Executable binaries cannot be converted to PDF.";
            return true;
        }

        // 3. macOS Mach-O & Java Class Bytecode
        if (bytes.Length >= 4)
        {
            if ((bytes[0] == 0xFE && bytes[1] == 0xED && bytes[2] == 0xFA && bytes[3] == 0xCE) || // 32-bit BE
                (bytes[0] == 0xCE && bytes[1] == 0xFA && bytes[2] == 0xED && bytes[3] == 0xFE) || // 32-bit LE
                (bytes[0] == 0xFE && bytes[1] == 0xED && bytes[2] == 0xFA && bytes[3] == 0xCF) || // 64-bit BE
                (bytes[0] == 0xCF && bytes[1] == 0xFA && bytes[2] == 0xED && bytes[3] == 0xFE))   // 64-bit LE
            {
                reason = "Potentially malicious executable detected (macOS Mach-O binary header). Executable binaries cannot be converted to PDF.";
                return true;
            }

            if (bytes[0] == 0xCA && bytes[1] == 0xFE && bytes[2] == 0xBA && bytes[3] == 0xBE) // Mach-O Universal FAT or Java bytecode
            {
                reason = "Potentially malicious executable detected (Mach-O Universal FAT binary / Java bytecode).";
                return true;
            }
        }

        // 4. Windows Shell Link (.lnk)
        if (bytes.Length >= 8 && Matches(bytes, WindowsShortcutSignature))
        {
            reason = "Potentially malicious file detected (Windows Shell Link .lnk shortcut).";
            return true;
        }

        // 5. Unix Shebang Script (#!)
        if (bytes.Length >= 2 && bytes[0] == (byte)'#' && bytes[1] == (byte)'!')
        {
            reason = "Potentially malicious script detected (Unix shebang #! header). Shell scripts cannot be converted to PDF.";
            return true;
        }

        // 6. Compiled HTML Help (ITSF / .chm)
        if (bytes.Length >= 4 && Matches(bytes, ChmSignature))
        {
            reason = "Potentially malicious file detected (Compiled HTML Help .chm header).";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Safely detects the authentic <see cref="DocumentFormat"/> by inspecting file header bytes.
    /// Performs zero allocations on structured binary formats (DOCX, DOC, RTF, ODT).
    /// </summary>
    /// <param name="bytes">Raw document bytes to inspect.</param>
    /// <param name="extensionHint">Optional file path or extension hint for text format disambiguation.</param>
    /// <returns>The resolved authentic document format.</returns>
    /// <exception cref="Doc2PdfException">Thrown if the header matches an executable or malicious binary signature.</exception>
    public static DocumentFormat DetectFormat(byte[] bytes, string? extensionHint = null)
    {
        if (bytes == null || bytes.Length == 0)
            return DocumentFormat.Docx;

        // 1. Mandatory security verification against malicious executables
        ValidateSafety(bytes, extensionHint);

        // 2. Exact magic bytes check for structured binary document formats (Zero Allocation)
        if (bytes.Length >= 5 && Matches(bytes, RtfSignature))
        {
            return DocumentFormat.Rtf;
        }

        if (bytes.Length >= 8 && Matches(bytes, OleDocSignature))
        {
            return DocumentFormat.Doc;
        }

        if (bytes.Length >= 4 && (Matches(bytes, ZipSignature) || Matches(bytes, ZipEmpty) || Matches(bytes, ZipSpanned)))
        {
            // Disambiguate ZIP packages: ODT vs DOCX without allocating strings
            if (IsOdtZipPackage(bytes))
            {
                return DocumentFormat.Odt;
            }
            return DocumentFormat.Docx;
        }

        // 3. Inspect text-based document formats (HTML, XML, JSON, LaTeX, Markdown, PlainText)
        int bomOffset = GetBomOffset(bytes);
        int sampleLength = Math.Min(bytes.Length - bomOffset, 4096);
        if (sampleLength <= 0)
        {
            return NormalizeExtensionHint(extensionHint, DocumentFormat.PlainText);
        }

        string textSample = DecodeTextSample(bytes, bomOffset, sampleLength);
        string trimmed = textSample.TrimStart();

        // 3a. HTML detection
        if (IsHtmlContent(trimmed, textSample))
        {
            return DocumentFormat.Html;
        }

        // 3b. XML / WordprocessingML detection
        if (IsXmlContent(trimmed, extensionHint))
        {
            return DocumentFormat.Xml;
        }

        // 3c. JSON document AST detection
        if (IsJsonContent(trimmed))
        {
            return DocumentFormat.Json;
        }

        // 3d. LaTeX detection
        if (IsLatexContent(trimmed))
        {
            return DocumentFormat.Latex;
        }

        // 3e. Markdown detection
        if (IsMarkdownContent(trimmed, extensionHint))
        {
            return DocumentFormat.Markdown;
        }

        // 3f. Plain text vs unknown binary
        if (IsPrintableText(bytes, bomOffset, sampleLength))
        {
            if (IsMarkdownExtension(extensionHint))
            {
                return DocumentFormat.Markdown;
            }
            return DocumentFormat.PlainText;
        }

        // If bytes contain non-text control characters / null bytes and didn't match any known format
        throw new Doc2PdfException(
            "Unsupported file format: File header bytes do not correspond to any recognized document format (DOCX, DOC, RTF, ODT, HTML, XML, Markdown, JSON, LaTeX, PlainText).",
            -415);
    }

    /// <summary>
    /// Safely detects the authentic <see cref="DocumentFormat"/> from a stream by inspecting its initial header bytes.
    /// Preserves stream position if the stream is seekable.
    /// </summary>
    public static DocumentFormat DetectFormat(Stream stream, string? extensionHint = null)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        long originalPos = stream.CanSeek ? stream.Position : 0;
        byte[] buffer = new byte[4096];
        int bytesRead = 0;

        while (bytesRead < buffer.Length)
        {
            int n = stream.Read(buffer, bytesRead, buffer.Length - bytesRead);
            if (n == 0) break;
            bytesRead += n;
        }

        if (stream.CanSeek)
        {
            stream.Position = originalPos;
        }

        if (bytesRead == buffer.Length)
        {
            return DetectFormat(buffer, extensionHint);
        }

        byte[] headerBytes = new byte[bytesRead];
        Array.Copy(buffer, headerBytes, bytesRead);
        return DetectFormat(headerBytes, extensionHint);
    }

    /// <summary>
    /// Safely detects the authentic <see cref="DocumentFormat"/> from a file on disk by reading its header bytes.
    /// </summary>
    public static DocumentFormat DetectFormatFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found for format inspection.", filePath);

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return DetectFormat(fs, Path.GetFileName(filePath));
    }

    /// <summary>
    /// Detects the format from a file path or extension string.
    /// Note: Extension-based format detection is vulnerable to extension spoofing.
    /// Prefer <see cref="DetectFormat(byte[], string?)"/> or <see cref="DetectFormatFromFile(string)"/>
    /// which validate file header magic bytes directly.
    /// </summary>
    public static DocumentFormat FromPathOrExtension(string pathOrExt)
    {
        if (string.IsNullOrWhiteSpace(pathOrExt))
            return DocumentFormat.Docx;

        string ext = Path.GetExtension(pathOrExt).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
            ext = pathOrExt.TrimStart('.').ToLowerInvariant();

        return ext switch
        {
            "docx" or "docm" or "dotx" or "dotm" => DocumentFormat.Docx,
            "doc" or "dot" => DocumentFormat.Doc,
            "md" or "markdown" => DocumentFormat.Markdown,
            "html" or "htm" or "xhtml" => DocumentFormat.Html,
            "rtf" => DocumentFormat.Rtf,
            "odt" => DocumentFormat.Odt,
            "tex" or "latex" or "ltx" => DocumentFormat.Latex,
            "txt" or "text" => DocumentFormat.PlainText,
            "xml" => DocumentFormat.Xml,
            "json" => DocumentFormat.Json,
            _ => DocumentFormat.Docx
        };
    }

    private static bool Matches(byte[] bytes, byte[] signature)
    {
        if (bytes.Length < signature.Length) return false;
        for (int i = 0; i < signature.Length; i++)
        {
            if (bytes[i] != signature[i]) return false;
        }
        return true;
    }

    private static bool IsOdtZipPackage(byte[] bytes)
    {
        if (bytes.Length < 38) return false;

        // In standard ODF packages, the first file entry is uncompressed "mimetype" at offset 30
        if (bytes[30] == (byte)'m' && bytes[31] == (byte)'i' && bytes[32] == (byte)'m' &&
            bytes[33] == (byte)'e' && bytes[34] == (byte)'t' && bytes[35] == (byte)'y' &&
            bytes[36] == (byte)'p' && bytes[37] == (byte)'e')
        {
            int searchLimit = Math.Min(bytes.Length, 256);
            if (ContainsAscii(bytes, 30, searchLimit - 30, "opendocument.text") ||
                ContainsAscii(bytes, 30, searchLimit - 30, "oasis.opendocument"))
            {
                return true;
            }
        }

        // Secondary scan across first 512 bytes for "oasis.opendocument.text"
        int maxScan = Math.Min(bytes.Length, 512);
        return ContainsAscii(bytes, 0, maxScan, "oasis.opendocument.text");
    }

    private static bool ContainsAscii(byte[] bytes, int offset, int length, string pattern)
    {
        if (bytes.Length < offset + length || pattern.Length > length) return false;
        int limit = offset + length - pattern.Length;
        for (int i = offset; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                byte b = bytes[i + j];
                char c = pattern[j];
                // Case-insensitive ASCII comparison
                if (b != c && (b | 0x20) != (c | 0x20))
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }

    private static int GetBomOffset(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return 3; // UTF-8 BOM
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return 2; // UTF-16 BOM
        return 0;
    }

    private static string DecodeTextSample(byte[] bytes, int offset, int length)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, offset, length);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, offset, length);

        return Encoding.UTF8.GetString(bytes, offset, length);
    }

    private static bool IsHtmlContent(string trimmed, string fullSample)
    {
        if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<head", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<body", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.StartsWith("<!--") || trimmed.StartsWith("<?xml"))
        {
            if (fullSample.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullSample.IndexOf("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullSample.IndexOf("xmlns=\"http://www.w3.org/1999/xhtml\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsXmlContent(string trimmed, string? extensionHint)
    {
        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<w:wordDocument", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<pkg:package", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string ext = (extensionHint ?? "").TrimStart('.').ToLowerInvariant();
        if (ext == "xml" && trimmed.StartsWith("<") && trimmed.Contains(">"))
        {
            return true;
        }

        return false;
    }

    private static bool IsJsonContent(string trimmed)
    {
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            if (trimmed.Contains("\"") && (trimmed.Contains(":") || trimmed.EndsWith("}") || trimmed.EndsWith("]")))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsLatexContent(string trimmed)
    {
        return trimmed.StartsWith("\\documentclass", StringComparison.OrdinalIgnoreCase) ||
               trimmed.IndexOf("\\documentclass", StringComparison.OrdinalIgnoreCase) >= 0 ||
               trimmed.IndexOf("\\begin{document}", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsMarkdownContent(string trimmed, string? extensionHint)
    {
        if (IsMarkdownExtension(extensionHint))
            return true;

        return trimmed.StartsWith("# ") ||
               trimmed.StartsWith("## ") ||
               trimmed.StartsWith("### ") ||
               trimmed.StartsWith("```") ||
               trimmed.StartsWith("---") ||
               trimmed.StartsWith("> ") ||
               trimmed.Contains("\n| ") ||
               trimmed.Contains("\r\n| ") ||
               trimmed.StartsWith("| ");
    }

    private static bool IsMarkdownExtension(string? extensionHint)
    {
        string ext = (extensionHint ?? "").TrimStart('.').ToLowerInvariant();
        return ext == "md" || ext == "markdown";
    }

    private static bool IsPrintableText(byte[] bytes, int offset, int length)
    {
        int limit = Math.Min(bytes.Length, offset + length);
        for (int i = offset; i < limit; i++)
        {
            byte b = bytes[i];
            if (b == 0 || (b < 9) || (b > 10 && b < 13) || (b > 13 && b < 32))
            {
                return false;
            }
        }
        return true;
    }

    private static DocumentFormat NormalizeExtensionHint(string? extensionHint, DocumentFormat fallback)
    {
        if (string.IsNullOrWhiteSpace(extensionHint)) return fallback;
        string ext = extensionHint!.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "md" or "markdown" => DocumentFormat.Markdown,
            "html" or "htm" or "xhtml" => DocumentFormat.Html,
            "xml" => DocumentFormat.Xml,
            "json" => DocumentFormat.Json,
            "tex" or "latex" or "ltx" => DocumentFormat.Latex,
            "txt" or "text" => DocumentFormat.PlainText,
            _ => fallback
        };
    }
}
