"""
pdf_to_html.py — Step 3: Heading detection, empty-row filtering, orphan cleanup.

Fixes vs Step 2:
  - Median size computed from ALL words before table exclusion (true body baseline)
  - Per-page median passed into _build_body so heading thresholds are consistent
  - Empty table rows (all cells blank) filtered out of <table> output
  - Short orphan <p> elements (< 3 visible chars) suppressed
"""

from __future__ import annotations

import re
import statistics
from dataclasses import dataclass, field
from html import escape
from pathlib import Path

import pdfplumber


# ---------------------------------------------------------------------------
# Tuning constants
# ---------------------------------------------------------------------------

_ROW_TOL          = 4.0   # pt: vertical tolerance for grouping words into one row
_COL_GAP_RATIO    = 0.12  # fraction of page width that qualifies as a column gap
_PARA_MERGE_RATIO = 1.8   # gap > ratio × line_height → new paragraph
_TABLE_MIN_ROWS   = 2     # minimum rows to treat a detected region as a table
_ORPHAN_MIN_CHARS = 3     # <p> shorter than this (visible chars) is suppressed

_BULLET_RE = re.compile(
    r"""^
    (?:
        [•·▪▸▶‒–—◦○●■□◆◇]    # unicode bullets
        | [-*]\s               # ASCII dash/star + space
        | (?:\d+|[a-zA-Z]|[ivxIVX]+)[.)]\s   # 1. a. iv.
        | \(\d+\)\s            # (1) style
    )""",
    re.VERBOSE,
)


# ---------------------------------------------------------------------------
# Internal data types
# ---------------------------------------------------------------------------

@dataclass
class _Row:
    text:      str
    size:      float   # average font size
    y_top:     float   # distance from top of page (increases downward)
    y_bot:     float
    x_left:    float
    x_right:   float
    is_bullet: bool
    median_sz: float = field(default=12.0)  # per-page median at time of creation


@dataclass
class _Table:
    rows:  list[list[str | None]]
    y_top: float


# ---------------------------------------------------------------------------
# Low-level helpers
# ---------------------------------------------------------------------------

def _strip_bullet(text: str) -> str:
    m = _BULLET_RE.match(text.lstrip())
    return text.lstrip()[m.end():] if m else text


def _classify_size(size: float, median: float) -> str:
    """Map font-size ratio to semantic tag."""
    if median <= 0:
        return "p"
    r = size / median
    if r >= 2.0:  return "h1"
    if r >= 1.6:  return "h2"
    if r >= 1.35: return "h3"
    return "p"


def _words_to_rows(words: list[dict]) -> list[_Row]:
    """
    Group pdfplumber word dicts into horizontal text rows using a 4-pt vertical
    tolerance.  median_sz on the returned rows is set to 12.0 as a placeholder;
    callers overwrite it after computing the page median.
    """
    if not words:
        return []

    buckets: list[tuple[float, list[dict]]] = []
    for w in sorted(words, key=lambda x: x["top"]):
        y = w["top"]
        for bkt in buckets:
            if abs(bkt[0] - y) <= _ROW_TOL:
                bkt[1].append(w)
                break
        else:
            buckets.append((y, [w]))

    rows: list[_Row] = []
    for _, bkt_words in buckets:
        bkt_words.sort(key=lambda w: w["x0"])
        text = " ".join(w["text"] for w in bkt_words).strip()
        if not text:
            continue
        sizes = [w["size"] for w in bkt_words if w.get("size", 0) >= 1]
        rows.append(_Row(
            text      = text,
            size      = statistics.mean(sizes) if sizes else 12.0,
            y_top     = min(w["top"]    for w in bkt_words),
            y_bot     = max(w["bottom"] for w in bkt_words),
            x_left    = bkt_words[0]["x0"],
            x_right   = bkt_words[-1]["x1"],
            is_bullet = bool(_BULLET_RE.match(text)),
        ))
    return rows


def _point_in_bbox(cx: float, cy: float, bbox: tuple) -> bool:
    x0, top, x1, bot = bbox
    return x0 <= cx <= x1 and top <= cy <= bot


# ---------------------------------------------------------------------------
# Column detection
# ---------------------------------------------------------------------------

def _detect_column_split(words: list[dict], page_width: float) -> float | None:
    """
    Return the x-midpoint of the column gutter for a 2-column page, or None.
    """
    if len(words) < 8 or page_width <= 0:
        return None

    centres = sorted((w["x0"] + w["x1"]) / 2 for w in words)
    lo, hi  = page_width * 0.20, page_width * 0.80
    min_gap = page_width * _COL_GAP_RATIO

    best_mid, best_gap = None, 0.0
    for i in range(1, len(centres)):
        gap = centres[i] - centres[i - 1]
        mid = (centres[i - 1] + centres[i]) / 2
        if lo <= mid <= hi and gap >= min_gap and gap > best_gap:
            best_gap, best_mid = gap, mid

    return best_mid


