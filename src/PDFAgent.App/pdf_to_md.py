"""
pdf_to_md.py — PDF to Markdown converter.

Reuses the same pdfplumber-based structural extraction as pdf_to_docx.py
and maps each logical block to the closest Markdown element:

  Headings (bold + underline) → ## heading text
  Bold single-line paragraphs → ### heading text
  Body paragraphs             → plain paragraphs separated by blank lines
  Bullet lists                → - item text
  Code/syntax boxes           → fenced code blocks (``` ... ```)
  Side-by-side code boxes     → label line + two consecutive fenced blocks
  Tabular data                → Markdown table (| col | col | ... |)
  Images next to text         → text-only (image is not embeddable in .md)
  Standalone images           → skipped
  Page breaks                 → horizontal rule (---)
"""

from __future__ import annotations

import sys
from pathlib import Path

import pdfplumber

_HERE = Path(__file__).parent
sys.path.insert(0, str(_HERE))

from pdf_to_html import (
    _MIN_TABLE_COLS,
    _assign_col,
    _content_boxes,
    _detect_col_starts,
    _group_into_lines,
    _is_bold,
    _remap_pua,
    _section_rects,
)
from pdf_to_docx import (
    _find_box_pairs,
    _group_into_paragraphs,
    _is_bullet_word,
    _underline_ys,
)


# ---------------------------------------------------------------------------
# Text helpers
# ---------------------------------------------------------------------------

def _words_to_text(words: list[dict]) -> str:
    if not words:
        return ""
    parts: list[str] = []
    prev_x1: float | None = None
    for w in sorted(words, key=lambda x: x["x0"]):
        text = _remap_pua(w["text"], w.get("fontname", ""))
        if prev_x1 is not None and w["x0"] - prev_x1 > 1.0:
            parts.append(" ")
        parts.append(text)
        prev_x1 = w.get("x1", w["x0"])
    return "".join(parts)


def _line_to_text(line: list[dict]) -> str:
    return _words_to_text(line)


def _para_to_text(para_lines: list[list[dict]]) -> str:
    return " ".join(_line_to_text(ln) for ln in para_lines if ln)


# ---------------------------------------------------------------------------
# Per-page processing
# ---------------------------------------------------------------------------

