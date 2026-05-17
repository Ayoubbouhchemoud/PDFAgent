"""
pdf_to_html.py — Faithful, position-preserving PDF-to-HTML converter.

Strategy
--------
  • One <div class="pdf-page"> per page, sized in PDF-point units (1 pt = 1 CSS px).
  • Every word is placed via position:absolute at its original PDF coordinate.
  • Bold / italic is inferred from the fontname string.
  • Embedded images are extracted and inlined as base64 data-URIs (<img>).
  • The real invoice/data table is detected by analysing column structure in
    section-background rectangles.  pdfplumber.find_tables() is NOT used —
    it misclassifies coloured background boxes as table cells and turns the
    entire page into a single table.
  • No semantic headings / paragraphs / bullet inference — the converter
    preserves what is visually in the PDF, not a reflowed reading order.
"""

from __future__ import annotations

import base64
import re
from html import escape as _esc
from pathlib import Path

import pdfplumber


# ---------------------------------------------------------------------------
# Tuning constants
# ---------------------------------------------------------------------------

_LINE_TOL       = 2.5   # pt – Y-tolerance for grouping words onto one line
_MERGE_GAP      = 15.0  # pt – max whitespace gap to merge adjacent same-style words
_COL_DETECT_GAP = 5.0   # pt – min whitespace gap (x0_next − x1_prev) between header columns
_MIN_TABLE_COLS = 3     # need ≥ this many columns to treat a section as a table
_DIGIT_RE       = re.compile(r'\d')   # presence of a digit → data row, not header


# ---------------------------------------------------------------------------
# Low-level helpers
# ---------------------------------------------------------------------------

def _is_bold(fname: str) -> bool:
    return "Bold" in fname or "bold" in fname


def _is_italic(fname: str) -> bool:
    return "Italic" in fname or "italic" in fname or "Oblique" in fname


def _group_into_lines(words: list[dict]) -> list[list[dict]]:
    """Group word dicts into horizontal lines by similar top-Y (±_LINE_TOL pt)."""
    if not words:
        return []
    buckets: list[tuple[float, list[dict]]] = []
    for w in sorted(words, key=lambda w: (w["top"], w["x0"])):
        y = w["top"]
        for bk in buckets:
            if abs(bk[0] - y) <= _LINE_TOL:
                bk[1].append(w)
                break
        else:
            buckets.append((y, [w]))
    return [sorted(bk[1], key=lambda w: w["x0"]) for bk in buckets]


def _detect_col_starts(header_line: list[dict]) -> list[float]:
    """
    Return the x0 of each column start, inferred from whitespace gaps in the
    first (header) line of the table.  A new column begins when
    x0_next − x1_prev ≥ _COL_DETECT_GAP.
    """
    if not header_line:
        return []
    starts     = [header_line[0]["x0"]]
    running_x1 = header_line[0]["x1"]
    for w in header_line[1:]:
        if w["x0"] - running_x1 >= _COL_DETECT_GAP:
            starts.append(w["x0"])
        if w["x1"] > running_x1:
            running_x1 = w["x1"]
    return starts


def _assign_col(x0: float, col_starts: list[float]) -> int:
    """Return the column index for a word at x0 (rightmost col_start ≤ x0)."""
    col = 0
    for i, cs in enumerate(col_starts):
        if x0 >= cs - 2:   # 2 pt tolerance
            col = i
    return col


def _merge_into_runs(line: list[dict]) -> list[dict]:
    """
    Merge consecutive same-style words within a line into text runs.
    A run extends as long as bold/italic/size match AND whitespace gap < _MERGE_GAP.
    Returns a list of 'run' dicts with keys: text, x0, x1, top, fontname, size.
    """
    if not line:
        return []
    runs = [dict(line[0])]
    for w in line[1:]:
        p = runs[-1]
        gap        = w["x0"] - p.get("x1", p["x0"])
        same_bold  = _is_bold(w.get("fontname", "")) == _is_bold(p.get("fontname", ""))
        same_ital  = _is_italic(w.get("fontname", "")) == _is_italic(p.get("fontname", ""))
        same_size  = abs(w.get("size", 12) - p.get("size", 12)) < 0.5
        if same_bold and same_ital and same_size and gap < _MERGE_GAP:
            p["text"] = p["text"] + " " + w["text"]
            p["x1"]   = w["x1"]
        else:
            runs.append(dict(w))
    return runs


