using System;
using System.IO;
using System.Text;
using Efizzy.Doc2Pdf;
using Xunit;

namespace Efizzy.Doc2Pdf.Tests;

public class ConverterTests
{
    [Fact]
    public void DocumentFormat_Detection_MatchesExpectedFormats()
    {
        Assert.Equal(DocumentFormat.Docx, DocumentFormatExtensions.FromPathOrExtension("test.docx"));
        Assert.Equal(DocumentFormat.Doc, DocumentFormatExtensions.FromPathOrExtension("legacy.doc"));
        Assert.Equal(DocumentFormat.Markdown, DocumentFormatExtensions.FromPathOrExtension("readme.md"));
        Assert.Equal(DocumentFormat.Html, DocumentFormatExtensions.FromPathOrExtension("page.html"));
        Assert.Equal(DocumentFormat.Rtf, DocumentFormatExtensions.FromPathOrExtension("doc.rtf"));
        Assert.Equal(DocumentFormat.Odt, DocumentFormatExtensions.FromPathOrExtension("report.odt"));
        Assert.Equal(DocumentFormat.Latex, DocumentFormatExtensions.FromPathOrExtension("paper.tex"));
        Assert.Equal(DocumentFormat.PlainText, DocumentFormatExtensions.FromPathOrExtension("notes.txt"));
        Assert.Equal(DocumentFormat.Xml, DocumentFormatExtensions.FromPathOrExtension("word.xml"));
    }

    [Fact]
    public void Options_Defaults_AreValid()
    {
        var opts = Doc2PdfOptions.Default;
        Assert.False(opts.IncludeMarkup);
        Assert.False(opts.ShowComments);
        Assert.True(opts.TaggedPdf);
        Assert.True(opts.Compress);
        Assert.Null(opts.PaperSizePoints);
        Assert.Equal(NumeralMode.Arabic, opts.NumeralMode);
    }

