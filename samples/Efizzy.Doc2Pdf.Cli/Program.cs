using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Efizzy.Doc2Pdf;

Console.WriteLine("========================================");
Console.WriteLine("Efizzy.Doc2Pdf Command-Line Conversion Utility");
Console.WriteLine($"Engine Version: {Doc2PdfConverter.GetNativeVersion()}");
Console.WriteLine("========================================");

if (args.Length > 0 && args[0].Equals("--benchmark", StringComparison.OrdinalIgnoreCase))
{
    RunBenchmark();
    return;
}

if (args.Length < 2)
{
    Console.WriteLine("Usage: Efizzy.Doc2Pdf.Cli <input-file> <output-file.pdf> [options]");
    Console.WriteLine("       Efizzy.Doc2Pdf.Cli --benchmark");
    Console.WriteLine("Options:");
    Console.WriteLine("  --markup                  Show tracked revisions");
    Console.WriteLine("  --comments                Render comment balloons in margin");
    Console.WriteLine("  --paper A4|Letter|Legal   Scale to paper size");
    Console.WriteLine("  --watermark <text>        Add semi-transparent watermark across pages");
    Console.WriteLine("  --watermark-color <hex>   Watermark color (default #C53030)");
    Console.WriteLine("  --pages <range>           Page range filter (e.g. '1-3, 5')");
    Console.WriteLine("  --title <text>            PDF Title metadata");
    Console.WriteLine("  --author <text>           PDF Author metadata");
    Console.WriteLine("  --subject <text>          PDF Subject metadata");
    Console.WriteLine("  --keywords <text>         PDF Keywords metadata");
    Console.WriteLine("  --fonts-dir <path>        Custom font folder (.ttf/.otf)");
    Console.WriteLine("\nSupported formats: .docx, .doc, .md, .html, .rtf, .odt, .tex, .xml, .txt");
    return;
}

string inputPath = args[0];
string outputPath = args[1];

bool markup = false;
bool comments = false;
(float Width, float Height)? paper = null;
string? watermarkText = null;
string? watermarkColor = null;
string? pageRange = null;
string? title = null;
string? author = null;
string? subject = null;
string? keywords = null;
string? fontsDir = null;

