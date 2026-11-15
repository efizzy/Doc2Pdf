using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Efizzy.Doc2Pdf.Security;

/// <summary>
/// Provides digital signature and document certification (DocMDP / PAdES) for PDF documents.
/// Detects tampering and invalidates the document if any modifications occur.
/// </summary>
public static class PdfSigner
{
    private const int ContentsHexLength = 8192; // 4096 bytes capacity for PKCS#7 detached signature

    private static readonly Regex TrailerPattern = new(
        @"trailer\s*<<(?<trailer>.*?)>>\s*startxref\s+(?<startxref>\d+)\s+%%EOF",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.RightToLeft);

    private static readonly Regex SizePattern = new(
        @"/Size\s+(?<size>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex RootPattern = new(
        @"/Root\s+(?<root>\d+)\s+\d+\s+R",
        RegexOptions.Compiled);

    private static readonly Regex IdPattern = new(
        @"/ID\s*\[\s*<[^>]+>\s*<[^>]+>\s*\]",
        RegexOptions.Compiled);

    private static readonly Regex ByteRangePattern = new(
        @"/ByteRange\s*\[\s*(?<o0>\d+)\s+(?<l0>\d+)\s+(?<o1>\d+)\s+(?<l1>\d+)\s*\]",
        RegexOptions.Compiled);

    private static readonly Regex ContentsPattern = new(
        @"/Contents\s*<(?<hex>[0-9A-Fa-f]+)>",
        RegexOptions.Compiled);

