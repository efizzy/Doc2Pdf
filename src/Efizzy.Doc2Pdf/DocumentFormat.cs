using System;
using System.IO;

namespace Efizzy.Doc2Pdf;

/// <summary>
/// Document formats supported by Efizzy.Doc2Pdf for conversion to PDF.
/// </summary>
public enum DocumentFormat
{
    /// <summary>
    /// Automatic detection from file extension or content signature.
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
/// Helper utilities for <see cref="DocumentFormat"/>.
/// </summary>
public static class DocumentFormatExtensions
{
    /// <summary>
    /// Returns the standard file extension hint string passed to the native converter.
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
        _ => "docx"
    };

    /// <summary>
    /// Detects the format from a file path or extension string.
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
}
