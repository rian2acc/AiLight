"""Erzeugt das Programmsymbol `packaging/ailight.ico` (reproduzierbar, ohne Zusatzpakete).

Gezeichnet wird eine abgerundete Fläche in der Akzentfarbe mit einem hellen
„Funkeln“ darin — dieselbe Marke, die das Programm notfalls selbst zeichnet.

Aufruf:
    python tools/make_icon.py            # schreibt packaging/ailight.ico
"""

from __future__ import annotations

import math
import struct
import sys
import zlib
from pathlib import Path

ACCENT_TOP = (0x62, 0x9B, 0xFF)
ACCENT_BOTTOM = (0x3B, 0x74, 0xE8)
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
SUPERSAMPLE = 4

OUT = Path(__file__).resolve().parent.parent / "packaging" / "ailight.ico"


def rounded_square_coverage(x: float, y: float, size: float, radius: float) -> float:
    """1.0 = vollständig innerhalb, 0.0 = außerhalb (weiche Kante wird später gemittelt)."""
    inset = size * 0.045  # kleiner Rand, damit die Rundung atmen kann
    left, top = inset, inset
    right, bottom = size - inset, size - inset
    if x < left or x > right or y < top or y > bottom:
        return 0.0

    cx = min(max(x, left + radius), right - radius)
    cy = min(max(y, top + radius), bottom - radius)
    return 1.0 if math.hypot(x - cx, y - cy) <= radius else 0.0


def sparkle_coverage(x: float, y: float, size: float) -> float:
    """Vierstrahliges Funkeln (Astroide): |u|^0.5 + |v|^0.5 <= 1."""
    center = size / 2
    reach = size * 0.30
    u = abs(x - center) / reach
    v = abs(y - center) / reach
    return 1.0 if math.sqrt(u) + math.sqrt(v) <= 1.0 else 0.0


def build_pixels(size: int) -> bytes:
    """BGRA-Zeilen von unten nach oben (BMP-Reihenfolge innerhalb des ICO)."""
    radius = size * 0.22
    rows = []
    step = 1.0 / SUPERSAMPLE
    offset = step / 2

    for y in range(size - 1, -1, -1):
        row = bytearray()
        for x in range(size):
            inside = 0.0
            sparkle = 0.0
            for sy in range(SUPERSAMPLE):
                for sx in range(SUPERSAMPLE):
                    px = x + offset + sx * step
                    py = y + offset + sy * step
                    if rounded_square_coverage(px, py, size, radius) > 0:
                        inside += 1.0
                        if sparkle_coverage(px, py, size) > 0:
                            sparkle += 1.0

            samples = SUPERSAMPLE * SUPERSAMPLE
            alpha = inside / samples
            if alpha <= 0:
                row += b"\x00\x00\x00\x00"
                continue

            mix = sparkle / samples
            level = y / max(1, size - 1)
            base = (
                int(ACCENT_TOP[0] + (ACCENT_BOTTOM[0] - ACCENT_TOP[0]) * level),
                int(ACCENT_TOP[1] + (ACCENT_BOTTOM[1] - ACCENT_TOP[1]) * level),
                int(ACCENT_TOP[2] + (ACCENT_BOTTOM[2] - ACCENT_TOP[2]) * level),
            )
            color = (
                int(base[0] + (255 - base[0]) * mix),
                int(base[1] + (255 - base[1]) * mix),
                int(base[2] + (255 - base[2]) * mix),
            )
            row += bytes((color[2], color[1], color[0], int(alpha * 255)))

        rows.append(bytes(row))

    return b"".join(rows)


def png_bytes(size: int) -> bytes:
    """Einfacher PNG-Kodierer (RGBA) — kleine Dateien für die großen Größen."""
    pixels = build_pixels(size)
    raw = bytearray()
    stride = size * 4
    for row in range(size):
        raw.append(0)  # Filter „None“
        raw += pixels[row * stride : (row + 1) * stride]

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def bmp_bytes(size: int) -> bytes:
    """BMP-Fassung für die kleinen Größen (klassisches ICO-Format)."""
    pixels = build_pixels(size)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(pixels), 0, 0, 0, 0)
    mask_row = ((size + 31) // 32) * 4
    mask = b"\x00" * (mask_row * size)
    return header + pixels + mask


def main() -> int:
    entries = []
    for size in SIZES:
        entries.append((size, png_bytes(size) if size >= 64 else bmp_bytes(size)))

    header = struct.pack("<HHH", 0, 1, len(entries))
    directory = b""
    offset = len(header) + 16 * len(entries)
    blobs = b""
    for size, blob in entries:
        dimension = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", dimension, dimension, 0, 0, 1, 32, len(blob), offset)
        blobs += blob
        offset += len(blob)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(header + directory + blobs)
    print(f"{OUT} geschrieben ({OUT.stat().st_size / 1024:.1f} kB, {len(entries)} Größen)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
