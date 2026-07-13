#!/usr/bin/env python3
"""Generate a 1024×1024 source icon with a stylized 'S' for Tauri."""
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

SIZE = 1024
OUT = os.path.join(os.path.dirname(__file__), "..", "src-tauri", "icons", "source.png")
os.makedirs(os.path.dirname(OUT), exist_ok=True)

# Background: rounded gradient blue → purple (matching the TopBar logo)
img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)

# Rounded square base
RADIUS = 220
mask = Image.new("L", (SIZE, SIZE), 0)
mask_draw = ImageDraw.Draw(mask)
mask_draw.rounded_rectangle((0, 0, SIZE, SIZE), radius=RADIUS, fill=255)

# Gradient
gradient = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
gd = ImageDraw.Draw(gradient)
for y in range(SIZE):
    t = y / SIZE
    r = int(59 + (139 - 59) * t)
    g = int(130 + (92 - 130) * t)
    b = int(246 + (246 - 246) * t)
    gd.line([(0, y), (SIZE, y)], fill=(r, g, b, 255))

img.paste(gradient, (0, 0), mask)

# Letter "S" in white, large and bold, centered
def find_font():
    candidates = [
        "/System/Library/Fonts/Supplemental/Arial Black.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "/Library/Fonts/Arial Black.ttf",
        "/System/Library/Fonts/SFNS.ttf",
    ]
    for c in candidates:
        if os.path.exists(c):
            return c
    return None

font_path = find_font()
font_size = 720
font = ImageFont.truetype(font_path, font_size) if font_path else ImageFont.load_default()

draw = ImageDraw.Draw(img)
text = "S"
bbox = draw.textbbox((0, 0), text, font=font)
text_w = bbox[2] - bbox[0]
text_h = bbox[3] - bbox[1]
x = (SIZE - text_w) // 2 - bbox[0]
y = (SIZE - text_h) // 2 - bbox[1] - 30  # nudge up for visual centering
# soft shadow
shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
sd = ImageDraw.Draw(shadow)
sd.text((x + 6, y + 8), text, font=font, fill=(0, 0, 0, 110))
shadow = shadow.filter(ImageFilter.GaussianBlur(radius=14))
img = Image.alpha_composite(img, shadow)
draw = ImageDraw.Draw(img)
draw.text((x, y), text, font=font, fill=(255, 255, 255, 255))

img.save(OUT, "PNG")
print(f"Saved: {OUT}")
