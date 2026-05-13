using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PDFAgent.App.Services;
using PDFAgent.App.ViewModels;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;
using Xunit;

namespace PDFAgent.App.Tests.ViewModels;

public sealed class MainViewModelTests
{
    private static MainViewModel CreateVm(
        IPdfEngine? engine = null,
        IPdfEditor? editor = null,
        IFileDialogService? dialog = null)
    {
        engine ??= Substitute.For<IPdfEngine>();
        editor ??= Substitute.For<IPdfEditor>();
        dialog ??= Substitute.For<IFileDialogService>();

        return new MainViewModel(
            NullLogger<MainViewModel>.Instance,
            engine,
            editor,
            Substitute.For<IOcrEngine>(),
            Substitute.For<IRedactionEngine>(),
            dialog);
    }

    private static (MainViewModel vm, IPdfEngine engine, IPdfEditor editor, IFileDialogService dialog)
        CreateDocumentLoadedVm(string filePath = "C:\\test.pdf")
    {
        var engine = Substitute.For<IPdfEngine>();
        var editor = Substitute.For<IPdfEditor>();
        var dialog = Substitute.For<IFileDialogService>();

        var info = new PdfDocumentInfo
        {
            FilePath = filePath,
            PageCount = 3,
            FileSizeBytes = 1024,
        };
        engine.IsOpen.Returns(true);
        engine.FilePath.Returns(filePath);
        engine.DocumentInfo.Returns(info);

        var vm = new MainViewModel(
            NullLogger<MainViewModel>.Instance,
            engine, editor,
            Substitute.For<IOcrEngine>(),
            Substitute.For<IRedactionEngine>(),
            dialog);

        // Simulate document already loaded
        vm.IsDocumentLoaded = true;
        vm.DocumentInfo = info;
        vm.TotalPages = 3;
        vm.CurrentPage = 2;

        return (vm, engine, editor, dialog);
    }

    // ── CanExecute guards ────────────────────────────────────────────────────

