using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Efizzy;
using Efizzy.Doc2Pdf;
using Xunit;

namespace Efizzy.Doc2Pdf.Tests;

public class DocumentTests
{
    [Fact]
    public void Document_ParameterlessConstructor_InitializesEmpty()
    {
        using var doc = new Document();
        Assert.False(doc.IsLoaded);
        Assert.Equal(DocumentFormat.Auto, doc.Format);
        Assert.Null(doc.SourcePath);
        Assert.Equal(0, doc.Length);
    }

    [Fact]
    public void Document_NotLoaded_ThrowsInvalidOperationExceptionOnConvert()
    {
        using var doc = new Document();
        Assert.Throws<InvalidOperationException>(() => doc.ConvertToPdf());
        Assert.Throws<InvalidOperationException>(() => doc.ConvertToPdf("output.pdf"));
        Assert.Throws<InvalidOperationException>(() => doc.ConvertToPdf(new MemoryStream()));
    }

    [Fact]
    public void Document_LoadFromString_Markdown_SetsPropertiesAndConverts()
    {
        using var doc = new Document();
        string md = "# Heading 1\n\nThis is a paragraph with **bold** text.";
        doc.LoadFromString(md, DocumentFormat.Markdown);

        Assert.True(doc.IsLoaded);
        Assert.Equal(DocumentFormat.Markdown, doc.Format);
        Assert.True(doc.Length > 0);
        Assert.Null(doc.SourcePath);

        byte[] pdf = doc.ConvertToPdf();
        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 100);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));
    }

    [Fact]
    public void Document_LoadFromString_Html_SetsPropertiesAndConverts()
    {
        using var doc = new Document();
        string html = "<html><body><h1>Hello HTML</h1><p>Test paragraph</p></body></html>";
        doc.LoadFromString(html, DocumentFormat.Html);

        Assert.True(doc.IsLoaded);
        Assert.Equal(DocumentFormat.Html, doc.Format);

        byte[] pdf = doc.ConvertToPdf();
        Assert.NotNull(pdf);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));
    }

    [Fact]
    public void Document_LoadFromBytes_DetectsFormat()
    {
        using var doc = new Document();
        byte[] mdBytes = Encoding.UTF8.GetBytes("# Markdown Bytes Test\n\nSimple content.");
        doc.LoadFromBytes(mdBytes);

        Assert.True(doc.IsLoaded);
        Assert.Equal(DocumentFormat.Markdown, doc.Format);

        byte[] pdf = doc.ConvertToPdf();
        Assert.NotNull(pdf);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));
    }

    [Fact]
    public void Document_LoadFromStream_ConvertsToPdf()
    {
        using var doc = new Document();
        byte[] htmlBytes = Encoding.UTF8.GetBytes("<html><body><h2>Stream Title</h2></body></html>");
        using var ms = new MemoryStream(htmlBytes);
        doc.LoadFromStream(ms, DocumentFormat.Html);

        Assert.True(doc.IsLoaded);
        Assert.Equal(DocumentFormat.Html, doc.Format);

        using var outMs = new MemoryStream();
        doc.ConvertToPdf(outMs);

        byte[] pdf = outMs.ToArray();
        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 100);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));
    }

    [Fact]
    public void Document_Constructor_WithBytes_ConvertsSuccessfully()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("# From Constructor Bytes\n\nContent.");
        using var doc = new Document(bytes, DocumentFormat.Markdown);

        Assert.True(doc.IsLoaded);
        byte[] pdf = doc.ConvertToPdf();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));
    }

    [Fact]
    public void Document_Constructor_WithStream_ConvertsSuccessfully()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("# From Constructor Stream\n\nContent."));
        using var doc = new Document(ms, DocumentFormat.Markdown);

        Assert.True(doc.IsLoaded);
        byte[] pdf = doc.ConvertToPdf();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));
    }

    [Fact]
    public void Document_StaticFactories_WorkAsExpected()
    {
        using var doc1 = Document.FromString("# Test", DocumentFormat.Markdown);
        Assert.True(doc1.IsLoaded);

        using var doc2 = Document.FromBytes(Encoding.UTF8.GetBytes("# Test"), DocumentFormat.Markdown);
        Assert.True(doc2.IsLoaded);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("# Test"));
        using var doc3 = Document.FromStream(ms, DocumentFormat.Markdown);
        Assert.True(doc3.IsLoaded);
    }

    [Fact]
    public void DocumentFormat_Extensions_ConvertCorrectly()
    {
        Assert.Equal(DocumentFormat.Docx, "docx".ToDocumentFormat());
        Assert.Equal(DocumentFormat.Markdown, "md".ToDocumentFormat());
        Assert.Equal(DocumentFormat.Markdown, "markdown".ToDocumentFormat());
        Assert.Equal(DocumentFormat.Html, "html".ToDocumentFormat());
        Assert.Equal(DocumentFormat.Auto, "unrecognized_ext".ToDocumentFormat());

        Assert.Equal("docx", DocumentFormat.Docx.ToFormatHint());
        Assert.Equal("md", DocumentFormat.Markdown.ToFormatHint());
        Assert.Equal("html", DocumentFormat.Html.ToFormatHint());
    }

    [Fact]
    public void Document_ConvertToPdf_FileToFile_Succeeds()
    {
        string tempIn = Path.Combine(Path.GetTempPath(), $"doc_test_{Guid.NewGuid():N}.md");
        string tempOut = Path.Combine(Path.GetTempPath(), $"doc_test_{Guid.NewGuid():N}.pdf");

        try
        {
            File.WriteAllText(tempIn, "# File to File Test\n\nTesting document loading from disk.");

            using var doc = new Document(tempIn);
            Assert.True(doc.IsLoaded);
            Assert.Equal(DocumentFormat.Markdown, doc.Format);
            Assert.Equal(Path.GetFullPath(tempIn), doc.SourcePath);

            doc.ConvertToPdf(tempOut);

            Assert.True(File.Exists(tempOut));
            Assert.True(new FileInfo(tempOut).Length > 100);
        }
        finally
        {
            if (File.Exists(tempIn)) File.Delete(tempIn);
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public async Task Document_ConvertToPdfAsync_GeneratesPdf()
    {
        using var doc = new Document();
        doc.LoadFromString("# Async Test\n\nTesting asynchronous conversion.", DocumentFormat.Markdown);

        byte[] pdf = await doc.ConvertToPdfAsync();
        Assert.NotNull(pdf);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));

        using var outMs = new MemoryStream();
        await doc.ConvertToPdfAsync(outMs);
        Assert.True(outMs.Length > 100);
    }

    [Fact]
    public void Document_MaliciousExecutableHeader_ThrowsDoc2PdfException()
    {
        // DOS MZ Header
        byte[] fakeExe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
        using var doc = new Document();

        Assert.Throws<Doc2PdfException>(() => doc.LoadFromBytes(fakeExe));
    }

    [Fact]
    public void Document_NonExistentFile_ThrowsFileNotFoundException()
    {
        using var doc = new Document();
        Assert.Throws<FileNotFoundException>(() => doc.LoadFromFile("C:\\non_existent_doc_file_12345.docx"));
    }

    [Fact]
    public void Document_Dispose_ClearsState()
    {
        var doc = new Document();
        doc.LoadFromString("# Markdown Content", DocumentFormat.Markdown);
        Assert.True(doc.IsLoaded);

        doc.Dispose();
        Assert.False(doc.IsLoaded);
        Assert.Throws<ObjectDisposedException>(() => doc.ConvertToPdf());
    }
}