# ---------------------------------------------------------------------------
# Vector-graphics color helpers
# ---------------------------------------------------------------------------

def _css_color(c, default: str = "none") -> str:
    """Convert a pdfplumber color value (grayscale float, RGB tuple, etc.) to a CSS hex string."""
    if c is None:
        return default
    if isinstance(c, str):
        return default          # spot-color / resource reference — unresolvable
    try:
        if isinstance(c, (int, float)):
            v = max(0, min(255, round(float(c) * 255)))
            return f"#{v:02x}{v:02x}{v:02x}"
        if isinstance(c, (tuple, list)):
            if len(c) == 1:
                v = max(0, min(255, round(float(c[0]) * 255)))
                return f"#{v:02x}{v:02x}{v:02x}"
            if len(c) == 3:
                r, g, b = (max(0, min(255, round(float(x) * 255))) for x in c)
                return f"#{r:02x}{g:02x}{b:02x}"
            if len(c) == 4:                         # CMYK → RGB
                c_, m, y_, k = (float(x) for x in c)
                r = max(0, min(255, round(255 * (1 - c_) * (1 - k))))
                g = max(0, min(255, round(255 * (1 - m)  * (1 - k))))
                b = max(0, min(255, round(255 * (1 - y_) * (1 - k))))
                return f"#{r:02x}{g:02x}{b:02x}"
    except Exception:
        pass
    return default


def _is_white_color(c) -> bool:
    """Return True when c represents white or near-white (≥ 0.99 in every channel)."""
    if c is None:
        return False
    try:
        if isinstance(c, (int, float)):
            return float(c) >= 0.99
        if isinstance(c, (tuple, list)):
            return all(float(x) >= 0.99 for x in c)
    except Exception:
        pass
    return False


def _svg_path_d(path_ops: list) -> str:
    """
    Convert pdfplumber path operators to an SVG path 'd' string.
    Operators: m=moveto, l=lineto, c=curveto(3pts), v=curveto(cp1=current),
               y=curveto(cp2=endpoint), h=closepath.
    Coordinates are already in pdfplumber's top-left pixel space.
    """
    parts: list[str] = []
    cx = cy = 0.0
    for entry in path_ops:
        op = entry[0]
        pts = entry[1:]
        if op == "m":
            x, y = pts[0]
            parts.append(f"M {x:.3f} {y:.3f}")
            cx, cy = x, y
        elif op == "l":
            x, y = pts[0]
            parts.append(f"L {x:.3f} {y:.3f}")
            cx, cy = x, y
        elif op == "c":
            (x1, y1), (x2, y2), (x3, y3) = pts
            parts.append(f"C {x1:.3f} {y1:.3f} {x2:.3f} {y2:.3f} {x3:.3f} {y3:.3f}")
            cx, cy = x3, y3
        elif op == "v":
            # PDF v: first control point = current point
            (x2, y2), (x3, y3) = pts
            parts.append(f"C {cx:.3f} {cy:.3f} {x2:.3f} {y2:.3f} {x3:.3f} {y3:.3f}")
            cx, cy = x3, y3
        elif op == "y":
            # PDF y: second control point = endpoint
            (x1, y1), (x3, y3) = pts
            parts.append(f"C {x1:.3f} {y1:.3f} {x3:.3f} {y3:.3f} {x3:.3f} {y3:.3f}")
            cx, cy = x3, y3
        elif op == "h":
            parts.append("Z")
    return " ".join(parts)


