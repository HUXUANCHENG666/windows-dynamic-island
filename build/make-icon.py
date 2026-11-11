#!/usr/bin/env python3
"""Draw a tray/exe icon: black pill, white edge, four bars. Pure stdlib PNG-in-ICO."""

import math
import struct
import zlib
from pathlib import Path


def sd_round_box(px, py, left, top, right, bottom, radius):
    cx = (left + right) * 0.5
    cy = (top + bottom) * 0.5
    bx = (right - left) * 0.5 - radius
    by = (bottom - top) * 0.5 - radius
    qx = abs(px - cx) - bx
    qy = abs(py - cy) - by
    return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - radius


def pixel(size, x, y):
    # Sample the center of the pixel.
    px = x + 0.5
    py = y + 0.5
    m = size * 0.14
    pill = sd_round_box(px, py, m, size * 0.34, size - m, size * 0.66, size * 0.16)
    # White rim so the glyph survives both light and dark trays.
    if pill < -1.15:
        # Four waveform bars inside the pill.
        bars = 4
        gap = size * 0.035
        bw = size * 0.045
        total = bars * bw + (bars - 1) * gap
        x0 = (size - total) * 0.5
        for i in range(bars):
            h = size * (0.10 + 0.08 * (1 if i in (0, 3) else 1.7))
            left = x0 + i * (bw + gap)
            bar = sd_round_box(px, py, left, (size - h) * 0.5, left + bw, (size + h) * 0.5, bw * 0.5)
            if bar < 0:
                return (255, 255, 255, 255)
        return (8, 8, 8, 255)
    if pill < 0.9:
        t = max(0.0, min(1.0, 0.5 - pill / 1.8))
        return (255, 255, 255, int(255 * t))
    return (0, 0, 0, 0)


def png(size):
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        for x in range(size):
            raw.extend(pixel(size, x, y))
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")


def ico(pngs):
    count = len(pngs)
    header = struct.pack("<HHH", 0, 1, count)
    entries = bytearray()
    offset = 6 + 16 * count
    blobs = []
    for size, blob in pngs:
        w = 0 if size >= 256 else size
        h = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
        blobs.append(blob)
    return header + bytes(entries) + b"".join(blobs)


def main():
    sizes = [16, 20, 24, 32, 48, 64, 256]
    images = [(s, png(s)) for s in sizes]
    out = Path(__file__).resolve().parents[1] / "src" / "DynamicIsland" / "Assets" / "island.ico"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(ico(images))
    print(out, out.stat().st_size)


if __name__ == "__main__":
    main()