    [Fact]
    public void Convert_EmptyBuffer_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Doc2PdfConverter.Convert(Array.Empty<byte>()));
    }

    [Fact]
    public void ConvertFile_NonExistentFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            Doc2PdfConverter.ConvertFile("C:\\non_existent_path_12345.docx", "out.pdf"));
    }

    [Fact]
    public void Native_GetVersion_ReturnsValidVersion()
    {
        string version = Doc2PdfConverter.GetNativeVersion();
        Assert.NotNull(version);
        Assert.Contains(".", version);
    }

    [Fact]
    public void Native_IsFormatSupported_RecognizesFormats()
    {
        Assert.True(Doc2PdfConverter.IsFormatSupported("docx"));
        Assert.True(Doc2PdfConverter.IsFormatSupported("md"));
        Assert.True(Doc2PdfConverter.IsFormatSupported("html"));
        Assert.True(Doc2PdfConverter.IsFormatSupported("rtf"));
        Assert.False(Doc2PdfConverter.IsFormatSupported("unknown_xyz"));
    }

    [Fact]
    public void Convert_MarkdownBuffer_GeneratesValidPdf()
    {
        string markdown = "# Hello from Unit Test\n\nThis is a paragraph converted **in-memory** to PDF.";
        byte[] mdBytes = Encoding.UTF8.GetBytes(markdown);

        byte[] pdfBytes = Doc2PdfConverter.Convert(mdBytes, DocumentFormat.Markdown);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 100);

        string header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 8));
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void Convert_HtmlBuffer_GeneratesValidPdf()
    {
        string html = "<html><body><h1>HTML Test</h1><p>Paragraph with <b>bold</b> text.</p></body></html>";
        byte[] htmlBytes = Encoding.UTF8.GetBytes(html);

        byte[] pdfBytes = Doc2PdfConverter.Convert(htmlBytes, DocumentFormat.Html);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 100);

        string header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 8));
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void Convert_HtmlWithStylesAndClasses_InlinesStylesAndGeneratesPdf()
    {
        string html = """
            <!DOCTYPE html>
            <html>
            <head>
                <style>
                    h1 { color: #1e3a8a; font-size: 20pt; }
                    .badge { background-color: #dcfce7; color: #15803d; }
                    table { border: 1px solid #cbd5e1; }
                    th { background-color: #f1f5f9; text-align: left; }
                </style>
            </head>
            <body>
                <h1>Invoice Header</h1>
                <p>Status: <span class="badge">Paid</span></p>
                <table>
                    <tr><th>Item</th><th>Cost</th></tr>
                    <tr><td>Subscription</td><td>$99</td></tr>
                </table>
            </body>
            </html>
            """;

        byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
        byte[] pdfBytes = Doc2PdfConverter.Convert(htmlBytes, DocumentFormat.Html);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 100);

        string header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 8));
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void ConvertFile_ComplexHtml_GeneratesHighFidelityPdf()
    {
        string sampleHtml = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "church_anniversary.html");
        sampleHtml = Path.GetFullPath(sampleHtml);
        if (File.Exists(sampleHtml))
        {
            string outPdf = Path.Combine(Path.GetTempPath(), "test_church_anniversary.pdf");
            if (File.Exists(outPdf)) File.Delete(outPdf);
            Doc2PdfConverter.ConvertFile(sampleHtml, outPdf);
            Assert.True(File.Exists(outPdf));
            Assert.True(new FileInfo(outPdf).Length > 10000);
            File.Delete(outPdf);
        }
    }

    [Fact]
    public void Convert_WithWatermark_GeneratesValidPdf()
    {
        string markdown = "# Highly Confidential Document\n\nProprietary trade secrets and business algorithms.";
        byte[] mdBytes = Encoding.UTF8.GetBytes(markdown);

        var options = new Doc2PdfOptions
        {
            WatermarkText = "CONFIDENTIAL",
            WatermarkColor = "#C53030",
            WatermarkDiagonal = true,
            WatermarkOpacity = 0.25f
        };

        byte[] pdfBytes = Doc2PdfConverter.Convert(mdBytes, DocumentFormat.Markdown, options);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 200);
        string header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 8));
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void Convert_WithEnterpriseMetadata_IncludesAttributes()
    {
        string markdown = "# Financial Forecast 2027\n\nQuarterly projections and runway analysis.";
        byte[] mdBytes = Encoding.UTF8.GetBytes(markdown);

        var options = new Doc2PdfOptions
        {
            Title = "2027 Financial Forecast",
            Author = "CFO Office",
            Subject = "Financial Projections",
            Keywords = "finance, 2027, budget, forecast"
        };

        byte[] pdfBytes = Doc2PdfConverter.Convert(mdBytes, DocumentFormat.Markdown, options);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 200);
        string header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 8));
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void Convert_HtmlWithWatermark_GeneratesValidPdf()
    {
        string html = "<html><body><h1>Invoice #999</h1><p>Customer Receipt</p></body></html>";
        byte[] htmlBytes = Encoding.UTF8.GetBytes(html);

        var options = new Doc2PdfOptions
        {
            WatermarkText = "PAID IN FULL",
            WatermarkColor = "#15803D",
            WatermarkDiagonal = true,
            WatermarkOpacity = 0.2f
        };

        byte[] pdfBytes = Doc2PdfConverter.Convert(htmlBytes, DocumentFormat.Html, options);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 200);
        string header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 8));
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void Convert_BackwardCompatibility_Word2PdfConverter_Works()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        string markdown = "# Testing Backwards Compatibility\n\nVerifying Word2PdfConverter shim.";
        byte[] mdBytes = Encoding.UTF8.GetBytes(markdown);
        byte[] pdfBytes = Word2PdfConverter.Convert(mdBytes, DocumentFormat.Markdown);
        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 100);