def _vector_svg(page, pw: float, ph: float) -> str:
    """
    Render all non-text vector elements — curves, lines, non-white filled rects
    (separator lines, decorative dots) — as an absolutely-positioned inline <svg>.
    The SVG sits behind text spans and bitmap images so those layers remain on top.
    """
    elems: list[str] = []

    # ── Rects (separator lines, decorative fills; skip white / full-page) ──
    for r in page.rects:
        rw = r["x1"] - r["x0"]
        rh = r["bottom"] - r["top"]
        if rw > pw * 0.95 or rw < 0.2 or rh < 0.2:
            continue
        do_fill   = bool(r.get("fill",   False))
        do_stroke = bool(r.get("stroke", False))
        fc_raw = r.get("non_stroking_color")
        sc_raw = r.get("stroking_color")
        lw     = r.get("linewidth", 0) or 0

        fill_vis   = do_fill   and not _is_white_color(fc_raw) and _css_color(fc_raw) != "none"
        stroke_vis = do_stroke and not _is_white_color(sc_raw) and lw > 0 and _css_color(sc_raw) != "none"
        if not fill_vis and not stroke_vis:
            continue

        fc_css = _css_color(fc_raw) if fill_vis   else "none"
        sc_css = _css_color(sc_raw) if stroke_vis else "none"
        attrs  = (f'x="{r["x0"]:.2f}" y="{r["top"]:.2f}" '
                  f'width="{rw:.2f}" height="{rh:.2f}" fill="{fc_css}"')
        if stroke_vis:
            attrs += f' stroke="{sc_css}" stroke-width="{lw:.2f}"'
        elems.append(f"<rect {attrs}/>")

    # ── Lines ──────────────────────────────────────────────────────────────
    for ln in page.lines:
        sc_raw = ln.get("stroking_color")
        if _is_white_color(sc_raw):
            continue
        sc_css = _css_color(sc_raw)
        if sc_css == "none":
            continue
        lw = ln.get("linewidth", 1) or 1
        elems.append(
            f'<line x1="{ln["x0"]:.2f}" y1="{ln["top"]:.2f}" '
            f'x2="{ln["x1"]:.2f}" y2="{ln["bottom"]:.2f}" '
            f'stroke="{sc_css}" stroke-width="{lw:.2f}"/>'
        )

    # ── Curves ─────────────────────────────────────────────────────────────
    for c in page.curves:
        do_fill   = bool(c.get("fill"))
        do_stroke = bool(c.get("stroke"))
        fc_raw    = c.get("non_stroking_color")
        sc_raw    = c.get("stroking_color")
        lw        = c.get("linewidth", 1) or 1

        fc_css = _css_color(fc_raw) if do_fill  else "none"
        sc_css = _css_color(sc_raw) if do_stroke else "none"

        # Skip if both fill and stroke are invisible
        fill_visible   = do_fill   and fc_css != "none" and not _is_white_color(fc_raw)
        stroke_visible = do_stroke and sc_css != "none" and not _is_white_color(sc_raw)
        if not fill_visible and not stroke_visible:
            continue

        path_d = _svg_path_d(c.get("path", []))
        if not path_d:
            continue

        fa = f'fill="{fc_css}"' if fill_visible else 'fill="none"'
        sa = (f'stroke="{sc_css}" stroke-width="{lw:.2f}"'
              if stroke_visible else 'stroke="none"')
        fr = ' fill-rule="evenodd"' if c.get("evenodd") else ""
        elems.append(f"<path {fa} {sa}{fr} d=\"{path_d}\"/>")

    if not elems:
        return ""

    return (
        f'<svg style="position:absolute;left:0;top:0;'
        f'width:{pw:.0f}px;height:{ph:.0f}px;overflow:visible;pointer-events:none;" '
        f'xmlns="http://www.w3.org/2000/svg" '
        f'viewBox="0 0 {pw:.0f} {ph:.0f}">\n'
        + "\n".join(elems)
        + "\n</svg>"
    )


