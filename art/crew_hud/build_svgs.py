"""Builds the crew HUD artwork as SVG: one top-down drawing per ship, the panel frame and the small sprites.

Each ship is drawn to scale, one SVG unit per centimeter, bow up. Hull sizes come from the game's own ships
(the hull meshes, measured with UnityPy) and are repeated in CrewHudArt.cs, which places the rowers on top.
Run it, then open export.html to turn the SVGs into the PNGs embedded in the mod:

    python art/crew_hud/build_svgs.py
"""

import math
import os
import random

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "svg")
CM = 100  # SVG units per meter

# Valheim-like palette: weathered wood, iron, faded paint.
WOOD_DARK = "#2b1a0e"
WOOD = "#6e4526"
WOOD_LIGHT = "#94643a"
DECK = "#a87a49"
DECK_LIGHT = "#c29563"
IRON = "#3a3a3c"
SAIL = "#d9c9a3"
SAIL_STRIPE = "#8e2a1f"
SHIELD_COLORS = ["#8e2a1f", "#d6c39a", "#2e475a", "#b8892c"]


def hull_half_width(t, beam, fullness):
    """Half-width at t (-1 stern .. 1 bow) of a pointed, symmetric hull."""
    return beam / 2 * max(0.0, 1 - t * t) ** fullness


def hull_points(beam, z0, z1, fullness, scale=1.0, inset=0.0, steps=48):
    """Outline of the hull (or a strake inside it) as a closed list of (x, z) points in meters."""
    right, left = [], []
    for i in range(steps + 1):
        t = -1 + 2 * i / steps
        z = z0 + (z1 - z0) * (t + 1) / 2
        w = max(0.0, hull_half_width(t, beam, fullness) * scale - inset)
        right.append((w, z))
        left.append((-w, z))
    return right + left[::-1]


def path(points, close=True):
    """Smooth path through the points (Catmull-Rom as cubic Beziers), in SVG units with y down."""
    p = [(x * CM, -z * CM) for x, z in points]
    n = len(p)
    d = f"M{p[0][0]:.1f},{p[0][1]:.1f}"
    count = n if close else n - 1
    for i in range(count):
        p0, p1, p2, p3 = p[(i - 1) % n], p[i], p[(i + 1) % n], p[(i + 2) % n]
        if not close:
            p0 = p[max(i - 1, 0)]
            p3 = p[min(i + 2, n - 1)]
        c1 = (p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6)
        c2 = (p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6)
        d += f" C{c1[0]:.1f},{c1[1]:.1f} {c2[0]:.1f},{c2[1]:.1f} {p2[0]:.1f},{p2[1]:.1f}"
    return d + (" Z" if close else "")


def svg(view, body, defs=""):
    x, y, w, h = view
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x:.0f} {y:.0f} {w:.0f} {h:.0f}" '
            f'width="{w:.0f}" height="{h:.0f}">\n<defs>{defs}</defs>\n{body}\n</svg>\n')


def grain_filter(fid, freq_x, freq_y, seed, opacity):
    """Wood grain: stretched turbulence turned into dark streaks, multiplied over whatever it is applied to."""
    return f"""
<filter id="{fid}" x="0" y="0" width="100%" height="100%">
  <feTurbulence type="fractalNoise" baseFrequency="{freq_x} {freq_y}" numOctaves="3" seed="{seed}" result="n"/>
  <feColorMatrix in="n" type="matrix" values="0 0 0 0 0.16  0 0 0 0 0.09  0 0 0 0 0.04  0 0 0 {opacity * 4} -{opacity * 1.6}" result="g"/>
  <feComposite in="g" in2="SourceGraphic" operator="in"/>
</filter>"""


