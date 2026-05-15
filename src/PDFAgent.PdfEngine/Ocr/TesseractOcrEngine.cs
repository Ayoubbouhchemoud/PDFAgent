using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;
using Tesseract;

namespace PDFAgent.PdfEngine.Ocr;

public sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly ILogger<TesseractOcrEngine> _logger;
    private readonly string? _dataPath;
    private bool _initialized;
    private readonly HashSet<string> _supportedLanguages = new() { "eng" };

    public bool IsAvailable => _initialized;
    public IReadOnlyList<string> SupportedLanguages => _supportedLanguages.ToList();

    public TesseractOcrEngine(ILogger<TesseractOcrEngine> logger, string? dataPath = null)
    {
        _logger = logger;
        _dataPath = dataPath
            ?? Environment.GetEnvironmentVariable("TESSDATA_PREFIX")
            ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            _supportedLanguages.Clear();
            var tessdataDir = _dataPath;

            if (tessdataDir == null || !Directory.Exists(tessdataDir))
            {
                _logger.LogWarning(
                    "Tesseract tessdata directory not found at '{Path}'. OCR unavailable. " +
                    "Download eng.traineddata from https://github.com/tesseract-ocr/tessdata " +
                    "and place it in that directory.",
                    tessdataDir);
                _initialized = false;
                return;
            }

            var files = Directory.GetFiles(tessdataDir, "*.traineddata");
            if (files.Length == 0)
            {
                _logger.LogWarning(
                    "Tesseract tessdata directory '{Path}' exists but contains no .traineddata files. " +
                    "Download eng.traineddata from https://github.com/tesseract-ocr/tessdata.",
                    tessdataDir);
                _initialized = false;
                return;
            }

            foreach (var file in files)
            {
                var lang = Path.GetFileNameWithoutExtension(file);
                if (lang != "osd") _supportedLanguages.Add(lang);
            }

            _initialized = true;
            _logger.LogInformation("Tesseract initialized with {Count} languages: {Langs}",
                _supportedLanguages.Count, string.Join(", ", _supportedLanguages));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tesseract not available — OCR will be unavailable");
            _initialized = false;
        }
    }

    public async Task<OperationResult<OcrResult>> ProcessPageAsync(
        byte[] imageData, string language = "eng", CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!_initialized)
                    return OperationResult.Fail<OcrResult>("OCR engine not initialized");

                using var engine = new TesseractEngine(_dataPath, language, EngineMode.Default);
                using var pix = Pix.LoadFromMemory(imageData);
                using var page = engine.Process(pix);

                var text = page.GetText();
                var confidence = page.GetMeanConfidence();
                var words = new List<OcrWord>();
                var paragraphs = new List<OcrParagraph>();

                using var iter = page.GetIterator();
                iter.Begin();

                do
                {
                    if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                    {
                        words.Add(new OcrWord
                        {
                            Text = iter.GetText(PageIteratorLevel.Word)?.Trim() ?? "",
                            Confidence = iter.GetConfidence(PageIteratorLevel.Word),
                            Bounds = new BoundingRect(bounds.X1, bounds.Y1,
                                bounds.X2 - bounds.X1, bounds.Y2 - bounds.Y1),
                        });
                    }
                } while (iter.Next(PageIteratorLevel.Word));

                paragraphs.Add(new OcrParagraph
                {
                    Text = text?.Trim() ?? "",
                    Confidence = confidence,
                    Words = words,
                });

                _logger.LogDebug("OCR processed page: {WordCount} words, {Confidence:P} confidence",
                    words.Count, confidence / 100.0);

                return OperationResult.Ok(new OcrResult
                {
                    FullText = text?.Trim() ?? "",
                    Confidence = confidence,
                    Words = words,
                    Paragraphs = paragraphs,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR processing failed");
                return OperationResult.Fail<OcrResult>($"OCR failed: {ex.Message}");
            }
        }, ct);
    }

    public async Task<OperationResult<IReadOnlyList<OcrResult>>> ProcessBatchAsync(
        IReadOnlyList<byte[]> images, string language = "eng",
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var results = new List<OcrResult>();
        for (var i = 0; i < images.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((double)i / images.Count);

            var result = await ProcessPageAsync(images[i], language, ct);
            if (result.IsSuccess && result.Value != null)
                results.Add(result.Value);
        }

        progress?.Report(1.0);
        return OperationResult.Ok<IReadOnlyList<OcrResult>>(results);
    }
}
