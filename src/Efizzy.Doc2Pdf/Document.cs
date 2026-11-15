using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Efizzy;

namespace Efizzy.Doc2Pdf;

/// <summary>
/// Represents a loaded document in memory or referenced on disk, ready for conversion to PDF.
/// Provides automatic format detection, security signature inspection, and versatile conversion
/// targets including files, byte arrays, and writable streams.
/// </summary>
public class Document : IDisposable
{
    private byte[]? _payload;
    private string? _sourcePath;
    private DocumentFormat _format = DocumentFormat.Auto;
    private bool _isLoaded;
    private long _length;
    private bool _disposed;

    /// <summary>
    /// Indicates whether a document payload is currently loaded.
    /// </summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>
    /// The resolved or explicitly specified document format as <see cref="DocumentFormat"/>.
    /// </summary>
    public DocumentFormat Format => _format;

    /// <summary>
    /// The physical file path if loaded from disk, otherwise <c>null</c>.
    /// </summary>
    public string? SourcePath => _sourcePath;

    /// <summary>
    /// Size of the raw document in bytes.
    /// </summary>
    public long Length => _length;

    // =========================================================================
    // Constructors
    // =========================================================================

    /// <summary>
    /// Creates an empty, uninitialized document instance.
    /// Use <c>LoadFromFile</c>, <c>LoadFromBytes</c>, <c>LoadFromString</c>, or <c>LoadFromStream</c> to load content.
    /// </summary>
    public Document()
    {
    }

    /// <summary>
    /// Loads a document from a file path. Auto-detects the authentic format from file header magic bytes and extension.
    /// </summary>
    /// <param name="filePath">Path to the document on disk.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="filePath"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <exception cref="Doc2PdfException">Thrown if the file has dangerous executable headers or invalid syntax.</exception>
    public Document(string filePath)
    {
        LoadFromFile(filePath, DocumentFormat.Auto);
    }

    /// <summary>
    /// Loads a document from a file path with an explicit format hint.
    /// </summary>
    /// <param name="filePath">Path to the document on disk.</param>
    /// <param name="format">Format hint (default: <see cref="DocumentFormat.Auto"/>).</param>
    public Document(string filePath, DocumentFormat format)
    {
        LoadFromFile(filePath, format);
    }

    /// <summary>
    /// Loads a document from raw in-memory bytes.
    /// </summary>
    /// <param name="bytes">Raw document bytes.</param>
    /// <param name="format">Format hint (default: <see cref="DocumentFormat.Auto"/>).</param>
    public Document(byte[] bytes, DocumentFormat format = DocumentFormat.Auto)
    {
        LoadFromBytes(bytes, format);
    }

    /// <summary>
    /// Loads a document from a readable stream.
    /// </summary>
    /// <param name="stream">Readable stream containing the document payload.</param>
    /// <param name="format">Format hint (default: <see cref="DocumentFormat.Auto"/>).</param>
    public Document(Stream stream, DocumentFormat format = DocumentFormat.Auto)
    {
        LoadFromStream(stream, format);
    }

    // =========================================================================
    // Static Factories
    // =========================================================================

    /// <summary>
    /// Creates and loads a new <see cref="Document"/> from a file path.
    /// </summary>
    public static Document FromFile(string filePath, DocumentFormat format = DocumentFormat.Auto)
        => new Document(filePath, format);

    /// <summary>
    /// Creates and loads a new <see cref="Document"/> from raw bytes.
    /// </summary>
    public static Document FromBytes(byte[] bytes, DocumentFormat format = DocumentFormat.Auto)
        => new Document(bytes, format);

    /// <summary>
    /// Creates and loads a new <see cref="Document"/> from text/markup content.
    /// </summary>
    public static Document FromString(string content, DocumentFormat format, Encoding? encoding = null)
    {
        var doc = new Document();
        doc.LoadFromString(content, format, encoding);
        return doc;
    }

    /// <summary>
    /// Creates and loads a new <see cref="Document"/> from a readable stream.
    /// </summary>
    public static Document FromStream(Stream stream, DocumentFormat format = DocumentFormat.Auto)
        => new Document(stream, format);

    // =========================================================================
    // Loading Methods
    // =========================================================================

