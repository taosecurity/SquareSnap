"""Explains a pixelcheck DIFFERENT result: which pixels differ, in which channel, and the PNG details of each file.

Run: python -I tools\\pixeldiff.py <pixelcheck folder>   (needs Pillow)
Reads method-1.0-before.png and squaresnap-2.0.png with Pillow, which decodes PNG independently of
Windows (GDI+) and of SquareSnap's own library (Skia).
"""
import collections
import os
import struct
import sys
import zlib

from PIL import Image


def png_info(path):
    """IHDR fields and chunk names, read straight from the file."""
    data = open(path, "rb").read()
    chunks, i = [], 8
    ihdr = None
    while i + 8 <= len(data):
        length = struct.unpack(">I", data[i:i + 4])[0]
        name = data[i + 4:i + 8].decode("ascii", "replace")
        body = data[i + 8:i + 8 + length]
        if name == "IHDR":
            w, h, depth, ctype = struct.unpack(">IIBB", body[:10])
            ihdr = dict(width=w, height=h, bit_depth=depth, colour_type={0: "grey", 2: "RGB", 3: "palette", 4: "grey+alpha", 6: "RGBA"}.get(ctype, ctype))
        if name == "sBIT":
            chunks.append(f"sBIT{tuple(body)}")
        else:
            chunks.append(name)
        i += 12 + length
    return ihdr, chunks


def main(folder):
    a_path = os.path.join(folder, "method-1.0-before.png")
    b_path = os.path.join(folder, "squaresnap-2.0.png")
    for p in (a_path, b_path):
        ihdr, chunks = png_info(p)
        print(f"{os.path.basename(p)}: {ihdr}; chunks {', '.join(c for c in chunks if c != 'IDAT')} (+{chunks.count('IDAT')} IDAT)")

    a = Image.open(a_path).convert("RGB")
    b = Image.open(b_path).convert("RGB")
    pa, pb = a.load(), b.load()
    w, h = a.size
    diffs = []
    for y in range(h):
        for x in range(w):
            if pa[x, y] != pb[x, y]:
                diffs.append((x, y, pa[x, y], pb[x, y]))
    print(f"Pillow: {len(diffs)} of {w * h} pixels differ")
    if not diffs:
        return 0

    per_channel = collections.Counter()
    deltas = collections.Counter()
    values = collections.Counter()
    for x, y, ca, cb in diffs:
        for ch, name in enumerate("RGB"):
            if ca[ch] != cb[ch]:
                per_channel[name] += 1
                deltas[cb[ch] - ca[ch]] += 1
                values[(name, ca[ch], cb[ch])] += 1
    print("channels that differ:", dict(per_channel))
    print("2.0 minus 1.0:", dict(deltas))
    print("most common (channel, 1.0 value, 2.0 value):", values.most_common(12))
    print("first differing pixels (x, y, 1.0 RGB, 2.0 RGB):")
    for d in diffs[:10]:
        print("  ", d)
    # Are the differing pixels transparent-ish in 1.0's file? (alpha would point at premultiplication)
    a_rgba = Image.open(a_path).convert("RGBA").load()
    alphas = collections.Counter(a_rgba[x, y][3] for x, y, _, _ in diffs)
    print("alpha of those pixels in the 1.0 file:", dict(alphas))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
