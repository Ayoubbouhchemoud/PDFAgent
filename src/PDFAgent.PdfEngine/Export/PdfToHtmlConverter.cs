using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;

namespace PDFAgent.PdfEngine.Export;

/// <summary>
/// Converts PDF to semantic HTML5 by invoking the bundled pdf_to_html.py script.
///
/// Output guarantees:
///   - Text is real selectable HTML text, never a rasterized page image.
///   - Headings are &lt;h1&gt;/&lt;h2&gt;/&lt;h3&gt; based on font-size ratio to body text.
///   - Paragraphs are &lt;p&gt; elements; wrapped lines are merged correctly.
///   - Tables with ruled lines become &lt;table&gt;&lt;thead&gt;&lt;tbody&gt; markup.
///   - Bullet/numbered lists become &lt;ul&gt;&lt;li&gt; / &lt;ol&gt;&lt;li&gt;.
///   - Embedded images remain &lt;img&gt; elements (not inline base64 page screenshots).
///   - The entire document is wrapped in a single &lt;article&gt; element.
///   - No &lt;div class="page"&gt; wrappers, no scanned-image fallbacks.
///
/// Requires Python 3 with pdfplumber installed on PATH.
/// Probes "py" (Windows Launcher), "python", and "python3" in order.
/// Returns a descriptive failure result if Python is unavailable.
/// </summary>
public sealed class PdfToHtmlConverter : IPdfExporter
{
    private readonly ILogger<PdfToHtmlConverter> _logger;

    public PdfToHtmlConverter(ILogger<PdfToHtmlConverter> logger) => _logger = logger;

    public Task<OperationResult> ExportAsync(
        string inputPath,
        string outputPath,
        ExportFormat format,
        CancellationToken ct = default)
    {
        if (format != ExportFormat.Html)
            return Task.FromResult(
                OperationResult.Fail($"Format '{format}' is not yet implemented."));

        return Task.Run(() => RunPython(inputPath, outputPath, ct), ct);
    }

    private OperationResult RunPython(string inputPath, string outputPath, CancellationToken ct)
    {
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "pdf_to_html.py");
        if (!File.Exists(scriptPath))
            return OperationResult.Fail(
                "pdf_to_html.py not found next to the application. Re-install or rebuild the app.");

        // Windows ships "py" (Python Launcher); Linux/macOS use "python3" / "python".
        string[] candidates = OperatingSystem.IsWindows()
            ? ["py", "python", "python3"]
            : ["python3", "python"];

        foreach (var exe in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName              = exe,
                    Arguments             = $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"",
                    CreateNoWindow        = true,
                    UseShellExecute       = false,
                    RedirectStandardError = true,
                };

                using var proc = Process.Start(psi);
                if (proc is null) continue;

                bool exited = proc.WaitForExit(60_000); // 60-second hard timeout
                ct.ThrowIfCancellationRequested();

                if (!exited)
                {
                    proc.Kill();
                    continue; // try next interpreter
                }

                if (proc.ExitCode == 0 && File.Exists(outputPath))
                {
                    _logger.LogInformation("HTML export complete: {Path}", outputPath);
                    return OperationResult.Ok($"Exported HTML → {Path.GetFileName(outputPath)}");
                }

                string stderr = proc.StandardError.ReadToEnd().Trim();
                _logger.LogError("Python script failed (exit {Code}): {Err}", proc.ExitCode, stderr);
                return OperationResult.Fail(
                    string.IsNullOrEmpty(stderr)
                        ? $"Python exited with code {proc.ExitCode}."
                        : stderr);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // This interpreter is not on PATH — try the next candidate.
                _logger.LogDebug("Interpreter '{Exe}' not found: {Msg}", exe, ex.Message);
            }
        }

        return OperationResult.Fail(
            "Python 3 is not installed or not on PATH.\n" +
            "Install Python 3 (python.org) and run: pip install pdfplumber");
    }
}
