using System;
using Efizzy.Doc2Pdf.Native;

namespace Efizzy.Doc2Pdf;

/// <summary>
/// Predefined standard paper dimensions in points (72 points = 1 inch).
/// </summary>
public static class StandardPaperSize
{
    /// <summary>US Letter: 8.5 x 11 inches (612 x 792 pt)</summary>
    public static (float Width, float Height) Letter => (612.0f, 792.0f);

    /// <summary>US Legal: 8.5 x 14 inches (612 x 1008 pt)</summary>
    public static (float Width, float Height) Legal => (612.0f, 1008.0f);

    /// <summary>ISO A4: 210 x 297 mm (595.28 x 841.89 pt)</summary>
    public static (float Width, float Height) A4 => (595.28f, 841.89f);

    /// <summary>ISO A3: 297 x 420 mm (841.89 x 1190.55 pt)</summary>
    public static (float Width, float Height) A3 => (841.89f, 1190.55f);

    /// <summary>ISO A5: 148 x 210 mm (419.53 x 595.28 pt)</summary>
    public static (float Width, float Height) A5 => (419.53f, 595.28f);
}

/// <summary>
/// Display mode for numerals in exported PDF.
/// </summary>
public enum NumeralMode
{
    /// <summary>Western Arabic digits (0, 1, 2, ...)</summary>
    Arabic = 0,
    /// <summary>Eastern Arabic-Indic (Hindi) digits</summary>
    Hindi = 1,
    /// <summary>Context-sensitive digits based on surrounding text script</summary>
    Context = 2
}

/// <summary>
/// Options controlling PDF generation.
/// </summary>
public record Doc2PdfOptions
{
    /// <summary>
    /// If true, tracked revisions are displayed marked up (e.g. insertions underlined, deletions struck through).
    /// Default is false.
    /// </summary>
    public bool IncludeMarkup { get; init; } = false;

    /// <summary>
    /// If true, document comments are rendered in callout balloons in the right-hand margin.
    /// Default is false.
    /// </summary>
    public bool ShowComments { get; init; } = false;

    /// <summary>
    /// If true, generates an accessible, tagged PDF structure tree (PDF/UA best effort).
    /// Default is true.
    /// </summary>
    public bool TaggedPdf { get; init; } = true;

    /// <summary>
    /// If true, content streams and embedded assets are compressed.
    /// Default is true.
    /// </summary>
    public bool Compress { get; init; } = true;

    /// <summary>
    /// Overrides document section page dimensions to scale-to-fit on the given paper size (width, height in points).
    /// If null, each page uses the document's own section geometry. See <see cref="StandardPaperSize"/>.
    /// </summary>
    public (float Width, float Height)? PaperSizePoints { get; init; } = null;

    /// <summary>
    /// Digit presentation mode (Arabic, Hindi, Context).
    /// </summary>
    public NumeralMode NumeralMode { get; init; } = NumeralMode.Arabic;

    /// <summary>
    /// Optional title metadata override. If null, the document's title property is used.
    /// </summary>
    public string? Title { get; init; } = null;

    /// <summary>
    /// Optional author metadata override. If null, the document's author property is used.
    /// </summary>
    public string? Author { get; init; } = null;

    /// <summary>
    /// Optional password to open encrypted Word documents (.docx or legacy .doc).
    /// </summary>
    public string? Password { get; init; } = null;

    /// <summary>
    /// Optional subject / description document metadata.
    /// </summary>
    public string? Subject { get; init; } = null;

    /// <summary>
    /// Optional keywords document metadata (e.g. comma-separated list like "contract, legal, 2026").
    /// </summary>
    public string? Keywords { get; init; } = null;

    /// <summary>
    /// Optional watermark text (e.g. "CONFIDENTIAL", "DRAFT", "SAMPLE") rendered across each page.
    /// </summary>
    public string? WatermarkText { get; init; } = null;

    /// <summary>
    /// Optional hex color for watermark text (e.g. "#C53030"). If null, a default semitransparent accent is used.
    /// </summary>
    public string? WatermarkColor { get; init; } = null;

    /// <summary>
    /// Whether the watermark is oriented diagonally (true, 45 degrees counterclockwise) or horizontally (false).
    /// Default is true.
    /// </summary>
    public bool WatermarkDiagonal { get; init; } = true;

    /// <summary>
    /// Opacity of the watermark (0.0 to 1.0). Default is 0.2.
    /// </summary>
    public float WatermarkOpacity { get; init; } = 0.2f;

    /// <summary>
    /// Optional 1-based page range filter (e.g. "1-3, 5, 8-10"). If null, all pages are exported.
    /// </summary>
    public string? PageRange { get; init; } = null;

    /// <summary>
    /// Optional directory containing custom font files (.ttf, .otf, .ttc) to catalog and use during conversion.
    /// </summary>
    public string? CustomFontsDirectory { get; init; } = null;

    /// <summary>
    /// Default options.
    /// </summary>
    public static Doc2PdfOptions Default => new();
}

/// <summary>
/// Backwards-compatible alias for <see cref="Doc2PdfOptions"/>.
/// </summary>
[Obsolete("Use Doc2PdfOptions instead.")]
public record Word2PdfOptions : Doc2PdfOptions
{
    /// <summary>
    /// Default options.
    /// </summary>
    public static new Word2PdfOptions Default => new();
}
