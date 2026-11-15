using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Efizzy.Doc2Pdf.Security;
using Xunit;

namespace Efizzy.Doc2Pdf.Tests;

public class SecurityTests
{
    private static X509Certificate2 CreateTestCertificate(string commonName = "CN=Efizzy Test Signer")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(commonName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    [Fact]
    public void CanCreateSelfSignedCertificate()
    {
        using var cert = CreateTestCertificate();
        Assert.NotNull(cert);
        Assert.True(cert.HasPrivateKey);

        byte[] pfx = cert.Export(X509ContentType.Pfx, "test1234");
        Assert.NotNull(pfx);
        Assert.True(pfx.Length > 0);
    }

    [Fact]
    public void PdfFlattener_RemovesAcroFormAndWidgetAnnotations()
    {
        string formPdfPath = Path.Combine(AppContext.BaseDirectory, "../../../../../test_form.pdf");
        if (!File.Exists(formPdfPath))
        {
            formPdfPath = "test_form.pdf";
        }
        Assert.True(File.Exists(formPdfPath), "test_form.pdf must exist for flattening test.");

        byte[] origBytes = File.ReadAllBytes(formPdfPath);
        string origAscii = Encoding.ASCII.GetString(origBytes);
        Assert.Contains("/AcroForm", origAscii);
        Assert.Contains("/Widget", origAscii);

        byte[] flatBytes = PdfFlattener.Flatten(origBytes);
        Assert.NotNull(flatBytes);
        Assert.True(flatBytes.Length > 0);

        string flatAscii = Encoding.ASCII.GetString(flatBytes);
        Assert.DoesNotContain("/AcroForm", flatAscii);
        Assert.Contains("/Annots[]", flatAscii);
        Assert.Contains("/Annot5 Do", flatAscii); // appearance baked into content stream
    }

    [Fact]
    public void PdfFlattener_AlreadyFlatPdf_ReturnsSameBytes()
    {
        string simplePdfPath = Path.Combine(AppContext.BaseDirectory, "../../../../../test_simple.pdf");
        if (!File.Exists(simplePdfPath))
        {
            simplePdfPath = "test_simple.pdf";
        }
        Assert.True(File.Exists(simplePdfPath), "test_simple.pdf must exist.");

        byte[] origBytes = File.ReadAllBytes(simplePdfPath);
        byte[] flatBytes = PdfFlattener.Flatten(origBytes);

        Assert.Equal(origBytes.Length, flatBytes.Length);
        Assert.Equal(origBytes, flatBytes);
    }

    [Fact]
    public void PdfSigner_SignsDocument_AndDetectsTampering()
    {
        string simplePdfPath = Path.Combine(AppContext.BaseDirectory, "../../../../../test_simple.pdf");
        if (!File.Exists(simplePdfPath))
        {
            simplePdfPath = "test_simple.pdf";
        }
        Assert.True(File.Exists(simplePdfPath), "test_simple.pdf must exist.");

        byte[] origBytes = File.ReadAllBytes(simplePdfPath);
        using var cert = CreateTestCertificate("CN=Doc2Pdf Secure Signer");

        var sigOptions = new PdfSignatureOptions
        {
            Certificate = cert,
            SignerName = "Efizzy Insurance Underwriter",
            Reason = "I certify this document is authentic and unmodified",
            Location = "Lagos, Nigeria",
            ContactInfo = "security@efizzy.com",
            CertificationLevel = PdfCertificationLevel.NoChangesPermitted
        };

        byte[] signedBytes = PdfSigner.Sign(origBytes, sigOptions);
        Assert.NotNull(signedBytes);
        Assert.True(signedBytes.Length > origBytes.Length);

        string signedAscii = Encoding.ASCII.GetString(signedBytes);
        Assert.Contains("/Type /Sig", signedAscii);
        Assert.Contains("/DocMDP", signedAscii);
        Assert.Contains("/TransformParams << /Type /TransformParams /P 1", signedAscii);
        Assert.Contains("/ByteRange", signedAscii);
        Assert.Contains("/Contents", signedAscii);

        // 1. Verify valid signature
        bool isValid = PdfSigner.VerifySignature(signedBytes, out string? failureReason);
        Assert.True(isValid, $"Signature should be valid, but failed: {failureReason}");
        Assert.Null(failureReason);

        // 2. Tamper with a byte in the document body (Range 1)
        byte[] tamperedBytes = (byte[])signedBytes.Clone();
        // Modify byte in the middle of original stream
        tamperedBytes[200] ^= 0xFF;

        bool isTamperedValid = PdfSigner.VerifySignature(tamperedBytes, out string? tamperedReason);
        Assert.False(isTamperedValid, "Tampered document must fail signature verification!");
        Assert.NotNull(tamperedReason);
        Assert.Contains("altered or tampered", tamperedReason);
    }

    [Fact]
    public void Convert_WithDigitalSignature_ProducesCertifiedPdf()
    {
        byte[] mdBytes = Encoding.UTF8.GetBytes("# Official Document\n\nThis document is cryptographically certified.");
        using var cert = CreateTestCertificate("CN=Automated Pipeline Signer");

        var opts = new Doc2PdfOptions
        {
            IsEditable = false,
            Signature = new PdfSignatureOptions
            {
                Certificate = cert,
                SignerName = "Efizzy Certified Signer",
                Reason = "Contract Finalization",
                CertificationLevel = PdfCertificationLevel.NoChangesPermitted
            }
        };

        byte[] pdfBytes = Doc2PdfConverter.Convert(mdBytes, DocumentFormat.Markdown, opts);
        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 0);

        string ascii = Encoding.ASCII.GetString(pdfBytes);
        Assert.Contains("/DocMDP", ascii);
        Assert.Contains("/ByteRange", ascii);

        bool isValid = PdfSigner.VerifySignature(pdfBytes, out string? failureReason);
        Assert.True(isValid, $"Pipeline signature verification failed: {failureReason}");
    }

    [Fact]
    public void Options_IsEditable_DefaultsToFalse()
    {
        var opts = new Doc2PdfOptions();
        Assert.False(opts.IsEditable);
        Assert.Null(opts.Signature);
    }
}