    /// <summary>
    /// Loads or reloads document content from a physical file.
    /// </summary>
    /// <param name="filePath">Path to the document file.</param>
    /// <param name="format">Format hint (default: <see cref="DocumentFormat.Auto"/>).</param>
    /// <returns>This document instance for fluent chaining.</returns>
    public Document LoadFromFile(string filePath, DocumentFormat format = DocumentFormat.Auto)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));
        if (filePath.IndexOf('\0') >= 0)
            throw new ArgumentException("File path contains invalid null characters.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Input document file was not found.", fullPath);

        var fi = new FileInfo(fullPath);
        if (fi.Length > Doc2PdfConverter.MaxDocumentSizeBytes)
            throw new ArgumentException($"File size ({fi.Length:N0} bytes) exceeds maximum allowable limit of {Doc2PdfConverter.MaxDocumentSizeBytes:N0} bytes.", nameof(filePath));

        DocumentFormat detected = DocumentFormatExtensions.DetectFormatFromFile(fullPath);
        _format = format != DocumentFormat.Auto ? format : detected;
        _sourcePath = fullPath;
        _payload = null;
        _length = fi.Length;
        _isLoaded = true;
        return this;
    }

    /// <summary>
    /// Loads or reloads document content from an in-memory byte array.
    /// </summary>
    /// <param name="bytes">Raw document bytes.</param>
    /// <param name="format">Format hint (default: <see cref="DocumentFormat.Auto"/>).</param>
    /// <returns>This document instance for fluent chaining.</returns>
    public Document LoadFromBytes(byte[] bytes, DocumentFormat format = DocumentFormat.Auto)
    {
        ThrowIfDisposed();
        if (bytes == null || bytes.Length == 0)
            throw new ArgumentException("Input buffer cannot be null or empty.", nameof(bytes));

        if (bytes.Length > Doc2PdfConverter.MaxDocumentSizeBytes)
            throw new ArgumentException($"Input document size ({bytes.Length:N0} bytes) exceeds maximum allowable limit of {Doc2PdfConverter.MaxDocumentSizeBytes:N0} bytes.", nameof(bytes));

        DocumentFormatExtensions.ValidateSafety(bytes);

        DocumentFormat detected = DocumentFormatExtensions.DetectFormat(bytes, format.ToFormatHint());
        _format = format != DocumentFormat.Auto ? format : detected;
        _payload = bytes;
        _sourcePath = null;
        _length = bytes.Length;
        _isLoaded = true;
        return this;
    }

    /// <summary>
    /// Loads document content from a text or markup string (Markdown, HTML, LaTeX, XML, RTF, PlainText).
    /// </summary>
    /// <param name="content">Document text or markup.</param>
    /// <param name="format">Document format (e.g., <see cref="DocumentFormat.Markdown"/>, <see cref="DocumentFormat.Html"/>).</param>
    /// <param name="encoding">Character encoding (default UTF-8).</param>
    /// <returns>This document instance for fluent chaining.</returns>
    public Document LoadFromString(string content, DocumentFormat format, Encoding? encoding = null)
    {
        ThrowIfDisposed();
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        encoding ??= Encoding.UTF8;
        byte[] bytes = encoding.GetBytes(content);
        return LoadFromBytes(bytes, format);
    }

    /// <summary>
    /// Loads document content from a readable stream.
    /// </summary>
    /// <param name="stream">Readable stream containing document payload.</param>
    /// <param name="format">Format hint (default: <see cref="DocumentFormat.Auto"/>).</param>
    /// <returns>This document instance for fluent chaining.</returns>
    public Document LoadFromStream(Stream stream, DocumentFormat format = DocumentFormat.Auto)
    {
        ThrowIfDisposed();
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        byte[] inputBytes;
        if (stream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> segment) && segment.Offset == 0 && segment.Count == ms.Length)
        {
            inputBytes = segment.Array!;
        }
        else if (stream is MemoryStream memStream)
        {
            inputBytes = memStream.ToArray();
        }
        else
        {
            int capacity = stream.CanSeek && stream.Length > 0 && stream.Length <= int.MaxValue
                ? (int)Math.Min(stream.Length, 100 * 1024 * 1024)
                : 4096;
            using var tempMs = new MemoryStream(capacity);
            stream.CopyTo(tempMs);
            inputBytes = tempMs.ToArray();
        }

        return LoadFromBytes(inputBytes, format);
    }

    // =========================================================================
    // Conversion Methods (Synchronous)
    // =========================================================================

    /// <summary>
    /// Converts the loaded document to PDF and returns the resulting bytes.
    /// </summary>
    /// <param name="options">Optional conversion configuration.</param>
    /// <returns>Rendered PDF bytes.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no document has been loaded.</exception>
    /// <exception cref="Doc2PdfException">Thrown if the native conversion fails.</exception>
    public byte[] ConvertToPdf(Doc2PdfOptions? options = null)
    {
        ThrowIfDisposed();
        EnsureLoaded();

        byte[] inputBytes = GetPayloadBytes();
        return Doc2PdfConverter.Convert(inputBytes, _format, options);
    }

    /// <summary>
    /// Converts the loaded document to PDF and saves it to a file on disk.
    /// </summary>
    /// <param name="outputPath">Destination path for the generated PDF.</param>
    /// <param name="options">Optional conversion configuration.</param>
    /// <exception cref="InvalidOperationException">Thrown if no document has been loaded.</exception>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="outputPath"/> is null or whitespace.</exception>
    /// <exception cref="Doc2PdfException">Thrown if the native conversion fails.</exception>
    public void ConvertToPdf(string outputPath, Doc2PdfOptions? options = null)
    {
        ThrowIfDisposed();
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentNullException(nameof(outputPath));
        if (outputPath.IndexOf('\0') >= 0)
            throw new ArgumentException("Output path contains invalid null characters.", nameof(outputPath));

        string fullOut = Path.GetFullPath(outputPath);
        string? outDir = Path.GetDirectoryName(fullOut);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        if (_sourcePath != null)
        {
            if (_format == DocumentFormat.Auto)
            {
                Doc2PdfConverter.ConvertFile(_sourcePath, fullOut, options);
            }
            else
            {
                string ext = Path.GetExtension(_sourcePath).TrimStart('.').ToLowerInvariant();
                string targetHint = _format.ToFormatHint();
                if (string.Equals(ext, targetHint, StringComparison.OrdinalIgnoreCase) ||
                    (_format == DocumentFormat.Docx && (ext == "docm" || ext == "dotx" || ext == "dotm")) ||
                    (_format == DocumentFormat.Doc && ext == "dot") ||
                    (_format == DocumentFormat.Html && (ext == "htm" || ext == "xhtml")) ||
                    (_format == DocumentFormat.Markdown && ext == "markdown") ||
                    (_format == DocumentFormat.PlainText && ext == "text") ||
                    (_format == DocumentFormat.Latex && (ext == "latex" || ext == "ltx")))
                {
                    Doc2PdfConverter.ConvertFile(_sourcePath, fullOut, options);
                }
                else
                {
                    byte[] inBytes = File.ReadAllBytes(_sourcePath);
                    byte[] outBytes = Doc2PdfConverter.Convert(inBytes, _format, options);
                    File.WriteAllBytes(fullOut, outBytes);
                }
            }
        }
        else
        {
            byte[] pdfBytes = ConvertToPdf(options);
            File.WriteAllBytes(fullOut, pdfBytes);
        }
    }

    /// <summary>
    /// Converts the loaded document to PDF and writes the output directly into a writable stream.
    /// </summary>
    /// <param name="outputStream">Destination writable stream.</param>
    /// <param name="options">Optional conversion configuration.</param>
    /// <exception cref="InvalidOperationException">Thrown if no document has been loaded.</exception>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="outputStream"/> is null.</exception>
    /// <exception cref="Doc2PdfException">Thrown if the native conversion fails.</exception>
    public void ConvertToPdf(Stream outputStream, Doc2PdfOptions? options = null)
    {
        ThrowIfDisposed();
        EnsureLoaded();
        if (outputStream == null)
            throw new ArgumentNullException(nameof(outputStream));

        byte[] pdfBytes = ConvertToPdf(options);
        outputStream.Write(pdfBytes, 0, pdfBytes.Length);
    }

    // =========================================================================
    // Conversion Methods (Asynchronous)
    // =========================================================================

    /// <summary>
    /// Asynchronously converts the loaded document to PDF and returns the resulting bytes.
    /// </summary>
    public Task<byte[]> ConvertToPdfAsync(Doc2PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoaded();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ConvertToPdf(options);
        }, cancellationToken);
    }

    /// <summary>
    /// Asynchronously converts the loaded document to PDF and saves it to a file on disk.
    /// </summary>
    public Task ConvertToPdfAsync(string outputPath, Doc2PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoaded();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConvertToPdf(outputPath, options);
        }, cancellationToken);
    }

    /// <summary>
    /// Asynchronously converts the loaded document to PDF and writes it into the destination stream.
    /// </summary>
    public Task ConvertToPdfAsync(Stream outputStream, Doc2PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureLoaded();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConvertToPdf(outputStream, options);
        }, cancellationToken);
    }

    // =========================================================================
    // Internal Helpers & Disposal
    // =========================================================================

    private byte[] GetPayloadBytes()
    {
        if (_payload != null)
            return _payload;

        if (_sourcePath != null)
            return File.ReadAllBytes(_sourcePath);

        throw new InvalidOperationException("Document data is not loaded.");
    }

    private void EnsureLoaded()
    {
        if (!_isLoaded)
            throw new InvalidOperationException("No document has been loaded. Call LoadFromFile(), LoadFromBytes(), LoadFromString(), or LoadFromStream() before converting.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Document));
    }

    /// <summary>
    /// Releases internal resources and references.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _payload = null;
        _sourcePath = null;
        _isLoaded = false;
        _length = 0;
    }
}
