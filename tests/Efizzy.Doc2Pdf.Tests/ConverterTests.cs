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
}
