using System;
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
/// </summary>
public static class Doc2PdfConverter
{
    /// <summary>
    /// Converts a document file on disk directly into a PDF file on disk.
    /// </summary>
    /// <param name="inputPath">Path to the source document.</param>
    /// <param name="outputPath">Destination path for the output PDF.</param>
    /// <param name="options">Optional conversion configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown if inputPath or outputPath is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown if the input file does not exist.</exception>
    /// <exception cref="Doc2PdfException">Thrown if the native conversion fails.</exception>
    public static unsafe void ConvertFile(string inputPath, string outputPath, Doc2PdfOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentNullException(nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentNullException(nameof(outputPath));
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input document file was not found.", inputPath);

        string? outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        options ??= Doc2PdfOptions.Default;
        using var nativeOptsScope = new NativeOptionsScope(options);
        using var inPathScope = new Utf8Scope(inputPath);
        using var outPathScope = new Utf8Scope(outputPath);

        int status = NativeMethods.doc2pdf_convert_file(
            inPathScope.Pointer,
            outPathScope.Pointer,
            in nativeOptsScope.Options);

        if (status != 0)
        {
            string error = NativeMethods.GetLastError();
            throw new Doc2PdfException($"PDF conversion failed for '{inputPath}': {error}", status);
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
    /// </summary>
    /// <param name="inputBytes">Raw document bytes.</param>
    /// <param name="formatHint">Format hint (e.g. "docx", "md", "html", "rtf", "odt", "txt").</param>
    /// <param name="options">Optional conversion configuration.</param>
    /// <returns>Rendered PDF bytes.</returns>
    /// <exception cref="ArgumentException">Thrown if inputBytes is null or empty.</exception>
    /// <exception cref="Doc2PdfException">Thrown if the native conversion fails or output exceeds maximum buffer bounds.</exception>
    public static unsafe byte[] Convert(byte[] inputBytes, string formatHint = "docx", Doc2PdfOptions? options = null)
    {
        if (inputBytes == null || inputBytes.Length == 0)
            throw new ArgumentException("Input buffer cannot be null or empty.", nameof(inputBytes));

        options ??= Doc2PdfOptions.Default;
        formatHint = string.IsNullOrWhiteSpace(formatHint) ? "docx" : formatHint;

        using var nativeOptsScope = new NativeOptionsScope(options);
        using var formatScope = new Utf8Scope(formatHint);

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
        return Convert(inputBytes, format.ToFormatHint(), options);
    }

    /// <summary>
    /// Asynchronously converts in-memory document bytes to PDF bytes.
    /// </summary>
    public static Task<byte[]> ConvertAsync(byte[] inputBytes, string formatHint = "docx", Doc2PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Convert(inputBytes, formatHint, options);
        }, cancellationToken);
    }

    /// <summary>
    /// Converts a document stream into a PDF output stream.
    /// </summary>
    public static void Convert(Stream inputStream, Stream outputStream, string formatHint = "docx", Doc2PdfOptions? options = null)
    {
        if (inputStream == null) throw new ArgumentNullException(nameof(inputStream));
        if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

        byte[] inputBytes;
        if (inputStream is MemoryStream ms)
        {
            inputBytes = ms.ToArray();
        }
        else
        {
            using var tempMs = new MemoryStream();
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

    private sealed class NativeOptionsScope : IDisposable
    {
        public NativeOptions Options;
        private IntPtr _titlePtr;
        private IntPtr _authorPtr;
        private IntPtr _passwordPtr;
        private IntPtr _subjectPtr;
        private IntPtr _keywordsPtr;
        private IntPtr _watermarkTextPtr;
        private IntPtr _watermarkColorPtr;
        private IntPtr _pageRangePtr;
        private IntPtr _customFontsDirPtr;

        public NativeOptionsScope(Doc2PdfOptions opts)
        {
            _titlePtr = AllocUtf8(opts.Title);
            _authorPtr = AllocUtf8(opts.Author);
            _passwordPtr = AllocUtf8(opts.Password);
            _subjectPtr = AllocUtf8(opts.Subject);
            _keywordsPtr = AllocUtf8(opts.Keywords);
            _watermarkTextPtr = AllocUtf8(opts.WatermarkText);
            _watermarkColorPtr = AllocUtf8(opts.WatermarkColor);
            _pageRangePtr = AllocUtf8(opts.PageRange);
            _customFontsDirPtr = AllocUtf8(opts.CustomFontsDirectory);

            Options = new NativeOptions
            {
                IncludeMarkup = opts.IncludeMarkup,
                Comments = opts.ShowComments,
                Tagged = opts.TaggedPdf,
                Compress = opts.Compress,
                PaperWidthPt = opts.PaperSizePoints?.Width ?? 0f,
                PaperHeightPt = opts.PaperSizePoints?.Height ?? 0f,
                NumeralMode = (int)opts.NumeralMode,
                Title = _titlePtr,
                Author = _authorPtr,
                Password = _passwordPtr,
                Subject = _subjectPtr,
                Keywords = _keywordsPtr,
                WatermarkText = _watermarkTextPtr,
                WatermarkColor = _watermarkColorPtr,
                WatermarkDiagonal = opts.WatermarkDiagonal,
                WatermarkOpacity = opts.WatermarkOpacity,
                PageRange = _pageRangePtr,
                CustomFontsDir = _customFontsDirPtr
            };
        }

        private static IntPtr AllocUtf8(string? str)
        {
            if (str == null) return IntPtr.Zero;
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            IntPtr mem = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, mem, bytes.Length);
            Marshal.WriteByte(mem, bytes.Length, 0); // null terminator
            return mem;
        }

        public void Dispose()
        {
            Free(ref _titlePtr);
            Free(ref _authorPtr);
            Free(ref _passwordPtr);
            Free(ref _subjectPtr);
            Free(ref _keywordsPtr);
            Free(ref _watermarkTextPtr);
            Free(ref _watermarkColorPtr);
            Free(ref _pageRangePtr);
            Free(ref _customFontsDirPtr);
        }

        private static void Free(ref IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(ptr);
                ptr = IntPtr.Zero;
            }
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
    public static byte[] Convert(byte[] inputBytes, string formatHint = "docx", Doc2PdfOptions? options = null)
        => Doc2PdfConverter.Convert(inputBytes, formatHint, options);

    /// <summary>Converts document bytes to PDF bytes with format enum.</summary>
    public static byte[] Convert(byte[] inputBytes, DocumentFormat format, Doc2PdfOptions? options = null)
        => Doc2PdfConverter.Convert(inputBytes, format, options);

    /// <summary>Asynchronously converts document bytes to PDF bytes.</summary>
    public static Task<byte[]> ConvertAsync(byte[] inputBytes, string formatHint = "docx", Doc2PdfOptions? options = null, CancellationToken cancellationToken = default)
        => Doc2PdfConverter.ConvertAsync(inputBytes, formatHint, options, cancellationToken);

    /// <summary>Converts a document stream to a PDF stream.</summary>
    public static void Convert(Stream inputStream, Stream outputStream, string formatHint = "docx", Doc2PdfOptions? options = null)
        => Doc2PdfConverter.Convert(inputStream, outputStream, formatHint, options);

    /// <summary>Checks if a format extension is supported.</summary>
    public static bool IsFormatSupported(string extension)
        => Doc2PdfConverter.IsFormatSupported(extension);

    /// <summary>Returns native library version.</summary>
    public static string GetNativeVersion()
        => Doc2PdfConverter.GetNativeVersion();
}
