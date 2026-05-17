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

        string? lastRealError = null;

        foreach (var exe in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = exe,
                    Arguments              = $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"",
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                };

                using var proc = Process.Start(psi);
                if (proc is null) continue;

                // Read both streams before WaitForExit to avoid pipe-buffer deadlocks.
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                bool exited = proc.WaitForExit(60_000);
                ct.ThrowIfCancellationRequested();

                string stdout = stdoutTask.Result.Trim();
                string stderr = stderrTask.Result.Trim();
                string combined = (stdout + "\n" + stderr).Trim();

                if (!exited)
                {
                    proc.Kill();
                    continue;
                }

                if (proc.ExitCode == 0 && File.Exists(outputPath))
                {
                    _logger.LogInformation("HTML export complete: {Path}", outputPath);
                    return OperationResult.Ok($"Exported HTML → {Path.GetFileName(outputPath)}");
                }

                // Windows Store Python alias exits non-zero and outputs a "not found" hint.
                // Treat it as "interpreter not installed" and try the next candidate.
                if (IsStoreAlias(combined))
                {
                    _logger.LogDebug("Interpreter '{Exe}' is a Windows Store alias — skipping.", exe);
                    continue;
                }

                // Real Python error (bad script, missing pdfplumber, etc.) — stop here.
                _logger.LogError("Python script failed (exit {Code}): {Err}", proc.ExitCode, combined);
                lastRealError = string.IsNullOrEmpty(combined)
                    ? $"Python exited with code {proc.ExitCode}."
                    : combined;
                return OperationResult.Fail(lastRealError);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Interpreter not on PATH — try the next candidate.
                _logger.LogDebug("Interpreter '{Exe}' not found: {Msg}", exe, ex.Message);
            }
        }

        // Last resort on Windows: run python3 inside WSL (if installed).
        if (OperatingSystem.IsWindows())
        {
            var wslResult = TryWsl(scriptPath, inputPath, outputPath, ct);
            if (wslResult is not null) return wslResult;
        }

        return OperationResult.Fail(
            "Python 3 is not installed or not on PATH.\n" +
            "Install Python 3 from python.org and run: pip install pdfplumber\n" +
            "Alternatively, install WSL and run: pip install pdfplumber inside it.");
    }

    private OperationResult? TryWsl(
        string scriptPath, string inputPath, string outputPath, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "wsl",
                Arguments              = $"python3 \"{ToWslPath(scriptPath)}\" \"{ToWslPath(inputPath)}\" \"{ToWslPath(outputPath)}\"",
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            bool exited = proc.WaitForExit(60_000);
            ct.ThrowIfCancellationRequested();

            string stdout  = stdoutTask.Result.Trim();
            string stderr  = stderrTask.Result.Trim();
            string combined = (stdout + "\n" + stderr).Trim();

            if (!exited) { proc.Kill(); return null; }

            if (proc.ExitCode == 0 && File.Exists(outputPath))
            {
                _logger.LogInformation("HTML export via WSL complete: {Path}", outputPath);
                return OperationResult.Ok($"Exported HTML → {Path.GetFileName(outputPath)}");
            }

            if (!string.IsNullOrEmpty(combined))
            {
                _logger.LogError("WSL python3 failed (exit {Code}): {Err}", proc.ExitCode, combined);
                return OperationResult.Fail(combined);
            }

            return null; // WSL not available or python3 not in WSL
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug("WSL not available: {Msg}", ex.Message);
            return null;
        }
    }

    private static string ToWslPath(string windowsPath)
    {
        if (windowsPath.Length >= 2 && windowsPath[1] == ':')
        {
            char drive = char.ToLower(windowsPath[0]);
            string rest = windowsPath[2..].Replace('\\', '/');
            return $"/mnt/{drive}{rest}";
        }
        return windowsPath.Replace('\\', '/');
    }

    private static bool IsStoreAlias(string output) =>
        output.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("nicht gefunden", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("was not found",  StringComparison.OrdinalIgnoreCase) ||
        output.Contains("App Installer",  StringComparison.OrdinalIgnoreCase);
}
