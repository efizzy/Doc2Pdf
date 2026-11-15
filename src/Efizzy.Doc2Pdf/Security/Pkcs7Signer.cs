using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Efizzy.Doc2Pdf.Security;

/// <summary>
/// Generates standard ISO 32000 compliant PKCS#7 / CMS detached digital signatures (adbe.pkcs7.detached).
/// </summary>
internal static class Pkcs7Signer
{
    private const string OidData = "1.2.840.113549.1.7.1";
    private const string OidSignedData = "1.2.840.113549.1.7.2";
    private const string OidSha256 = "2.16.840.1.101.3.4.2.1";
    private const string OidRsaEncryption = "1.2.840.113549.1.1.1";
    private const string OidEcdsaSha256 = "1.2.840.10045.4.3.2";
    private const string OidContentType = "1.2.840.113549.1.9.3";
    private const string OidMessageDigest = "1.2.840.113549.1.9.4";
    private const string OidSigningTime = "1.2.840.113549.1.9.5";

    public static byte[] CreateDetachedSignature(byte[] documentHash, X509Certificate2 cert, DateTimeOffset signingTime)
    {
        if (cert == null)
            throw new ArgumentNullException(nameof(cert));
        if (documentHash == null || documentHash.Length == 0)
            throw new ArgumentException("Document hash cannot be null or empty.", nameof(documentHash));

        bool isRsa = cert.GetRSAPrivateKey() != null;
        bool isEcdsa = !isRsa && cert.GetECDsaPrivateKey() != null;
        if (!isRsa && !isEcdsa)
            throw new InvalidOperationException("Certificate must have an accessible RSA or ECDSA private key.");

        // 1. Build SignedAttributes
        byte[] signedAttrsContent = BuildSignedAttributesContent(documentHash, signingTime);

        // In RFC 5652 §5.4, the input to signature generation is the DER SET OF (tag 0x31)
        var setWriter = new DerWriter();
        setWriter.WriteSet(signedAttrsContent);
        byte[] signedAttrsSetBytes = setWriter.ToArray();

        // 2. Sign the signed attributes
        byte[] signatureBytes;
        if (isRsa)
        {
            using RSA rsa = cert.GetRSAPrivateKey()!;
            signatureBytes = rsa.SignData(signedAttrsSetBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        else
        {
            using ECDsa ecdsa = cert.GetECDsaPrivateKey()!;
            signatureBytes = ecdsa.SignData(signedAttrsSetBytes, HashAlgorithmName.SHA256);
        }

        // 3. Build SignerInfo
        byte[] signerInfoBytes = BuildSignerInfo(cert, signedAttrsContent, signatureBytes, isRsa);

        // 4. Build SignedData
        byte[] signedDataBytes = BuildSignedData(cert, signerInfoBytes);

        // 5. Wrap in ContentInfo
        var contentInfoWriter = new DerWriter();
        var contentInfoBody = new DerWriter();
        contentInfoBody.WriteOid(OidSignedData);
        contentInfoBody.WriteContextSpecific(0, signedDataBytes, constructed: true);
        contentInfoWriter.WriteSequence(contentInfoBody.ToArray());

        return contentInfoWriter.ToArray();
    }

    private static byte[] BuildSignedAttributesContent(byte[] documentHash, DateTimeOffset signingTime)
    {
        using var ms = new MemoryStream();

        // Attribute 1: ContentType
        var attr1 = new DerWriter();
        var attr1Body = new DerWriter();
        attr1Body.WriteOid(OidContentType);
        var attr1Set = new DerWriter();
        attr1Set.WriteOid(OidData);
        attr1Body.WriteSet(attr1Set.ToArray());
        attr1.WriteSequence(attr1Body.ToArray());
        byte[] b1 = attr1.ToArray();
        ms.Write(b1, 0, b1.Length);

        // Attribute 2: MessageDigest
        var attr2 = new DerWriter();
        var attr2Body = new DerWriter();
        attr2Body.WriteOid(OidMessageDigest);
        var attr2Set = new DerWriter();
        attr2Set.WriteOctetString(documentHash);
        attr2Body.WriteSet(attr2Set.ToArray());
        attr2.WriteSequence(attr2Body.ToArray());
        byte[] b2 = attr2.ToArray();
        ms.Write(b2, 0, b2.Length);

        // Attribute 3: SigningTime
        var attr3 = new DerWriter();
        var attr3Body = new DerWriter();
        attr3Body.WriteOid(OidSigningTime);
        var attr3Set = new DerWriter();
        attr3Set.WriteUtcTime(signingTime);
        attr3Body.WriteSet(attr3Set.ToArray());
        attr3.WriteSequence(attr3Body.ToArray());
        byte[] b3 = attr3.ToArray();
        ms.Write(b3, 0, b3.Length);

        return ms.ToArray();
    }

    private static byte[] BuildSignerInfo(X509Certificate2 cert, byte[] signedAttrsContent, byte[] signatureBytes, bool isRsa)
    {
        var si = new DerWriter();
        var siBody = new DerWriter();

        // version CMSVersion = 1
        siBody.WriteInteger(1);

        // sid: IssuerAndSerialNumber
        var sidWriter = new DerWriter();
        sidWriter.WriteBytes(cert.IssuerName.RawData);
        byte[] serialNumberBytes = cert.GetSerialNumber();
        Array.Reverse(serialNumberBytes); // Certificate serial numbers are little-endian in .NET, DER requires big-endian
        sidWriter.WriteBigInteger(serialNumberBytes);
        siBody.WriteSequence(sidWriter.ToArray());

        // digestAlgorithm: SHA-256
        var daWriter = new DerWriter();
        daWriter.WriteOid(OidSha256);
        daWriter.WriteNull();
        siBody.WriteSequence(daWriter.ToArray());

        // signedAttrs [0] IMPLICIT
        siBody.WriteContextSpecific(0, signedAttrsContent, constructed: true);

        // signatureAlgorithm
        var saWriter = new DerWriter();
        saWriter.WriteOid(isRsa ? OidRsaEncryption : OidEcdsaSha256);
        if (isRsa) saWriter.WriteNull();
        siBody.WriteSequence(saWriter.ToArray());

        // signature: OCTET STRING
        siBody.WriteOctetString(signatureBytes);

        si.WriteSequence(siBody.ToArray());
        return si.ToArray();
    }

    private static byte[] BuildSignedData(X509Certificate2 cert, byte[] signerInfoBytes)
    {
        var sd = new DerWriter();
        var sdBody = new DerWriter();

        // version CMSVersion = 1
        sdBody.WriteInteger(1);

        // digestAlgorithms: SET OF { AlgorithmIdentifier (SHA-256) }
        var da = new DerWriter();
        da.WriteOid(OidSha256);
        da.WriteNull();
        var daSeq = new DerWriter();
        daSeq.WriteSequence(da.ToArray());
        var daSet = new DerWriter();
        daSet.WriteSet(daSeq.ToArray());
        sdBody.WriteBytes(daSet.ToArray());

        // encapContentInfo: SEQUENCE { ContentType: data } (detached: content omitted)
        var eci = new DerWriter();
        eci.WriteOid(OidData);
        var eciSeq = new DerWriter();
        eciSeq.WriteSequence(eci.ToArray());
        sdBody.WriteBytes(eciSeq.ToArray());

        // certificates: [0] IMPLICIT { cert.RawData }
        sdBody.WriteContextSpecific(0, cert.RawData, constructed: true);

        // signerInfos: SET OF { SignerInfo }
        var siSet = new DerWriter();
        siSet.WriteSet(signerInfoBytes);
        sdBody.WriteBytes(siSet.ToArray());

        sd.WriteSequence(sdBody.ToArray());
        return sd.ToArray();
    }
}
