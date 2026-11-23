"""Build the Photo Studio UI glyphs as pixel-hinted white RGBA PNGs.

Usage (conda base with Pillow 12.0.0 and resvg-py 0.5.0):
    conda run -n base python tools/icons/gen-studio-icons.py --contact tools/icons/preview.png
"""

import argparse
from dataclasses import dataclass
from io import BytesIO
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from resvg_py import svg_to_bytes


RAIL_SIZES = (12, 15, 16, 20, 24, 30, 32, 40, 64, 128)
HEADER_SIZES = (16, 18, 24, 32, 36, 48, 64, 128)
FOLD_SIZES = (9, 12, 15, 16, 18, 24)
BUTTON_SIZES = (12, 15, 16, 18, 24, 32)
SUPERSAMPLE = 16
ROOT = Path(__file__).resolve().parents[2]
WHITE = (255, 255, 255)
PREVIEW_PALETTES = (
    ("Teal", "#5FB8C4", WHITE),
    ("Pink", "#C8508F", WHITE),
    ("Inactive", "#1C2126", (126, 138, 142)),
)


def half_up(value):
    return math.floor(value + 0.5)


@dataclass(frozen=True)
class Grid:
    size: int

    @property
    def unit(self):
        return self.size / 24

    @property
    def stroke(self):
        return max(2, half_up(2 * self.unit))

    @property
    def center(self):
        return self.size / 2

    def axis(self, coord):
        """Place both edges of a straight stroke on pixel boundaries."""
        offset = (self.stroke % 2) / 2
        return half_up(coord * self.unit - offset) + offset


def line(path):
    return f'<path d="{path}"/>'


def disk(x, y, radius):
    return f'<circle cx="{x}" cy="{y}" r="{radius}" fill="white" stroke="none"/>'


def sparkle(x, y, rx, ry, *, solid=False):
    """Four points with curved shoulders and a rounded stroke at the tips."""
    path = (
        f'M {x:g} {y - ry:g} '
        f'Q {x + rx * .19:g} {y - ry * .19:g} {x + rx:g} {y:g} '
        f'Q {x + rx * .19:g} {y + ry * .19:g} {x:g} {y + ry:g} '
        f'Q {x - rx * .19:g} {y + ry * .19:g} {x - rx:g} {y:g} '
        f'Q {x - rx * .19:g} {y - ry * .19:g} {x:g} {y - ry:g} Z'
    )
    if solid:
        return f'<path d="{path}" fill="white" stroke="none"/>'
    return line(path)


def header(grid):
    a, u = grid.axis, grid.unit
    # At 16/18 px the lens is given room by making the body taller.
    if grid.size == 16:
        left, right, top, bottom = 2, 14, 5, 14
        heel, peak_left, peak_right, toe, peak = 4, 5, 8, 9, 3
        flash = sparkle(12.5, 1.9, 2.1, 1.45, solid=True)
        lens_radius = 2
    elif grid.size == 18:
        left, right, top, bottom = 2, 16, 6, 16
        heel, peak_left, peak_right, toe, peak = 5, 6, 9, 10, 3
        flash = sparkle(14, 2.5, 2.3, 1.8, solid=True)
        lens_radius = 2.1
    else:
        left, right, top, bottom = a(3), grid.size - a(3), a(8), a(21)
        heel, peak_left, peak_right, toe, peak = a(6), a(8), a(12), a(14), a(5)
        flash = sparkle(19 * u, 3.7 * u, 3.4 * u, 2.6 * u, solid=True)
        lens_radius = 3.1 * u

    corner = max(1, 2 * u)
    body = line(
        f'M {left + corner:g} {top:g} H {heel:g} L {peak_left:g} {peak:g} '
        f'H {peak_right:g} L {toe:g} {top:g} H {right - corner:g} '
        f'Q {right:g} {top:g} {right:g} {top + corner:g} V {bottom - corner:g} '
        f'Q {right:g} {bottom:g} {right - corner:g} {bottom:g} H {left + corner:g} '
        f'Q {left:g} {bottom:g} {left:g} {bottom - corner:g} V {top + corner:g} '
        f'Q {left:g} {top:g} {left + corner:g} {top:g} Z'
    )
    lens = f'<circle cx="{grid.center}" cy="{(top + bottom) / 2}" r="{lens_radius}"/>'
    return body + lens + flash


