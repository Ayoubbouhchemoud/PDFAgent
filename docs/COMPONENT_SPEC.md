# Component Specification & Design Mapping

This document maps each Stitch design screen to its WPF implementation: the XAML file, ViewModel, and key bindings.

---

## 1. Main Document Workspace

**Design screen:** `Main Document Workspace.html`

| Design element | WPF file | Notes |
|---|---|---|
| App toolbar (top bar) | `Views/MainWindow.xaml` — `Grid.Row="0"` | `ToolbarBackgroundBrush` (`#FFF0ED`), 52 px, shadow |
| App wordmark | `TextBlock` in toolbar `DockPanel.Dock="Left"` | Playfair Display 18 pt, `PrimaryBrush` |
| Toolbar buttons | `Style=ToolbarButton` in `Themes/Controls.xaml` | Hover/pressed state layers via `StateHoverBrush` |
| Thumbnail strip | `Views/MainWindow.xaml` — `Grid.Column="0"` | `SidebarBackgroundBrush`, 216 px default, resizable |
| Page viewer | `Views/MainWindow.xaml` — `Grid.Column="2"` | `SurfaceContainerBrush` canvas, `PageShadow` effect on each page |
| Properties panel | `Views/MainWindow.xaml` — `Grid.Column="4"` | 300 px, scrollable `StackPanel` |
| Status bar | `Views/MainWindow.xaml` — `Grid.Row="2"` | `AppStatusBar` style |

**ViewModel:** `ViewModels/MainViewModel.cs` + `ViewModels/MainViewModel.Navigation.cs`

Key bindings:

```
OpenFileCommand       → Opens file dialog, calls IPdfEngine.OpenAsync
SaveFileCommand       → Copies file via FileDialogService.SavePdf
ZoomInCommand         → Increments CurrentZoom through ZoomLevels list
ZoomOutCommand        → Decrements CurrentZoom
NextPageCommand       → CurrentPage++
PreviousPageCommand   → CurrentPage--
OpenBatchWorkflow     → Fires BatchWorkflowRequested event
OpenOcrReview         → Fires OcrReviewRequested event
```

---

## 2. Batch Workflow Editor

**Design screen:** `Batch Workflow Editor.html`

| Design element | WPF file | Notes |
|---|---|---|
| Header bar | `Views/BatchWorkflowView.xaml` — `Grid.Row="0"` | Heading "Batch Workflow Editor", Save + Run buttons |
| Step library (left column) | `Grid.Column="0"` — 280 px sidebar | `SidebarBackgroundBrush`, `ItemsControl` bound to `AvailableStepTypes` |
| Step palette card | `DataTemplate` with `WorkflowStepCard` style | Double-click adds to pipeline via `AddStepCommand` |
| Pipeline canvas (right column) | `Grid.Column="1"` — `ScrollViewer` + `ItemsControl` | Bound to `Steps` observable collection |
| Pipeline step card | `DataTemplate` with drag handle + icon + text + remove button | Remove bound to `RemoveStepCommand` with `CommandParameter={Binding}` |
| Add Step button | `OutlinedButton` below the list | `AddEmptyStepCommand` |
| Progress bar | `ProgressBar` + `LinearProgress` style | `Value={Binding RunProgress}`, shown only when `IsRunning` |
| Footer | `Border Grid.Row="2"` | `StatusMessage` + run percentage |

**ViewModel:** `ViewModels/BatchWorkflowViewModel.cs`

Key bindings:

```
AddStepCommand(descriptor)  → WorkflowStepViewModel appended to Steps
RemoveStepCommand(step)     → Removed from Steps
RunWorkflowCommand          → IBatchWorkflowService.ExecuteAsync with progress
SaveWorkflowCommand         → IBatchWorkflowService.SavePipeline
```

**Service:** `Core/Services/IBatchWorkflowService.cs` → impl `App/Services/BatchWorkflowService.cs`

---

## 3. OCR Review Mode

**Design screen:** `OCR Review Mode.html`

| Design element | WPF file | Notes |
|---|---|---|
| Header bar | `Views/OcrReviewView.xaml` — `Grid.Row="0"` | Page nav buttons, confidence badge, Accept/Reject buttons |
| Confidence badge | `Border` + `SecondaryContainerBrush` + `Confidence` binding | Format: `{Confidence:P0}` |
| Scanned image (left pane) | `Grid.Column="0"` — `ScrollViewer` + `Image` | `PageImageData` → `BytesToImageConverter`; zoom in/out/fit |
| OCR text editor (right pane) | `Grid.Column="2"` — `TextBox AcceptsReturn="True"` | `OcrText` two-way binding, `UpdateSourceTrigger=PropertyChanged` |
| Word-count footer | `Border Grid.Row="2"` in text pane | `WordCount`, `LowConfidenceWordCount`, `ChipButton` toggle |
| Action footer | `Views/OcrReviewView.xaml` — `Grid.Row="2"` | Accept All + Save & Close |

**ViewModel:** `ViewModels/OcrReviewViewModel.cs`

Key bindings:

```
AcceptPageCommand       → Marks current page accepted; saves edited text
RejectPageCommand       → Marks current page rejected
AcceptAllCommand        → Marks all pages accepted
ReRunOcrCommand         → Clears cached result, re-runs OCR on current page
CopyTextCommand         → Clipboard.SetText(OcrText)
SaveAndCloseCommand     → Saves all edits, fires CloseRequested event
ZoomInImageCommand      → ImageZoomWidth *= 1.2
ZoomOutImageCommand     → ImageZoomWidth /= 1.2
FitImageCommand         → ImageZoomWidth = 600
```

---

## 4. Design Token Reference

All tokens live in `src/PDFAgent.App/Themes/Colors.xaml` and are loaded via `HumanisticLight.xaml`.

### Color Tokens

| Key | Hex | Usage |
|-----|-----|-------|
| `PrimaryBrush` | `#9A4023` | Primary actions, headings, wordmark |
| `OnPrimaryBrush` | `#FFFFFF` | Text/icons on primary-colored surfaces |
| `PrimaryContainerBrush` | `#FFDBD1` | Tonal button backgrounds, selected thumbnail |
| `OnPrimaryContainerBrush` | `#3A0A00` | Text on primary container |
| `SecondaryBrush` | `#56642B` | Secondary/sage actions |
| `SecondaryContainerBrush` | `#D8ECA4` | Confidence badge background |
| `TertiaryBrush` | `#006768` | Informational accents |
| `ErrorBrush` | `#BA1A1A` | Reject actions, error states |
| `SurfaceBrush` | `#FFF8F6` | Window / panel background |
| `SurfaceContainerLowBrush` | `#FFF0ED` | Toolbar, footer backgrounds |
| `SurfaceContainerBrush` | `#FCE9E5` | PDF viewer canvas background |
| `SidebarBackgroundBrush` | `#FCE9E5` | Thumbnail strip + properties panel |
| `OutlineBrush` | `#857371` | Default borders, placeholder text |
| `OutlineVariantBrush` | `#D8C2BE` | Subtle dividers, card borders |
| `OnSurfaceBrush` | `#201A19` | Primary text |
| `OnSurfaceVariantBrush` | `#534341` | Secondary text, labels |

### Typography Tokens

| Key | Font | Size | Weight | Usage |
|-----|------|------|--------|-------|
| `HeadingFont` | Playfair Display → Georgia | — | — | Panel titles, wordmark |
| `BodyFont` | Inter → Segoe UI | — | — | All body / label text |
| `HeadlineSmall` | Heading | 24 pt | Regular | Section headings |
| `TitleMedium` | Body | 16 pt | Medium | Panel sub-headers |
| `BodyMedium` | Body | 14 pt | Normal | Property values, OCR text |
| `LabelLarge` | Body | 14 pt | Medium | Button labels, step names |
| `LabelSmall` | Body | 11 pt | Medium | Thumbnail page numbers |

### Control Styles (defined in Controls.xaml)

| Key | TargetType | Description |
|-----|-----------|-------------|
| `FilledButton` | Button | Terracotta fill, rounded pill — primary CTA |
| `TonalButton` | Button | PrimaryContainer fill — secondary CTA |
| `OutlinedButton` | Button | Outline only — secondary / neutral |
| `TextButton` | Button | Transparent — tertiary / inline |
| `IconButton` | Button | 36×36 circular, icon only |
| `ToolbarButton` | Button | Flat, 6 px radius — toolbar actions |
| `FlatButton` | Button | Left-aligned, outlined — sidebar actions |
| `ChipButton` | ToggleButton | Rounded chip — filter toggles |
| `WorkflowStepCard` | Border | Padded card for pipeline steps |
| `Card` | Border | Elevated card with shadow |
| `ThumbnailListBoxItem` | ListBoxItem | Hover + selected state on thumbnail |
| `InputTextBox` | TextBox | Outlined input with focus ring |
| `LinearProgress` | ProgressBar | 4 px track, `PrimaryBrush` fill |
| `AppStatusBar` | StatusBar | Transparent, `BodyFont` 12 pt |

---

## 5. Icon Inventory

All icons are SVG-style `Geometry` resources in `Themes/Icons.xaml`. Apply via `{StaticResource IconPath}` style or `{StaticResource IconPathPrimary}` for terracotta stroke.

| Key | Usage |
|-----|-------|
| `IconOpenFile` | Open file toolbar button |
| `IconSaveFile` | Save toolbar button |
| `IconMerge` | Merge toolbar button |
| `IconSplit` | Split toolbar button |
| `IconRotate` | Rotate toolbar button |
| `IconOcr` | OCR toolbar button |
| `IconRedact` | Redact toolbar button |
| `IconSign` | Sign toolbar button |
| `IconAnnotate` | Annotate toolbar button |
| `IconEditText` | Edit text toolbar button |
| `IconBatch` | Batch workflow button |
| `IconZoomIn` | Zoom in |
| `IconZoomOut` | Zoom out |
| `IconFitWidth` | Fit to window |
| `IconNext` | Next page |
| `IconPrev` | Previous page |
| `IconSearch` | Search bar |
| `IconAdd` | Add step |
| `IconDelete` | Delete / remove |
| `IconClose` | Close / dismiss |
| `IconAccept` | Accept OCR page |
| `IconReject` | Reject OCR page |
| `IconExport` | Export / copy |
| `IconPrint` | Print document |
| `IconPlay` | Run workflow |
| `IconDragHandle` | Drag-reorder handle on step cards |
| `IconSettings` | Settings |