def shield(x, z, r, color, angle):
    cx, cy = x * CM, -z * CM
    rr = r * CM
    return (f'<g transform="rotate({angle:.1f} {cx:.1f} {cy:.1f})">'
            f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="{rr:.1f}" fill="{color}" stroke="{WOOD_DARK}" stroke-width="{rr * 0.16:.1f}"/>'
            f'<path d="M{cx - rr * 0.92:.1f},{cy:.1f} H{cx + rr * 0.92:.1f} M{cx:.1f},{cy - rr * 0.92:.1f} V{cy + rr * 0.92:.1f}" '
            f'stroke="#000" stroke-opacity="0.25" stroke-width="{rr * 0.12:.1f}"/>'
            f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="{rr * 0.32:.1f}" fill="{IRON}" stroke="#1a1a1a" stroke-width="{rr * 0.06:.1f}"/>'
            f'<circle cx="{cx - rr * 0.1:.1f}" cy="{cy - rr * 0.1:.1f}" r="{rr * 0.12:.1f}" fill="#8a8a8a" opacity="0.7"/>'
            f'</g>')


def dragon_head(z_base, length, width, color, accent):
    """Top-down dragon figurehead on the stem post: a neck that narrows into a snouted head with eyes."""
    s = CM
    zb, l, w = z_base, length, width
    neck = [(0.5 * w, zb), (0.32 * w, zb + 0.35 * l), (0.42 * w, zb + 0.62 * l), (0.3 * w, zb + 0.86 * l),
            (0.0, zb + l), (-0.3 * w, zb + 0.86 * l), (-0.42 * w, zb + 0.62 * l), (-0.32 * w, zb + 0.35 * l),
            (-0.5 * w, zb)]
    d = path(neck)
    eyes = "".join(
        f'<ellipse cx="{sx * 0.26 * w * s:.1f}" cy="{-(zb + 0.66 * l) * s:.1f}" rx="{0.07 * w * s:.1f}" ry="{0.05 * l * s:.1f}" fill="{accent}"/>'
        for sx in (-1, 1))
    horns = "".join(
        f'<path d="M{sx * 0.34 * w * s:.1f},{-(zb + 0.55 * l) * s:.1f} q{sx * 0.45 * w * s:.1f},{0.12 * l * s:.1f} {sx * 0.55 * w * s:.1f},{0.34 * l * s:.1f}" '
        f'stroke="{WOOD_DARK}" stroke-width="{0.09 * w * s:.1f}" fill="none" stroke-linecap="round"/>'
        for sx in (-1, 1))
    scales = "".join(
        f'<path d="M{-0.25 * w * s:.1f},{-(zb + f * l) * s:.1f} q{0.25 * w * s:.1f},{-0.06 * l * s:.1f} {0.5 * w * s:.1f},0" '
        f'stroke="{WOOD_DARK}" stroke-opacity="0.55" stroke-width="{0.04 * w * s:.1f}" fill="none"/>'
        for f in (0.12, 0.22, 0.32, 0.42))
    return (f'{horns}<path d="{d}" fill="{color}" stroke="{WOOD_DARK}" stroke-width="{0.08 * w * s:.1f}"/>'
            f'{scales}{eyes}')


def stern_curl(z_base, length, width, color):
    s = CM
    zb, l, w = z_base, length, width
    post = [(0.45 * w, zb), (0.3 * w, zb - 0.6 * l), (0.0, zb - l), (-0.3 * w, zb - 0.6 * l), (-0.45 * w, zb)]
    spiral = (f'<path d="M0,{-(zb - 0.82 * l) * s:.1f} a{0.22 * w * s:.1f},{0.22 * w * s:.1f} 0 1 1 {0.2 * w * s:.1f},{0.15 * l * s:.1f}" '
              f'stroke="{WOOD_DARK}" stroke-width="{0.07 * w * s:.1f}" fill="none"/>')
    return f'<path d="{path(post)}" fill="{color}" stroke="{WOOD_DARK}" stroke-width="{0.08 * w * s:.1f}"/>{spiral}'


