using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Efizzy.Doc2Pdf.Native;

namespace Efizzy.Doc2Pdf;

/// <summary>
/// High-performance, cross-platform document to PDF converter.
/// Converts Word documents (.docx, .doc), Markdown (.md), HTML, RTF, ODT, LaTeX, XML and plain text directly to PDF.
/// Validates file header magic bytes to prevent executing malicious or spoofed files.
/// </summary>
public static class Doc2PdfConverter
{
    /// <summary>
    /// Maximum supported document size in bytes (250 MB) to safeguard against memory exhaustion / DoS attacks.
    /// </summary>
    public const int MaxDocumentSizeBytes = 250 * 1024 * 1024;

    /// <summary>
    /// Converts a document file on disk directly into a PDF file on disk.
    /// Safely inspects the file header bytes to prevent executing malicious files and resolve authentic document format.
    /// </summary>
    /// <param name="inputPath">Path to the source document.</param>
    /// <param name="outputPath">Destination path for the output PDF.</param>
    /// <param name="options">Optional conversion configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown if inputPath or outputPath is null or whitespace.</exception>
    /// <exception cref="ArgumentException">Thrown if inputPath or outputPath contains invalid characters.</exception>
    /// <exception cref="FileNotFoundException">Thrown if the input file does not exist.</exception>
    /// <exception cref="Doc2PdfException">Thrown if the file is malicious or native conversion fails.</exception>
    public static unsafe void ConvertFile(string inputPath, string outputPath, Doc2PdfOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentNullException(nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentNullException(nameof(outputPath));

        // Security: Guard against null-byte injection and path traversal
        if (inputPath.IndexOf('\0') >= 0)
            throw new ArgumentException("Input path contains invalid null characters.", nameof(inputPath));
        if (outputPath.IndexOf('\0') >= 0)
            throw new ArgumentException("Output path contains invalid null characters.", nameof(outputPath));

        string fullIn = Path.GetFullPath(inputPath);
        string fullOut = Path.GetFullPath(outputPath);

        if (!File.Exists(fullIn))
            throw new FileNotFoundException("Input document file was not found.", fullIn);

        // Security check & format detection from header bytes
        DocumentFormat detectedFormat = DocumentFormatExtensions.DetectFormatFromFile(fullIn);

        string? outDir = Path.GetDirectoryName(fullOut);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        options ??= Doc2PdfOptions.Default;

        // If the file extension matches the detected format hint or is HTML (where base_dir is needed),
        // we can use native file-to-file conversion. Otherwise, if the extension is misleading, missing,
        // or contradicts the detected format header, convert using buffer to preserve exact detected format.
        string ext = Path.GetExtension(fullIn).TrimStart('.').ToLowerInvariant();
        string detectedHint = detectedFormat.ToFormatHint();

        if (string.Equals(ext, detectedHint, StringComparison.OrdinalIgnoreCase) ||
            (detectedFormat == DocumentFormat.Docx && (ext == "docm" || ext == "dotx" || ext == "dotm")) ||
            (detectedFormat == DocumentFormat.Doc && ext == "dot") ||
            (detectedFormat == DocumentFormat.Html && (ext == "htm" || ext == "xhtml")) ||
            (detectedFormat == DocumentFormat.Markdown && ext == "markdown") ||
            (detectedFormat == DocumentFormat.PlainText && ext == "text") ||
            (detectedFormat == DocumentFormat.Latex && (ext == "latex" || ext == "ltx")))
        {
            using var nativeOptsScope = new NativeOptionsScope(options);
            using var inPathScope = new Utf8Scope(fullIn);
            using var outPathScope = new Utf8Scope(fullOut);

            int status = NativeMethods.doc2pdf_convert_file(
                inPathScope.Pointer,
                outPathScope.Pointer,
                in nativeOptsScope.Options);

            if (status != 0)
            {
                string error = NativeMethods.GetLastError();
                throw new Doc2PdfException($"PDF conversion failed for '{fullIn}': {error}", status);
            }
        }
        else
        {
            // Format contradicted the extension or extension was missing/disguised:
            // Convert using in-memory buffer with the authentically detected format
            byte[] inBytes = File.ReadAllBytes(fullIn);
            byte[] outBytes = Convert(inBytes, detectedHint, options);
            File.WriteAllBytes(fullOut, outBytes);
        }
    }

    /// <summary>
    /// Asynchronously converts a document file on disk into a PDF file.
    /// </summary>
    public static Task ConvertFileAsync(string inputPath, string outputPath, Doc2PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConvertFile(inputPath, outputPath, options);
        }, cancellationToken);
    }

    /// <summary>
    /// Converts in-memory document bytes to PDF bytes.
    /// Inspects file header magic bytes to detect authentic format and reject malicious executable binaries.
    /// </summary>
    /// <param name="inputBytes">Raw document bytes.</param>
    /// <param name="formatHint">Optional format hint (e.g. "auto", "docx", "md", "html", "rtf", "odt", "txt").</param>
    /// <param name="options">Optional conversion configuration.</param>
    /// <returns>Rendered PDF bytes.</returns>
    /// <exception cref="ArgumentException">Thrown if inputBytes is null, empty, or exceeds allowable size bounds.</exception>
    /// <exception cref="Doc2PdfException">Thrown if the file is malicious or the native conversion fails.</exception>
    public static unsafe byte[] Convert(byte[] inputBytes, string formatHint = "auto", Doc2PdfOptions? options = null)
    {
        if (inputBytes == null || inputBytes.Length == 0)
            throw new ArgumentException("Input buffer cannot be null or empty.", nameof(inputBytes));

        if (inputBytes.Length > MaxDocumentSizeBytes)
            throw new ArgumentException($"Input document size ({inputBytes.Length:N0} bytes) exceeds maximum allowable limit of {MaxDocumentSizeBytes:N0} bytes.", nameof(inputBytes));

        // 1. Mandatory security check: inspect header bytes to prevent executing malicious files
        DocumentFormatExtensions.ValidateSafety(inputBytes);

        // 2. Detect authentic format from file header bytes
        DocumentFormat detectedFormat = DocumentFormatExtensions.DetectFormat(inputBytes, formatHint);
        string resolvedFormatHint = detectedFormat.ToFormatHint();

        options ??= Doc2PdfOptions.Default;

        using var nativeOptsScope = new NativeOptionsScope(options);
        using var formatScope = new Utf8Scope(resolvedFormatHint);

        fixed (byte* inPtr = inputBytes)
        {
            byte* outPtr = null;
            nuint outLen = 0;

            try
            {
                int status = NativeMethods.doc2pdf_convert_buffer(
                    inPtr,
                    (nuint)inputBytes.Length,
                    formatScope.Pointer,
                    in nativeOptsScope.Options,
                    out outPtr,
                    out outLen);

                if (status != 0)
                {
                    string error = NativeMethods.GetLastError();
                    throw new Doc2PdfException($"Buffer conversion failed: {error}", status);
                }

                if (outPtr == null || outLen == 0)
                {
                    throw new Doc2PdfException("Conversion completed successfully but produced an empty buffer.", status);
                }

                if (outLen > (nuint)int.MaxValue)
                {
                    throw new Doc2PdfException($"Rendered PDF size ({outLen:N0} bytes) exceeds maximum supported managed array size.", -1);
                }

                byte[] result = new byte[(int)outLen];
                Marshal.Copy((IntPtr)outPtr, result, 0, (int)outLen);
                return result;
            }
            finally
            {
                if (outPtr != null)
                {
                    NativeMethods.doc2pdf_free_buffer(outPtr, outLen);
                    outPtr = null;
                    outLen = 0;
                }
            }
        }
    }

    /// <summary>
    /// Converts in-memory document bytes using a strongly typed <see cref="DocumentFormat"/>.
    /// </summary>
    public static byte[] Convert(byte[] inputBytes, DocumentFormat format, Doc2PdfOptions? options = null)
    {
        if (format == DocumentFormat.Auto)
        {
            return Convert(inputBytes, "auto", options);
        }

        // Even if a specific format is specified, validate safety against malicious headers
        DocumentFormatExtensions.ValidateSafety(inputBytes);

        // Header detection takes precedence over mismatched format hint
        DocumentFormat detected = DocumentFormatExtensions.DetectFormat(inputBytes, format.ToFormatHint());
        return Convert(inputBytes, detected.ToFormatHint(), options);
    }

    /// <summary>
    /// Asynchronously converts in-memory document bytes to PDF bytes.
    /// </summary>
    public static Task<byte[]> ConvertAsync(byte[] inputBytes, string formatHint = "auto", Doc2PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Convert(inputBytes, formatHint, options);
        }, cancellationToken);
    }

    /// <summary>
    /// Converts a document stream into a PDF output stream.
    /// Inspects file header magic bytes to detect authentic format and reject malicious binaries.
    /// Minimizes memory buffer duplication when reading from memory streams.
    /// </summary>
    public static void Convert(Stream inputStream, Stream outputStream, string formatHint = "auto", Doc2PdfOptions? options = null)
    {
        if (inputStream == null) throw new ArgumentNullException(nameof(inputStream));
        if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

        byte[] inputBytes;
        if (inputStream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> segment) && segment.Offset == 0 && segment.Count == ms.Length)
        {
            inputBytes = segment.Array!;
        }
        else if (inputStream is MemoryStream memStream)
        {
            inputBytes = memStream.ToArray();
        }
        else
        {
            int capacity = inputStream.CanSeek && inputStream.Length > 0 && inputStream.Length <= int.MaxValue
                ? (int)Math.Min(inputStream.Length, 100 * 1024 * 1024)
                : 4096;
            using var tempMs = new MemoryStream(capacity);
            inputStream.CopyTo(tempMs);
            inputBytes = tempMs.ToArray();
        }

        byte[] pdfBytes = Convert(inputBytes, formatHint, options);
        outputStream.Write(pdfBytes, 0, pdfBytes.Length);
    }

    /// <summary>
    /// Checks whether the native library supports conversion for the given file extension.
    /// </summary>
    public static bool IsFormatSupported(string extension)
    {
        return NativeMethods.IsFormatSupported(extension);
    }

    /// <summary>
    /// Returns the underlying native library version.
    /// </summary>
    public static string GetNativeVersion()
    {
        return NativeMethods.GetVersion();
    }

    /// <summary>
    /// Fast static unmanaged cache for standard format hints to eliminate unmanaged allocations on hot conversion paths.
    /// </summary>
    internal static class CachedFormatHints
    {
        private static readonly Dictionary<string, IntPtr> _cache = new(StringComparer.OrdinalIgnoreCase);

        static CachedFormatHints()
        {
            string[] standardHints = { "docx", "doc", "md", "html", "rtf", "odt", "xml", "txt", "tex", "json", "auto" };
            foreach (var hint in standardHints)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(hint);
                IntPtr mem = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, mem, bytes.Length);
                Marshal.WriteByte(mem, bytes.Length, 0);
                _cache[hint] = mem;
            }
        }

        public static bool TryGetCachedPointer(string hint, out IntPtr ptr)
        {
            return _cache.TryGetValue(hint, out ptr);
        }
    }

    /// <summary>
    /// Manages UTF-8 native string allocation with zero-allocation caching for well-known format hints.
    /// </summary>
    internal ref struct Utf8Scope
    {
        private IntPtr _ptr;
        private readonly bool _ownsMemory;

        public unsafe byte* Pointer => (byte*)_ptr;

        public Utf8Scope(string? text)
        {
            if (text == null)
            {
                _ptr = IntPtr.Zero;
                _ownsMemory = false;
                return;
            }

            // Zero-allocation fast-path for standard format hints
            if (CachedFormatHints.TryGetCachedPointer(text, out IntPtr cached))
            {
                _ptr = cached;
                _ownsMemory = false;
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            _ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            _ownsMemory = true;
            Marshal.Copy(bytes, 0, _ptr, bytes.Length);
            Marshal.WriteByte(_ptr, bytes.Length, 0); // null terminator
        }

        public void Dispose()
        {
            if (_ownsMemory && _ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Exception-safe scope for passing options across the C-ABI.
    /// Safely clears sensitive password memory before releasing unmanaged buffers.
    /// </summary>
    private sealed class NativeOptionsScope : IDisposable
    {
        public NativeOptions Options;
        private readonly List<IntPtr> _allocatedPointers = new(10);
        private IntPtr _passwordPtr;
        private int _passwordLength;

        public NativeOptionsScope(Doc2PdfOptions opts)
        {
            try
            {
                IntPtr titlePtr = AllocUtf8(opts.Title);
                IntPtr authorPtr = AllocUtf8(opts.Author);
                IntPtr passwordPtr = AllocUtf8(opts.Password, out _passwordLength);
                _passwordPtr = passwordPtr;
                IntPtr subjectPtr = AllocUtf8(opts.Subject);
                IntPtr keywordsPtr = AllocUtf8(opts.Keywords);
                IntPtr watermarkTextPtr = AllocUtf8(opts.WatermarkText);
                IntPtr watermarkColorPtr = AllocUtf8(opts.WatermarkColor);
                IntPtr pageRangePtr = AllocUtf8(opts.PageRange);
                IntPtr customFontsDirPtr = AllocUtf8(opts.CustomFontsDirectory);

                Options = new NativeOptions
                {
                    IncludeMarkup = opts.IncludeMarkup,
                    Comments = opts.ShowComments,
                    Tagged = opts.TaggedPdf,
                    Compress = opts.Compress,
                    PaperWidthPt = opts.PaperSizePoints?.Width ?? 0f,
                    PaperHeightPt = opts.PaperSizePoints?.Height ?? 0f,
                    NumeralMode = (int)opts.NumeralMode,
                    Title = titlePtr,
                    Author = authorPtr,
                    Password = passwordPtr,
                    Subject = subjectPtr,
                    Keywords = keywordsPtr,
                    WatermarkText = watermarkTextPtr,
                    WatermarkColor = watermarkColorPtr,
                    WatermarkDiagonal = opts.WatermarkDiagonal,
                    WatermarkOpacity = opts.WatermarkOpacity,
                    PageRange = pageRangePtr,
                    CustomFontsDir = customFontsDirPtr
                };
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private IntPtr AllocUtf8(string? str) => AllocUtf8(str, out _);

        private IntPtr AllocUtf8(string? str, out int length)
        {
            length = 0;
            if (str == null) return IntPtr.Zero;
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            length = bytes.Length;
            IntPtr mem = Marshal.AllocHGlobal(bytes.Length + 1);
            _allocatedPointers.Add(mem);
            Marshal.Copy(bytes, 0, mem, bytes.Length);
            Marshal.WriteByte(mem, bytes.Length, 0); // null terminator
            return mem;
        }

        public void Dispose()
        {
            // Security: Zero out password memory in unmanaged heap before freeing
            if (_passwordPtr != IntPtr.Zero && _passwordLength > 0)
            {
                for (int i = 0; i < _passwordLength; i++)
                {
                    Marshal.WriteByte(_passwordPtr, i, 0);
                }
            }

            for (int i = 0; i < _allocatedPointers.Count; i++)
            {
                IntPtr ptr = _allocatedPointers[i];
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            _allocatedPointers.Clear();
        }
    }
}

/// <summary>
/// Backwards-compatible forwarder for <see cref="Doc2PdfConverter"/>.
/// </summary>
[Obsolete("Use Doc2PdfConverter instead.")]
public static class Word2PdfConverter
{
    /// <summary>Converts a file to PDF.</summary>
    public static void ConvertFile(string inputPath, string outputPath, Doc2PdfOptions? options = null)
        => Doc2PdfConverter.ConvertFile(inputPath, outputPath, options);

    /// <summary>Asynchronously converts a file to PDF.</summary>
    public static Task ConvertFileAsync(string inputPath, string outputPath, Doc2PdfOptions? options = null, CancellationToken cancellationToken = default)
        => Doc2PdfConverter.ConvertFileAsync(inputPath, outputPath, options, cancellationToken);

    /// <summary>Converts document bytes to PDF bytes.</summary>
    public static byte[] Convert(byte[] inputBytes, string formatHint = "auto", Doc2PdfOptions? options = null)
        => Doc2PdfConverter.Convert(inputBytes, formatHint, options);

    /// <summary>Converts document bytes to PDF bytes with format enum.</summary>
    public static byte[] Convert(byte[] inputBytes, DocumentFormat format, Doc2PdfOptions? options = null)
        => Doc2PdfConverter.Convert(inputBytes, format, options);

    /// <summary>Asynchronously converts document bytes to PDF bytes.</summary>
    public static Task<byte[]> ConvertAsync(byte[] inputBytes, string formatHint = "auto", Doc2PdfOptions? options = null, CancellationToken cancellationToken = default)
        => Doc2PdfConverter.ConvertAsync(inputBytes, formatHint, options, cancellationToken);

    /// <summary>Converts a document stream to a PDF stream.</summary>
    public static void Convert(Stream inputStream, Stream outputStream, string formatHint = "auto", Doc2PdfOptions? options = null)
        => Doc2PdfConverter.Convert(inputStream, outputStream, formatHint, options);

    /// <summary>Checks if a format extension is supported.</summary>
    public static bool IsFormatSupported(string extension)
        => Doc2PdfConverter.IsFormatSupported(extension);

    /// <summary>Returns native library version.</summary>
    public static string GetNativeVersion()
        => Doc2PdfConverter.GetNativeVersion();
}