def _split_words_by_column(
    words: list[dict], split_x: float
) -> tuple[list[dict], list[dict]]:
    """Partition individual word dicts into left/right columns by x-centre."""
    left, right = [], []
    for w in words:
        mid = (w["x0"] + w["x1"]) / 2
        (left if mid < split_x else right).append(w)
    return left, right


# ---------------------------------------------------------------------------
# Paragraph merging
# ---------------------------------------------------------------------------

def _merge_paragraphs(rows: list[_Row], median_size: float) -> list[_Row]:
    """
    Merge consecutive body-text rows separated by less than
    _PARA_MERGE_RATIO × line_height into a single _Row.
    Headings and bullet items are always kept separate.
    The median_sz field on every output row is set to median_size.
    """
    if not rows:
        return []

    result: list[_Row] = []
    buf: list[_Row] = []

    def flush() -> None:
        if not buf:
            return
        if len(buf) == 1:
            r = buf[0]
            r.median_sz = median_size
            result.append(r)
        else:
            result.append(_Row(
                text      = " ".join(r.text for r in buf),
                size      = statistics.mean(r.size for r in buf),
                y_top     = buf[0].y_top,
                y_bot     = buf[-1].y_bot,
                x_left    = min(r.x_left  for r in buf),
                x_right   = max(r.x_right for r in buf),
                is_bullet = False,
                median_sz = median_size,
            ))
        buf.clear()

    for row in rows:
        tag = _classify_size(row.size, median_size)

        if tag != "p" or row.is_bullet:
            flush()
            row.median_sz = median_size
            result.append(row)
            continue

        if buf:
            prev   = buf[-1]
            line_h = max(prev.y_bot - prev.y_top, 4.0)
            gap    = row.y_top - prev.y_bot
            if gap > line_h * _PARA_MERGE_RATIO:
                flush()

        buf.append(row)

    flush()
    return result


# ---------------------------------------------------------------------------
# Table → HTML
# ---------------------------------------------------------------------------

def _table_html(table_rows: list[list[str | None]]) -> str:
    if not table_rows:
        return ""

    def cell(v: str | None) -> str:
        return escape((v or "").strip())

    def _row_is_empty(row: list[str | None]) -> bool:
        return all(not (c or "").strip() for c in row)

    # Drop rows where every cell is blank (unfilled form fields)
    non_empty = [r for r in table_rows if not _row_is_empty(r)]
    if len(non_empty) < _TABLE_MIN_ROWS:
        return ""

    parts = ["<table>"]
    for ri, row in enumerate(non_empty):
        if ri == 0:
            parts.append("<thead><tr>")
            parts += [f"<th>{cell(c)}</th>" for c in row]
            parts.append("</tr></thead>")
            if len(non_empty) > 1:
                parts.append("<tbody>")
        else:
            parts.append("<tr>")
            parts += [f"<td>{cell(c)}</td>" for c in row]
            parts.append("</tr>")
    if len(non_empty) > 1:
        parts.append("</tbody>")
    parts.append("</table>")
    return "\n".join(parts)


# ---------------------------------------------------------------------------
# Per-page processing
# ---------------------------------------------------------------------------

def _process_page(page) -> list[tuple[float, _Row | _Table]]:
    """
    Return (sort_y, item) pairs for all semantic items on the page.

    Key fix: median_sz is computed from ALL words on the page (before table
    exclusion) so it reflects the true body-text baseline, not a biased sample.
    """
    pw = float(page.width)
    ph = float(page.height)

    # ── 1. Extract ALL words first (needed for unbiased median) ─────────────
    all_words = page.extract_words(extra_attrs=["size"])
    all_sizes = [w["size"] for w in all_words if w.get("size", 0) >= 4]
    median_sz = statistics.median(all_sizes) if all_sizes else 12.0

    # ── 2. Detect and extract tables ─────────────────────────────────────────
    tables:     list[_Table] = []
    tbl_bboxes: list[tuple]  = []

    for tbl in page.find_tables():
        data = tbl.extract()
        non_empty = [r for r in (data or []) if any(c and c.strip() for c in r)]
        if len(non_empty) >= _TABLE_MIN_ROWS:
            tbl_bboxes.append(tbl.bbox)
            tables.append(_Table(rows=data, y_top=tbl.bbox[1]))

    # ── 3. Filter words that fall inside table regions ───────────────────────
    words = all_words
    if tbl_bboxes:
        words = [
            w for w in words
            if not any(
                _point_in_bbox(
                    (w["x0"] + w["x1"]) / 2,
                    (w["top"] + w["bottom"]) / 2,
                    bb,
                )
                for bb in tbl_bboxes
            )
        ]

    # ── 4. Column detection on non-table words ───────────────────────────────
    split_x = _detect_column_split(words, pw)

    if split_x:
        left_words, right_words = _split_words_by_column(words, split_x)
        left_raw  = _words_to_rows(left_words)
        right_raw = _words_to_rows(right_words)

        left  = _merge_paragraphs(sorted(left_raw,  key=lambda r: r.y_top), median_sz)
        right = _merge_paragraphs(sorted(right_raw, key=lambda r: r.y_top), median_sz)

        text_items: list[tuple[float, _Row]] = (
            [(r.y_top,      r) for r in left] +
            [(ph + r.y_top, r) for r in right]
        )
    else:
        raw    = _words_to_rows(words)
        merged = _merge_paragraphs(sorted(raw, key=lambda r: r.y_top), median_sz)
        text_items = [(r.y_top, r) for r in merged]

    # ── 5. Interleave tables and text, sorted by y ───────────────────────────
    all_items: list[tuple[float, _Row | _Table]] = (
        [(t.y_top, t) for t in tables] + text_items
    )
    return sorted(all_items, key=lambda x: x[0])