def seats(stools, benches):
    """Stools (x, z) and cross benches (x0, x1, z) where the crew sits, drawn under the rowers' tokens."""
    s = CM
    out = ""
    for x, z in stools:
        out += (f'<rect x="{(x - 0.3) * s:.1f}" y="{-(z + 0.24) * s:.1f}" width="{0.6 * s:.1f}" height="{0.48 * s:.1f}" rx="6" '
                f'fill="{WOOD_LIGHT}" stroke="{WOOD_DARK}" stroke-width="5"/>'
                f'<path d="M{(x - 0.3) * s:.1f},{-z * s:.1f} h{0.6 * s:.1f}" stroke="{WOOD_DARK}" stroke-opacity="0.5" stroke-width="3"/>')
    for x0, x1, z in benches:
        out += (f'<rect x="{(x0 - 0.35) * s:.1f}" y="{-(z + 0.3) * s:.1f}" width="{(x1 - x0 + 0.7) * s:.1f}" height="{0.6 * s:.1f}" rx="8" '
                f'fill="{WOOD_LIGHT}" stroke="{WOOD_DARK}" stroke-width="6"/>')
    return out


def mast_and_yard(z, yard_length, sail_stripes):
    """Mast seen from above with the yard across the ship and the sail furled along it."""
    s = CM
    half = yard_length / 2
    stripes = ""
    if sail_stripes:
        for i in range(-3, 4):
            if i % 2 == 0:
                x = i * half / 3.6
                stripes += (f'<rect x="{(x - half / 14) * s:.1f}" y="{-(z + 0.17) * s:.1f}" width="{half / 7 * s:.1f}" '
                            f'height="{0.34 * s:.1f}" fill="{SAIL_STRIPE}" opacity="0.85"/>')
    folds = "".join(
        f'<path d="M{(-half * 0.95 + i * half * 0.19) * s:.1f},{-(z + 0.17) * s:.1f} v{0.34 * s:.1f}" '
        f'stroke="#6b5a3a" stroke-opacity="0.45" stroke-width="2"/>' for i in range(11))
    return (f'<rect x="{-half * s:.1f}" y="{-(z + 0.06) * s:.1f}" width="{yard_length * s:.1f}" height="{0.12 * s:.1f}" rx="6" fill="{WOOD_DARK}"/>'
            f'<rect x="{-half * 0.95 * s:.1f}" y="{-(z + 0.17) * s:.1f}" width="{half * 1.9 * s:.1f}" height="{0.34 * s:.1f}" rx="{0.17 * s:.1f}" '
            f'fill="{SAIL}" stroke="#5b4a2e" stroke-width="3"/>{stripes}{folds}'
            f'<circle cx="0" cy="{-z * s:.1f}" r="{0.24 * s:.1f}" fill="{WOOD}" stroke="{WOOD_DARK}" stroke-width="5"/>'
            f'<circle cx="0" cy="{-z * s:.1f}" r="{0.09 * s:.1f}" fill="{WOOD_DARK}"/>')


def rudder(x, z, length):
    s = CM
    return (f'<g transform="rotate(-8 {x * s:.1f} {-z * s:.1f})">'
            f'<rect x="{(x - 0.11) * s:.1f}" y="{-(z + length * 0.25) * s:.1f}" width="{0.22 * s:.1f}" height="{length * s:.1f}" rx="8" '
            f'fill="{WOOD}" stroke="{WOOD_DARK}" stroke-width="5"/>'
            f'<circle cx="{x * s:.1f}" cy="{-(z + length * 0.12) * s:.1f}" r="{0.06 * s:.1f}" fill="{IRON}"/></g>')


