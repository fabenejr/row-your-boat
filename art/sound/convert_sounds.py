"""
Converts the rowing sound sources into the files embedded in the mod's DLL.

Run from the repo root (Python 3, standard library only):
    python3 art/sound/convert_sounds.py

Reads art/sound/*.wav (the sources as recorded, any PCM bit depth and channel count) and writes
VikingOarsmen/Sounds/<name>.wav as 16-bit mono PCM at the source's sample rate, the only format ModSounds
reads. Mono because the sounds are played positionally in 3D, where Unity folds stereo to mono anyway;
16 bit keeps them small inside the DLL.
"""

import os
import struct
import sys

SOURCE_DIR = os.path.join("art", "sound")
OUTPUT_DIR = os.path.join("VikingOarsmen", "Sounds")


def read_wav(path):
    """Returns (sample_rate, channels, frames) with frames as lists of floats in -1..1."""
    with open(path, "rb") as f:
        data = f.read()
    if data[0:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError(f"{path}: not a RIFF/WAVE file")

    fmt = None
    samples = None
    pos = 12
    while pos + 8 <= len(data):
        chunk_id = data[pos:pos + 4]
        size = struct.unpack("<I", data[pos + 4:pos + 8])[0]
        body = data[pos + 8:pos + 8 + size]
        if chunk_id == b"fmt ":
            fmt = struct.unpack("<HHIIHH", body[:16])
        elif chunk_id == b"data":
            samples = body
        # Chunks are padded to an even size.
        pos += 8 + size + (size & 1)

    if fmt is None or samples is None:
        raise ValueError(f"{path}: missing fmt or data chunk")
    audio_format, channels, rate, _, block_align, bits = fmt
    # 0xFFFE = WAVE_FORMAT_EXTENSIBLE, which Reaper may use for the same integer PCM.
    if audio_format not in (1, 0xFFFE) or bits not in (16, 24, 32):
        raise ValueError(f"{path}: only integer PCM is supported (format {audio_format}, {bits} bit)")

    width = bits // 8
    scale = float(1 << (bits - 1))
    frames = []
    for start in range(0, len(samples) - block_align + 1, block_align):
        frame = []
        for channel in range(channels):
            offset = start + channel * width
            raw = samples[offset:offset + width]
            value = int.from_bytes(raw, "little", signed=True)
            frame.append(value / scale)
        frames.append(frame)
    return rate, channels, frames


def write_wav16_mono(path, rate, values):
    pcm = bytearray()
    for value in values:
        pcm += struct.pack("<h", max(-32768, min(32767, round(value * 32767))))
    header = b"RIFF" + struct.pack("<I", 36 + len(pcm)) + b"WAVE"
    header += b"fmt " + struct.pack("<IHHIIHH", 16, 1, 1, rate, rate * 2, 2, 16)
    header += b"data" + struct.pack("<I", len(pcm))
    with open(path, "wb") as f:
        f.write(header + pcm)


def main():
    if not os.path.isdir(SOURCE_DIR):
        sys.exit(f"Run from the repo root ({SOURCE_DIR} not found).")
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    for name in sorted(os.listdir(SOURCE_DIR)):
        if not name.lower().endswith(".wav"):
            continue
        rate, channels, frames = read_wav(os.path.join(SOURCE_DIR, name))
        mono = [sum(frame) / channels for frame in frames]
        out = os.path.join(OUTPUT_DIR, name)
        write_wav16_mono(out, rate, mono)
        print(f"{name}: {channels} ch -> mono, {len(mono) / rate:.2f} s, {os.path.getsize(out) // 1024} KB")


if __name__ == "__main__":
    main()