def capture(grid):
    tiny = {12: (2, 2), 15: (2, 3), 16: (2, 3)}
    edge, arm = tiny.get(grid.size, (grid.axis(3), half_up(5 * grid.unit)))
    corner = max(1, 2.05 * grid.unit)
    near, far = edge, grid.size - edge
    brackets = []
    for x, dx in ((near, 1), (far, -1)):
        for y, dy in ((near, 1), (far, -1)):
            brackets.append(line(
                f'M {x:g} {y + dy * arm:g} V {y + dy * corner:g} '
                f'Q {x:g} {y:g} {x + dx * corner:g} {y:g} H {x + dx * arm:g}'
            ))
    return ''.join(brackets) + disk(grid.center, grid.center, max(1.65, 2.15 * grid.unit))


def hide(grid):
    c, u, s = grid.center, grid.unit, grid.size
    if s <= 16:
        inset, arch = 2, s * .075
        slash_inset = 2
    else:
        inset, arch = 2.5 * u, 1.75 * u
        slash_inset = 3.5 * u

    # Clip away a diagonal strip so the eye and slash stay visually separate.
    # The strip has the slash width plus clearance on both sides.
    band = math.sqrt(2) * (grid.stroke / 2 + max(.7, .7 * u))
    clip = (
        f'<clipPath id="eye">'
        f'<path d="M {band:g} 0 H {s:g} V {s - band:g} Z '
        f'M 0 {band:g} L {s - band:g} {s:g} H 0 Z"/>'
        f'</clipPath>'
    )
    eye = line(
        f'M {inset:g} {c:g} Q {c:g} {arch:g} {s - inset:g} {c:g} '
        f'Q {c:g} {s - arch:g} {inset:g} {c:g} Z'
    )
    if s >= 20:
        eye += f'<circle cx="{c}" cy="{c}" r="{2.8 * u}"/>'
    slash = line(f'M {slash_inset:g} {slash_inset:g} L {s - slash_inset:g} {s - slash_inset:g}')
    return clip + f'<g clip-path="url(#eye)">{eye}</g>' + slash


def camera(grid):
    # One open orbit and an arrowhead keep the free-camera mark apart from the viewfinder.
    c, u = grid.center, grid.unit
    radius = c - max(2.55, 3.7 * u)
    end_angle = math.radians(-23)
    start_angle = math.radians(60)
    start_x = c + radius * math.cos(start_angle)
    start_y = c + radius * math.sin(start_angle)
    end_x = c + radius * math.cos(end_angle)
    end_y = c + radius * math.sin(end_angle)
    orbit = line(f'M {start_x:g} {start_y:g} A {radius:g} {radius:g} 0 1 1 {end_x:g} {end_y:g}')

    # The wings point back along the orbit's tangent, with clear space inside the ring.
    back = max(2, 3.3 * u)
    wing = max(1.65, 2.9 * u)
    back_x = end_x + back * math.sin(end_angle)
    back_y = end_y - back * math.cos(end_angle)
    wing_x = wing * math.cos(end_angle)
    wing_y = wing * math.sin(end_angle)
    arrow = line(
        f'M {back_x - wing_x:g} {back_y - wing_y:g} L {end_x:g} {end_y:g} '
        f'L {back_x + wing_x:g} {back_y + wing_y:g}'
    )
    return orbit + arrow + disk(c, c, max(1.25, 1.9 * u))


