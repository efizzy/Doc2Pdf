using System;

namespace Efizzy.Doc2Pdf;

/// <summary>
/// Exception thrown when a conversion error occurs in Efizzy.Doc2Pdf.
/// </summary>
public class Doc2PdfException : Exception
{
    /// <summary>
    /// Native return status code (0 = success, 1 = operational failure, 2 = critical panic).
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="Doc2PdfException"/>.
    /// </summary>
    public Doc2PdfException(string message, int statusCode = 1) : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="Doc2PdfException"/> with an inner exception.
    /// </summary>
    public Doc2PdfException(string message, Exception innerException, int statusCode = 1)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Backwards-compatible alias for <see cref="Doc2PdfException"/>.
/// </summary>
[Obsolete("Use Doc2PdfException instead.")]
public class Word2PdfException : Doc2PdfException
{
    /// <summary>Initializes a new instance of <see cref="Word2PdfException"/>.</summary>
    public Word2PdfException(string message, int statusCode = 1) : base(message, statusCode) { }
    /// <summary>Initializes a new instance of <see cref="Word2PdfException"/> with an inner exception.</summary>
    public Word2PdfException(string message, Exception innerException, int statusCode = 1) : base(message, innerException, statusCode) { }
}
