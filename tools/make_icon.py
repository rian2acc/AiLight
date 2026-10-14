"""Erzeugt das Programmsymbol `packaging/ailight.ico` (reproduzierbar).

Die Marke: ein Lichtstrahl, der aus einem leuchtenden Punkt nach oben
aufgeht — „AIlight“ als Bild, klar unterscheidbar von den üblichen
vierstrahligen KI-Funkeln. Auf einer abgerundeten Fläche in der
Akzentfarbe, dieselbe Marke, die das Programm notfalls selbst zeichnet.

Aufruf:
    python tools/make_icon.py            # schreibt packaging/ailight.ico
"""

from __future__ import annotations

import struct
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

# Dieselben Farben wie das dunkle Leisten-Thema (Theme.xaml).
ACCENT_TOP = (0x6E, 0xA8, 0xFF)
ACCENT_BOTTOM = (0x2F, 0x6B, 0xE0)
CORE = (0xFF, 0xFF, 0xFF)
HALO = (0xE8, 0xF1, 0xFF)

SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
SUPERSAMPLE = 4

OUT = Path(__file__).resolve().parent.parent / "packaging" / "ailight.ico"


def render(size: int) -> Image.Image:
    """Die Marke in voller Größe zeichnen (weiche Kanten über Weichzeichnen)."""
    s = size * SUPERSAMPLE
    small = size <= 24  # kleine Größen brauchen kräftigere Formen

    image = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # ── Abgerundete Fläche mit sanftem Verlauf ─────────────────────────────
    inset = s * 0.045
    radius = s * (0.26 if small else 0.225)
    face = Image.new("L", (s, s), 0)
    ImageDraw.Draw(face).rounded_rectangle(
        [inset, inset, s - inset, s - inset], radius=radius, fill=255)

    gradient = Image.linear_gradient("L").resize((s, s))
    top = Image.new("RGBA", (s, s), (*ACCENT_TOP, 255))
    bottom = Image.new("RGBA", (s, s), (*ACCENT_BOTTOM, 255))
    image.paste(Image.composite(bottom, top, gradient), (0, 0), face)

    center_x = s * 0.5
    orb_y = s * (0.79 if small else 0.80)
    orb_r = s * (0.125 if small else 0.108)

    # ── Lichtstrahl: unten schmal, nach oben offen, nach oben verblassend ──
    beam = Image.new("L", (s, s), 0)
    draw = ImageDraw.Draw(beam)
    top_y = s * (0.155 if small else 0.15)
    bottom_y = orb_y - orb_r * 0.55
    width_top = s * (0.215 if small else 0.20)
    width_bottom = s * (0.052 if small else 0.042)
    draw.polygon(
        [
            (center_x - width_top, top_y),
            (center_x + width_top, top_y),
            (center_x + width_bottom, bottom_y),
            (center_x - width_bottom, bottom_y),
        ],
        fill=255,
    )
    beam = beam.filter(ImageFilter.GaussianBlur(s * (0.014 if small else 0.011)))

    # Der Strahl bleibt bewusst halbtransparent — hell genug, um zu leuchten,
    # aber der Lichtpunkt darunter soll der einzige ganz helle Fleck sein.
    fade = Image.linear_gradient("L").resize((s, s)).point(
        lambda value: int(255 * (0.30 + 0.44 * (value / 255.0))))
    beam = ImageChops.multiply(beam, fade)
    # Alles Licht bleibt innerhalb der Fläche — sonst franst der Schein am
    # Rand des Symbols aus und wird hart abgeschnitten.
    beam = ImageChops.multiply(beam, face)
    image.paste(Image.new("RGBA", (s, s), (*CORE, 255)), (0, 0), beam)

    # ── Lichtpunkt (die Quelle) mit weichem Hof ────────────────────────────
    halo = Image.new("L", (s, s), 0)
    halo_draw = ImageDraw.Draw(halo)
    halo_draw.ellipse(
        [center_x - orb_r * 2.3, orb_y - orb_r * 2.3,
         center_x + orb_r * 2.3, orb_y + orb_r * 2.3],
        fill=120)
    halo = ImageChops.multiply(halo.filter(ImageFilter.GaussianBlur(s * 0.05)), face)
    image.paste(Image.new("RGBA", (s, s), (*HALO, 255)), (0, 0), halo)

    orb = Image.new("L", (s, s), 0)
    ImageDraw.Draw(orb).ellipse(
        [center_x - orb_r, orb_y - orb_r, center_x + orb_r, orb_y + orb_r],
        fill=255)
    orb = ImageChops.multiply(orb.filter(ImageFilter.GaussianBlur(s * 0.008)), face)
    image.paste(Image.new("RGBA", (s, s), (*CORE, 255)), (0, 0), orb)

    return image.resize((size, size), Image.LANCZOS)


def png_bytes(image: Image.Image) -> bytes:
    """PNG direkt aus PIL — kleine Dateien für die großen Größen."""
    import io

    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def bmp_bytes(image: Image.Image) -> bytes:
    """Klassisches ICO-Bitmap (BGRA, Zeilen von unten nach oben)."""
    size = image.size[0]
    rgba = image.convert("RGBA")
    rows = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            r, g, b, a = rgba.getpixel((x, y))
            rows += bytes((b, g, r, a))

    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(rows), 0, 0, 0, 0)
    mask_row = ((size + 31) // 32) * 4
    mask = b"\x00" * (mask_row * size)
    return header + bytes(rows) + mask


def main() -> int:
    entries = []
    for size in SIZES:
        image = render(size)
        entries.append((size, png_bytes(image) if size >= 64 else bmp_bytes(image)))

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