def _process_page(out: list[str], page, is_first: bool) -> None:
    pw = float(page.width)
    ph = float(page.height)

    if not is_first:
        _ensure_blank(out)
        out.append("---")
        out.append("")

    # Footer separator: widest thin horizontal rect in bottom 20%
    footer_y = ph
    for rect in (page.rects or []):
        rh = float(rect["bottom"]) - float(rect["top"])
        rw = float(rect["x1"])    - float(rect["x0"])
        if rh < 2 and rw > pw * 0.5 and float(rect["top"]) > ph * 0.8:
            footer_y = min(footer_y, float(rect["top"]))

    all_words = [
        w for w in page.extract_words(extra_attrs=["size", "fontname"])
        if float(w["top"]) < footer_y
    ]

    boxes = _content_boxes(page)

    box_words: dict[int, list[dict]] = {i: [] for i in range(len(boxes))}
    free_words: list[dict] = []
    for w in all_words:
        wx1 = w.get("x1", w["x0"] + 1)
        wb  = w.get("bottom", w["top"] + 1)
        assigned = False
        for i, box in enumerate(boxes):
            if (box["x0"] - 2 <= w["x0"] and wx1 <= box["x1"] + 2 and
                    box["top"] - 2 <= w["top"] and wb <= box["bottom"] + 2):
                box_words[i].append(w)
                assigned = True
                break
        if not assigned:
            free_words.append(w)

    sections = _section_rects(page, bordered_boxes=boxes)
    table_regions: list[tuple] = []
    for (y_top, y_bot) in sections:
        band = [w for w in free_words if y_top - 1 <= w["top"] <= y_bot + 1]
        if not band:
            continue
        ls = _group_into_lines(band)
        if not ls:
            continue
        cs = _detect_col_starts(ls[0])
        if len(cs) >= _MIN_TABLE_COLS:
            table_regions.append((y_top, y_bot, cs))

    non_table_free = [
        w for w in free_words
        if not any(yt - 1 <= w["top"] <= yb + 1 for (yt, yb, _cs) in table_regions)
    ]

    ul_ys = _underline_ys(page)

    def _near_underline(top: float, bottom: float) -> bool:
        return any(top - 2 <= y <= bottom + 20 for y in ul_ys)

    pairs, singles = _find_box_pairs(boxes)

    def _labels_for_pair(lbox: dict, rbox: dict) -> tuple[str, str]:
        box_top = min(float(lbox["top"]), float(rbox["top"]))
        gap_mid = (float(lbox["x1"]) + float(rbox["x0"])) / 2
        candidates = [
            w for w in free_words
            if 0 < box_top - w["top"] < 35 and
               "symbol" not in w.get("fontname", "").lower()
        ]
        if not candidates:
            return "", ""
        ls = _group_into_lines(candidates)
        if not ls:
            return "", ""
        last_line = ls[-1]
        ll = " ".join(
            _remap_pua(w["text"], w.get("fontname", ""))
            for w in last_line if w["x0"] < gap_mid)
        rl = " ".join(
            _remap_pua(w["text"], w.get("fontname", ""))
            for w in last_line if w["x0"] >= gap_mid)
        return ll, rl

    label_line_ys: set[float] = set()
    for (lbox, rbox) in pairs:
        for w in free_words:
            if 0 < lbox["top"] - w["top"] < 35:
                label_line_ys.add(round(w["top"], 1))
    for box in singles:
        for w in free_words:
            if 0 < box["top"] - w["top"] < 35:
                label_line_ys.add(round(w["top"], 1))

    content_words = [
        w for w in non_table_free
        if round(w["top"], 1) not in label_line_ys
    ]
    content_lines = _group_into_lines(content_words)
    para_groups   = _group_into_paragraphs(content_lines, pw)

    consumed_para_ids: set[int] = set()
    image_events: list[tuple[float, str, object]] = []
    for img in page.images:
        img_top = float(img["top"])
        img_bot = float(img["bottom"])
        img_x1  = float(img["x1"])
        side_paras = [
            pg for pg in para_groups
            if (pg[0][0]["top"] < img_bot + 5 and
                max(w.get("bottom", w["top"] + 12) for ln in pg for w in ln) > img_top - 5 and
                min(w["x0"] for ln in pg for w in ln) > img_x1 - 5)
        ]
        if side_paras:
            for pg in side_paras:
                consumed_para_ids.add(id(pg))
            image_events.append((img_top, "header", (img, side_paras)))
        else:
            image_events.append((img_top, "image", img))

    rendered_table: set = set()
    rendered_box:   set = set()
    rendered_pair:  set = set()

    events: list[tuple[float, str, object]] = list(image_events)
    for (yt, yb, cs) in table_regions:
        events.append((yt, "table", (yt, yb, cs)))
    for pair_idx, (lbox, rbox) in enumerate(pairs):
        events.append((min(lbox["top"], rbox["top"]), "pair", pair_idx))
    for box_idx, box in enumerate(singles):
        events.append((box["top"], "single", box_idx))
    for pg in para_groups:
        events.append((pg[0][0]["top"], "para", pg))
    events.sort(key=lambda e: e[0])

    bullet_buffer: list[list[dict]] = []

    def flush_bullets() -> None:
        if not bullet_buffer:
            return
        _ensure_blank(out)
        for line in bullet_buffer:
            text_words = [w for w in line if not _is_bullet_word(w)]
            out.append(f"- {_words_to_text(text_words)}")
        bullet_buffer.clear()
        out.append("")

    def write_code_block(words: list[dict]) -> None:
        if not words:
            return
        _ensure_blank(out)
        out.append("```")
        for line in _group_into_lines(words):
            out.append(_line_to_text(line))
        out.append("```")
        out.append("")

    def write_table(words_list: list[dict], col_starts: list[float]) -> None:
        from pdf_to_html import _DIGIT_RE
        lines = _group_into_lines(words_list)
        if not lines:
            return
        n = len(col_starts)

        header_lines: list[list[dict]] = []
        data_lines:   list[list[dict]] = []
        in_header = True
        for line in lines:
            col0 = [w for w in line if _assign_col(w["x0"], col_starts) == 0]
            col0_text = " ".join(w["text"] for w in col0)
            if in_header:
                if col0 and _DIGIT_RE.search(col0_text):
                    in_header = False
                    data_lines.append(line)
                else:
                    header_lines.append(line)
            else:
                data_lines.append(line)

        def row_cells(line: list[dict]) -> list[str]:
            return [
                _words_to_text(
                    [w for w in line if _assign_col(w["x0"], col_starts) == ci]
                ).replace("|", "\\|")
                for ci in range(n)
            ]

        _ensure_blank(out)
        if header_lines:
            out.append("| " + " | ".join(row_cells(header_lines[0])) + " |")
            out.append("| " + " | ".join(["---"] * n) + " |")
            for hl in header_lines[1:]:
                out.append("| " + " | ".join(row_cells(hl)) + " |")
        else:
            out.append("| " + " | ".join([""] * n) + " |")
            out.append("| " + " | ".join(["---"] * n) + " |")

        for dl in data_lines:
            out.append("| " + " | ".join(row_cells(dl)) + " |")
        out.append("")

    for (y, kind, data) in events:

        if kind == "header":
            flush_bullets()
            _img, side_paras = data
            for para_lines in side_paras:
                text = _para_to_text(para_lines)
                if text.strip():
                    _ensure_blank(out)
                    out.append(text)
                    out.append("")

        elif kind == "image":
            pass  # standalone images not embeddable as .md without side effects

        elif kind == "table":
            (yt, yb, cs) = data
            if (yt, yb) in rendered_table:
                continue
            rendered_table.add((yt, yb))
            flush_bullets()
            tbl_words = [w for w in free_words if yt - 1 <= w["top"] <= yb + 1]
            if tbl_words:
                write_table(tbl_words, cs)

        elif kind == "pair":
            pair_idx = data
            if pair_idx in rendered_pair:
                continue
            rendered_pair.add(pair_idx)
            flush_bullets()
            lbox, rbox = pairs[pair_idx]
            ll, rl = _labels_for_pair(lbox, rbox)
            l_words = box_words[boxes.index(lbox)]
            r_words = box_words[boxes.index(rbox)]

            if ll or rl:
                _ensure_blank(out)
                if ll and rl:
                    out.append(f"**{ll}** | **{rl}**")
                else:
                    out.append(f"**{ll or rl}**")
                out.append("")

            if l_words:
                out.append("```")
                for line in _group_into_lines(l_words):
                    out.append(_line_to_text(line))
                out.append("```")
                out.append("")
            if r_words:
                out.append("```")
                for line in _group_into_lines(r_words):
                    out.append(_line_to_text(line))
                out.append("```")
                out.append("")

        elif kind == "single":
            box_idx = data
            if box_idx in rendered_box:
                continue
            rendered_box.add(box_idx)
            flush_bullets()
            write_code_block(box_words[boxes.index(singles[box_idx])])

        elif kind == "para":
            pg: list[list[dict]] = data
            if id(pg) in consumed_para_ids:
                continue

            first_line = pg[0]

            if first_line and _is_bullet_word(first_line[0]):
                for line in pg:
                    bullet_buffer.append(line)
                continue

            flush_bullets()

            all_bold = all(_is_bold(w.get("fontname", "")) for w in first_line)
            top_y = first_line[0]["top"]
            bot_y = max(w.get("bottom", w["top"] + 12) for w in first_line)
            is_underlined = _near_underline(top_y, bot_y)

            text = _para_to_text(pg)
            _ensure_blank(out)

            if all_bold and is_underlined:
                out.append(f"## {text}")
            elif all_bold and len(pg) == 1:
                out.append(f"### {text}")
            else:
                out.append(text)
            out.append("")

    flush_bullets()