    [Fact]
    public void SignCommand_WhenNoDocument_IsDisabled()
    {
        var vm = CreateVm();
        vm.SignCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AnnotateCommand_WhenNoDocument_IsDisabled()
    {
        var vm = CreateVm();
        vm.AnnotateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ExportToImageCommand_WhenNoDocument_IsDisabled()
    {
        var vm = CreateVm();
        vm.ExportToImageCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void PrintCommand_WhenNoDocument_IsDisabled()
    {
        var vm = CreateVm();
        vm.PrintCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void PropertiesCommand_WhenNoDocument_IsDisabled()
    {
        var vm = CreateVm();
        vm.PropertiesCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SignCommand_WhenDocumentLoaded_IsEnabled()
    {
        var (vm, _, _, _) = CreateDocumentLoadedVm();
        vm.SignCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ExportToImageCommand_WhenDocumentLoaded_IsEnabled()
    {
        var (vm, _, _, _) = CreateDocumentLoadedVm();
        vm.ExportToImageCommand.CanExecute(null).Should().BeTrue();
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_CopiesSourceFileToChosenDestination()
    {
        // Proves: File.Copy actually runs and produces a real output file.
        // Save uses File.Copy (read-only on source) so it works even when
        // PdfiumViewer holds the file open.
        var src = Path.GetTempFileName();
        var dst = Path.GetTempFileName();
        await File.WriteAllBytesAsync(src, new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF

        try
        {
            var engine = Substitute.For<IPdfEngine>();
            var dialog = Substitute.For<IFileDialogService>();
            var info = new PdfDocumentInfo { FilePath = src, PageCount = 1 };
            engine.FilePath.Returns(src);
            engine.DocumentInfo.Returns(info);
            dialog.SavePdf(Arg.Any<string>()).Returns(dst);

            var vm = new MainViewModel(
                NullLogger<MainViewModel>.Instance,
                engine, Substitute.For<IPdfEditor>(),
                Substitute.For<IOcrEngine>(), Substitute.For<IRedactionEngine>(), dialog);
            vm.IsDocumentLoaded = true;
            vm.DocumentInfo = info;

            await vm.SaveFileCommand.ExecuteAsync(null);

            File.Exists(dst).Should().BeTrue("destination file must exist after save");
            File.ReadAllBytes(dst).Should().BeEquivalentTo(File.ReadAllBytes(src));
            vm.StatusText.Should().StartWith("Saved to");
            vm.IsBusy.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }

    [Fact]
    public async Task Save_WhenDialogCancelled_DoesNotCopyFile()
    {
        var (vm, _, _, dialog) = CreateDocumentLoadedVm();
        dialog.SavePdf(Arg.Any<string>()).Returns((string?)null);

        await vm.SaveFileCommand.ExecuteAsync(null);

        // Status should be unchanged (still "Ready" from initial value)
        vm.StatusText.Should().Be("Ready");
    }

    // ── Merge ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_CallsEditorWithAllSelectedFiles()
    {
        var (vm, _, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        var inputFiles = new[] { "C:\\a.pdf", "C:\\b.pdf" };
        dialog.OpenMultiplePdfs().Returns(inputFiles);
        dialog.SavePdf(Arg.Any<string>()).Returns("C:\\merged.pdf");
        editor.MergeAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult.Ok("Merged 2 files"));

        await vm.MergeCommand.ExecuteAsync(null);

        await editor.Received(1).MergeAsync(
            Arg.Is<IReadOnlyList<string>>(l => l.SequenceEqual(inputFiles)),
            "C:\\merged.pdf",
            Arg.Any<CancellationToken>());
        vm.StatusText.Should().Contain("complete");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Merge_WhenOneFileSelected_SetsRequiresMoreFilesStatus()
    {
        var (vm, _, editor, dialog) = CreateDocumentLoadedVm();
        dialog.OpenMultiplePdfs().Returns(new[] { "C:\\only_one.pdf" });

        await vm.MergeCommand.ExecuteAsync(null);

        await editor.DidNotReceive().MergeAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        vm.StatusText.Should().Contain("at least 2");
    }

    [Fact]
    public async Task Merge_WhenNoFilesSelected_SetsCancelledStatus()
    {
        var (vm, _, editor, dialog) = CreateDocumentLoadedVm();
        dialog.OpenMultiplePdfs().Returns(Array.Empty<string>());

        await vm.MergeCommand.ExecuteAsync(null);

        await editor.DidNotReceive().MergeAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        vm.StatusText.Should().Contain("cancelled");
    }

    // ── Split ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Split_CallsEditorWithDocumentPathAndChosenFolder()
    {
        var (vm, engine, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        dialog.SelectFolder().Returns("C:\\out");
        editor.SplitAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SplitMode>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult.Ok("Split into 3 pages"));

        await vm.SplitCommand.ExecuteAsync(null);

        await editor.Received(1).SplitAsync(
            "C:\\doc.pdf", "C:\\out", SplitMode.SplitAll, Arg.Any<CancellationToken>());
        vm.StatusText.Should().Contain("complete");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Split_WhenFolderDialogCancelled_DoesNotCallEditor()
    {
        var (vm, _, editor, dialog) = CreateDocumentLoadedVm();
        dialog.SelectFolder().Returns((string?)null);

        await vm.SplitCommand.ExecuteAsync(null);

        await editor.DidNotReceive().SplitAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SplitMode>(), Arg.Any<CancellationToken>());
    }

    // ── Redact ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Redact_CallsRedactPiiWithAllFourProfileFlagsEnabled()
    {
        var (vm, engine, _, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        var redaction = Substitute.For<IRedactionEngine>();
        redaction.RedactPiiAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<PiiRedactionProfile>(), Arg.Any<CancellationToken>())
                 .Returns(OperationResult.Ok("Redacted 5 region(s)"));

        dialog.SavePdf(Arg.Any<string>()).Returns("C:\\doc_redacted.pdf");

        // Inject a VM with our specific redaction mock
        var vm2 = new MainViewModel(
            NullLogger<MainViewModel>.Instance,
            engine, Substitute.For<IPdfEditor>(),
            Substitute.For<IOcrEngine>(), redaction, dialog);
        vm2.IsDocumentLoaded = true;
        vm2.DocumentInfo = engine.DocumentInfo;

        await vm2.RedactCommand.ExecuteAsync(null);

        await redaction.Received(1).RedactPiiAsync(
            "C:\\doc.pdf",
            "C:\\doc_redacted.pdf",
            Arg.Is<PiiRedactionProfile>(p =>
                p.RedactEmails &&
                p.RedactPhoneNumbers &&
                p.RedactSsn &&
                p.RedactCreditCards),
            Arg.Any<CancellationToken>());
        vm2.StatusText.Should().Contain("complete");
        vm2.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Redact_WhenDialogCancelled_DoesNotCallEngine()
    {
        var (vm, engine, _, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        var redaction = Substitute.For<IRedactionEngine>();
        dialog.SavePdf(Arg.Any<string>()).Returns((string?)null);

        var vm2 = new MainViewModel(
            NullLogger<MainViewModel>.Instance,
            engine, Substitute.For<IPdfEditor>(),
            Substitute.For<IOcrEngine>(), redaction, dialog);
        vm2.IsDocumentLoaded = true;
        vm2.DocumentInfo = engine.DocumentInfo;

        await vm2.RedactCommand.ExecuteAsync(null);

        await redaction.DidNotReceive().RedactPiiAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<PiiRedactionProfile>(), Arg.Any<CancellationToken>());
    }

    // ── OCR ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ocr_WhenEngineUnavailable_SetsStatusAndDoesNotCallRender()
    {
        // ROOT CAUSE FIX: TesseractOcrEngine was logging "initialized" even when
        // tessdata was missing, then crashing in ProcessPageAsync (log 2026-05-13 18:17:27).
        // Now IsAvailable = false when tessdata is absent; OcrAsync bails out early.
        var engine = Substitute.For<IPdfEngine>();
        var ocrEngine = Substitute.For<IOcrEngine>();
        ocrEngine.IsAvailable.Returns(false);

        var info = new PdfDocumentInfo { FilePath = "C:\\doc.pdf", PageCount = 2 };
        engine.IsOpen.Returns(true);
        engine.FilePath.Returns("C:\\doc.pdf");
        engine.DocumentInfo.Returns(info);

        var vm = new MainViewModel(
            NullLogger<MainViewModel>.Instance,
            engine, Substitute.For<IPdfEditor>(),
            ocrEngine, Substitute.For<IRedactionEngine>(),
            Substitute.For<IFileDialogService>());

        vm.IsDocumentLoaded = true;
        vm.DocumentInfo = info;
        vm.TotalPages = 2;

        await vm.OcrCommand.ExecuteAsync(null);

        vm.StatusText.Should().Contain("unavailable");
        await engine.DidNotReceive().RenderPageAsync(
            Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
        vm.IsBusy.Should().BeFalse();
    }

    // ── Rotate ───────────────────────────────────────────────────────────────

    private static RotateDialogResult AllPages90() =>
        new(RotatePageSelection.All, string.Empty, 90);

    private static void SetupRotateEngineForReopen(
        IPdfEngine engine, IPdfEditor editor, string filePath = "C:\\doc.pdf")
    {
        engine.OpenAsync(filePath, Arg.Any<string?>(), Arg.Any<CancellationToken>())
              .Returns(ci =>
              {
                  var info = new PdfDocumentInfo { FilePath = ci.Arg<string>(), PageCount = 3 };
                  return Task.FromResult(OperationResult.Ok(info));
              });
        engine.RenderPageAsync(Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult.Ok(Array.Empty<byte>()));
        engine.RenderThumbnailAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult.Ok(Array.Empty<byte>()));
        editor.RotatePagesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(OperationResult.Ok("Rotated pages")));
    }

    [Fact]
    public async Task Rotate_WhenDialogCancelled_DoesNotCloseEngineOrRotate()
    {
        var (vm, engine, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        dialog.ShowRotateDialog(Arg.Any<int>(), Arg.Any<int>()).Returns((RotateDialogResult?)null);

        await vm.RotateCommand.ExecuteAsync(null);

        await engine.DidNotReceive().CloseAsync();
        await editor.DidNotReceive().RotatePagesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Rotate_AllPages_PassesAllPageIndicesToEditor()
    {
        var (vm, engine, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        dialog.ShowRotateDialog(Arg.Any<int>(), Arg.Any<int>()).Returns(AllPages90());

        IReadOnlyList<int>? capturedPages = null;
        engine.When(e => e.CloseAsync()).Do(_ => { });
        SetupRotateEngineForReopen(engine, editor);
        editor.RotatePagesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(ci =>
              {
                  capturedPages = ci.Arg<IReadOnlyList<int>>();
                  return Task.FromResult(OperationResult.Ok("Rotated 3 pages by 90°"));
              });

        await vm.RotateCommand.ExecuteAsync(null);

        capturedPages.Should().NotBeNull();
        capturedPages!.Should().BeEquivalentTo(new[] { 0, 1, 2 },
            "TotalPages=3, so all-pages is [0,1,2]");
        vm.StatusText.Should().Contain("Rotation complete");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Rotate_CurrentPageOnly_PassesOnlyCurrentPageIndex()
    {
        var (vm, engine, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        // CurrentPage is 2 (1-based), so 0-based index = 1
        dialog.ShowRotateDialog(Arg.Any<int>(), Arg.Any<int>())
              .Returns(new RotateDialogResult(RotatePageSelection.CurrentPage, string.Empty, 90));

        IReadOnlyList<int>? capturedPages = null;
        engine.When(e => e.CloseAsync()).Do(_ => { });
        SetupRotateEngineForReopen(engine, editor);
        editor.RotatePagesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(ci =>
              {
                  capturedPages = ci.Arg<IReadOnlyList<int>>();
                  return Task.FromResult(OperationResult.Ok("Rotated 1 page"));
              });

        await vm.RotateCommand.ExecuteAsync(null);

        capturedPages.Should().BeEquivalentTo(new[] { 1 },
            "CurrentPage=2 → 0-based index 1");
    }

    [Fact]
    public async Task Rotate_PageRange_ParsesRangeAndPassesCorrectIndices()
    {
        var (vm, engine, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        dialog.ShowRotateDialog(Arg.Any<int>(), Arg.Any<int>())
              .Returns(new RotateDialogResult(RotatePageSelection.Range, "1,3", 180));

        IReadOnlyList<int>? capturedPages = null;
        int capturedDegrees = 0;
        engine.When(e => e.CloseAsync()).Do(_ => { });
        SetupRotateEngineForReopen(engine, editor);
        editor.RotatePagesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(ci =>
              {
                  capturedPages = ci.Arg<IReadOnlyList<int>>();
                  capturedDegrees = ci.ArgAt<int>(2);
                  return Task.FromResult(OperationResult.Ok("Rotated 2 pages by 180°"));
              });

        await vm.RotateCommand.ExecuteAsync(null);

        capturedPages.Should().BeEquivalentTo(new[] { 0, 2 },
            "range '1,3' → 0-based [0, 2]");
        capturedDegrees.Should().Be(180);
    }

    [Fact]
    public async Task Rotate_EmptyPageRange_AbortsWith_NoValidPages_Status()
    {
        var (vm, engine, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        dialog.ShowRotateDialog(Arg.Any<int>(), Arg.Any<int>())
              .Returns(new RotateDialogResult(RotatePageSelection.Range, "abc", 90));

        await vm.RotateCommand.ExecuteAsync(null);

        vm.StatusText.Should().Contain("no valid pages");
        await editor.DidNotReceive().RotatePagesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Rotate_ClosesEngineBefore_RotatePagesAsync()
    {
        // ROOT CAUSE FIX: PdfiumViewer holds a file lock. Engine must be closed
        // before PdfSharp can overwrite the file, otherwise File.Move throws
        // UnauthorizedAccessException (confirmed in log 2026-05-13 17:51:32).
        var callOrder = new List<string>();

        var (vm, engine, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        dialog.ShowRotateDialog(Arg.Any<int>(), Arg.Any<int>()).Returns(AllPages90());

        engine.When(e => e.CloseAsync())
              .Do(_ => callOrder.Add("close"));
        engine.OpenAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
              .Returns(ci =>
              {
                  callOrder.Add("open");
                  var info = new PdfDocumentInfo { FilePath = ci.Arg<string>(), PageCount = 3 };
                  return Task.FromResult(OperationResult.Ok(info));
              });
        editor.RotatePagesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(ci =>
              {
                  callOrder.Add("rotate");
                  return Task.FromResult(OperationResult.Ok("Rotated 3 pages by 90°"));
              });
        engine.RenderPageAsync(Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult.Ok(Array.Empty<byte>()));
        engine.RenderThumbnailAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult.Ok(Array.Empty<byte>()));

        await vm.RotateCommand.ExecuteAsync(null);

        // "close" must come before "rotate"
        callOrder.Should().ContainInOrder("close", "rotate");
        vm.StatusText.Should().Contain("Rotation complete");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Rotate_ReopensFileEvenWhenRotationFails()
    {
        var (vm, engine, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        dialog.ShowRotateDialog(Arg.Any<int>(), Arg.Any<int>()).Returns(AllPages90());
        var reopened = false;

        engine.OpenAsync("C:\\doc.pdf", Arg.Any<string?>(), Arg.Any<CancellationToken>())
              .Returns(_ =>
              {
                  reopened = true;
                  var info = new PdfDocumentInfo { FilePath = "C:\\doc.pdf", PageCount = 3 };
                  return Task.FromResult(OperationResult.Ok(info));
              });
        editor.RotatePagesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(OperationResult.Fail("Access denied")));
        engine.RenderPageAsync(Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult.Ok(Array.Empty<byte>()));
        engine.RenderThumbnailAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult.Ok(Array.Empty<byte>()));

        await vm.RotateCommand.ExecuteAsync(null);

        reopened.Should().BeTrue("file must always be reopened after rotate attempt");
        vm.StatusText.Should().Contain("failed");
        vm.IsBusy.Should().BeFalse();
    }

    // ── Sign ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sign_CallsAddStampAndSetsSuccessStatus()
    {
        var (vm, engine, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        dialog.SavePdf(Arg.Any<string>()).Returns("C:\\doc_signed.pdf");
        editor.AddStampAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok("Done"));

        await vm.SignCommand.ExecuteAsync(null);

        await editor.Received(1).AddStampAsync(
            "C:\\doc.pdf",
            "C:\\doc_signed.pdf",
            Arg.Is<string>(s => s.StartsWith("SIGNED")),
            Arg.Any<CancellationToken>());

        vm.StatusText.Should().Contain("Signed");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Sign_WhenEditorFails_SetsFailedStatus()
    {
        var (vm, _, editor, dialog) = CreateDocumentLoadedVm();
        dialog.SavePdf(Arg.Any<string>()).Returns("C:\\out.pdf");
        editor.AddStampAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("PdfSharp error"));

        await vm.SignCommand.ExecuteAsync(null);

        vm.StatusText.Should().Contain("failed");
    }

    [Fact]
    public async Task Sign_WhenUserCancelsDialog_DoesNotCallEditor()
    {
        var (vm, _, editor, dialog) = CreateDocumentLoadedVm();
        dialog.SavePdf(Arg.Any<string>()).Returns((string?)null);

        await vm.SignCommand.ExecuteAsync(null);

        await editor.DidNotReceive().AddStampAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Annotate ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Annotate_CallsAddPageAnnotationOnCurrentPage()
    {
        var (vm, _, editor, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        dialog.SavePdf(Arg.Any<string>()).Returns("C:\\doc_annotated.pdf");
        editor.AddPageAnnotationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok("Annotation added to page 2"));

        await vm.AnnotateCommand.ExecuteAsync(null);

        await editor.Received(1).AddPageAnnotationAsync(
            "C:\\doc.pdf",
            "C:\\doc_annotated.pdf",
            2,   // CurrentPage set in CreateDocumentLoadedVm
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        vm.StatusText.Should().Contain("page 2");
    }

    [Fact]
    public async Task Annotate_WhenUserCancelsDialog_DoesNotCallEditor()
    {
        var (vm, _, editor, dialog) = CreateDocumentLoadedVm();
        dialog.SavePdf(Arg.Any<string>()).Returns((string?)null);

        await vm.AnnotateCommand.ExecuteAsync(null);

        await editor.DidNotReceive().AddPageAnnotationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── ExportToImage ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportToImage_RendersCurrentPageAndSavesFile()
    {
        var (vm, engine, _, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        var outPath = Path.Combine(Path.GetTempPath(), $"test_export_{Guid.NewGuid()}.png");
        dialog.SaveImageFile(Arg.Any<string>()).Returns(outPath);
        var imageBytes = new byte[] { 1, 2, 3 };
        engine.RenderPageAsync(Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult.Ok(imageBytes));

        try
        {
            await vm.ExportToImageCommand.ExecuteAsync(null);

            // CurrentPage=2, so zero-indexed page 1
            await engine.Received(1).RenderPageAsync(1, Arg.Any<double>(), Arg.Any<CancellationToken>());
            vm.StatusText.Should().Contain("page 2");
            vm.IsBusy.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public async Task ExportToImage_WhenRenderFails_SetsErrorStatus()
    {
        var (vm, engine, _, dialog) = CreateDocumentLoadedVm();
        dialog.SaveImageFile(Arg.Any<string>()).Returns("C:\\out.png");
        engine.RenderPageAsync(Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult.Fail<byte[]>("Render error"));

        await vm.ExportToImageCommand.ExecuteAsync(null);

        vm.StatusText.Should().Contain("failed");
    }

    [Fact]
    public async Task ExportToImage_WhenUserCancelsDialog_DoesNotRender()
    {
        var (vm, engine, _, dialog) = CreateDocumentLoadedVm();
        dialog.SaveImageFile(Arg.Any<string>()).Returns((string?)null);

        await vm.ExportToImageCommand.ExecuteAsync(null);

        await engine.DidNotReceive().RenderPageAsync(
            Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }

    // ── Print ────────────────────────────────────────────────────────────────

    [Fact]
    public void Print_CallsPrintFileWithDocumentPath()
    {
        var (vm, engine, _, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");

        vm.PrintCommand.Execute(null);

        dialog.Received(1).PrintFile("C:\\doc.pdf");
        vm.StatusText.Should().Contain("Print");
    }

    // ── Properties ───────────────────────────────────────────────────────────

    [Fact]
    public void Properties_CallsShowPropertiesWithCurrentDocumentInfo()
    {
        var (vm, _, _, dialog) = CreateDocumentLoadedVm("C:\\doc.pdf");
        var info = vm.DocumentInfo!;

        vm.PropertiesCommand.Execute(null);

        dialog.Received(1).ShowProperties(info);
    }
}
