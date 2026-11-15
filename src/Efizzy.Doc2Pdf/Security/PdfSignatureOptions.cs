using System;
using System.Security.Cryptography.X509Certificates;

namespace Efizzy.Doc2Pdf;

/// <summary>
/// Configuration options for cryptographically signing and certifying a generated PDF document.
/// </summary>
public class PdfSignatureOptions
{
    /// <summary>
    /// An existing in-memory X.509 certificate containing an exportable or accessible private key.
    /// </summary>
    public X509Certificate2? Certificate { get; set; }

    /// <summary>
    /// Physical file path to a PKCS#12 (.pfx or .p12) certificate file on disk.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Raw byte payload of a PKCS#12 (.pfx or .p12) certificate archive.
    /// </summary>
    public byte[]? CertificateBytes { get; set; }

    /// <summary>
    /// Password required to decrypt the certificate's private key.
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Signer name or signatory entity (e.g. "Efizzy Insurance Underwriter").
    /// </summary>
    public string? SignerName { get; set; }

    /// <summary>
    /// Reason for signing (e.g. "I certify the accuracy and integrity of this document").
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Physical location of signing (e.g. "Lagos, Nigeria").
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Signer contact information (e.g. email or telephone).
    /// </summary>
    public string? ContactInfo { get; set; }

    /// <summary>
    /// Date and time of signing. Defaults to UTC now if null.
    /// </summary>
    public DateTimeOffset? SigningTime { get; set; }

    /// <summary>
    /// Certification level (DocMDP). Defaults to <see cref="PdfCertificationLevel.NoChangesPermitted"/>
    /// which permanently locks the document against any future alterations.
    /// </summary>
    public PdfCertificationLevel CertificationLevel { get; set; } = PdfCertificationLevel.NoChangesPermitted;

    /// <summary>
    /// Resolves the signing certificate from the provided properties.
    /// </summary>
    /// <returns>Loaded <see cref="X509Certificate2"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no valid certificate was configured.</exception>
    public X509Certificate2 LoadCertificate()
    {
        if (Certificate != null)
            return Certificate;

#pragma warning disable SYSLIB0057
        if (CertificateBytes != null && CertificateBytes.Length > 0)
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(CertificateBytes, CertificatePassword, X509KeyStorageFlags.Exportable);
#else
            return new X509Certificate2(CertificateBytes, CertificatePassword, X509KeyStorageFlags.Exportable);
#endif
        }

        if (!string.IsNullOrEmpty(CertificatePath))
        {
            if (!System.IO.File.Exists(CertificatePath))
                throw new System.IO.FileNotFoundException("Certificate file was not found.", CertificatePath);

#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12FromFile(CertificatePath, CertificatePassword, X509KeyStorageFlags.Exportable);
#else
            return new X509Certificate2(CertificatePath, CertificatePassword, X509KeyStorageFlags.Exportable);
#endif
        }
#pragma warning restore SYSLIB0057

        throw new InvalidOperationException("No certificate was provided. Specify Certificate, CertificateBytes, or CertificatePath in PdfSignatureOptions.");
    }
}