# ---------------------------------------------------------------------------
# Section-rect detection
# ---------------------------------------------------------------------------

def _section_rects(page) -> list[tuple[float, float]]:
    """
    Return (y_top, y_bot) of content-section background rectangles.
    Full-page-width backgrounds and tiny decorations are excluded.
    """
    pw = float(page.width)
    result: set[tuple[float, float]] = set()
    for r in page.rects:
        w = r["x1"] - r["x0"]
        h = r["bottom"] - r["top"]
        if w > pw * 0.95:   # full-page background
            continue
        if h < 15 or w < 50:  # decoration
            continue
        result.add((r["top"], r["bottom"]))
    return sorted(result)


# ---------------------------------------------------------------------------
# Image extraction helpers
# ---------------------------------------------------------------------------

def _resolve_indexed_palette(cs_raw) -> bytes | None:
    """
    Given the raw ColorSpace value from a stream dict, return a 768-byte RGB
    palette suitable for PIL.putpalette(), or None if unresolvable.

    PDF Indexed colorspace: [/Indexed, baseCS, maxIndex, lookup]
    The lookup is either raw bytes or a PDFObjRef to a stream/bytes object.
    """
    if not isinstance(cs_raw, list) or len(cs_raw) < 4:
        return None

    name = cs_raw[0]
    if "Indexed" not in (name.name if hasattr(name, "name") else str(name)):
        return None

    base   = cs_raw[1]
    max_idx = int(cs_raw[2])
    lookup = cs_raw[3]

    # Resolve the lookup table (may be a PDFObjRef pointing to a stream or bytes).
    pal_bytes: bytes | None = None
    if isinstance(lookup, bytes):
        pal_bytes = lookup
    elif hasattr(lookup, "resolve"):
        try:
            obj = lookup.resolve()
            if isinstance(obj, bytes):
                pal_bytes = obj
            elif hasattr(obj, "get_data"):
                pal_bytes = obj.get_data()
        except Exception:
            pass

    if not pal_bytes:
        return None

    # Determine bytes-per-entry from the base colorspace.
    base_str = str(base)
    if ("Gray" in base_str or "gray" in base_str) and not isinstance(base, list):
        n_ch = 1
    elif ("CMYK" in base_str or "cmyk" in base_str) and not isinstance(base, list):
        n_ch = 4
    else:
        # DeviceRGB, ICCBased, Lab, CalRGB → treat as 3-channel RGB output.
        n_ch = 3

    # Build the full 256-entry palette PIL expects (768 bytes for RGB output).
    full_pal = bytearray(256 * 3)
    if n_ch == 3:
        copy_len = min(len(pal_bytes), (max_idx + 1) * 3, 256 * 3)
        full_pal[:copy_len] = pal_bytes[:copy_len]
    elif n_ch == 1:
        for k in range(min(max_idx + 1, len(pal_bytes), 256)):
            v = pal_bytes[k]
            full_pal[k * 3: k * 3 + 3] = bytes([v, v, v])
    else:
        return None  # CMYK palette not handled

    return bytes(full_pal)


# ---------------------------------------------------------------------------
# Image extraction
# ---------------------------------------------------------------------------