def longship_like(name, beam, z0, z1, fullness, hull_color, deck_color, shields, shield_span, shield_r,
                  head, stern, yard, stripes, seed, stools=(), benches=(), extra=""):
    """A planked longship: hull, strakes, deck boards, shields along the gunwale, mast, rudder and posts."""
    s = CM
    outline = hull_points(beam, z0, z1, fullness)
    deck = hull_points(beam, z0 + (z1 - z0) * 0.04, z1 - (z1 - z0) * 0.04, fullness, scale=0.84)
    strakes = [hull_points(beam, z0, z1, fullness, scale=k) for k in (0.95, 0.9)]

    boards = ""
    z = z0
    while z < z1:
        boards += f'<line x1="{-beam * s:.1f}" y1="{-z * s:.1f}" x2="{beam * s:.1f}" y2="{-z * s:.1f}" stroke="{WOOD_DARK}" stroke-opacity="0.35" stroke-width="3"/>'
        z += 0.45
    keel = f'<line x1="0" y1="{-z0 * 0.92 * s:.1f}" x2="0" y2="{-z1 * 0.92 * s:.1f}" stroke="{WOOD_DARK}" stroke-opacity="0.35" stroke-width="8"/>'

    shields_svg = ""
    if shields:
        rnd = random.Random(seed)
        count = int((shield_span[1] - shield_span[0]) / (shield_r * 2.9))
        for side in (-1, 1):
            for i in range(count + 1):
                zz = shield_span[0] + (shield_span[1] - shield_span[0]) * i / max(count, 1)
                t = -1 + 2 * (zz - z0) / (z1 - z0)
                xx = side * (hull_half_width(t, beam, fullness) - shield_r * 0.55)
                shields_svg += shield(xx, zz, shield_r, SHIELD_COLORS[(i + (side > 0)) % len(SHIELD_COLORS)], rnd.uniform(0, 90))

    pad = 0.6
    top = z1 + head[0] + pad
    bottom = z0 - stern[0] - pad
    half = beam / 2 + pad
    view = (-half * s, -top * s, 2 * half * s, (top - bottom) * s)

    defs = (grain_filter("grain", 0.9 / s * 4, 0.04 / s * 4, seed, 0.45)
            + f'<linearGradient id="hullShade" x1="0" x2="1" y1="0" y2="0">'
              f'<stop offset="0" stop-color="#000" stop-opacity="0.45"/><stop offset="0.25" stop-color="#000" stop-opacity="0"/>'
              f'<stop offset="0.75" stop-color="#000" stop-opacity="0"/><stop offset="1" stop-color="#000" stop-opacity="0.45"/></linearGradient>'
            + f'<clipPath id="deckClip"><path d="{path(deck)}"/></clipPath>'
            + f'<clipPath id="hullClip"><path d="{path(outline)}"/></clipPath>'
            + '<filter id="shadow" x="-20%" y="-10%" width="140%" height="120%"><feGaussianBlur stdDeviation="14"/></filter>')

    if head[2]:
        bow = dragon_head(z1 - 0.15, head[0], head[1], hull_color, "#c9a14a")
    else:
        # A plain stem post: the stern curl mirrored to the bow.
        bow = f'<g transform="scale(1 -1)">{stern_curl(-(z1 - 0.15), head[0], head[1], hull_color)}</g>'

    body = f"""
<path d="{path(outline)}" transform="translate(10 18)" fill="#000" opacity="0.45" filter="url(#shadow)"/>
<g>{stern_curl(z0 + 0.15, stern[0], stern[1], hull_color)}</g>
<g>{bow}</g>
<path d="{path(outline)}" fill="{hull_color}" stroke="{WOOD_DARK}" stroke-width="12"/>
<g clip-path="url(#hullClip)"><rect x="{view[0]}" y="{view[1]}" width="{view[2]}" height="{view[3]}" filter="url(#grain)"/></g>
{''.join(f'<path d="{path(st)}" fill="none" stroke="{c}" stroke-width="5" stroke-opacity="0.6"/>' for st, c in zip(strakes, (WOOD_LIGHT, WOOD_DARK)))}
<path d="{path(deck)}" fill="{deck_color}" stroke="{WOOD_DARK}" stroke-width="7"/>
<g clip-path="url(#deckClip)">
  <rect x="{view[0]}" y="{view[1]}" width="{view[2]}" height="{view[3]}" filter="url(#grain)" opacity="0.8"/>
  {boards}{keel}
  <rect x="{-beam / 2 * s:.1f}" y="{view[1]}" width="{beam * s:.1f}" height="{view[3]}" fill="url(#hullShade)"/>
</g>
{extra}
{seats(stools, benches)}
{shields_svg}
{mast_and_yard(yard[0], yard[1], stripes)}
{rudder(beam / 2 * 0.62, z0 + (z1 - z0) * 0.13, (z1 - z0) * 0.09)}
"""
    with open(os.path.join(OUT, f"ship_{name}.svg"), "w", encoding="utf-8") as f:
        f.write(svg(view, body, defs))
    return {"halfWidth": half, "zMin": bottom, "zMax": top}


