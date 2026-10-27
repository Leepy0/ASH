# Copies the captures into docs/manual/img: numbered red callouts on the web pages, crops, palette quantised PNGs.
import json, os, shutil
from PIL import Image, ImageDraw, ImageFont

here = os.path.dirname(os.path.abspath(__file__))
dst = r"C:\Users\ioi\Documents\ASH\.claude\worktrees\server-ux\docs\manual\img"
os.makedirs(dst, exist_ok=True)
FB = ImageFont.truetype(r"C:\Windows\Fonts\malgunbd.ttf", 17)
RED = (220, 30, 40)

def save(im, name):
    im = im.convert("RGB").quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    im.save(os.path.join(dst, name), optimize=True)

def marks(name, overrides=None):
    d = json.load(open(os.path.join(here, "web", name + ".json"), encoding="utf-8"))
    boxes = [(m["x"], m["y"], m["w"], m["h"]) if m else None for m in d]
    for i, b in (overrides or {}).items(): boxes[i] = b
    return boxes

def annotate(src, out, boxes, crop=None):
    im = Image.open(os.path.join(here, "web", src + ".png")).convert("RGB")
    d = ImageDraw.Draw(im)
    for n, b in enumerate(boxes, 1):
        if b is None: continue
        x, y, w, h = b
        if w < 600: d.rectangle([x - 4, y - 3, x + w + 4, y + h + 3], outline=RED, width=3)
        cx, cy = max(14, x - 20), y + h / 2
        d.ellipse([cx - 13, cy - 13, cx + 13, cy + 13], fill=RED)
        t = str(n); tw = d.textlength(t, font=FB)
        d.text((cx - tw / 2, cy - 12), t, font=FB, fill="white")
    if crop: im = im.crop(crop)
    save(im, out + ".png")
    print(out)

def plain(src, out, crop=None):
    im = Image.open(os.path.join(here, "web", src + ".png"))
    if crop: im = im.crop(crop)
    save(im, out + ".png"); print(out)

hm = marks("home"); hm[2] = (884, 83, 34, 23)
annotate("home", "web-home", hm, crop=(0, 0, 1440, 420))
annotate("concl", "web-conclusion", marks("concl"))
plain("concl-traffic", "web-conclusion-traffic", crop=(828, 240, 1440, 1000))
plain("concl-limit", "web-limit", crop=(340, 660, 830, 1000))
annotate("compare", "web-compare", marks("compare"), crop=(330, 0, 1440, 1400))
cm = marks("cause"); cm[0] = (912, 124, 77, 30)
annotate("cause", "web-cause", cm)
plain("films", "web-films", crop=(330, 0, 1440, 520))
plain("status", "web-versions", crop=(330, 0, 1440, 520))
annotate("job-summary", "web-job-summary", marks("job-summary"))
plain("job-detail", "web-job-detail")
annotate("job-input", "web-job-input", marks("job-input"))
plain("job-files", "web-job-files", crop=(60, 0, 1440, 760))
plain("cuts", "web-cut-editor")
plain("film-page", "web-film-page")
plain("report", "web-report")
plain("film-frame", "film-frame")

for f in os.listdir(os.path.join(here, "caps")):
    if f.endswith(".png"): save(Image.open(os.path.join(here, "caps", f)), f)
for f in os.listdir(os.path.join(here, "figs")):
    if f.endswith(".png"): save(Image.open(os.path.join(here, "figs", f)), f)
print(sum(os.path.getsize(os.path.join(dst, f)) for f in os.listdir(dst)) // 1024, "KB")