def _img_html(img: dict) -> str | None:
    """
    Build an absolutely-positioned <img> tag for a pdfplumber image dict.
    Supports DCTDecode (JPEG) natively; reconstructs FlateDecode rasters via PIL.
    Indexed colorspace palettes are resolved from the PDF object graph so that
    colours match the original rather than defaulting to black.
    Returns None if the image cannot be read.
    """
    stream = img.get("stream")
    if stream is None:
        return None
    try:
        raw = stream.get_data()
    except Exception:
        return None

    filt = str(stream.attrs.get("Filter", ""))
    if "DCT" in filt or "JPEG" in filt:
        # get_data() returns the raw JPEG bytes for DCTDecode streams.
        mime = "image/jpeg"
    else:
        try:
            from PIL import Image
            import io as _io

            # Pixel dimensions come from the image XObject's /Width & /Height,
            # NOT from img["width"/"height"] which are PDF coordinate widths.
            px_w = int(stream.attrs.get("Width",  0))
            px_h = int(stream.attrs.get("Height", 0))
            if not px_w or not px_h:
                return None

            cs_raw = stream.attrs.get("ColorSpace", img.get("colorspace", ""))
            cs     = str(cs_raw)

            if "Gray" in cs:
                mode     = "L"
                expected = px_w * px_h
            elif "Indexed" in cs or "indexed" in cs.lower():
                # Palette-indexed: 1 byte per pixel (palette index).
                mode     = "P"
                expected = px_w * px_h
            elif "CMYK" in cs:
                mode     = "CMYK"
                expected = px_w * px_h * 4
            else:
                mode     = "RGB"
                expected = px_w * px_h * 3

            if len(raw) < expected:
                return None

            pil = Image.frombytes(mode, (px_w, px_h), raw[:expected])

            if mode == "P":
                # Apply the real PDF palette so colours match the original.
                pal = _resolve_indexed_palette(cs_raw)
                if pal:
                    pil.putpalette(pal)
                pil = pil.convert("RGB")
            elif mode == "CMYK":
                pil = pil.convert("RGB")

            buf = _io.BytesIO()
            pil.save(buf, format="PNG")
            raw  = buf.getvalue()
            mime = "image/png"
        except Exception:
            return None

    b64    = base64.b64encode(raw).decode("ascii")
    x0     = float(img["x0"])
    top    = float(img["top"])
    width  = float(img["x1"]) - x0
    height = float(img["bottom"]) - top

    return (
        f'<img src="data:{mime};base64,{b64}"'
        f' style="position:absolute;left:{x0:.1f}px;top:{top:.1f}px;'
        f'width:{width:.1f}px;height:{height:.1f}px;object-fit:contain;"'
        f' alt="embedded image"/>'
    )


# ---------------------------------------------------------------------------
# Positioned text span
# ---------------------------------------------------------------------------

def _span_html(run: dict) -> str:
    """Build a single absolutely-positioned <span> for a text run."""
    fname  = run.get("fontname", "")
    size   = float(run.get("size", 9.8))
    x0     = float(run["x0"])
    top    = float(run["top"])

    styles = [
        "position:absolute",
        f"left:{x0:.1f}px",
        f"top:{top:.1f}px",
        f"font-size:{size:.2f}px",
    ]
    if _is_bold(fname):
        styles.append("font-weight:bold")
    if _is_italic(fname):
        styles.append("font-style:italic")

    return f'<span style="{"; ".join(styles)}">{_esc(run["text"])}</span>'


# ---------------------------------------------------------------------------
# Table rendering
# ---------------------------------------------------------------------------

