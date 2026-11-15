using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Efizzy.Doc2Pdf.Security;

/// <summary>
/// Flattens interactive PDF form fields into non-editable static visual content.
/// Removes /AcroForm dictionaries and interactive /Widget annotations.
/// </summary>
public static class PdfFlattener
{
    private static readonly Regex ObjPattern = new(
        @"(?<id>\d+)\s+(?<gen>\d+)\s+obj\s*(?<body>.*?)\s*endobj",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex RootPattern = new(
        @"/Root\s+(?<id>\d+)\s+\d+\s+R",
        RegexOptions.Compiled);

    private static readonly Regex HeaderPattern = new(
        @"^%PDF-[^\r\n]+[\r\n]+%[^\r\n]+[\r\n]+",
        RegexOptions.Compiled);

    private static readonly Regex IdPattern = new(
        @"/ID\s*\[\s*<[^>]+>\s*<[^>]+>\s*\]",
        RegexOptions.Compiled);

    private static readonly Regex AnnotsPattern = new(
        @"/Annots\s*\[(?<content>.*?)\]",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex RefPattern = new(
        @"(?<id>\d+)\s+(?<gen>\d+)\s+R",
        RegexOptions.Compiled);

    private static readonly Regex ApPattern = new(
        @"/AP\s*<<[^\r\n]*?/N\s+(?<id>\d+)\s+\d+\s+R",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex RectPattern = new(
        @"/Rect\s*\[\s*(?<x1>[\d\.-]+)\s+(?<y1>[\d\.-]+)\s+(?<x2>[\d\.-]+)\s+(?<y2>[\d\.-]+)\s*\]",
        RegexOptions.Compiled);

    private static readonly Regex ResPattern = new(
        @"/Resources\s+(?<id>\d+)\s+\d+\s+R",
        RegexOptions.Compiled);

    private static readonly Regex CntArrayPattern = new(
        @"/Contents\s*\[(?<content>.*?)\]",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex CntSinglePattern = new(
        @"/Contents\s+(?<id>\d+)\s+\d+\s+R",
        RegexOptions.Compiled);

    /// <summary>
    /// Flattens interactive form fields in the provided PDF bytes.
    /// If the PDF contains no form fields, returns the original bytes untouched.
    /// </summary>
    /// <param name="pdfBytes">Source PDF bytes.</param>
    /// <returns>Non-editable flattened PDF bytes.</returns>
    public static byte[] Flatten(byte[] pdfBytes)
    {
        if (pdfBytes == null || pdfBytes.Length == 0)
            return pdfBytes ?? Array.Empty<byte>();

        string ascii = Encoding.ASCII.GetString(pdfBytes);

        // Fast-path: If no AcroForm or Widget annotation exists, it is already non-editable
        if (!ascii.Contains("/AcroForm") && !ascii.Contains("/Widget"))
            return pdfBytes;

        var objects = new Dictionary<int, (int Gen, string Body)>();
        int maxId = 0;

        foreach (Match m in ObjPattern.Matches(ascii))
        {
            int id = int.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture);
            int gen = int.Parse(m.Groups["gen"].Value, CultureInfo.InvariantCulture);
            string body = m.Groups["body"].Value;
            objects[id] = (gen, body);
            if (id > maxId) maxId = id;
        }

        if (objects.Count == 0)
            return pdfBytes;

        Match rootMatch = RootPattern.Match(ascii);
        if (!rootMatch.Success)
            return pdfBytes;

        int rootId = int.Parse(rootMatch.Groups["id"].Value, CultureInfo.InvariantCulture);
        if (!objects.ContainsKey(rootId))
            return pdfBytes;

        // 1. Remove /AcroForm from Catalog /Root
        string rootBody = objects[rootId].Body;
        rootBody = Regex.Replace(rootBody, @"/AcroForm\s*<<.*?>>", "", RegexOptions.Singleline);
        rootBody = Regex.Replace(rootBody, @"/AcroForm\s+\d+\s+\d+\s+R", "");
        objects[rootId] = (objects[rootId].Gen, rootBody);

        // 2. Identify all Widget objects
        var widgetIds = new HashSet<int>();
        foreach (var kvp in objects)
        {
            if (kvp.Value.Body.Contains("/Widget"))
            {
                widgetIds.Add(kvp.Key);
            }
        }

        // 3. For each Page object, remove widget references and bake appearance streams
        var pageIds = new List<int>();
        foreach (var kvp in objects)
        {
            if (kvp.Value.Body.Contains("/Type/Page") || kvp.Value.Body.Contains("/Type /Page"))
            {
                pageIds.Add(kvp.Key);
            }
        }

        foreach (int pageId in pageIds)
        {
            string pageBody = objects[pageId].Body;
            Match annMatch = AnnotsPattern.Match(pageBody);
            if (!annMatch.Success)
                continue;

            string annContent = annMatch.Groups["content"].Value;
            var remainingRefs = new List<string>();
            var widgetsToBake = new List<int>();

            foreach (Match refMatch in RefPattern.Matches(annContent))
            {
                int rId = int.Parse(refMatch.Groups["id"].Value, CultureInfo.InvariantCulture);
                int rGen = int.Parse(refMatch.Groups["gen"].Value, CultureInfo.InvariantCulture);
                if (widgetIds.Contains(rId))
                {
                    widgetsToBake.Add(rId);
                }
                else
                {
                    remainingRefs.Add($"{rId} {rGen} R");
                }
            }

            string newAnnots = remainingRefs.Count > 0
                ? $"/Annots[{string.Join(" ", remainingRefs)}]"
                : "/Annots[]";

            pageBody = pageBody.Substring(0, annMatch.Index) + newAnnots + pageBody.Substring(annMatch.Index + annMatch.Length);

            // Bake appearance for widgets with /AP and /Rect
            foreach (int wid in widgetsToBake)
            {
                if (!objects.TryGetValue(wid, out var wObj))
                    continue;

                Match apMatch = ApPattern.Match(wObj.Body);
                Match rectMatch = RectPattern.Match(wObj.Body);

                if (apMatch.Success && rectMatch.Success)
                {
                    int xobjId = int.Parse(apMatch.Groups["id"].Value, CultureInfo.InvariantCulture);
                    string x1 = rectMatch.Groups["x1"].Value;
                    string y1 = rectMatch.Groups["y1"].Value;
                    string annotName = $"Annot{wid}";

                    // Inject into Page's Resources /XObject
                    Match resMatch = ResPattern.Match(pageBody);
                    if (resMatch.Success)
                    {
                        int resId = int.Parse(resMatch.Groups["id"].Value, CultureInfo.InvariantCulture);
                        if (objects.TryGetValue(resId, out var resObj))
                        {
                            string resBody = resObj.Body;
                            if (resBody.Contains("/XObject"))
                            {
                                resBody = Regex.Replace(resBody, @"/XObject\s*<<", $"/XObject<< /{annotName} {xobjId} 0 R ");
                            }
                            else
                            {
                                int firstDict = resBody.IndexOf("<<", StringComparison.Ordinal);
                                if (firstDict >= 0)
                                {
                                    resBody = resBody.Insert(firstDict + 2, $" /XObject<< /{annotName} {xobjId} 0 R >> ");
                                }
                            }
                            objects[resId] = (resObj.Gen, resBody);
                        }
                    }

                    // Create new Content stream stamping the XObject
                    string streamCmd = $"q\n1 0 0 1 {x1} {y1} cm\n/{annotName} Do\nQ\n";
                    byte[] streamBytes = Encoding.ASCII.GetBytes(streamCmd);
                    string streamHeader = $"<</Length {streamBytes.Length}>>\nstream\n{streamCmd}\nendstream";

                    maxId++;
                    int newStreamId = maxId;
                    objects[newStreamId] = (0, streamHeader);

                    // Add to page /Contents
                    Match cntArr = CntArrayPattern.Match(pageBody);
                    if (cntArr.Success)
                    {
                        int closeBracket = cntArr.Index + cntArr.Length - 1;
                        pageBody = pageBody.Insert(closeBracket, $" {newStreamId} 0 R");
                    }
                    else
                    {
                        Match cntSingle = CntSinglePattern.Match(pageBody);
                        if (cntSingle.Success)
                        {
                            int existingCnt = int.Parse(cntSingle.Groups["id"].Value, CultureInfo.InvariantCulture);
                            pageBody = pageBody.Substring(0, cntSingle.Index) + $"/Contents[{existingCnt} 0 R {newStreamId} 0 R]" + pageBody.Substring(cntSingle.Index + cntSingle.Length);
                        }
                        else
                        {
                            int lastDict = pageBody.LastIndexOf(">>", StringComparison.Ordinal);
                            if (lastDict >= 0)
                            {
                                pageBody = pageBody.Insert(lastDict, $"/Contents[{newStreamId} 0 R]");
                            }
                        }
                    }
                }
            }

            objects[pageId] = (objects[pageId].Gen, pageBody);
        }

        // 4. Serialize reconstructed PDF
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, Encoding.ASCII);

        Match headerMatch = HeaderPattern.Match(ascii);
        if (headerMatch.Success)
        {
            writer.Write(headerMatch.Value);
        }
        else
        {
            writer.Write("%PDF-1.7\n%\x80\x81\x82\x83\n");
        }
        writer.Flush();

        var xrefOffsets = new Dictionary<int, long>();
        var sortedIds = new List<int>(objects.Keys);
        sortedIds.Sort();

        foreach (int oid in sortedIds)
        {
            xrefOffsets[oid] = ms.Position;
            writer.Write($"{oid} 0 obj\n");
            writer.Write(objects[oid].Body);
            writer.Write("\nendobj\n\n");
            writer.Flush();
        }

        long xrefPos = ms.Position;
        int totalObjCount = maxId + 1;
        writer.Write($"xref\n0 {totalObjCount}\n0000000000 65535 f \n");

        for (int i = 1; i <= maxId; i++)
        {
            if (xrefOffsets.TryGetValue(i, out long offset))
            {
                writer.Write($"{offset:D10} 00000 n \n");
            }
            else
            {
                writer.Write("0000000000 00000 f \n");
            }
        }

        Match idMatch = IdPattern.Match(ascii);
        string idPart = idMatch.Success ? idMatch.Value : "";

        writer.Write($"trailer\n<</Size {totalObjCount}/Root {rootId} 0 R {idPart}>>\nstartxref\n{xrefPos}\n%%EOF\n");
        writer.Flush();

        return ms.ToArray();
    }
}