def raft():
    """Six lashed logs with a small mast, 6.3 m square like the game's raft."""
    s = CM
    half = 3.2
    logs = ""
    log_shapes = ""
    rnd = random.Random(7)
    for i in range(6):
        x = -2.6 + i * 1.04
        r = 0.5
        length = 6.0 + rnd.uniform(-0.15, 0.15)
        y = -length / 2 + rnd.uniform(-0.1, 0.1)
        logs += (f'<rect x="{(x - r) * s:.1f}" y="{y * s:.1f}" width="{2 * r * s:.1f}" height="{length * s:.1f}" rx="{r * s:.1f}" '
                 f'fill="url(#log)" stroke="{WOOD_DARK}" stroke-width="7"/>'
                 f'<ellipse cx="{x * s:.1f}" cy="{(y + 0.12) * s:.1f}" rx="{r * 0.8 * s:.1f}" ry="{0.12 * s:.1f}" fill="#c99a62" stroke="{WOOD_DARK}" stroke-width="4"/>')
        log_shapes += (f'<rect x="{(x - r) * s:.1f}" y="{y * s:.1f}" width="{2 * r * s:.1f}" height="{length * s:.1f}" '
                       f'rx="{r * s:.1f}"/>')
    lashings = "".join(
        f'<rect x="{-3.15 * s:.1f}" y="{zz * s:.1f}" width="{6.3 * s:.1f}" height="{0.16 * s:.1f}" fill="#7a6a45" stroke="#3a2f1a" stroke-width="3"/>'
        for zz in (-2.3, 2.1))
    view = (-(half + 0.4) * s, -(half + 0.4) * s, (half + 0.4) * 2 * s, (half + 0.4) * 2 * s)
    defs = (grain_filter("grain", 0.03, 0.002, 7, 0.5)
            + f'<linearGradient id="log" x1="0" x2="1"><stop offset="0" stop-color="{WOOD_DARK}"/>'
              f'<stop offset="0.35" stop-color="{WOOD_LIGHT}"/><stop offset="0.65" stop-color="{WOOD}"/><stop offset="1" stop-color="{WOOD_DARK}"/></linearGradient>'
            + '<filter id="shadow" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="14"/></filter>'
            + f'<clipPath id="logsClip">{log_shapes}</clipPath>')
    body = f"""
<rect x="{-3.1 * s + 10:.1f}" y="{-3.1 * s + 18:.1f}" width="{6.2 * s:.1f}" height="{6.2 * s:.1f}" rx="40" fill="#000" opacity="0.45" filter="url(#shadow)"/>
{logs}
<g opacity="0.6" clip-path="url(#logsClip)"><rect x="{-3.2 * s:.1f}" y="{-3.2 * s:.1f}" width="{6.4 * s:.1f}" height="{6.4 * s:.1f}" filter="url(#grain)"/></g>
{lashings}
{seats([(-1.1, 1.9)], [])}
{mast_and_yard(-0.86, 3.4, False)}
{rudder(0.0, -3.3, 1.2)}
"""
    with open(os.path.join(OUT, "ship_raft.svg"), "w", encoding="utf-8") as f:
        f.write(svg(view, body, defs))
    return {"halfWidth": half + 0.4, "zMin": -(half + 0.4), "zMax": half + 0.4}


