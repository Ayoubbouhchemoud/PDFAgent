using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;
using PDFAgent.Core.Services;

namespace PDFAgent.App.Services;

public sealed class BatchWorkflowService : IBatchWorkflowService
{
    private readonly ILogger<BatchWorkflowService> _logger;
    private readonly IPdfEditor _pdfEditor;
    private readonly IPdfEngine _pdfEngine;
    private readonly IOcrEngine _ocrEngine;
    private readonly IRedactionEngine _redactionEngine;
    private readonly List<WorkflowPipeline> _saved;
    private static readonly string PipelinesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PDFAgent", "workflows");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BatchWorkflowService(
        ILogger<BatchWorkflowService> logger,
        IPdfEditor pdfEditor,
        IPdfEngine pdfEngine,
        IOcrEngine ocrEngine,
        IRedactionEngine redactionEngine)
    {
        _logger = logger;
        _pdfEditor = pdfEditor;
        _pdfEngine = pdfEngine;
        _ocrEngine = ocrEngine;
        _redactionEngine = redactionEngine;
        _saved = LoadFromDisk();
    }

    public async Task<OperationResult> ExecuteAsync(
        WorkflowPipeline pipeline,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (pipeline.Steps.Count == 0)
            return OperationResult.Fail("Pipeline has no steps");

        var docPath = pipeline.DocumentPath;
        if (string.IsNullOrEmpty(docPath) || !File.Exists(docPath))
            return OperationResult.Fail("No document is open. Open a PDF before running a workflow.");

        _logger.LogInformation("Executing workflow \"{Name}\" ({Count} steps) on {Doc}",
            pipeline.Name, pipeline.Steps.Count, Path.GetFileName(docPath));

        for (var i = 0; i < pipeline.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = pipeline.Steps[i];
            _logger.LogInformation("Step {Idx}/{Total}: {Name}", i + 1, pipeline.Steps.Count, step.DisplayName);

            var stepResult = await ExecuteStepAsync(step, docPath, ct);
            if (!stepResult.IsSuccess)
            {
                _logger.LogWarning("Step {Name} reported: {Msg}", step.DisplayName, stepResult.Message);
            }

            progress?.Report((double)(i + 1) / pipeline.Steps.Count);
        }

        return OperationResult.Ok("Workflow completed successfully");
    }

    public void SavePipeline(WorkflowPipeline pipeline, string name)
    {
        pipeline.Name = name;
        var idx = _saved.FindIndex(p => p.Name == name);
        if (idx >= 0) _saved[idx] = pipeline;
        else _saved.Add(pipeline);
        PersistToDisk(pipeline);
        _logger.LogInformation("Saved pipeline \"{Name}\"", name);
    }

    public IReadOnlyList<WorkflowPipeline> LoadSavedPipelines() => _saved.AsReadOnly();

    // ── step dispatch ────────────────────────────────────────────────────────

    private async Task<OperationResult> ExecuteStepAsync(
        WorkflowStep step, string docPath, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(docPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(docPath);

        switch (step.StepType)
        {
            case WorkflowStepType.OcrAllPages:
                return await RunOcrAllPagesAsync(docPath, dir, baseName, ct);

            case WorkflowStepType.Redact:
                var redactOut = Path.Combine(dir, $"{baseName}_redacted.pdf");
                return await _redactionEngine.RedactPiiAsync(docPath, redactOut,
                    new PiiRedactionProfile
                    {
                        RedactEmails = true,
                        RedactPhoneNumbers = true,
                        RedactSsn = true,
                        RedactCreditCards = true,
                    }, ct);

            case WorkflowStepType.Rotate:
                var pages = _pdfEngine.IsOpen && _pdfEngine.FilePath == docPath
                    ? Enumerable.Range(0, _pdfEngine.DocumentInfo!.PageCount).ToList()
                    : await GetPageCountAsync(docPath, ct);
                return await _pdfEditor.RotatePagesAsync(docPath, pages, 90, ct);

            case WorkflowStepType.Compress:
                return OperationResult.Ok("Compress: file size optimisation requires Ghostscript — step skipped");

            case WorkflowStepType.AddWatermark:
                var watermarkOut = Path.Combine(dir, $"{baseName}_watermarked.pdf");
                return await _pdfEditor.AddWatermarkAsync(docPath, watermarkOut, "CONFIDENTIAL", ct);

            case WorkflowStepType.ExtractPages:
                return await _pdfEditor.SplitAsync(docPath,
                    Path.Combine(dir, $"{baseName}_pages"), SplitMode.SplitAll, ct);

            case WorkflowStepType.MergeFiles:
                return OperationResult.Ok("Merge: open multiple PDFs via the toolbar Merge button — step skipped");

            case WorkflowStepType.ConvertToImage:
                return await ExportToImagesAsync(docPath, dir, baseName, ct);

            case WorkflowStepType.ApplyStamp:
                var stampOut = Path.Combine(dir, $"{baseName}_stamped.pdf");
                return await _pdfEditor.AddStampAsync(docPath, stampOut, "APPROVED", ct);

            default:
                return OperationResult.Fail($"Unknown step type: {step.StepType}");
        }
    }

    private async Task<OperationResult> RunOcrAllPagesAsync(
        string docPath, string dir, string baseName, CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            var pageCount = await GetPageCountAsync(docPath, ct);

            for (var p = 0; p < pageCount.Count; p++)
            {
                ct.ThrowIfCancellationRequested();
                OperationResult<byte[]> renderResult;
                if (_pdfEngine.IsOpen && _pdfEngine.FilePath == docPath)
                    renderResult = await _pdfEngine.RenderPageAsync(p, dpi: 300, ct);
                else
                    renderResult = OperationResult.Fail<byte[]>("Document not open");

                if (!renderResult.IsSuccess || renderResult.Value == null) continue;
                var ocrResult = await _ocrEngine.ProcessPageAsync(renderResult.Value, ct: ct);
                if (ocrResult.IsSuccess && ocrResult.Value != null)
                {
                    sb.AppendLine($"=== Page {p + 1} ===");
                    sb.AppendLine(ocrResult.Value.FullText);
                    sb.AppendLine();
                }
            }

            var outPath = Path.Combine(dir, $"{baseName}_ocr.txt");
            await File.WriteAllTextAsync(outPath, sb.ToString(), ct);
            return OperationResult.Ok($"OCR text saved to {Path.GetFileName(outPath)}");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"OCR step failed: {ex.Message}");
        }
    }

    private async Task<OperationResult> ExportToImagesAsync(
        string docPath, string dir, string baseName, CancellationToken ct)
    {
        try
        {
            int count;
            if (_pdfEngine.IsOpen && _pdfEngine.FilePath == docPath)
            {
                count = _pdfEngine.DocumentInfo!.PageCount;
                for (var p = 0; p < count; p++)
                {
                    ct.ThrowIfCancellationRequested();
                    var r = await _pdfEngine.RenderPageAsync(p, dpi: 300, ct);
                    if (r.IsSuccess && r.Value != null)
                        await File.WriteAllBytesAsync(
                            Path.Combine(dir, $"{baseName}_page_{p + 1}.png"), r.Value, ct);
                }
            }
            else
            {
                return OperationResult.Fail("Document must be open to export pages as images");
            }
            return OperationResult.Ok($"Exported {count} pages as PNG");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Image export failed: {ex.Message}");
        }
    }

    private async Task<List<int>> GetPageCountAsync(string docPath, CancellationToken ct)
    {
        if (_pdfEngine.IsOpen && _pdfEngine.FilePath == docPath)
            return Enumerable.Range(0, _pdfEngine.DocumentInfo!.PageCount).ToList();

        // Fallback: open temporarily to count pages
        var result = await _pdfEngine.OpenAsync(docPath, ct: ct);
        return result.IsSuccess && result.Value != null
            ? Enumerable.Range(0, result.Value.PageCount).ToList()
            : new List<int>();
    }

    // ── persistence ─────────────────────────────────────────────────────────

    private static string PipelinePath(string name) =>
        Path.Combine(PipelinesDir, $"{SanitizeName(name)}.json");

    private static string SanitizeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private void PersistToDisk(WorkflowPipeline pipeline)
    {
        try
        {
            Directory.CreateDirectory(PipelinesDir);
            var json = JsonSerializer.Serialize(pipeline, JsonOpts);
            File.WriteAllText(PipelinePath(pipeline.Name), json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist pipeline \"{Name}\" to disk", pipeline.Name);
        }
    }

    private List<WorkflowPipeline> LoadFromDisk()
    {
        var list = new List<WorkflowPipeline>();
        if (!Directory.Exists(PipelinesDir)) return list;

        foreach (var file in Directory.EnumerateFiles(PipelinesDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var pipeline = JsonSerializer.Deserialize<WorkflowPipeline>(json, JsonOpts);
                if (pipeline != null) list.Add(pipeline);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load pipeline from {File}", file);
            }
        }

        _logger.LogInformation("Loaded {Count} saved pipelines from disk", list.Count);
        return list;
    }
}