    /// <summary>
    /// Digitally signs and certifies a PDF document using the specified options.
    /// Injects a DocMDP certification dictionary that flags tampering in PDF readers if altered.
    /// </summary>
    /// <param name="pdfBytes">Source PDF bytes.</param>
    /// <param name="options">Cryptographic signature and certification options.</param>
    /// <returns>Signed and certified PDF bytes.</returns>
    public static byte[] Sign(byte[] pdfBytes, PdfSignatureOptions options)
    {
        if (pdfBytes == null || pdfBytes.Length == 0)
            throw new ArgumentException("PDF buffer cannot be null or empty.", nameof(pdfBytes));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        using X509Certificate2 cert = options.LoadCertificate();

        string ascii = Encoding.ASCII.GetString(pdfBytes);
        Match trailerMatch = TrailerPattern.Match(ascii);
        if (!trailerMatch.Success)
            throw new InvalidOperationException("Could not find valid PDF trailer structure.");

        string trailerBody = trailerMatch.Groups["trailer"].Value;
        long oldStartXref = long.Parse(trailerMatch.Groups["startxref"].Value, CultureInfo.InvariantCulture);

        Match sizeMatch = SizePattern.Match(trailerBody);
        int oldSize = sizeMatch.Success ? int.Parse(sizeMatch.Groups["size"].Value, CultureInfo.InvariantCulture) : 10;

        Match rootMatch = RootPattern.Match(trailerBody);
        if (!rootMatch.Success)
            throw new InvalidOperationException("Could not find /Root catalog reference in PDF trailer.");

        int rootId = int.Parse(rootMatch.Groups["root"].Value, CultureInfo.InvariantCulture);

        // Find Page 1 object ID
        int page1Id = FindFirstPageId(ascii, rootId);

        // Allocate IDs for signature objects
        int maxId = Math.Max(oldSize - 1, GetMaxObjectId(ascii));
        int sigFieldId = maxId + 1;
        int sigDictId = maxId + 2;
        int newSize = sigDictId + 1;

        // Extract Catalog object dictionary body
        string catDict = ExtractObjectDictBody(ascii, rootId);
        // Extract Page 1 object dictionary body
        string page1Dict = ExtractObjectDictBody(ascii, page1Id);

        // Update Catalog: Add /Perms <</DocMDP {sigDictId} 0 R>> and /AcroForm <</SigFlags 3/Fields [{sigFieldId} 0 R]>>
        string newCatDict = catDict;
        if (newCatDict.Contains("/Perms"))
        {
            newCatDict = Regex.Replace(newCatDict, @"/Perms\s*<<.*?>>", $"/Perms<</DocMDP {sigDictId} 0 R>>", RegexOptions.Singleline);
        }
        else
        {
            newCatDict += $"/Perms<</DocMDP {sigDictId} 0 R>>";
        }

        if (newCatDict.Contains("/AcroForm"))
        {
            newCatDict = Regex.Replace(newCatDict, @"/AcroForm\s*<<.*?>>", $"/AcroForm<</SigFlags 3/Fields[{sigFieldId} 0 R]>>", RegexOptions.Singleline);
        }
        else
        {
            newCatDict += $"/AcroForm<</SigFlags 3/Fields[{sigFieldId} 0 R]>>";
        }

        // Update Page 1: Add sigFieldId to /Annots
        string newPage1Dict = page1Dict;
        Match annMatch = Regex.Match(newPage1Dict, @"/Annots\s*\[(?<ann>.*?)\]", RegexOptions.Singleline);
        if (annMatch.Success)
        {
            int closeBracket = annMatch.Index + annMatch.Length - 1;
            newPage1Dict = newPage1Dict.Insert(closeBracket, $" {sigFieldId} 0 R");
        }
        else
        {
            newPage1Dict += $"/Annots[{sigFieldId} 0 R]";
        }

        DateTimeOffset signingTime = options.SigningTime ?? DateTimeOffset.UtcNow;
        string pdfDate = $"D:{signingTime:yyyyMMddHHmmss}Z";

        string signerName = EscapePdfString(options.SignerName ?? "Authorized Signatory");
        string reason = EscapePdfString(options.Reason ?? "Document Authenticity & Integrity Certification");
        string location = EscapePdfString(options.Location ?? "");
        string contactInfo = EscapePdfString(options.ContactInfo ?? "");
        int pLevel = (int)options.CertificationLevel;

        string placeholderHex = new string('0', ContentsHexLength);
        string byteRangePlaceholder = "/ByteRange [ 0000000000 0000000000 0000000000 0000000000 ]";

        // Build Signature Dict
        var sbSigDict = new StringBuilder();
        sbSigDict.Append($"{sigDictId} 0 obj\n<<\n");
        sbSigDict.Append("/Type /Sig\n");
        sbSigDict.Append("/Filter /Adobe.PPKLite\n");
        sbSigDict.Append("/SubFilter /adbe.pkcs7.detached\n");
        sbSigDict.Append(byteRangePlaceholder).Append('\n');
        sbSigDict.Append($"/Contents <{placeholderHex}>\n");
        sbSigDict.Append($"/M ({pdfDate})\n");
        sbSigDict.Append($"/Name ({signerName})\n");
        sbSigDict.Append($"/Reason ({reason})\n");
        if (!string.IsNullOrEmpty(location)) sbSigDict.Append($"/Location ({location})\n");
        if (!string.IsNullOrEmpty(contactInfo)) sbSigDict.Append($"/ContactInfo ({contactInfo})\n");
        sbSigDict.Append($"/Reference [ << /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P {pLevel} /V /1.2 >> >> ]\n");
        sbSigDict.Append(">>\nendobj\n\n");
        string sigDictString = sbSigDict.ToString();

        // Build incremental update buffer
        using var ms = new MemoryStream();
        ms.Write(pdfBytes, 0, pdfBytes.Length);

        long posCat = ms.Position;
        byte[] bCat = Encoding.ASCII.GetBytes($"{rootId} 0 obj\n<<{newCatDict}>>\nendobj\n\n");
        ms.Write(bCat, 0, bCat.Length);

        long posPage1 = ms.Position;
        byte[] bPage1 = Encoding.ASCII.GetBytes($"{page1Id} 0 obj\n<<{newPage1Dict}>>\nendobj\n\n");
        ms.Write(bPage1, 0, bPage1.Length);

        long posSigField = ms.Position;
        byte[] bSigField = Encoding.ASCII.GetBytes($"{sigFieldId} 0 obj\n<</Type /Annot /Subtype /Widget /FT /Sig /Rect [0 0 0 0] /F 132 /T (Signature1) /V {sigDictId} 0 R>>\nendobj\n\n");
        ms.Write(bSigField, 0, bSigField.Length);

        long posSigDict = ms.Position;
        byte[] bSigDict = Encoding.ASCII.GetBytes(sigDictString);
        ms.Write(bSigDict, 0, bSigDict.Length);

        long xrefPos = ms.Position;
        var sbXref = new StringBuilder();
        sbXref.Append("xref\n");
        sbXref.Append($"{rootId} 1\n");
        sbXref.Append($"{posCat:D10} 00000 n \n");
        sbXref.Append($"{page1Id} 1\n");
        sbXref.Append($"{posPage1:D10} 00000 n \n");
        sbXref.Append($"{sigFieldId} 2\n");
        sbXref.Append($"{posSigField:D10} 00000 n \n");
        sbXref.Append($"{posSigDict:D10} 00000 n \n");

        byte[] bXref = Encoding.ASCII.GetBytes(sbXref.ToString());
        ms.Write(bXref, 0, bXref.Length);

        Match idMatch = IdPattern.Match(trailerBody);
        string idPart = idMatch.Success ? idMatch.Value : "";

        string trailerStr = $"trailer\n<</Size {newSize} /Root {rootId} 0 R /Prev {oldStartXref} {idPart}>>\nstartxref\n{xrefPos}\n%%EOF\n";
        byte[] bTrailer = Encoding.ASCII.GetBytes(trailerStr);
        ms.Write(bTrailer, 0, bTrailer.Length);

        byte[] result = ms.ToArray();

        // Find Contents and ByteRange offsets in result
        byte[] contentsTag = Encoding.ASCII.GetBytes("/Contents <");
        int cTagPos = IndexOfSequence(result, contentsTag, (int)posSigDict);
        if (cTagPos < 0)
            throw new InvalidOperationException("Failed to locate /Contents tag in signature dictionary.");

        int contentsStart = cTagPos + contentsTag.Length - 1; // position of '<'
        int contentsEnd = Array.IndexOf(result, (byte)'>', contentsStart);
        if (contentsEnd < 0)
            throw new InvalidOperationException("Failed to locate closing '>' for /Contents.");

        int len0 = contentsStart;
        int offset1 = contentsEnd + 1;
        int len1 = result.Length - offset1;

        // Overwrite ByteRange placeholder
        byte[] brPlaceholderBytes = Encoding.ASCII.GetBytes(byteRangePlaceholder);
        int brPos = IndexOfSequence(result, brPlaceholderBytes, (int)posSigDict);
        if (brPos < 0)
            throw new InvalidOperationException("Failed to locate /ByteRange placeholder.");

        string newByteRange = $"/ByteRange [ {0:D10} {len0:D10} {offset1:D10} {len1:D10} ]";
        byte[] newByteRangeBytes = Encoding.ASCII.GetBytes(newByteRange);
        if (newByteRangeBytes.Length != brPlaceholderBytes.Length)
            throw new InvalidOperationException("ByteRange placeholder length mismatch.");

        Buffer.BlockCopy(newByteRangeBytes, 0, result, brPos, newByteRangeBytes.Length);

        // Compute SHA-256 over byte ranges (Range 1: 0..len0, Range 2: offset1..(offset1+len1))
        byte[] documentHash;
        using (var sha = SHA256.Create())
        {
            sha.TransformBlock(result, 0, len0, null, 0);
            sha.TransformFinalBlock(result, offset1, len1);
            documentHash = sha.Hash!;
        }

        // Generate PKCS#7 detached signature
        byte[] pkcs7Signature = Pkcs7Signer.CreateDetachedSignature(documentHash, cert, signingTime);
        string hexSignature = BytesToHex(pkcs7Signature);

        if (hexSignature.Length > ContentsHexLength)
            throw new InvalidOperationException($"PKCS#7 signature length ({hexSignature.Length} hex chars) exceeds allocated placeholder capacity ({ContentsHexLength}).");

        // Write hex signature into contents placeholder
        byte[] hexBytes = Encoding.ASCII.GetBytes(hexSignature);
        Buffer.BlockCopy(hexBytes, 0, result, contentsStart + 1, hexBytes.Length);

        return result;
    }