def knot(cx, cy, r, color):
    """A small interlaced knot (three looped lobes) for the frame's corners."""
    lobes = ""
    for i in range(3):
        a = math.radians(i * 120 - 90)
        x1, y1 = cx + r * math.cos(a), cy + r * math.sin(a)
        lobes += (f'<path d="M{cx:.1f},{cy:.1f} Q{x1 + r * 0.9 * math.cos(a + 1.2):.1f},{y1 + r * 0.9 * math.sin(a + 1.2):.1f} {x1:.1f},{y1:.1f} '
                  f'Q{x1 + r * 0.9 * math.cos(a - 1.2):.1f},{y1 + r * 0.9 * math.sin(a - 1.2):.1f} {cx:.1f},{cy:.1f}" '
                  f'fill="none" stroke="{WOOD_DARK}" stroke-width="{r * 0.34:.1f}" stroke-linecap="round"/>'
                  f'<path d="M{cx:.1f},{cy:.1f} Q{x1 + r * 0.9 * math.cos(a + 1.2):.1f},{y1 + r * 0.9 * math.sin(a + 1.2):.1f} {x1:.1f},{y1:.1f} '
                  f'Q{x1 + r * 0.9 * math.cos(a - 1.2):.1f},{y1 + r * 0.9 * math.sin(a - 1.2):.1f} {cx:.1f},{cy:.1f}" '
                  f'fill="none" stroke="{color}" stroke-width="{r * 0.18:.1f}" stroke-linecap="round"/>')
    return (f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="{r * 1.25:.1f}" fill="#4a2e17" stroke="{WOOD_DARK}" stroke-width="4"/>'
            f'{lobes}<circle cx="{cx:.1f}" cy="{cy:.1f}" r="{r * 0.18:.1f}" fill="{color}"/>')


def frame(width, height, header):
    """The panel: carved wooden frame, parchment inside, a carved bar under the header with the counts."""
    b = 22  # border thickness, px
    defs = f"""
{grain_filter("grain", 0.004, 0.09, 3, 0.55)}
<filter id="paper" x="0" y="0" width="100%" height="100%">
  <feTurbulence type="fractalNoise" baseFrequency="0.035" numOctaves="4" seed="11" result="n"/>
  <feColorMatrix in="n" type="matrix" values="0 0 0 0 0.35  0 0 0 0 0.25  0 0 0 0 0.12  0 0 0 0.9 -0.32"/>
  <feComposite in2="SourceGraphic" operator="in"/>
</filter>
<radialGradient id="burn" cx="0.5" cy="0.5" r="0.75">
  <stop offset="0.55" stop-color="#3b2410" stop-opacity="0"/><stop offset="1" stop-color="#3b2410" stop-opacity="0.75"/>
</radialGradient>
<linearGradient id="bevel" x1="0" y1="0" x2="1" y2="1">
  <stop offset="0" stop-color="#9a6a3c"/><stop offset="0.5" stop-color="#5e3b1e"/><stop offset="1" stop-color="#2f1b0c"/>
</linearGradient>
<linearGradient id="bar" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#8a5c32"/><stop offset="1" stop-color="#3e2611"/>
</linearGradient>"""
    inner = f'x="{b}" y="{b}" width="{width - 2 * b}" height="{height - 2 * b}"'
    body = f"""
<rect x="0" y="0" width="{width}" height="{height}" rx="18" fill="url(#bevel)"/>
<rect x="0" y="0" width="{width}" height="{height}" rx="18" filter="url(#grain)"/>
<rect x="5" y="5" width="{width - 10}" height="{height - 10}" rx="14" fill="none" stroke="#c08d55" stroke-opacity="0.5" stroke-width="2"/>
<rect x="{b - 4}" y="{b - 4}" width="{width - 2 * b + 8}" height="{height - 2 * b + 8}" rx="6" fill="{WOOD_DARK}"/>
<rect {inner} rx="4" fill="#e3cf9f" fill-opacity="0.93"/>
<rect {inner} rx="4" filter="url(#paper)"/>
<rect {inner} rx="4" fill="url(#burn)"/>
<rect x="{b - 4}" y="{header}" width="{width - 2 * b + 8}" height="14" fill="url(#bar)" stroke="{WOOD_DARK}" stroke-width="3"/>
{''.join(f'<circle cx="{x}" cy="{header + 7}" r="3.2" fill="#2a2a2a" stroke="#888" stroke-width="1"/>' for x in (b + 8, width - b - 8))}
{knot(b - 2, b - 2, 15, "#c9a14a")}{knot(width - b + 2, b - 2, 15, "#c9a14a")}
{knot(b - 2, height - b + 2, 15, "#c9a14a")}{knot(width - b + 2, height - b + 2, 15, "#c9a14a")}
"""
    with open(os.path.join(OUT, "frame.svg"), "w", encoding="utf-8") as f:
        f.write(svg((0, 0, width, height), body, defs))


