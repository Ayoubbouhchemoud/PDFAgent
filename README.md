# PDF Agent

A production-ready WPF application for viewing, editing, and processing PDF documents. Built on .NET 8 with CommunityToolkit.MVVM, Pdfium, and Tesseract OCR.

## Requirements

| Tool | Version |
|------|---------|
| .NET SDK | 8.0+ |
| Visual Studio | 2022 17.8+ (or Rider 2023.3+) |
| Windows | 10 / 11 x64 |
| Tesseract data | `eng.traineddata` (optional — only for OCR) |

## Build & Run

```bash
# Restore packages
dotnet restore PDF-Agent.sln

# Build (Debug)
dotnet build PDF-Agent.sln

# Run the app
dotnet run --project src/PDFAgent.App

# Run all tests
dotnet test PDF-Agent.sln
```

In Visual Studio, set **PDFAgent.App** as the startup project and press F5.

## OCR Setup (optional)

1. Download `eng.traineddata` from the [Tesseract data repo](https://github.com/tesseract-ocr/tessdata).
2. Copy it to `%LOCALAPPDATA%\PDFAgent\tessdata\` **or** set the `TESSDATA_PREFIX` environment variable.
3. The app detects OCR availability at startup and disables OCR features gracefully if data is missing.

## Project Structure

```
PDF-Agent.sln
├── src/
│   ├── PDFAgent.App/          — WPF application (XAML, ViewModels, DI wiring)
│   │   ├── Themes/            — HumanisticLight design system (Colors, Typography, Icons, Controls)
│   │   ├── Views/             — MainWindow, BatchWorkflowView, OcrReviewView
│   │   ├── ViewModels/        — MainViewModel, BatchWorkflowViewModel, OcrReviewViewModel
│   │   └── Services/          — FileDialogService, BatchWorkflowService
│   ├── PDFAgent.Core/         — Interfaces, models, OperationResult, HistoryManager
│   └── PDFAgent.PdfEngine/    — Pdfium renderer, Tesseract OCR, redaction engine
└── tests/
    ├── PDFAgent.Core.Tests/
    ├── PDFAgent.App.Tests/
    └── PDFAgent.PdfEngine.Tests/
```

## Design System — Humanistic Utility

The visual theme is defined in `src/PDFAgent.App/Themes/`:

| Token | Value | Usage |
|-------|-------|-------|
| Primary | `#9A4023` | Terracotta — buttons, accents, headings |
| Surface | `#FFF8F6` | Off-white — window background |
| Secondary | `#56642B` | Sage — chip highlights |
| Tertiary | `#006768` | Teal — informational badges |
| Error | `#BA1A1A` | Reject / error states |
| Heading font | Playfair Display → Georgia | Panel titles, app wordmark |
| Body font | Inter → Segoe UI | Labels, body text |

All colors are exposed as `SolidColorBrush` resources and consumed via `{DynamicResource}` so a future dark theme can swap them at runtime without touching XAML structure.

## Features

- **Document viewer** — scroll through all pages, thumbnail strip, zoom 25 %–1000 %, page navigation
- **Edit tools** — merge, split, rotate, annotate, edit text
- **OCR** — run Tesseract on any page; **OCR Review** side-by-side pane for correcting recognized text
- **Redaction** — PII pattern detection (email, phone, SSN, credit card) with black-box redaction
- **Digital signing** — placeholder wired to X.509 certificate flow
- **Batch Workflow** — visual pipeline editor; drag-and-drop step cards (OCR, redact, rotate, compress, watermark, export…); save and re-run named workflows

## CI

GitHub Actions at `.github/workflows/build.yml` runs five jobs on every push and pull request:

| Job | What it does |
|-----|-------------|
| `build` | `dotnet build` + `dotnet test` |
| `code-quality` | Roslyn analyzer warnings-as-errors |
| `security-scan` | `dotnet list package --vulnerable` |
| `package-msix` | Creates a self-contained MSIX on `main` |
| `release-drafter` | Auto-generates release notes |

## Architecture Notes

- **MVVM** enforced: zero business logic in code-behind files (`.xaml.cs` contains only `InitializeComponent()`).
- **DI** via `Microsoft.Extensions.DependencyInjection`; the container is built in `App.xaml.cs`.
- **Result pattern**: all service methods return `OperationResult` / `OperationResult<T>` — never throw for domain errors.
- **Undo/redo**: `HistoryManager` (linked-list, 100-entry cap) is available for all destructive operations.
- **Cloud opt-in**: no cloud calls at startup; OCR and redaction run locally by default.