# ---------------------------------------------------------------------------
# HTML body assembly
# ---------------------------------------------------------------------------

def _build_body(
    page_items: list[tuple[float, _Row | _Table]],
) -> str:
    """
    Emit HTML for all items.  Each _Row carries its own median_sz so heading
    classification uses the per-page baseline, not a global average.
    """
    parts:    list[str] = []
    list_buf: list[str] = []

    def flush_list() -> None:
        if list_buf:
            lis = "".join(f"<li>{escape(t)}</li>" for t in list_buf)
            parts.append(f"<ul>{lis}</ul>")
            list_buf.clear()

    for _, item in page_items:

        if isinstance(item, _Table):
            flush_list()
            html = _table_html(item.rows)
            if html:
                parts.append(html)
            continue

        row: _Row = item
        text = row.text.strip()
        if not text:
            continue

        if row.is_bullet:
            list_buf.append(_strip_bullet(text))
            continue

        flush_list()
        tag = _classify_size(row.size, row.median_sz)

        # Suppress orphan body-text fragments (page numbers, cut-off words)
        if tag == "p" and len(text) < _ORPHAN_MIN_CHARS:
            continue

        parts.append(f"<{tag}>{escape(text)}</{tag}>")

    flush_list()
    return "\n".join(parts)


# ---------------------------------------------------------------------------
# CSS / HTML shell
# ---------------------------------------------------------------------------

_CSS = """\
    body {
      font-family: Georgia, serif;
      font-size: 1rem;
      line-height: 1.6;
      max-width: 780px;
      margin: 2rem auto;
      padding: 0 1rem;
      color: #111;
    }
    h1 { font-size: 2rem;   margin: 1.2rem 0 0.4rem; }
    h2 { font-size: 1.5rem; margin: 1rem 0 0.35rem;  }
    h3 { font-size: 1.2rem; margin: 0.8rem 0 0.3rem; }
    p  { margin: 0.5rem 0; }
    ul { margin: 0.5rem 0 0.5rem 2rem; padding: 0; list-style: disc; }
    li { margin: 0.25rem 0; }
    table { border-collapse: collapse; width: 100%; margin: 1rem 0; font-size: 0.95rem; }
    th, td { border: 1px solid #ccc; padding: 0.4rem 0.6rem; text-align: left; vertical-align: top; }
    th { background: #f0f0f0; font-weight: bold; }"""


def _wrap(title: str, body: str) -> str:
    return (
        "<!DOCTYPE html>\n"
        '<html lang="en">\n'
        "<head>\n"
        '  <meta charset="utf-8"/>\n'
        '  <meta name="viewport" content="width=device-width,initial-scale=1"/>\n'
        f"  <title>{escape(title)}</title>\n"
        f"  <style>\n{_CSS}\n  </style>\n"
        "</head>\n"
        "<body>\n"
        "<article>\n"
        f"{body}\n"
        "</article>\n"
        "</body>\n"
        "</html>"
    )


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

def convert(pdf_path: str | Path, title: str | None = None) -> str:
    """
    Convert a PDF to a fluid semantic HTML5 document.

    Handles:
      - Tables     → <table><thead><tbody><tr><th>/<td>  (empty rows suppressed)
      - 2-column   → left-column-first reading order
      - Wrapped    → merged into single <p>
      - Headings   → h1/h2/h3 by per-page font-size ratio
      - Bullets    → <ul><li>
      - Orphans    → very short <p> fragments suppressed
      - No <img>   → text-only, zero image fallbacks
    """
    pdf_path  = Path(pdf_path)
    doc_title = title or pdf_path.stem

    all_items: list[tuple[float, _Row | _Table]] = []
    y_offset = 0.0

    with pdfplumber.open(pdf_path) as pdf:
        for page in pdf.pages:
            ph    = float(page.height)
            items = _process_page(page)
            for y, item in items:
                all_items.append((y_offset + y, item))
            y_offset += ph * 2

    if not all_items:
        return _wrap(doc_title, "<p>No text content found in this document.</p>")

    body = _build_body(all_items)
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