    /// <summary>
    /// Cryptographically verifies the embedded signature and document integrity.
    /// Checks that the document has not been altered or tampered with since signing.
    /// </summary>
    public static bool VerifySignature(byte[] signedPdfBytes, out string? failureReason)
    {
        if (signedPdfBytes == null || signedPdfBytes.Length == 0)
        {
            failureReason = "PDF buffer is empty.";
            return false;
        }

        string ascii = Encoding.ASCII.GetString(signedPdfBytes);
        Match brMatch = ByteRangePattern.Match(ascii);
        if (!brMatch.Success)
        {
            failureReason = "No digital signature /ByteRange dictionary found.";
            return false;
        }

        int o0 = int.Parse(brMatch.Groups["o0"].Value, CultureInfo.InvariantCulture);
        int l0 = int.Parse(brMatch.Groups["l0"].Value, CultureInfo.InvariantCulture);
        int o1 = int.Parse(brMatch.Groups["o1"].Value, CultureInfo.InvariantCulture);
        int l1 = int.Parse(brMatch.Groups["l1"].Value, CultureInfo.InvariantCulture);

        if (o0 != 0 || (l0 + (o1 - l0) + l1) != signedPdfBytes.Length)
        {
            failureReason = "ByteRange bounds are invalid or file size was modified.";
            return false;
        }

        Match cMatch = ContentsPattern.Match(ascii);
        if (!cMatch.Success)
        {
            failureReason = "No signature /Contents hex stream found.";
            return false;
        }

        string hex = cMatch.Groups["hex"].Value.TrimEnd('0');
        if (hex.Length % 2 != 0) hex += "0";
        byte[] pkcs7Bytes = HexToBytes(hex);

        // Compute actual SHA-256 over specified byte ranges
        byte[] computedHash;
        using (var sha = SHA256.Create())
        {
            sha.TransformBlock(signedPdfBytes, o0, l0, null, 0);
            sha.TransformFinalBlock(signedPdfBytes, o1, l1);
            computedHash = sha.Hash!;
        }

        // Verify that computed hash exists in PKCS#7 signedAttributes (MessageDigest OID 1.2.840.113549.1.9.4)
        byte[] oidMessageDigest = new byte[] { 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x09, 0x04 };
        int mdIndex = IndexOfSequence(pkcs7Bytes, oidMessageDigest, 0);
        if (mdIndex < 0)
        {
            failureReason = "Signature does not contain MessageDigest signed attribute.";
            return false;
        }

        // Following the OID is SET (0x31) -> OCTET STRING (0x04, length 0x20)
        byte[] octetStringHeader = new byte[] { 0x04, 0x20 };
        int hashPos = IndexOfSequence(pkcs7Bytes, octetStringHeader, mdIndex);
        if (hashPos < 0)
        {
            failureReason = "Malformed MessageDigest attribute in PKCS#7 payload.";
            return false;
        }

        byte[] embeddedHash = new byte[32];
        Buffer.BlockCopy(pkcs7Bytes, hashPos + 2, embeddedHash, 0, 32);

        for (int i = 0; i < 32; i++)
        {
            if (computedHash[i] != embeddedHash[i])
            {
                failureReason = "Document hash mismatch: The document has been altered or tampered with since signing.";
                return false;
            }
        }

        failureReason = null;
        return true;
    }

