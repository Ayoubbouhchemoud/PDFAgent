# PDF Agent — Design Document v0.1

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                   PDF Agent Desktop App                  │
├─────────────────────────────────────────────────────────┤
│  Presentation Layer (WPF / MVVM)                         │
│  ┌───────────┐ ┌──────────┐ ┌──────────┐ ┌───────────┐ │
│  │ MainWindow│ │Ribbon/Tool│ │ PDF Viewer│ │Dialogs    │ │
│  │ (Shell)   │ │Bar       │ │ (SkiaSharp)│ │(Settings,  │ │
│  └───────────┘ └──────────┘ └──────────┘ │  About)    │ │
│                                          └───────────┘ │
├─────────────────────────────────────────────────────────┤
│  ViewModels (CommunityToolkit.Mvvm)                      │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────────┐ │
│  │MainVM    │ │ViewerVM  │ │EditorVM  │ │BatchJobVM  │ │
│  └──────────┘ └──────────┘ └──────────┘ └────────────┘ │
│        ↕ DI Container (Microsoft.Extensions.DI)          │
├─────────────────────────────────────────────────────────┤
│  Core / Domain Layer (PDFAgent.Core)                     │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────────┐ │
│  │Models    │ │Interfaces │ │Services  │ │Configuration│ │
│  └──────────┘ └──────────┘ └──────────┘ └────────────┘ │
├─────────────────────────────────────────────────────────┤
│  PDF Engine / Services (PDFAgent.PdfEngine)               │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────────┐ │
│  │Pdfium    │ │PdfPig    │ │Tesseract │ │SkiaSharp   │ │
│  │(Render)  │ │(Parse)   │ │(OCR)     │ │(Annotate)  │ │
│  └──────────┘ └──────────┘ └──────────┘ └────────────┘ │
├─────────────────────────────────────────────────────────┤
│  Additional Services                                     │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐ │
│  │VirtualPrinter│ │ScriptingHost │ │SecurityService   │ │
│  │(Spooler svc) │ │(Roslyn/JS)   │ │(Signing/Redact)  │ │
│  └──────────────┘ └──────────────┘ └──────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

## Library Choices & Trade-offs (Bill of Materials)

| Component | Library | License | Cost | Pros | Cons |
|-----------|---------|---------|------|------|------|
| PDF Rendering | **PdfiumViewer** (Pdfium) | Apache 2.0 / BSD 3 | Free | Fast, HW-accelerated, battle-tested in Chrome | Read-only render; no editing |
| PDF Editing | **PdfPig** | Apache 2.0 | Free | Pure .NET, reads/writes PDF objects, text extraction | No layout preservation on write; limited editing |
| PDF Editing (Advanced) | **iText 7 Community** | AGPL | Free (AGPL) / Commercial | Full editing, forms, digital signatures, PDF/A | AGPL requires commercial license for proprietary use |
| PDF Rendering (Alt) | **MuPDF** (libmupdf) | AGPL | Free (AGPL) / Commercial | Best rendering quality, annotation support | AGPL restrictions; native bindings needed |
| PDF Editing (Alt) | **PDFTron / Apryse** | Commercial | $~$5K/yr | Best-in-class editing, all features | Expensive for indie dev |
| OCR | **Tesseract** (tesseract) | Apache 2.0 | Free | Offline, 100+ languages, well-maintained | CPU-only; less accurate than cloud APIs |
| GPU OCR | **ONNX Runtime** + Surya/TrOCR | MIT | Free (model) | GPU-accelerated, better accuracy | Large model downloads; requires ONNX runtime |
| UI | **WPF + CommunityToolkit.Mvvm** | MIT | Free | Native Windows, mature ecosystem | Windows-only; no cross-platform |
| Rendering | **SkiaSharp** | MIT | Free | HW-accelerated 2D, cross-platform WPF support | Additional dependency for annotation layer |
| Text Shaping | **HarfBuzzSharp** | MIT | Free | Proper glyph shaping for complex scripts | Additional native binary dependency |
| Scripting | **Roslyn (C#)** | MIT | Free | Type-safe, full .NET access | Heavy; sandboxing required |
| Scripting (Alt) | **ClearScript (JS/V8)** | MIT | Free | Lightweight, sandboxed by default | Less .NET integration |
| Logging | **Serilog** | Apache 2.0 | Free | Structured logging, file sinks | None |
| DI | **Microsoft.Extensions.DI** | MIT | Free | Standard .NET DI, well-known | None |
| Packaging | **MSIX** | Proprietary | Free | Modern Windows packaging, sandboxed | Windows 10 1809+ only |

### Recommended Stack (MVP)

| Layer | Choice | Reason |
|-------|--------|--------|
| PDF Rendering | PdfiumViewer | Free, fast, proven |
| PDF Editing (basic) | PdfPig | Free, pure .NET, good enough for merge/split |
| PDF Editing (advanced) | iText 7 Community (AGPL) | For redaction + signing + PDF/A — **or** purchase commercial license |
| OCR | Tesseract | Free, offline, 100+ languages |
| Annotation overlay | SkiaSharp | HW-accelerated, composable over PDFium renders |

### Licensing Strategy

**For open-source release (AGPL-compatible):**
- Use iText 7 Community (AGPL) for all editing features
- User must comply with AGPL terms or purchase iText commercial license

**For proprietary/commercial release:**
- Replace iText with PdfPig + custom PDF object manipulation layer
- Or purchase Apryse/PDFTron commercial license (~$5K/year)
- Key cost driver: advanced editing (redaction, signing, form filling)

## Security Architecture

```
┌──────────────────────────────────┐
│   Process Isolation              │
│  ┌────────────┐ ┌──────────────┐│
│  │ Main App   │ │ Sandboxed    ││
│  │ (User)     │ │ Parsers      ││
│  └────────────┘ └──────────────┘│
│         ↕ IPC (Named Pipes)      │
│  ┌──────────────────────────────┐│
│  │ Privileged Service           ││
│  │ (Signing, Redaction, Audit)  ││
│  └──────────────────────────────┘│
└──────────────────────────────────┘
```

- PDF parsing runs in a separate AppContainer sandbox (Win32 isolation)
- Signing and true redaction use a privileged Windows Service with signed audit logs
- All user data stays local unless explicit opt-in for cloud features
- Audit logs are JSON Lines, signed with the user's certificate
- Memory-sensitive operations use `CryptographicOperations.ZeroMemory`

## Key Design Decisions

1. **Offline-first**: All processing runs locally. Cloud features opt-in per-task.
2. **Non-destructive editing**: Edits stored as operation stacks; original content preserved.
3. **Plugin architecture**: `IPdfEngine`, `IOcrEngine`, `IRedactionEngine` interfaces allow swapping implementations (e.g., Tesseract → ONNX).
4. **Async everywhere**: Long-running operations use async/await + CancellationToken + progress reporting.
5. **MVVM with DI**: CommunityToolkit.Mvvm for source generators, Microsoft.Extensions.DI for loose coupling.
