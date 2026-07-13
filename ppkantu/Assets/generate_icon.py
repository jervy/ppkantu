#!/usr/bin/env python3
from pathlib import Path
import math, struct, zlib, binascii

ROOT = Path(__file__).resolve().parent
ASSETS = ROOT
SIZES = [16, 24, 32, 48, 64, 128, 256]


def clamp(v):
    return max(0, min(255, int(round(v))))


def mix(a, b, t):
    return tuple(clamp(a[i] * (1 - t) + b[i] * t) for i in range(4))


def over(dst, src):
    sr, sg, sb, sa = src
    if sa <= 0:
        return dst
    if sa >= 255:
        return (sr, sg, sb, 255)
    dr, dg, db, da = dst
    a = sa / 255.0
    ia = 1.0 - a
    outa = a + (da / 255.0) * ia
    if outa <= 0:
        return (0, 0, 0, 0)
    r = (sr * a + dr * (da / 255.0) * ia) / outa
    g = (sg * a + dg * (da / 255.0) * ia) / outa
    b = (sb * a + db * (da / 255.0) * ia) / outa
    return (clamp(r), clamp(g), clamp(b), clamp(outa * 255))


def point_in_round_rect(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = x0 + r if x < x0 + r else x1 - r if x > x1 - r else x
    cy = y0 + r if y < y0 + r else y1 - r if y > y1 - r else y
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def draw_round_rect(px, x0, y0, x1, y1, r, color):
    h, w = len(px), len(px[0])
    for y in range(max(0, int(y0)), min(h, int(math.ceil(y1)) + 1)):
        for x in range(max(0, int(x0)), min(w, int(math.ceil(x1)) + 1)):
            if point_in_round_rect(x + .5, y + .5, x0, y0, x1, y1, r):
                px[y][x] = over(px[y][x], color)


def draw_circle(px, cx, cy, r, color):
    h, w = len(px), len(px[0])
    r2 = r * r
    for y in range(max(0, int(cy - r - 1)), min(h, int(cy + r + 2))):
        for x in range(max(0, int(cx - r - 1)), min(w, int(cx + r + 2))):
            if (x + .5 - cx) ** 2 + (y + .5 - cy) ** 2 <= r2:
                px[y][x] = over(px[y][x], color)


def draw_poly(px, poly, color):
    h, w = len(px), len(px[0])
    xs = [p[0] for p in poly]
    ys = [p[1] for p in poly]
    for y in range(max(0, int(min(ys)) - 1), min(h, int(max(ys)) + 2)):
        for x in range(max(0, int(min(xs)) - 1), min(w, int(max(xs)) + 2)):
            inside = False
            j = len(poly) - 1
            py = y + .5
            px0 = x + .5
            for i in range(len(poly)):
                xi, yi = poly[i]
                xj, yj = poly[j]
                if ((yi > py) != (yj > py)) and (px0 < (xj - xi) * (py - yi) / ((yj - yi) or 1e-9) + xi):
                    inside = not inside
                j = i
            if inside:
                px[y][x] = over(px[y][x], color)


def draw_line(px, x0, y0, x1, y1, width, color):
    h, w = len(px), len(px[0])
    dx, dy = x1 - x0, y1 - y0
    l2 = dx * dx + dy * dy
    rad = width / 2.0
    for y in range(max(0, int(min(y0, y1) - rad - 1)), min(h, int(max(y0, y1) + rad + 2))):
        for x in range(max(0, int(min(x0, x1) - rad - 1)), min(w, int(max(x0, x1) + rad + 2))):
            px0, py0 = x + .5, y + .5
            t = 0 if l2 == 0 else max(0, min(1, ((px0 - x0) * dx + (py0 - y0) * dy) / l2))
            qx, qy = x0 + t * dx, y0 + t * dy
            if (px0 - qx) ** 2 + (py0 - qy) ** 2 <= rad * rad:
                px[y][x] = over(px[y][x], color)


def render(size):
    S = size * 4
    px = [[(0, 0, 0, 0) for _ in range(S)] for __ in range(S)]

    draw_round_rect(px, .12 * S, .16 * S, .88 * S, .92 * S, .20 * S, (0, 0, 0, 70))

    for y in range(S):
        t = y / max(1, S - 1)
        base = mix((76, 145, 255, 255), (118, 83, 255, 255), t)
        for x in range(S):
            if point_in_round_rect(x + .5, y + .5, .08 * S, .06 * S, .92 * S, .88 * S, .20 * S):
                radial = min(1, math.hypot((x / S) - .25, (y / S) - .18) / .85)
                px[y][x] = over(px[y][x], mix((89, 213, 255, 255), base, radial * .65))

    draw_round_rect(px, .21 * S, .24 * S, .79 * S, .72 * S, .075 * S, (255, 255, 255, 236))
    draw_round_rect(px, .255 * S, .30 * S, .745 * S, .665 * S, .045 * S, (32, 48, 83, 255))

    for y in range(int(.30 * S), int(.665 * S)):
        t = (y - .30 * S) / (.365 * S)
        col = mix((69, 191, 255, 255), (30, 87, 196, 255), t)
        for x in range(int(.255 * S), int(.745 * S)):
            if point_in_round_rect(x + .5, y + .5, .255 * S, .30 * S, .745 * S, .665 * S, .045 * S):
                px[y][x] = over(px[y][x], col)

    draw_circle(px, .63 * S, .40 * S, .055 * S, (255, 214, 77, 255))
    draw_poly(px, [(.27 * S, .665 * S), (.44 * S, .48 * S), (.58 * S, .665 * S)], (32, 202, 132, 255))
    draw_poly(px, [(.42 * S, .665 * S), (.58 * S, .53 * S), (.735 * S, .665 * S)], (21, 157, 111, 255))
    draw_poly(px, [(.405 * S, .52 * S), (.44 * S, .48 * S), (.475 * S, .52 * S)], (235, 255, 255, 230))

    draw_line(px, .24 * S, .80 * S, .73 * S, .80 * S, .065 * S, (255, 77, 109, 255))
    draw_poly(px, [(.73 * S, .765 * S), (.86 * S, .80 * S), (.73 * S, .835 * S)], (255, 193, 7, 255))
    draw_poly(px, [(.835 * S, .794 * S), (.885 * S, .80 * S), (.835 * S, .806 * S)], (62, 39, 35, 255))
    draw_circle(px, .25 * S, .80 * S, .04 * S, (255, 255, 255, 230))
    draw_line(px, .24 * S, .18 * S, .39 * S, .18 * S, .018 * S, (255, 255, 255, 120))
    draw_line(px, .20 * S, .22 * S, .30 * S, .22 * S, .014 * S, (255, 255, 255, 90))

    out = []
    for y in range(size):
        row = []
        for x in range(size):
            acc = [0, 0, 0, 0]
            for yy in range(4):
                for xx in range(4):
                    c = px[y * 4 + yy][x * 4 + xx]
                    for k in range(4):
                        acc[k] += c[k]
            row.append(tuple(clamp(v / 16) for v in acc))
        out.append(row)
    return out


def png_bytes(rgba):
    h, w = len(rgba), len(rgba[0])
    raw = b''.join(b'\x00' + b''.join(bytes((r, g, b, a)) for r, g, b, a in row) for row in rgba)
    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', binascii.crc32(t + d) & 0xffffffff)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')


pngs = []
for s in SIZES:
    data = png_bytes(render(s))
    pngs.append((s, data))
    if s in (64, 256):
        (ASSETS / f'AppIcon-{s}.png').write_bytes(data)

header = struct.pack('<HHH', 0, 1, len(pngs))
offset = 6 + 16 * len(pngs)
entries, blobs = [], []
for s, data in pngs:
    b = 0 if s == 256 else s
    entries.append(struct.pack('<BBBBHHII', b, b, 0, 0, 1, 32, len(data), offset))
    blobs.append(data)
    offset += len(data)
(ASSETS / 'AppIcon.ico').write_bytes(header + b''.join(entries) + b''.join(blobs))
print('generated', ASSETS / 'AppIcon.ico')
print('sizes', ','.join(map(str, SIZES)))