def sprites():
    """Rower token (white, tinted by gear in game), helmet overlay, the local player's ring, the oar, icons."""
    files = {
        "token.svg": svg((0, 0, 64, 64), """
<circle cx="32" cy="34" r="27" fill="#000" opacity="0.35"/>
<circle cx="32" cy="32" r="27" fill="#ffffff" stroke="#1b120a" stroke-width="5"/>
<circle cx="32" cy="32" r="21" fill="none" stroke="#000" stroke-opacity="0.18" stroke-width="3"/>"""),
        "helmet.svg": svg((0, 0, 64, 64), """
<radialGradient id="steel" cx="0.38" cy="0.32" r="0.8"><stop offset="0" stop-color="#d8d8d4"/><stop offset="0.6" stop-color="#7c7c7a"/><stop offset="1" stop-color="#3c3c3c"/></radialGradient>
<circle cx="32" cy="32" r="15" fill="url(#steel)" stroke="#1d1d1d" stroke-width="2.5"/>
<path d="M32,17 V47" stroke="#2a2a2a" stroke-width="3.2"/>
<path d="M17,32 H47" stroke="#2a2a2a" stroke-width="2" stroke-opacity="0.7"/>
<rect x="29.5" y="10" width="5" height="9" rx="1.5" fill="#5a5a5a" stroke="#1d1d1d" stroke-width="1.5"/>
<circle cx="25" cy="25" r="1.4" fill="#eee"/><circle cx="39" cy="25" r="1.4" fill="#eee"/>
<circle cx="25" cy="39" r="1.4" fill="#eee"/><circle cx="39" cy="39" r="1.4" fill="#eee"/>""".replace(
            '<radialGradient', '<defs><radialGradient').replace('</radialGradient>', '</radialGradient></defs>')),
        "self_ring.svg": svg((0, 0, 64, 64), """
<circle cx="32" cy="32" r="29" fill="none" stroke="#2a1a08" stroke-width="7"/>
<circle cx="32" cy="32" r="29" fill="none" stroke="#e8c15a" stroke-width="4"/>
<circle cx="32" cy="32" r="29" fill="none" stroke="#fff3c4" stroke-width="1.2" stroke-dasharray="4 6"/>"""),
        "oar.svg": svg((0, 0, 128, 24), f"""
<rect x="2" y="9" width="92" height="6" rx="3" fill="{WOOD_LIGHT}" stroke="{WOOD_DARK}" stroke-width="2"/>
<rect x="2" y="8" width="14" height="8" rx="3" fill="{WOOD_DARK}"/>
<path d="M88,9 Q100,2 124,5 Q127,12 124,19 Q100,22 88,15 Z" fill="{WOOD}" stroke="{WOOD_DARK}" stroke-width="2.5"/>
<path d="M94,12 H120" stroke="{WOOD_DARK}" stroke-opacity="0.5" stroke-width="1.5"/>"""),
        "icon_oar.svg": svg((0, 0, 64, 64), f"""
<g transform="rotate(-45 32 32)">
<rect x="29" y="2" width="6" height="40" rx="3" fill="#e9d6a8" stroke="{WOOD_DARK}" stroke-width="2.5"/>
<path d="M26,38 Q24,52 32,62 Q40,52 38,38 Z" fill="#e9d6a8" stroke="{WOOD_DARK}" stroke-width="2.5"/></g>
<g transform="rotate(45 32 32)">
<rect x="29" y="2" width="6" height="40" rx="3" fill="#e9d6a8" stroke="{WOOD_DARK}" stroke-width="2.5"/>
<path d="M26,38 Q24,52 32,62 Q40,52 38,38 Z" fill="#e9d6a8" stroke="{WOOD_DARK}" stroke-width="2.5"/></g>"""),
        "icon_crew.svg": svg((0, 0, 64, 64), f"""
<path d="M10,58 Q12,40 32,38 Q52,40 54,58 Z" fill="#e9d6a8" stroke="{WOOD_DARK}" stroke-width="3"/>
<path d="M17,24 Q17,8 32,8 Q47,8 47,24 L47,28 L17,28 Z" fill="#e9d6a8" stroke="{WOOD_DARK}" stroke-width="3"/>
<path d="M32,8 V30" stroke="{WOOD_DARK}" stroke-width="3"/>
<circle cx="32" cy="30" r="9" fill="#e9d6a8" stroke="{WOOD_DARK}" stroke-width="3"/>"""),
    }
    for name, content in files.items():
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
            f.write(content)