def _table_html(words: list[dict], col_starts: list[float], y_top: float) -> str:
    """
    Convert the words in a detected table region into a positioned <table>.

    Header rows   — consecutive lines at the top of the table whose col-0
                    text contains no digits (column labels, not data).
    Data rows     — lines whose col-0 text contains a digit (e.g., a date),
                    plus any subsequent continuation lines with no col-0 text.
    """
    lines = _group_into_lines(words)
    if not lines:
        return ""

    n = len(col_starts)

    # ── Separate header lines from data lines ─────────────────────────────
    header_lines: list[list[dict]] = []
    data_lines:   list[list[dict]] = []
    in_header = True

    for line in lines:
        col0_words = [w for w in line if _assign_col(w["x0"], col_starts) == 0]
        col0_text  = " ".join(w["text"] for w in col0_words)
        has_col0   = bool(col0_words)

        if in_header:
            if has_col0 and _DIGIT_RE.search(col0_text):
                in_header = False    # first data row found
                data_lines.append(line)
            else:
                header_lines.append(line)
        else:
            data_lines.append(line)

    # ── Build cell text for a group of lines ─────────────────────────────
    # Each physical line becomes one "segment" per cell; segments are joined
    # with <br> so multi-line cell content is preserved faithfully.
    def cells_from(row_lines: list[list[dict]]) -> list[str]:
        cols: list[list[str]] = [[] for _ in range(n)]  # per-col line segments
        for ln in row_lines:
            line_cells: list[list[str]] = [[] for _ in range(n)]
            for w in ln:
                ci   = _assign_col(w["x0"], col_starts)
                text = _esc(w["text"])
                if _is_bold(w.get("fontname", "")):
                    text = f"<strong>{text}</strong>"
                line_cells[ci].append(text)
            for ci in range(n):
                if line_cells[ci]:
                    cols[ci].append(" ".join(line_cells[ci]))
        return ["<br>".join(c) for c in cols]

    # ── Group data lines into logical rows ────────────────────────────────
    # A new logical row starts on a line that has col-0 content.
    logical_data_rows: list[list[list[dict]]] = []
    cur: list[list[dict]] = []

    for line in data_lines:
        col0_words = [w for w in line if _assign_col(w["x0"], col_starts) == 0]
        has_col0   = bool(col0_words)
        if has_col0 and cur:
            logical_data_rows.append(cur)
            cur = []
        cur.append(line)
    if cur:
        logical_data_rows.append(cur)

    if not header_lines and not logical_data_rows:
        return ""

    # ── Column widths ─────────────────────────────────────────────────────
    right_edge = max((float(w["x1"]) for w in words), default=col_starts[-1] + 50)
    col_widths = [col_starts[i + 1] - col_starts[i] for i in range(n - 1)]
    col_widths.append(max(right_edge - col_starts[-1], 30.0))

    # ── Assemble HTML ─────────────────────────────────────────────────────
    tbl_left = col_starts[0]
    parts: list[str] = [
        f'<table style="position:absolute;left:{tbl_left:.1f}px;top:{y_top:.1f}px;'
        f'border-collapse:collapse;font-size:9.8px;font-family:inherit;">'
    ]

    if header_lines:
        parts.append("<thead><tr>")
        for ci, txt in enumerate(cells_from(header_lines)):
            w_style = f"width:{col_widths[ci]:.1f}px"
            parts.append(
                f'<th style="vertical-align:top;padding:1px 4px;{w_style}">{txt}</th>'
            )
        parts.append("</tr></thead>")

    if logical_data_rows:
        parts.append("<tbody>")
        for row_lines in logical_data_rows:
            parts.append("<tr>")
            for ci, txt in enumerate(cells_from(row_lines)):
                parts.append(
                    f'<td style="vertical-align:top;padding:1px 4px">{txt}</td>'
                )
            parts.append("</tr>")
        parts.append("</tbody>")

    parts.append("</table>")
    return "\n".join(parts)


# ---------------------------------------------------------------------------
# Per-page processing
# ---------------------------------------------------------------------------

