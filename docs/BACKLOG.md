# PDF Agent — Prioritized Backlog

## Epics & User Stories

### Epic 0: Project Foundation (MVP)

| ID | User Story | Priority | Est. |
|----|-----------|----------|------|
| 0.1 | As a developer, I want a working VS solution so I can build and debug | P0 | 1d |
| 0.2 | As a developer, I want DI and logging wired up so I can trace issues | P0 | 1d |
| 0.3 | As a developer, I want CI/CD pipeline so builds are automated | P0 | 1d |
| 0.4 | As a developer, I want unit tests running in CI so I maintain quality | P0 | 1d |
| 0.5 | As a developer, I want configuration management so settings persist | P1 | 1d |
| 0.6 | As a developer, I want MSIX packaging so the app installs cleanly | P2 | 2d |

### Epic 1: PDF Viewer

| ID | User Story | Priority | Est. |
|----|-----------|----------|------|
| 1.1 | As a user, I want to open a PDF file from disk | P0 | 2d |
| 1.2 | As a user, I want to see rendered pages in a scrollable view | P0 | 3d |
| 1.3 | As a user, I want thumbnail previews in a sidebar | P1 | 2d |
| 1.4 | As a user, I want to zoom in/out and fit to width | P1 | 1d |
| 1.5 | As a user, I want to see document properties (pages, size, author) | P1 | 1d |
| 1.6 | As a user, I want to navigate pages (next/prev/goto) | P1 | 1d |
| 1.7 | As a user, I want search text within the document | P2 | 3d |
| 1.8 | As a user, I want smooth scrolling with high-DPI rendering | P2 | 2d |

### Epic 2: Page Operations

| ID | User Story | Priority | Est. |
|----|-----------|----------|------|
| 2.1 | As a user, I want to merge multiple PDFs into one | P0 | 2d |
| 2.2 | As a user, I want to split a PDF into separate files | P0 | 2d |
| 2.3 | As a user, I want to rotate pages (90°, 180°, 270°) | P0 | 1d |
| 2.4 | As a user, I want to extract selected pages to a new PDF | P1 | 1d |
| 2.5 | As a user, I want to delete pages | P1 | 1d |
| 2.6 | As a user, I want to reorder pages by drag-and-drop | P1 | 2d |
| 2.7 | As a user, I want to insert pages from another PDF | P1 | 1d |

### Epic 3: Annotations

| ID | User Story | Priority | Est. |
|----|-----------|----------|------|
| 3.1 | As a user, I want to highlight text | P1 | 3d |
| 3.2 | As a user, I want to add sticky notes | P1 | 2d |
| 3.3 | As a user, I want to draw freehand annotations | P1 | 3d |
| 3.4 | As a user, I want to underline/strikethrough text | P1 | 2d |
| 3.5 | As a user, I want to add rectangles, ellipses, lines, arrows | P1 | 3d |
| 3.6 | As a user, I want to edit/delete/move annotations | P2 | 2d |
| 3.7 | As a user, I want to see annotation list and jump to them | P2 | 2d |

### Epic 4: OCR

| ID | User Story | Priority | Est. |
|----|-----------|----------|------|
| 4.1 | As a user, I want to OCR a scanned page with Tesseract | P0 | 3d |
| 4.2 | As a user, I want to see OCR results overlaid on the page | P1 | 2d |
| 4.3 | As a user, I want to correct OCR mistakes word-by-word | P1 | 4d |
| 4.4 | As a user, I want to OCR all pages in batch with progress | P1 | 1d |
| 4.5 | As a user, I want to search within OCR text | P2 | 2d |

### Epic 5: Security

| ID | User Story | Priority | Est. |
|----|-----------|----------|------|
| 5.1 | As a user, I want to encrypt a PDF with password | P1 | 3d |
| 5.2 | As a user, I want to decrypt a password-protected PDF | P1 | 2d |
| 5.3 | As a user, I want to redact text by selecting regions | P0 | 4d |
| 5.4 | As a user, I want PII auto-redaction (email, SSN, credit cards) | P1 | 3d |
| 5.5 | As a user, I want to digitally sign a PDF | P1 | 4d |
| 5.6 | As a user, I want to verify existing signatures | P2 | 2d |
| 5.7 | As a user, I want to see a redaction audit log | P2 | 2d |

### Epic 6: Batch & Automation

| ID | User Story | Priority | Est. |
|----|-----------|----------|------|
| 6.1 | As a user, I want to define processing profiles | P2 | 3d |
| 6.2 | As a user, I want to run a batch job on multiple files | P2 | 3d |
| 6.3 | As a user, I want to author automation scripts | P3 | 5d |
| 6.4 | As a user, I want scheduled batch processing | P3 | 3d |

### Epic 7: Novel Features

| ID | User Story | Priority | Est. |
|----|-----------|----------|------|
| 7.1 | As a user, I want Font Rescue to fill in missing glyphs | P3 | 4w |
| 7.2 | As a user, I want Semantic Edit Mode with LLM assistance | P3 | 4w |
| 7.3 | As a user, I want Live Compare & Merge between versions | P3 | 3w |
| 7.4 | As a user, I want Vector Ink Repair for scans | P3 | 3w |
| 7.5 | As a user, I want Policy Templates for compliance | P3 | 2w |
| 7.6 | As a user, I want Augmented Page Layers for teaching | P3 | 2w |
| 7.7 | As a user, I want Adaptive Accessibility auto-tagging | P3 | 3w |

## MVP Scope

Minimum Viable Product covers Epics 0–5:
- Open/view/save PDFs
- Merge, split, rotate, extract pages
- Annotations (highlights, notes, freehand)
- OCR with correction UI
- Encryption, redaction, signatures

Estimated: 20 weeks with 2 developers.
