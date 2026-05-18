"""
pdf_to_image.py — PDF to raster/vector image converter using PyMuPDF.

Usage:
    pdf_to_image.py <input.pdf> <output> <format> [dpi]

<format>  : png | jpg | svg
<output>  : destination file path
            - Single-page PDF → image written directly to <output>
            - Multi-page PDF  → ZIP archive written to <output>,
              containing  page_001.<ext>  page_002.<ext>  …
<dpi>     : render resolution for png/jpg (default 150)

Requires: pymupdf  (pip install pymupdf)
"""

from __future__ import annotations

import io
import sys
import zipfile
from pathlib import Path

try:
    import fitz  # PyMuPDF
except ImportError:
    print(
        "ERROR: pymupdf is not installed.\n"
        "Install it with:  pip install pymupdf",
        file=sys.stderr,
    )
    sys.exit(1)


def _render_png(page, dpi: int) -> bytes:
    zoom = dpi / 72.0
    mat  = fitz.Matrix(zoom, zoom)
    pix  = page.get_pixmap(matrix=mat, colorspace=fitz.csRGB, alpha=False)
    return pix.tobytes("png")


def _render_jpg(page, dpi: int) -> bytes:
    zoom = dpi / 72.0
    mat  = fitz.Matrix(zoom, zoom)
    pix  = page.get_pixmap(matrix=mat, colorspace=fitz.csRGB, alpha=False)
    return pix.tobytes("jpeg", jpg_quality=92)


def _render_svg(page, _dpi: int) -> bytes:
    return page.get_svg_image().encode("utf-8")


_RENDERERS = {
    "png": (_render_png, "png"),
    "jpg": (_render_jpg, "jpg"),
    "svg": (_render_svg, "svg"),
}


def convert(pdf_path: str | Path, output_path: str | Path, fmt: str, dpi: int = 150) -> None:
    fmt = fmt.lower().strip()
    if fmt == "jpeg":
        fmt = "jpg"
    if fmt not in _RENDERERS:
        print(f"ERROR: unsupported format '{fmt}'. Use png, jpg, or svg.", file=sys.stderr)
        sys.exit(1)

    render_fn, ext = _RENDERERS[fmt]
    pdf_path    = Path(pdf_path)
    output_path = Path(output_path)

    doc = fitz.open(str(pdf_path))
    try:
        n = len(doc)
        if n == 1:
            output_path.write_bytes(render_fn(doc[0], dpi))
        else:
            buf = io.BytesIO()
            with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as zf:
                for i, page in enumerate(doc, start=1):
                    zf.writestr(f"page_{i:03d}.{ext}", render_fn(page, dpi))
            output_path.write_bytes(buf.getvalue())
    finally:
        doc.close()

    print(f"Written → {output_path}")


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print(
            "Usage: pdf_to_image.py <input.pdf> <output> <format> [dpi]",
            file=sys.stderr,
        )
        sys.exit(1)

    src  = sys.argv[1]
    dst  = sys.argv[2]
    fmt  = sys.argv[3]
    dpi  = int(sys.argv[4]) if len(sys.argv) > 4 else 150

    convert(src, dst, fmt, dpi)