def _process_page(page) -> str:
    pw = float(page.width)
    ph = float(page.height)

    all_words = page.extract_words(extra_attrs=["size", "fontname"])
    sections  = _section_rects(page)

    # ── Detect table regions ──────────────────────────────────────────────
    # Each section-background rect is a candidate; it is confirmed as a table
    # if its first word-line has ≥ _MIN_TABLE_COLS distinct column starts.
    table_regions: list[tuple[float, float, list[float]]] = []

    for (y_top, y_bot) in sections:
        band = [w for w in all_words if y_top - 1 <= w["top"] <= y_bot + 1]
        if not band:
            continue
        lines = _group_into_lines(band)
        if not lines:
            continue
        col_starts = _detect_col_starts(lines[0])
        if len(col_starts) >= _MIN_TABLE_COLS:
            table_regions.append((y_top, y_bot, col_starts))

    def table_region_for(w: dict) -> tuple | None:
        for (yt, yb, cs) in table_regions:
            if yt - 1 <= w["top"] <= yb + 1:
                return (yt, yb, cs)
        return None

    parts: list[str] = []

    # ── Vector graphics (bottom layer: logos, separator lines, signatures) ─
    svg = _vector_svg(page, pw, ph)
    if svg:
        parts.append(svg)

    # ── Images ───────────────────────────────────────────────────────────
    for img in page.images:
        h = _img_html(img)
        if h:
            parts.append(h)

    # ── Tables ───────────────────────────────────────────────────────────
    rendered: set[tuple[float, float]] = set()
    for (yt, yb, cs) in table_regions:
        if (yt, yb) in rendered:
            continue
        tbl_words = [w for w in all_words if yt - 1 <= w["top"] <= yb + 1]
        rendered.add((yt, yb))
        if not tbl_words:
            continue
        # Use actual first-word top so the table aligns with its text content,
        # not with the section background rect which may start a few pt higher.
        first_top = min(w["top"] for w in tbl_words)
        parts.append(_table_html(tbl_words, cs, first_top))

    # ── Positioned text spans ─────────────────────────────────────────────
    non_table = [w for w in all_words if table_region_for(w) is None]
    for line in _group_into_lines(non_table):
        for run in _merge_into_runs(line):
            parts.append(_span_html(run))

    inner = "\n".join(p for p in parts if p)
    return (
        f'<div class="pdf-page" '
        f'style="width:{pw:.0f}px;height:{ph:.0f}px;">\n'
        f'{inner}\n'
        f'</div>'
    )


# ---------------------------------------------------------------------------
# CSS / HTML shell
# ---------------------------------------------------------------------------

_CSS = """\
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { background: #888; }
  .pdf-page {
    position: relative;
    background: white;
    overflow: hidden;
    box-shadow: 0 4px 16px rgba(0,0,0,.4);
    margin: 20px auto;
    font-family: 'Open Sans', Arial, sans-serif;
  }
  .pdf-page span {
    position: absolute;
    white-space: nowrap;
    line-height: 1;
    color: #000;
  }
  table {
    border-collapse: collapse;
    font-family: 'Open Sans', Arial, sans-serif;
  }
  th { font-weight: bold; text-align: left; }
  td { text-align: left; }
  img { display: block; }"""


def _wrap(title: str, body: str) -> str:
    return (
        "<!DOCTYPE html>\n"
        '<html lang="de">\n'
        "<head>\n"
        '  <meta charset="utf-8"/>\n'
        '  <meta name="viewport" content="width=device-width,initial-scale=1"/>\n'
        f"  <title>{_esc(title)}</title>\n"
        f"  <style>\n{_CSS}\n  </style>\n"
        "</head>\n"
        "<body>\n"
        f"{body}\n"
        "</body>\n"
        "</html>"
    )


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

def convert(pdf_path: str | Path, title: str | None = None) -> str:
    """Convert a PDF to faithful position-preserving HTML5."""
    pdf_path  = Path(pdf_path)
    doc_title = title or pdf_path.stem

    page_htmls: list[str] = []
    with pdfplumber.open(pdf_path) as pdf:
        for page in pdf.pages:
            page_htmls.append(_process_page(page))

    body = "\n".join(page_htmls)
    if not body.strip():
        body = '<div class="pdf-page"><p>No content found.</p></div>'

    return _wrap(doc_title, body)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    import sys

    if len(sys.argv) < 2:
        print("Usage: python pdf_to_html.py <input.pdf> [output.html]", file=sys.stderr)
        sys.exit(1)

    src = Path(sys.argv[1])
    dst = Path(sys.argv[2]) if len(sys.argv) > 2 else src.with_suffix(".html")
    dst.write_text(convert(src), encoding="utf-8")
    print(f"Written → {dst}")