def pose(grid):
    a, u = grid.axis, grid.unit
    tiny = {
        12: (2.4, 1.25, 6, 8, (2, 7), (10, 5), (3.7, 10), (8.8, 10)),
        15: (2.9, 1.45, 7, 10, (2.5, 8.5), (12.5, 5), (4.4, 13), (11.1, 13)),
        16: (3, 1.55, 7, 10, (3, 9), (13, 5), (4.4, 14), (11.6, 14)),
    }
    if grid.size in tiny:
        head_y, head_r, shoulder, hip, hand_left, hand_right, foot_left, foot_right = tiny[grid.size]
    else:
        head_y, head_r, shoulder, hip = 4.5 * u, 2.25 * u, a(10.5), a(15.25)
        hand_left, hand_right = (4.3 * u, 13.2 * u), (19.7 * u, 7.9 * u)
        foot_left, foot_right = (6.5 * u, a(21)), (17.5 * u, a(21))

    x = a(12)
    hx, hy = hand_left
    jx, jy = hand_right
    fx, fy = foot_left
    gx, gy = foot_right
    arms = line(f'M {hx:g} {hy:g} L {x:g} {shoulder:g} {jx:g} {jy:g}')
    torso = line(f'M {x:g} {shoulder:g} V {hip:g}')
    legs = line(f'M {fx:g} {fy:g} L {x:g} {hip:g} {gx:g} {gy:g}')
    return disk(x, head_y, head_r) + arms + torso + legs


def lights(grid):
    a, u, c = grid.axis, grid.unit, grid.center
    tiny = {
        12: (2, 7, 10, 3.8, 2),
        15: (2, 10, 13, 4.65, 2.5),
        16: (2, 11, 14, 5, 2),
    }
    top, bottom, base, widest, neck = tiny.get(grid.size, (a(3), a(16), a(21), 6.7 * u, c - a(9.1)))
    rise = (bottom - top) * .54
    shoulder = top + rise
    neck_top = bottom - 1.3 * u
    # Quarter-ellipse shoulders flow into the tapered, pixel-aligned neck.
    curve = .5523
    left, right = c - widest, c + widest
    left_neck, right_neck = c - neck, c + neck
    bend = neck_top - 1.5 * u
    body = line(
        f'M {left_neck:g} {bottom:g} V {neck_top:g} '
        f'C {left_neck:g} {bend:g} {left:g} {shoulder + rise / 2:g} {left:g} {shoulder:g} '
        f'C {left:g} {shoulder - rise * curve:g} {c - widest * curve:g} {top:g} {c:g} {top:g} '
        f'C {c + widest * curve:g} {top:g} {right:g} {shoulder - rise * curve:g} {right:g} {shoulder:g} '
        f'C {right:g} {shoulder + rise / 2:g} {right_neck:g} {bend:g} {right_neck:g} {neck_top:g} '
        f'V {bottom:g} Z'
    )
    return body + line(f'M {left_neck:g} {base:g} H {right_neck:g}')


def look(grid):
    c, u, s = grid.center, grid.unit, grid.size
    radius = c - max(2, 3.3 * u)
    # The half-fill ends at a whole pixel, even in the 15 px raster.
    split = half_up(c)
    clip = f'<clipPath id="half"><rect width="{split}" height="{s}"/></clipPath>'
    half = f'<g clip-path="url(#half)">{disk(c, c, radius)}</g>'
    outline = f'<circle cx="{c}" cy="{c}" r="{radius}"/>'
    return clip + half + outline


def reshade(grid):
    u, c = grid.unit, grid.center
    if grid.size <= 16:
        return sparkle(c, c, c - 2, c - 2)
    main = sparkle(10.25 * u, 13.4 * u, 6.75 * u, 7.15 * u)
    accent = sparkle(19.35 * u, 5.25 * u, 3.2 * u, 3.35 * u, solid=True)
    return main + accent


def fold(grid, *, closed):
    # Keep the round-stroked bounds about twice as wide as they are tall.
    half_span, rise = {
        9: (2.5, 2),
        12: (3.5, 2.5),
        15: (4, 3),
        16: (4.5, 3.5),
        18: (5, 4),
        24: (6.5, 5.5),
    }[grid.size]
    c = grid.center
    start, tip = c - rise / 2, c + rise / 2
    near, far = c - half_span, c + half_span
    if closed:
        return line(f'M {start:g} {near:g} L {tip:g} {c:g} {start:g} {far:g}')
    return line(f'M {near:g} {start:g} L {c:g} {tip:g} {far:g} {start:g}')


def fold_open(grid):
    return fold(grid, closed=False)


def fold_closed(grid):
    return fold(grid, closed=True)