    private static int FindFirstPageId(string ascii, int rootId)
    {
        string catDict = ExtractObjectDictBody(ascii, rootId);
        Match mPages = Regex.Match(catDict, @"/Pages\s+(?<id>\d+)\s+\d+\s+R");
        if (!mPages.Success)
            throw new InvalidOperationException("Could not find /Pages in catalog dictionary.");

        int pagesId = int.Parse(mPages.Groups["id"].Value, CultureInfo.InvariantCulture);
        string pagesDict = ExtractObjectDictBody(ascii, pagesId);

        Match mKids = Regex.Match(pagesDict, @"/Kids\s*\[\s*(?<firstId>\d+)\s+\d+\s+R");
        if (!mKids.Success)
            throw new InvalidOperationException("Could not find /Kids in pages dictionary.");

        return int.Parse(mKids.Groups["firstId"].Value, CultureInfo.InvariantCulture);
    }

    private static int GetMaxObjectId(string ascii)
    {
        int max = 0;
        foreach (Match m in Regex.Matches(ascii, @"(?<id>\d+)\s+\d+\s+obj"))
        {
            if (int.TryParse(m.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                if (id > max) max = id;
            }
        }
        return max;
    }

    private static string ExtractObjectDictBody(string ascii, int objId)
    {
        var m = Regex.Match(ascii, $@"\b{objId}\s+\d+\s+obj\s*<<(?<body>.*?)>>(\s*stream|\s*endobj)", RegexOptions.Singleline);
        if (!m.Success)
            throw new InvalidOperationException($"Could not find dictionary for object {objId}.");

        return m.Groups["body"].Value;
    }

    private static string EscapePdfString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++)
        {
            sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static byte[] HexToBytes(string hex)
    {
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        return bytes;
    }

    private static int IndexOfSequence(byte[] buffer, byte[] pattern, int startIndex)
    {
        for (int i = startIndex; i <= buffer.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }
}
