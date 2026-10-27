#!/usr/bin/env python3
import struct
import sys

PRIME1, PRIME2, PRIME3, PRIME4, PRIME5 = 0x9E3779B1, 0x85EBCA77, 0xC2B2AE3D, 0x27D4EB2F, 0x165667B1
MASK = 0xFFFFFFFF


def _rotl(value: int, bits: int) -> int:
    return ((value << bits) | (value >> (32 - bits))) & MASK


def _round(seed: int, value: int) -> int:
    return (_rotl((seed + value * PRIME2) & MASK, 13) * PRIME1) & MASK


def game_hash(text: str) -> int:
    data = text.encode("ascii")
    length = len(data)
    pos = 0
    h32 = 0x178A54A4
    if length >= 16:
        lanes = [0x2557311B, 0x871FB76A, 0x0133ECF3, 0x62FC7342]
        while True:
            for lane in range(4):
                lanes[lane] = _round(lanes[lane], struct.unpack_from("<I", data, pos + 4 * lane)[0])
            pos += 16
            if length - pos <= 16:
                break
        h32 = (_rotl(lanes[0], 1) + _rotl(lanes[1], 7) + _rotl(lanes[2], 12) + _rotl(lanes[3], 18)) & MASK
    h32 = (h32 + length) & MASK
    while length - pos >= 4:
        h32 = (_rotl((h32 + struct.unpack_from("<I", data, pos)[0] * PRIME3) & MASK, 17) * PRIME4) & MASK
        pos += 4
    while pos < length:
        h32 = (_rotl((h32 + data[pos] * PRIME5) & MASK, 11) * PRIME1) & MASK
        pos += 1
    h32 ^= h32 >> 15
    h32 = (h32 * PRIME2) & MASK
    h32 ^= h32 >> 13
    h32 = (h32 * PRIME3) & MASK
    h32 ^= h32 >> 16
    return h32


h = game_hash


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit("usage: gbhash.py TEXT [TEXT ...]")
    for text in sys.argv[1:]:
        print(f"{game_hash(text):08x}  {text}")
