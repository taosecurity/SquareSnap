"""Draws the SquareSnap icon (dark rounded tile, white square with corner brackets) as PNG and multi-size ICO.

Run: python -I tools\\make_icon.py  (needs Pillow). Writes src/SquareSnap.App/Assets/squaresnap.png and .ico.
With --linux it writes only the hicolor PNGs in packaging/linux/icons (the Linux .desktop icon).
"""
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "src", "SquareSnap.App", "Assets")


def draw(size: int) -> Image.Image:
    s = size * 4  # draw large, then shrink for smooth edges
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=s * 0.22, fill=(30, 33, 48, 255))

    m = s * 0.24          # margin to the bracket square
    L = s * 0.17          # bracket arm length
    w = max(4, int(s * 0.075))
    white = (255, 255, 255, 255)
    for (x, y, dx, dy) in [(m, m, 1, 1), (s - m, m, -1, 1), (m, s - m, 1, -1), (s - m, s - m, -1, -1)]:
        d.line([(x, y), (x + dx * L, y)], fill=white, width=w)
        d.line([(x, y), (x, y + dy * L)], fill=white, width=w)
        d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=white)  # round the corner joint
    inner = s * 0.38
    d.rectangle([inner, inner, s - inner, s - inner], fill=(86, 156, 255, 255))
    return img.resize((size, size), Image.LANCZOS)


LINUX_ICONS = os.path.join(HERE, "..", "packaging", "linux", "icons", "hicolor")
LINUX_SIZES = (16, 22, 24, 32, 48, 64, 128, 256, 512)


def linux_icons() -> int:
    """hicolor PNGs for the Linux .desktop entry (step 4, 2026-11-13, as in BejView), each size drawn on its own for sharp edges."""
    for n in LINUX_SIZES:
        d = os.path.join(LINUX_ICONS, f"{n}x{n}", "apps")
        os.makedirs(d, exist_ok=True)
        draw(n).save(os.path.join(d, "squaresnap.png"))
    print("wrote", os.path.normpath(LINUX_ICONS), "sizes", ", ".join(map(str, LINUX_SIZES)))
    return 0


def main() -> int:
    if "--linux" in sys.argv[1:]:
        return linux_icons()
    os.makedirs(ASSETS, exist_ok=True)
    big = draw(256)
    big.save(os.path.join(ASSETS, "squaresnap.png"))
    big.save(os.path.join(ASSETS, "squaresnap.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("wrote", os.path.normpath(os.path.join(ASSETS, "squaresnap.png")), "and .ico")
    return 0


if __name__ == "__main__":
    sys.exit(main())