def main():
    os.makedirs(OUT, exist_ok=True)
    rects = {
        # name: hull (beam, stern z, bow z, fullness) from the game's hull meshes; posts (length, width, dragon?)
        "karve": longship_like("karve", 2.7, -5.07, 5.04, 0.95, WOOD, DECK, False, (0, 0), 0.3,
                               (0.7, 0.32, False), (0.6, 0.3), (0.6, 2.4), False, 3,
                               benches=[(-0.48, 0.48, 2.23), (0, 0, -3.24)]),
        "longship": longship_like("longship", 5.5, -10.6, 10.0, 1.0, WOOD, DECK, True, (-6.5, 6.5), 0.5,
                                  (1.6, 0.6, True), (1.0, 0.45), (0.3, 4.6), True, 5,
                                  stools=[(1.46, -3.03), (-1.54, -3.03), (-0.79, 5.64), (0.79, 5.64), (0.45, -7.03)]),
        "drakkar": longship_like("drakkar", 12.0, -16.6, 16.6, 1.44, "#4a3227", "#7d5a43", True, (-11, 11), 0.7,
                                 (2.6, 1.1, True), (1.6, 0.8), (0.0, 9.0), True, 9,
                                 stools=[(-3.28, -2.42), (-2.47, -6.17), (2.41, -6.14), (3.25, -2.44), (3.24, 1.78),
                                         (2.42, 5.45), (-2.46, 5.51), (-3.27, 1.78), (1.33, -11.14)],
                                 benches=[(-1.95, 2.05, -9.15), (-2.05, 1.86, 9.15)],
                                 extra='<rect x="-260" y="-220" width="520" height="440" rx="30" fill="#8b6a45" '
                                       'stroke="#3a2614" stroke-width="10" opacity="0.9"/>'),
        "raft": raft(),
    }
    frame(360, 680, 92)
    sprites()
    for name, r in rects.items():
        print(f"{name}: halfWidth {r['halfWidth']:.2f}, zMin {r['zMin']:.2f}, zMax {r['zMax']:.2f}")


if __name__ == "__main__":
    main()