def _ensure_blank(out: list[str]) -> None:
    if out and out[-1] != "":
        out.append("")


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

def convert(pdf_path: str | Path) -> str:
    """Convert a PDF to Markdown and return the Markdown string."""
    pdf_path = Path(pdf_path)
    raw: list[str] = []

    with pdfplumber.open(pdf_path) as pdf:
        for page_idx, page in enumerate(pdf.pages):
            _process_page(raw, page, is_first=(page_idx == 0))

    # Collapse runs of blank lines to a single blank line
    result: list[str] = []
    prev_blank = False
    for line in raw:
        is_blank = (line == "")
        if is_blank and prev_blank:
            continue
        result.append(line)
        prev_blank = is_blank

    # Strip leading/trailing blank lines
    while result and result[0] == "":
        result.pop(0)
    while result and result[-1] == "":
        result.pop()

    return "\n".join(result) + "\n"


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    import sys as _sys

    if len(_sys.argv) < 2:
        print("Usage: python pdf_to_md.py <input.pdf> [output.md]", file=_sys.stderr)
        _sys.exit(1)

    src = Path(_sys.argv[1])
    dst = Path(_sys.argv[2]) if len(_sys.argv) > 2 else src.with_suffix(".md")
    dst.write_text(convert(src), encoding="utf-8")
    print(f"Written → {dst}")
