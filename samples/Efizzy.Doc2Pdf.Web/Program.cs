using System.Diagnostics;
using Efizzy.Doc2Pdf;

var builder = WebApplication.CreateBuilder(args);

// Enable static files
builder.Services.AddRouting();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Health/info endpoint
app.MapGet("/api/info", () =>
{
    return Results.Ok(new
    {
        name = "Efizzy.Doc2Pdf Web Converter",
        engineVersion = Doc2PdfConverter.GetNativeVersion(),
        supportedFormats = new[]
        {
            "docx", "doc", "docm", "dotx", "dotm", "xml",
            "md", "markdown", "html", "htm", "xhtml",
            "rtf", "odt", "tex", "latex", "txt", "json"
        }
    });
});

// Sample documents endpoint for one-click testing
app.MapGet("/api/sample/{name}", (string name) =>
{
    return name.ToLowerInvariant() switch
    {
        "markdown" => Results.Content(
            "# Quarterly Business Report\n\n" +
            "Generated automatically using **Efizzy.Doc2Pdf**!\n\n" +
            "## Key Accomplishments\n" +
            "- Native Rust headless conversion engine running at sub-50ms speeds.\n" +
            "- Cross-platform runtime packaging for Windows, Linux, and macOS.\n" +
            "- Direct integration into .NET applications via clean C# P/Invoke.\n\n" +
            "## Summary Table\n\n" +
            "| Module | Language | Status | Performance |\n" +
            "| :--- | :--- | :--- | :--- |\n" +
            "| Parser & Layout | Rust | Completed | 30-60 ms |\n" +
            "| PDF Generator | Rust (Krilla) | Completed | Real text & subsets |\n" +
            "| Managed Wrapper | C# (.NET 8/9/10) | Completed | Zero copy unsafe API |\n",
            "text/markdown",
            System.Text.Encoding.UTF8),

        "html" => Results.Content(
            "<!DOCTYPE html>\n<html><head><meta charset='utf-8'><title>HTML Invoice Sample</title>" +
            "<style>body { font-family: sans-serif; margin: 40px; color: #333; }" +
            "h1 { color: #1e3a8a; border-bottom: 2px solid #2563eb; padding-bottom: 8px; }" +
            "table { width: 100%; border-collapse: collapse; margin-top: 24px; }" +
            "th, td { border: 1px solid #cbd5e1; padding: 12px; text-align: left; }" +
            "th { background-color: #f1f5f9; color: #0f172a; }" +
            ".badge { background: #dcfce7; color: #15803d; padding: 4px 8px; border-radius: 4px; font-size: 12px; font-weight: bold; }" +
            "</style></head><body>" +
            "<h1>Invoice #INV-2026-0042</h1>" +
            "<p>Client: <strong>Acme Corporation</strong> | Date: <strong>October 24, 2026</strong></p>" +
            "<table><thead><tr><th>Description</th><th>Qty</th><th>Status</th><th>Total</th></tr></thead>" +
            "<tbody><tr><td>Efizzy.Doc2Pdf Enterprise Converter</td><td>1</td><td><span class='badge'>Delivered</span></td><td>$0.00</td></tr>" +
            "<tr><td>High-Speed Rust Rendering Engine</td><td>1</td><td><span class='badge'>Active</span></td><td>$0.00</td></tr>" +
            "</tbody></table></body></html>",
            "text/html",
            System.Text.Encoding.UTF8),

        "rtf" => Results.Content(
            "{\\rtf1\\ansi\\deff0\n{\\fonttbl{\\f0\\fswiss\\fcharset0 Arial;}{\\f1\\froman\\fcharset0 Times New Roman;}}\n" +
            "{\\colortbl ;\\red30\\green58\\blue138;\\red22\\green101\\blue52;}\n" +
            "\\f0\\fs32 \\b \\cf1 Rich Text Format Document \\cf0\\b0\\par\n" +
            "\\fs22 This RTF document was converted seamlessly to PDF using \\b Efizzy.Doc2Pdf\\b0.\\par\n" +
            "\\bullet  High-fidelity font formatting and layout retention\\par\n" +
            "\\bullet  Bullet points, paragraph spacing, and margins\\par\n" +
            "\\cf2 \\b Ready for production testing!\\b0\\cf0\\par\n}",
            "application/rtf",
            System.Text.Encoding.UTF8),

        _ => Results.NotFound()
    };
});