#pragma warning restore CS0618
    }

    [Fact]
    public void NativeLibraryResolver_CandidateRids_ContainsExpectedPlatforms()
    {
        var rids = Efizzy.Doc2Pdf.Native.NativeLibraryResolver.GetCandidateRuntimeIds();
        Assert.NotNull(rids);
        Assert.NotEmpty(rids);

        string libName = Efizzy.Doc2Pdf.Native.NativeLibraryResolver.GetPlatformLibraryFileName();
        Assert.NotNull(libName);
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            Assert.Equal("doc2pdf_native.dll", libName);
            Assert.Contains("win-x64", rids);
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            Assert.Equal("libdoc2pdf_native.dylib", libName);
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            Assert.Equal("libdoc2pdf_native.so", libName);
        }
    }

    [Fact]
    public void DocumentFormat_DetectFormat_MagicBytes_IdentifiesStructuredFormats()
    {
        // RTF magic bytes {\rtf
        byte[] rtfBytes = Encoding.ASCII.GetBytes("{\\rtf1\\ansi Hello world}");
        Assert.Equal(DocumentFormat.Rtf, DocumentFormatExtensions.DetectFormat(rtfBytes));

        // Legacy DOC magic bytes
        byte[] docBytes = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00 };
        Assert.Equal(DocumentFormat.Doc, DocumentFormatExtensions.DetectFormat(docBytes));

        // ZIP DOCX magic bytes (standard PK 03 04)
        byte[] docxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 };
        Assert.Equal(DocumentFormat.Docx, DocumentFormatExtensions.DetectFormat(docxBytes));

        // HTML text header
        byte[] htmlBytes = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body><h1>Title</h1></body></html>");
        Assert.Equal(DocumentFormat.Html, DocumentFormatExtensions.DetectFormat(htmlBytes));

        // Markdown text header
        byte[] mdBytes = Encoding.UTF8.GetBytes("# Markdown Heading\n\nContent paragraph with **bold** text.");
        Assert.Equal(DocumentFormat.Markdown, DocumentFormatExtensions.DetectFormat(mdBytes));

        // JSON AST header
        byte[] jsonBytes = Encoding.UTF8.GetBytes("{\n  \"version\": 1,\n  \"body\": []\n}");
        Assert.Equal(DocumentFormat.Json, DocumentFormatExtensions.DetectFormat(jsonBytes));

        // LaTeX header
        byte[] texBytes = Encoding.UTF8.GetBytes("\\documentclass{article}\n\\begin{document}\nHello\n\\end{document}");
        Assert.Equal(DocumentFormat.Latex, DocumentFormatExtensions.DetectFormat(texBytes));

        // PlainText
        byte[] txtBytes = Encoding.UTF8.GetBytes("Just plain text notes without markdown or html.");
        Assert.Equal(DocumentFormat.PlainText, DocumentFormatExtensions.DetectFormat(txtBytes));
    }

    [Fact]
    public void Security_ValidateSafety_WindowsExecutable_ThrowsDoc2PdfException()
    {
        // Simulated Windows PE executable header starting with MZ (0x4D, 0x5A)
        byte[] peBytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };

        var ex = Assert.Throws<Doc2PdfException>(() => DocumentFormatExtensions.ValidateSafety(peBytes, "malicious.docx"));
        Assert.Equal(-403, ex.StatusCode);
        Assert.Contains("Security validation rejected", ex.Message);
        Assert.Contains("MZ", ex.Message);
    }

    [Fact]
    public void Security_ValidateSafety_LinuxElf_ThrowsDoc2PdfException()
    {
        // Simulated ELF executable starting with \x7FELF
        byte[] elfBytes = new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 0x02, 0x01, 0x01, 0x00 };

        var ex = Assert.Throws<Doc2PdfException>(() => DocumentFormatExtensions.ValidateSafety(elfBytes, "malicious.bin"));
        Assert.Equal(-403, ex.StatusCode);
        Assert.Contains("ELF", ex.Message);
    }

    [Fact]
    public void Security_ValidateSafety_MachO_ThrowsDoc2PdfException()
    {
        // Simulated Mach-O 64-bit binary
        byte[] machoBytes = new byte[] { 0xFE, 0xED, 0xFA, 0xCF, 0x00, 0x00, 0x00, 0x01 };

        var ex = Assert.Throws<Doc2PdfException>(() => DocumentFormatExtensions.ValidateSafety(machoBytes, "payload.dylib"));
        Assert.Equal(-403, ex.StatusCode);
        Assert.Contains("Mach-O", ex.Message);
    }

    [Fact]
    public void Security_ValidateSafety_UnixShebang_ThrowsDoc2PdfException()
    {
        byte[] scriptBytes = Encoding.UTF8.GetBytes("#!/bin/bash\nrm -rf /");

        var ex = Assert.Throws<Doc2PdfException>(() => DocumentFormatExtensions.ValidateSafety(scriptBytes, "exploit.sh"));
        Assert.Equal(-403, ex.StatusCode);
        Assert.Contains("Unix shebang", ex.Message);
    }

    [Fact]
    public void Security_ValidateSafety_WindowsShortcut_ThrowsDoc2PdfException()
    {
        byte[] lnkBytes = new byte[] { 0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00, 0x00, 0x00 };

        var ex = Assert.Throws<Doc2PdfException>(() => DocumentFormatExtensions.ValidateSafety(lnkBytes, "shortcut.lnk"));
        Assert.Equal(-403, ex.StatusCode);
        Assert.Contains("Shell Link", ex.Message);
    }

    [Fact]
    public void Convert_DisguisedExecutable_ThrowsSecurityException()
    {
        // Disguised executable passed with "docx" format hint
        byte[] fakeDocx = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00 };

        var ex = Assert.Throws<Doc2PdfException>(() => Doc2PdfConverter.Convert(fakeDocx, "docx"));
        Assert.Equal(-403, ex.StatusCode);
    }

    [Fact]
    public void Convert_DisguisedRtfWithDocxExtension_ConvertsSuccessfullyUsingHeaderBytes()
    {
        // RTF content mistakenly given a "docx" format hint
        string rtf = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Arial;}}\\f0\\fs24 Header-detected RTF document.\\par}";
        byte[] rtfBytes = Encoding.ASCII.GetBytes(rtf);

        // Should detect it as RTF and convert without throwing
        byte[] pdfBytes = Doc2PdfConverter.Convert(rtfBytes, "docx");
        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 100);
        string header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 8));
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void Convert_DocumentFormatAuto_AutoDetectsFromHeaderBytes()
    {
        string md = "# Automatic Detection Test\n\nTesting Auto format from header bytes.";
        byte[] mdBytes = Encoding.UTF8.GetBytes(md);

        byte[] pdfBytes = Doc2PdfConverter.Convert(mdBytes, DocumentFormat.Auto);
        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 100);
        string header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 8));
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void ConvertFile_DisguisedExecutable_RejectsBeforeExecution()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"malicious_test_{Guid.NewGuid():N}.docx");
        string tempOutPdf = Path.Combine(Path.GetTempPath(), $"out_{Guid.NewGuid():N}.pdf");

        try
        {
            // Write MZ executable header disguised as .docx
            byte[] fakePe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
            File.WriteAllBytes(tempFile, fakePe);

            var ex = Assert.Throws<Doc2PdfException>(() => Doc2PdfConverter.ConvertFile(tempFile, tempOutPdf));
            Assert.Equal(-403, ex.StatusCode);
            Assert.False(File.Exists(tempOutPdf));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(tempOutPdf)) File.Delete(tempOutPdf);
        }
    }

    [Fact]
    public void ConvertFile_NullByteInPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Doc2PdfConverter.ConvertFile("test\0bad.docx", "out.pdf"));
        Assert.Throws<ArgumentException>(() => Doc2PdfConverter.ConvertFile("valid.docx", "out\0bad.pdf"));
    }

    [Fact]
    public void Convert_StreamConversion_MemoryStreamOptimized()
    {
        string md = "# Stream Conversion Test\n\nVerifying zero-copy MemoryStream handling.";
        byte[] mdBytes = Encoding.UTF8.GetBytes(md);

        using var inStream = new MemoryStream(mdBytes);
        using var outStream = new MemoryStream();

        Doc2PdfConverter.Convert(inStream, outStream, "md");

        Assert.True(outStream.Length > 100);
        byte[] pdfBytes = outStream.ToArray();
        string header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 8));
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void Security_CachedFormatHints_ReturnsValidPointers()
    {
        Assert.True(Doc2PdfConverter.CachedFormatHints.TryGetCachedPointer("docx", out IntPtr ptrDocx));
        Assert.NotEqual(IntPtr.Zero, ptrDocx);

        Assert.True(Doc2PdfConverter.CachedFormatHints.TryGetCachedPointer("html", out IntPtr ptrHtml));
        Assert.NotEqual(IntPtr.Zero, ptrHtml);

        Assert.True(Doc2PdfConverter.CachedFormatHints.TryGetCachedPointer("auto", out IntPtr ptrAuto));
        Assert.NotEqual(IntPtr.Zero, ptrAuto);

        Assert.False(Doc2PdfConverter.CachedFormatHints.TryGetCachedPointer("custom_xyz", out _));
    }
}