def pause(grid):
    # Mirror the bars about the center so the gap stays centered at odd sizes.
    left, top, inner = {
        12: (2.75, 2.25, 4.875),
        15: (3.25, 2.75, 5.875),
        16: (3.5, 2.75, 6.375),
        18: (4, 3.25, 7.25),
        24: (5.5, 4, 10),
        32: (7.25, 5.25, 13.25),
    }[grid.size]
    s = grid.size
    width, height = inner - left, s - 2 * top
    return ''.join(
        f'<rect x="{x:g}" y="{top:g}" width="{width:g}" height="{height:g}" '
        f'rx="{width / 2:g}" fill="white" stroke="none"/>'
        for x in (left, s - inner)
    )


def play(grid):
    # Give the tip more room to the right to balance the weight of the back edge.
    left, top, tip, corner = {
        12: (3.5, 1.875, 10.75, .95),
        15: (4.375, 2.375, 13.625, 1.2),
        16: (4.75, 2.5, 14.5, 1.25),
        18: (5.25, 2.75, 16.25, 1.4),
        24: (7, 3.75, 21.75, 1.8),
        32: (9.25, 5, 29, 2.4),
    }[grid.size]
    c = grid.center
    bottom = grid.size - top
    inset = corner * (c - top) / (tip - left)
    path = (
        f'M {left:g} {top + corner:g} '
        f'Q {left:g} {top:g} {left + corner:g} {top + inset:g} '
        f'L {tip - corner:g} {c - inset:g} '
        f'Q {tip:g} {c:g} {tip - corner:g} {c + inset:g} '
        f'L {left + corner:g} {bottom - inset:g} '
        f'Q {left:g} {bottom:g} {left:g} {bottom - corner:g} Z'
    )
    return f'<path d="{path}" fill="white" stroke="none"/>'


def reset(grid):
    radius, back, wing, drop = {
        12: (3.375, 1.8, 1.55, .375),
        15: (4.5, 2.15, 1.8, .5),
        16: (4.875, 2.25, 1.9, .5),
        18: (5.5, 2.5, 2.1, .5),
        24: (7.5, 3.35, 2.75, .75),
        32: (10, 4.4, 3.55, 1),
    }[grid.size]
    c = grid.center
    # Lower the orbit slightly to balance the arrowhead at the top.
    cy = c + drop
    start_angle = math.radians(155)
    end_angle = math.radians(-146)
    dx, dy = math.cos(end_angle), math.sin(end_angle)
    start_x = c + radius * math.cos(start_angle)
    start_y = cy + radius * math.sin(start_angle)
    end_x, end_y = c + radius * dx, cy + radius * dy
    arc = line(f'M {start_x:g} {start_y:g} A {radius:g} {radius:g} 0 1 0 {end_x:g} {end_y:g}')

    # Aim the head along the counter-clockwise tangent at the upper-left end.
    back_x, back_y = end_x - back * dy, end_y + back * dx
    wing_x, wing_y = wing * dx, wing * dy
    arrow = line(
        f'M {back_x + wing_x:g} {back_y + wing_y:g} L {end_x:g} {end_y:g} '
        f'L {back_x - wing_x:g} {back_y - wing_y:g}'
    )
    return arc + arrow


DESIGNS = {
    "header": (header, HEADER_SIZES),
    "rail_capture": (capture, RAIL_SIZES),
    "rail_hide": (hide, RAIL_SIZES),
    "rail_camera": (camera, RAIL_SIZES),
    "rail_pose": (pose, RAIL_SIZES),
    "rail_lights": (lights, RAIL_SIZES),
    "rail_look": (look, RAIL_SIZES),
    "rail_reshade": (reshade, RAIL_SIZES),
    "fold_open": (fold_open, FOLD_SIZES),
    "fold_closed": (fold_closed, FOLD_SIZES),
    "pause": (pause, BUTTON_SIZES),
    "play": (play, BUTTON_SIZES),
    "reset": (reset, BUTTON_SIZES),
}


