"""Erzeugt das Programmsymbol `packaging/ailight.ico` (reproduzierbar).

Die Marke: ein leuchtender Punkt oben, daraus gehen drei Strahlen nach unten.
Die beiden äußeren Strahlen mit dem Querstrich dazwischen bilden ein **A**, der
mittlere Strahl ist das **I** — „AI“ als ein einziges Bild, aus dem Namen
AIlight. Auf einer abgerundeten Fläche in der Akzentfarbe; dieselbe Marke
zeichnet das Programm notfalls selbst (Core/Icons.cs) und als Vektorgrafik
liegt sie in Views/Theme.xaml.

Aufruf:
    python tools/make_icon.py            # packaging/ailight.ico + docs/bilder/logo.png
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

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "packaging" / "ailight.ico"
LOGO = ROOT / "docs" / "bilder" / "logo.png"

# ── Die Marke in einem Raster von 100 × 100 ────────────────────────────────
# Damit Vektorgrafik (Theme.xaml), Notfallzeichnung (Icons.cs) und dieses
# Symbol dieselbe Form haben, stehen die Maße nur hier.
POINT = (50.0, 14.0, 9.5)       # Lichtpunkt: Mitte x, Mitte y, Radius
STROKE = 11.0                    # Strichstärke der Strahlen
RAYS = [
    ((50.0, 32.0), (50.0, 90.0)),   # mittlerer Strahl — das I
    ((44.0, 32.0), (22.0, 90.0)),   # linker Schenkel des A
    ((56.0, 32.0), (78.0, 90.0)),   # rechter Schenkel des A
    ((30.0, 68.0), (70.0, 68.0)),   # Querstrich zwischen den Schenkeln
]


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

    # Das Raster von 100 × 100 in die Fläche übertragen. Kleine Größen bekommen
    # eine etwas größere Marke, sonst verschwinden die Zwischenräume.
    box = s * (0.70 if small else 0.64)
    origin = (s - box) / 2.0
    stroke = box * (STROKE / 100.0) * (1.12 if small else 1.0)

    def point(x: float, y: float) -> tuple[float, float]:
        return (origin + (x / 100.0 * box), origin + (y / 100.0 * box))

    # ── Die Strahlen: runde Enden, damit die Marke freundlich bleibt ───────
    mark = Image.new("L", (s, s), 0)
    draw = ImageDraw.Draw(mark)
    cap = stroke / 2.0
    for start, end in RAYS:
        x1, y1 = point(*start)
        x2, y2 = point(*end)
        draw.line([(x1, y1), (x2, y2)], fill=255, width=max(2, int(round(stroke))))
        for x, y in ((x1, y1), (x2, y2)):
            draw.ellipse([x - cap, y - cap, x + cap, y + cap], fill=255)

    # Nur ein Hauch Weichzeichnen: die Marke soll klar bleiben, nicht leuchten.
    mark = mark.filter(ImageFilter.GaussianBlur(s * (0.004 if small else 0.003)))
    # Alles Licht bleibt innerhalb der Fläche — sonst franst die Marke am
    # Rand des Symbols aus und wird hart abgeschnitten.
    mark = ImageChops.multiply(mark, face)
    image.paste(Image.new("RGBA", (s, s), (*CORE, 245)), (0, 0), mark)

    # ── Der Lichtpunkt (die Quelle) mit weichem Hof ────────────────────────
    point_x, point_y = point(POINT[0], POINT[1])
    point_r = box * (POINT[2] / 100.0)

    if not small:
        halo = Image.new("L", (s, s), 0)
        ImageDraw.Draw(halo).ellipse(
            [point_x - point_r * 2.4, point_y - point_r * 2.4,
             point_x + point_r * 2.4, point_y + point_r * 2.4],
            fill=110)
        halo = ImageChops.multiply(halo.filter(ImageFilter.GaussianBlur(s * 0.045)), face)
        image.paste(Image.new("RGBA", (s, s), (*HALO, 255)), (0, 0), halo)

    orb = Image.new("L", (s, s), 0)
    ImageDraw.Draw(orb).ellipse(
        [point_x - point_r, point_y - point_r, point_x + point_r, point_y + point_r],
        fill=255)
    orb = ImageChops.multiply(orb.filter(ImageFilter.GaussianBlur(s * 0.006)), face)
    image.paste(Image.new("RGBA", (s, s), (*CORE, 255)), (0, 0), orb)

    return image.resize((size, size), Image.LANCZOS)


def write_logo() -> None:
    """Dieselbe Marke als Bild für das README — aus der 256-px-Ebene."""
    LOGO.parent.mkdir(parents=True, exist_ok=True)
    render(256).save(LOGO, format="PNG", optimize=True)
    print(f"{LOGO} geschrieben ({LOGO.stat().st_size / 1024:.1f} kB)")


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
    write_logo()
    return 0


if __name__ == "__main__":
    sys.exit(main())