for (int i = 2; i < args.Length; i++)
{
    if (args[i].Equals("--markup", StringComparison.OrdinalIgnoreCase))
        markup = true;
    else if (args[i].Equals("--comments", StringComparison.OrdinalIgnoreCase))
        comments = true;
    else if (args[i].Equals("--paper", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        string p = args[++i].ToLowerInvariant();
        paper = p switch
        {
            "a4" => StandardPaperSize.A4,
            "a3" => StandardPaperSize.A3,
            "legal" => StandardPaperSize.Legal,
            _ => StandardPaperSize.Letter
        };
    }
    else if (args[i].Equals("--watermark", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        watermarkText = args[++i];
    else if (args[i].Equals("--watermark-color", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        watermarkColor = args[++i];
    else if (args[i].Equals("--pages", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        pageRange = args[++i];
    else if (args[i].Equals("--title", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        title = args[++i];
    else if (args[i].Equals("--author", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        author = args[++i];
    else if (args[i].Equals("--subject", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        subject = args[++i];
    else if (args[i].Equals("--keywords", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        keywords = args[++i];
    else if (args[i].Equals("--fonts-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        fontsDir = args[++i];
}

try
{
    var detectedFormat = DocumentFormatExtensions.DetectFormatFromFile(inputPath);
    Console.WriteLine($"Converting '{inputPath}' ({detectedFormat}) -> '{outputPath}'...");
    var options = new Doc2PdfOptions
    {
        IncludeMarkup = markup,
        ShowComments = comments,
        PaperSizePoints = paper,
        WatermarkText = watermarkText,
        WatermarkColor = watermarkColor,
        PageRange = pageRange,
        Title = title,
        Author = author,
        Subject = subject,
        Keywords = keywords,
        CustomFontsDirectory = fontsDir
    };

    var sw = Stopwatch.StartNew();
    Doc2PdfConverter.ConvertFile(inputPath, outputPath, options);
    sw.Stop();

    var fi = new FileInfo(outputPath);
    Console.WriteLine($"Success! Generated '{outputPath}' ({fi.Length:N0} bytes) in {sw.ElapsedMilliseconds} ms.");
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
    Environment.Exit(1);
}

static void RunBenchmark()
{
    Console.WriteLine("\n>>> RUNNING STANDALONE PERFORMANCE & RESOURCE BENCHMARK <<<");
    var proc = Process.GetCurrentProcess();
    proc.Refresh();

    long initialWorkingSet = proc.WorkingSet64;
    long initialPrivateMemory = proc.PrivateMemorySize64;
    long initialGcHeap = GC.GetTotalMemory(false);

    Console.WriteLine($"Initial Process Baseline:");
    Console.WriteLine($"  Working Set:    {initialWorkingSet / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine($"  Private Memory: {initialPrivateMemory / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine($"  Managed GC Heap: {initialGcHeap / 1024.0 / 1024.0:F2} MB");

    // Sample 1: Markdown
    string mdSample = "# Financial Report 2026\n\nExecutive summary of quarterly earnings and projections.\n\n| Region | Revenue | Growth |\n| :--- | :--- | :--- |\n| North America | $12.4M | +18% |\n| EMEA | $8.2M | +12% |\n| APAC | $15.1M | +24% |\n\n## Recommendations\n- Expand automated pipeline\n- Reduce cloud compute latency\n";
    byte[] mdBytes = Encoding.UTF8.GetBytes(mdSample);

    // Sample 2: Complex 10-Page HTML (Church Anniversary)
    string htmlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "church_anniversary.html");
    htmlPath = Path.GetFullPath(htmlPath);
    byte[] htmlBytes;
    if (File.Exists(htmlPath))
    {
        htmlBytes = File.ReadAllBytes(htmlPath);
    }
    else
    {
        htmlBytes = Encoding.UTF8.GetBytes("<html><body><h1>Benchmark Document</h1><p>Test document</p></body></html>");
    }

    Console.WriteLine("\n--- 1. Cold Start Benchmarks ---");
    var sw = Stopwatch.StartNew();
    byte[] pdf1 = Doc2PdfConverter.Convert(mdBytes, DocumentFormat.Markdown);
    sw.Stop();
    long coldMdMs = sw.ElapsedMilliseconds;

    proc.Refresh();
    long coldWorkingSet = proc.WorkingSet64;
    Console.WriteLine($"Cold Start (Markdown): {coldMdMs} ms (Output: {pdf1.Length:N0} bytes)");
    Console.WriteLine($"  Working Set after cold start: {coldWorkingSet / 1024.0 / 1024.0:F2} MB");

    sw.Restart();
    byte[] pdf2 = Doc2PdfConverter.Convert(htmlBytes, DocumentFormat.Html);
    sw.Stop();
    long coldHtmlMs = sw.ElapsedMilliseconds;

    proc.Refresh();
    long coldHtmlWorkingSet = proc.WorkingSet64;
    Console.WriteLine($"Cold Start (10-Page HTML): {coldHtmlMs} ms (Output: {pdf2.Length:N0} bytes)");
    Console.WriteLine($"  Working Set after 10-Page HTML: {coldHtmlWorkingSet / 1024.0 / 1024.0:F2} MB");

    Console.WriteLine("\n--- 2. Warm Throughput & CPU Utilization (50 Iterations) ---");
    const int iterations = 50;

    // Benchmark Markdown
    int gen0Before = GC.CollectionCount(0);
    int gen1Before = GC.CollectionCount(1);
    int gen2Before = GC.CollectionCount(2);
    TimeSpan cpuBefore = proc.TotalProcessorTime;
    sw.Restart();

    for (int i = 0; i < iterations; i++)
    {
        _ = Doc2PdfConverter.Convert(mdBytes, DocumentFormat.Markdown);
    }

    sw.Stop();
    proc.Refresh();
    TimeSpan cpuAfter = proc.TotalProcessorTime;
    double mdElapsedSec = sw.Elapsed.TotalSeconds;
    double mdCpuSec = (cpuAfter - cpuBefore).TotalSeconds;
    double mdCpuUtil = (mdCpuSec / mdElapsedSec / Environment.ProcessorCount) * 100.0;
    double mdAvgMs = sw.ElapsedMilliseconds / (double)iterations;

    Console.WriteLine($"Markdown Throughput ({iterations} runs):");
    Console.WriteLine($"  Total Time:    {sw.ElapsedMilliseconds} ms ({mdAvgMs:F2} ms/doc)");
    Console.WriteLine($"  Throughput:    {iterations / mdElapsedSec:F1} docs/sec");
    Console.WriteLine($"  CPU Time:      {mdCpuSec:F2} s (Avg CPU: {mdCpuUtil:F1}% of {Environment.ProcessorCount} cores)");
    Console.WriteLine($"  GC Gen0/1/2:   {GC.CollectionCount(0) - gen0Before} / {GC.CollectionCount(1) - gen1Before} / {GC.CollectionCount(2) - gen2Before}");

    // Benchmark 10-Page HTML
    const int htmlIterations = 20;
    gen0Before = GC.CollectionCount(0);
    gen1Before = GC.CollectionCount(1);
    gen2Before = GC.CollectionCount(2);
    cpuBefore = proc.TotalProcessorTime;
    sw.Restart();

    for (int i = 0; i < htmlIterations; i++)
    {
        _ = Doc2PdfConverter.Convert(htmlBytes, DocumentFormat.Html);
    }

    sw.Stop();
    proc.Refresh();
    cpuAfter = proc.TotalProcessorTime;
    double htmlElapsedSec = sw.Elapsed.TotalSeconds;
    double htmlCpuSec = (cpuAfter - cpuBefore).TotalSeconds;
    double htmlCpuUtil = (htmlCpuSec / htmlElapsedSec / Environment.ProcessorCount) * 100.0;
    double htmlAvgMs = sw.ElapsedMilliseconds / (double)htmlIterations;

    Console.WriteLine($"\n10-Page HTML Throughput ({htmlIterations} runs):");
    Console.WriteLine($"  Total Time:    {sw.ElapsedMilliseconds} ms ({htmlAvgMs:F2} ms/doc)");
    Console.WriteLine($"  Throughput:    {htmlIterations / htmlElapsedSec:F1} docs/sec");
    Console.WriteLine($"  CPU Time:      {htmlCpuSec:F2} s (Avg CPU: {htmlCpuUtil:F1}% of {Environment.ProcessorCount} cores)");
    Console.WriteLine($"  GC Gen0/1/2:   {GC.CollectionCount(0) - gen0Before} / {GC.CollectionCount(1) - gen1Before} / {GC.CollectionCount(2) - gen2Before}");

    Console.WriteLine("\n--- 3. Steady State & Peak Memory Footprint ---");
    proc.Refresh();
    Console.WriteLine($"  Peak Working Set:      {proc.PeakWorkingSet64 / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine($"  Current Working Set:   {proc.WorkingSet64 / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine($"  Private Memory:        {proc.PrivateMemorySize64 / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine($"  Managed GC Heap:       {GC.GetTotalMemory(false) / 1024.0 / 1024.0:F2} MB");

    GC.Collect(2, GCCollectionMode.Forced, true, true);
    proc.Refresh();
    Console.WriteLine($"  Post-GC Managed Heap:  {GC.GetTotalMemory(true) / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine("========================================");
}