// Conversion endpoint
app.MapPost("/api/convert", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Request must be multipart/form-data." });

    var form = await request.ReadFormAsync();
    var file = form.Files["file"];

    if (file == null || file.Length == 0)
        return Results.BadRequest(new { error = "No file uploaded or file is empty." });

    const long maxFileSizeBytes = 100 * 1024 * 1024; // 100 MB limit
    if (file.Length > maxFileSizeBytes)
        return Results.BadRequest(new { error = "Uploaded file exceeds maximum allowable size of 100 MB." });

    string? paper = form["paper"].FirstOrDefault()?.ToLowerInvariant();
    bool markup = bool.TryParse(form["markup"], out bool m) && m;
    bool comments = bool.TryParse(form["comments"], out bool c) && c;
    bool tagged = !bool.TryParse(form["tagged"], out bool t) || t; // default true
    string? password = form["password"].FirstOrDefault();
    string? title = form["title"].FirstOrDefault();
    string? author = form["author"].FirstOrDefault();
    string? subject = form["subject"].FirstOrDefault();
    string? keywords = form["keywords"].FirstOrDefault();
    string? watermark = form["watermark"].FirstOrDefault();
    string? watermarkColor = form["watermarkColor"].FirstOrDefault();
    bool watermarkDiagonal = !bool.TryParse(form["watermarkDiagonal"], out bool wd) || wd;
    string? pages = form["pages"].FirstOrDefault();

    byte[] inputBytes;
    using (var ms = new MemoryStream())
    {
        await file.CopyToAsync(ms);
        inputBytes = ms.ToArray();
    }

    // Securely inspect file header bytes to determine authentic format and reject malicious binaries
    DocumentFormat detectedFormat;
    try
    {
        detectedFormat = DocumentFormatExtensions.DetectFormat(inputBytes, file.FileName);
    }
    catch (Doc2PdfException secEx) when (secEx.StatusCode == -403 || secEx.Message.Contains("Security validation rejected"))
    {
        return Results.Problem(
            detail: secEx.Message,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Dangerous or Malicious File Rejected");
    }
    catch (Doc2PdfException fmtEx)
    {
        return Results.Problem(
            detail: fmtEx.Message,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unsupported Document Header");
    }

    var options = new Doc2PdfOptions
    {
        IncludeMarkup = markup,
        ShowComments = comments,
        TaggedPdf = tagged,
        Compress = true,
        PaperSizePoints = paper switch
        {
            "a4" => StandardPaperSize.A4,
            "a3" => StandardPaperSize.A3,
            "a5" => StandardPaperSize.A5,
            "legal" => StandardPaperSize.Legal,
            "letter" => StandardPaperSize.Letter,
            _ => null
        },
        Password = string.IsNullOrWhiteSpace(password) ? null : password,
        Title = string.IsNullOrWhiteSpace(title) ? null : title,
        Author = string.IsNullOrWhiteSpace(author) ? null : author,
        Subject = string.IsNullOrWhiteSpace(subject) ? null : subject,
        Keywords = string.IsNullOrWhiteSpace(keywords) ? null : keywords,
        WatermarkText = string.IsNullOrWhiteSpace(watermark) ? null : watermark,
        WatermarkColor = string.IsNullOrWhiteSpace(watermarkColor) ? null : watermarkColor,
        WatermarkDiagonal = watermarkDiagonal,
        PageRange = string.IsNullOrWhiteSpace(pages) ? null : pages
    };

    var sw = Stopwatch.StartNew();
    try
    {
        byte[] pdfBytes = Doc2PdfConverter.Convert(inputBytes, detectedFormat, options);
        sw.Stop();

        string rawName = Path.GetFileNameWithoutExtension(file.FileName);
        string safeName = string.Join("_", rawName.Split(Path.GetInvalidFileNameChars()))
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "converted";
        string outName = safeName + ".pdf";

        request.HttpContext.Response.Headers["X-Conversion-Time-Ms"] = sw.ElapsedMilliseconds.ToString();
        request.HttpContext.Response.Headers["X-Detected-Format"] = detectedFormat.ToString();
        request.HttpContext.Response.Headers["X-Input-Bytes"] = inputBytes.Length.ToString();
        request.HttpContext.Response.Headers["X-Output-Bytes"] = pdfBytes.Length.ToString();

        return Results.File(pdfBytes, "application/pdf", outName, enableRangeProcessing: true);
    }
    catch (Exception ex)
    {
        sw.Stop();
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "PDF Conversion Failed");
    }
});

app.Run();
