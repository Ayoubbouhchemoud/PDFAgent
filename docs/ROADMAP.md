# PDF Agent — Roadmap & Effort Estimates

## Phase 0: Foundation (Weeks 1–4)

| Epic | Stories | Dev-weeks |
|------|---------|-----------|
| Project scaffold | Solution, projects, DI, logging, config, CI/CD | 1 |
| PDF viewer MVP | Open, render, thumbnails, page nav, zoom | 2 |
| File operations | Save-as, print, document info extraction | 1 |
| **Total** | | **4** |

**Deliverable**: Working viewer that opens any PDF and renders pages.

## Phase 1: Core PDF Operations (Weeks 5–8)

| Epic | Stories | Dev-weeks |
|------|---------|-----------|
| Merge/Split | Multi-file merge, split modes, page range extraction | 1 |
| Rotate/Reorder | Page rotation, drag-drop reorder, delete/insert | 1 |
| Virtual printer stub | Printer driver + converter service | 2 |
| **Total** | | **4** |

**Deliverable**: Complete page manipulation + virtual printer.

## Phase 2: Annotations & Editing (Weeks 9–12)

| Epic | Stories | Dev-weeks |
|------|---------|-----------|
| Annotation layer | Highlight, underline, sticky notes, freehand, shapes | 2 |
| Undo/redo | History stack, diff preview | 1 |
| Text editing basic | Overlay textboxes, font selection | 1 |
| **Total** | | **4** |

**Deliverable**: Annotatable PDF with full undo.

## Phase 3: OCR & Text (Weeks 13–16)

| Epic | Stories | Dev-weeks |
|------|---------|-----------|
| OCR pipeline | Tesseract integration, progress UI | 1 |
| OCR correction UI | Word-by-word correction, accept/reject suggestions | 2 |
| Text layer generation | OCR → PDF text layer overlay | 1 |
| **Total** | | **4** |

**Deliverable**: OCR for scanned PDFs with correction UI.

## Phase 4: Security & Redaction (Weeks 17–20)

| Epic | Stories | Dev-weeks |
|------|---------|-----------|
| Encryption/decryption | Password, certificate-based, permission flags | 1 |
| True redaction | Content removal + metadata wipe, audit log | 2 |
| Digital signatures | Certificate import, signing UI, verification | 1 |
| **Total** | | **4** |

**Deliverable**: Enterprise-ready security features.

## Phase 5: Batch & Automation (Weeks 21–24)

| Epic | Stories | Dev-weeks |
|------|---------|-----------|
| Batch processing | Queue, profiles, progress tracking | 1 |
| Scripting host | Roslyn + JS integration, API bindings | 2 |
| Policy templates | PII profiles, compliance reports | 1 |
| **Total** | | **4** |

**Deliverable**: Batch processing + scripting.

## Phase 6: Novel Features (Weeks 25–36)

| Epic | Stories | Dev-weeks |
|------|---------|-----------|
| Font Rescue | Glyph reconstruction, subset analysis, AI interpolation | 4 |
| Semantic Edit Mode | LLM integration, NER, language-aware reflow | 4 |
| Live Compare & Merge | Visual diff, three-way merge, conflict UI | 3 |
| Vector Ink Repair | Auto-trace, Bézier smoothing, shape reconstruction | 3 |
| Augmented Page Layers | Non-destructive overlay layers, export toggle | 2 |
| Adaptive Accessibility | Tagged PDF, reading order, EPUB/DAISY export | 3 |
| **Total** | | **19** |

## Phase 7: Polish & Release (Weeks 37–40)

| Epic | Stories | Dev-weeks |
|------|---------|-----------|
| UI polish | Animation, transitions, theming, responsive layout | 1 |
| Performance | Lazy loading, memory optimization, caching | 1 |
| Testing | E2E tests, PDF/A validation, accessibility audit | 1 |
| Packaging | MSIX signing, installer, auto-update | 1 |
| **Total** | | **4** |

---

## Summary Estimates

| Phase | Duration | Dev-weeks | Cumulative |
|-------|----------|-----------|------------|
| 0: Foundation | 4 weeks | 4 | 4 |
| 1: Core Ops | 4 weeks | 4 | 8 |
| 2: Annotations | 4 weeks | 4 | 12 |
| 3: OCR | 4 weeks | 4 | 16 |
| 4: Security | 4 weeks | 4 | 20 |
| 5: Batch | 4 weeks | 4 | 24 |
| 6: Novel | 12 weeks | 19 | 43 |
| 7: Polish | 4 weeks | 4 | 47 |

**MVP (Phases 0–4): ~20 weeks, 1–2 developers = 4–5 months**

**Full product (Phases 0–7): ~40 weeks, 2–3 developers = 8–10 months**

## Team Recommendation

| Role | Headcount | Phase |
|------|-----------|-------|
| Senior .NET/WPF dev | 2 | All |
| PDF engineer | 1 | 0–6 |
| ML/AI engineer | 1 (part-time) | 6 |
| QA engineer | 1 | 2–7 |
| UI/UX designer | 1 (part-time) | 0–2, 7 |
