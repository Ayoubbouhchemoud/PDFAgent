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

    [Fact]
    public async Task Rotate_ClosesEngineBefore_RotatePagesAsync()
    {
        // ROOT CAUSE FIX: PdfiumViewer holds a file lock. Engine must be closed
        // before PdfSharp can overwrite the file, otherwise File.Move throws
        // UnauthorizedAccessException (confirmed in log 2026-05-13 17:51:32).
        var callOrder = new List<string>();

        var (vm, engine, editor, _) = CreateDocumentLoadedVm("C:\\doc.pdf");

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
        var (vm, engine, editor, _) = CreateDocumentLoadedVm("C:\\doc.pdf");
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