def render(draw, size):
    grid = Grid(size)
    side = size * SUPERSAMPLE
    svg = (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{side}" height="{side}" '
        f'viewBox="0 0 {size} {size}"><g fill="none" stroke="white" stroke-width="{grid.stroke}" '
        f'stroke-linecap="round" stroke-linejoin="round">{draw(grid)}</g></svg>'
    )
    raster = svg_to_bytes(svg_string=svg, skip_system_fonts=True)
    with Image.open(BytesIO(raster)) as source:
        alpha = source.getchannel("A").resize((size, size), Image.Resampling.BOX)
    icon = Image.new("RGBA", (size, size), (*WHITE, 0))
    icon.putalpha(alpha)
    return icon


def preview_pair(icon, background, tint):
    tinted = Image.new("RGBA", icon.size, (*tint, 0))
    tinted.putalpha(icon.getchannel("A"))
    size = icon.width
    pair = Image.new("RGBA", (5 * size + 24, 4 * size + 12), "#101419")
    x = 0
    for zoom, pad in ((1, 3), (4, 6)):
        side = size * zoom
        tile = Image.new("RGBA", (side + 2 * pad, side + 2 * pad), background)
        tile.alpha_composite(tinted.resize((side, side), Image.Resampling.NEAREST), (pad, pad))
        pair.paste(tile, (x, half_up((pair.height - tile.height) / 2)))
        x += tile.width + 6
    return pair


def contact(path, icons):
    sizes = sorted({size for _, size in icons})
    margin, gap = 24, 12
    width = 4160
    sections = []
    height = 106
    for size in sizes:
        names = [name for name in DESIGNS if (name, size) in icons]
        card_width = 3 * (5 * size + 24) + 4 * gap
        columns = min(len(names), (width - 2 * margin + gap) // (card_width + gap))
        card_height = 4 * size + 54
        rows = (len(names) + columns - 1) // columns
        sections.append((size, names, columns, card_width, card_height, height))
        height += 34 + rows * (card_height + gap) + 18

    sheet = Image.new("RGB", (width, height), "#101419")
    draw = ImageDraw.Draw(sheet)
    title = ImageFont.load_default(size=30)
    heading = ImageFont.load_default(size=20)
    label = ImageFont.load_default(size=14)
    draw.text((margin, 18), "Photo Studio icons", font=title, fill="#F0F4F5")
    for index, (name, background, tint) in enumerate(PREVIEW_PALETTES):
        x = margin + 175 * index
        draw.rectangle((x, 66, x + 15, 81), fill=background)
        draw.rectangle((x + 5, 71, x + 10, 76), fill=tint)
        draw.text((x + 23, 65), f"{name}  1:1 / x4", font=label, fill="#BBC6CB")

    for size, names, columns, card_width, card_height, top in sections:
        draw.text((margin, top), f"{size} px", font=heading, fill="#F0F4F5")
        draw.line((margin + 85, top + 14, width - margin, top + 14), fill="#343D44")
        for index, name in enumerate(names):
            row, column = divmod(index, columns)
            x = margin + column * (card_width + gap)
            y = top + 34 + row * (card_height + gap)
            draw.rounded_rectangle((x, y, x + card_width - 1, y + card_height - 1),
                                   radius=6, outline="#343D44")
            draw.text((x + gap, y + 7), name, font=label, fill="#C8D3D8")
            for _, background, tint in PREVIEW_PALETTES:
                pair = preview_pair(icons[name, size], background, tint)
                x += gap
                sheet.paste(pair, (x, y + 29))
                x += pair.width

    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path, optimize=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--contact", type=Path, metavar="PNG", help="write the contact sheet")
    args = parser.parse_args()
    icons = {}
    for name, (draw, sizes) in DESIGNS.items():
        for size in sizes:
            icon = render(draw, size)
            counts = icon.getchannel("A").histogram()
            distinct = sum(count > 0 for count in counts)
            if distinct < 8 or not counts[0] or not counts[255]:
                raise ValueError(f"{name} at {size} px has insufficient alpha coverage ({distinct} values)")
            directory = ROOT / "Resources" / "icons" / str(size)
            directory.mkdir(parents=True, exist_ok=True)
            icon.save(directory / f"{name}.png", optimize=True)
            icons[name, size] = icon
            print(f"{size:3} {name:13} {distinct:3} alpha values")
    if args.contact:
        contact(args.contact, icons)


if __name__ == "__main__":
    main()
