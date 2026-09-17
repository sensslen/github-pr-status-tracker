#!/usr/bin/env python3
"""Regenerates the platform icon files from the app's own window icon.

    python packaging/icon/make-icons.py

Run it when src/PrStatusTracker/Assets/github.png changes; the results are committed so a
build needs neither Python nor Pillow.
"""
import io
import struct
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "src/PrStatusTracker/Assets/github.png"
OUT = ROOT / "packaging/icon"

# The OSTypes macOS reads a PNG from, and the pixel size each one means. 1024 is left out
# rather than upscaled past the source.
ICNS_TYPES = {
    "ic07": 128,
    "ic08": 256,
    "ic09": 512,
    "ic11": 32,
    "ic12": 64,
    "ic13": 256,
    "ic14": 512,
}

source = Image.open(SOURCE).convert("RGBA")

source.save(OUT / "AppIcon.ico", sizes=[(s, s) for s in (16, 32, 48, 64, 128, 256)])

chunks = b""
for ostype, size in ICNS_TYPES.items():
    png = io.BytesIO()
    source.resize((size, size), Image.LANCZOS).save(png, format="PNG")
    chunks += ostype.encode() + struct.pack(">I", len(png.getvalue()) + 8) + png.getvalue()
(OUT / "AppIcon.icns").write_bytes(b"icns" + struct.pack(">I", len(chunks) + 8) + chunks)

print(f"wrote {OUT / 'AppIcon.ico'} and {OUT / 'AppIcon.icns'}")
