# Drawing-step illustrations of the follow-along example (from gen's layout.json, metres), in the ASH layer colours on white.
import json, math, os, sys
from PIL import Image, ImageDraw, ImageFont

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
L = json.load(open(src, encoding="utf-8"))
rgb = {x["layer"]: tuple(x["rgb"]) for x in L["layers"]}
FONT = r"C:\Windows\Fonts\malgun.ttf"; FONTB = r"C:\Windows\Fonts\malgunbd.ttf"
f = lambda s, b=False: ImageFont.truetype(FONTB if b else FONT, s)
X0, X1, Y0, Y1, S, M, TOP = -8, 48, -10, 24, 20, 40, 44
W, H = int((X1 - X0) * S + 2 * M), int((Y1 - Y0) * S + 2 * M + TOP)
P = lambda x, y: (M + (x - X0) * S, TOP + M + (Y1 - y) * S)
GREY, LIGHT = (150, 150, 150), (236, 236, 236)
LABEL = {"COLUMN": "기둥", "RACK": "랙", "MACHINE": "설비"}

def base(title, sub):
    im = Image.new("RGB", (W, H), "white"); d = ImageDraw.Draw(im)
    for x in range(X0, X1 + 1, 2):
        d.line([P(x, Y0), P(x, Y1)], fill=LIGHT if x % 10 else (215, 215, 215))
    for y in range(Y0, Y1 + 1, 2):
        d.line([P(X0, y), P(X1, y)], fill=LIGHT if y % 10 else (215, 215, 215))
    for x in range(0, 41, 10): d.text((P(x, Y0)[0] - 8, P(x, Y0)[1] + 4), f"{x}", font=f(13), fill=GREY)
    for y in (0, 10, 20): d.text((M - 30, P(X0, y)[1] - 9), f"{y}", font=f(13), fill=GREY)
    d.text((W - M - 60, P(X0, Y0)[1] + 4), "x (m)", font=f(13), fill=GREY)
    d.rectangle([0, 0, W, TOP], fill=(245, 247, 250))
    d.text((14, 9), title, font=f(20, True), fill=(30, 30, 30))
    tw = d.textlength(title, font=f(20, True))
    d.text((24 + tw, 13), sub, font=f(15), fill=(110, 110, 110))
    d.text((W - 200, 13), "설명용 그림 (실제 화면 아님)", font=f(13), fill=(180, 60, 60))
    return im, d

def background(d, layer3d=False):
    for a in L["assets"]:
        if a["kind"] == "WALL":
            for x1, y1, x2, y2 in a["segs"]:
                d.line([P(x1, y1), P(x2, y2)], fill=rgb["AGV_3D_WALL"] if layer3d else GREY, width=6 if layer3d else 2)
        elif a["fp"]:
            x, y = a["at"]; a0, b0, a1, b1 = a["fp"]
            box = [P(x + a0, y + b1), P(x + a1, y + b0)]
            if layer3d:
                d.rectangle(box, fill=(225, 225, 225), outline=rgb["AGV_3D"], width=2)
                d.text((box[0][0], box[0][1] - 18 if a["kind"] != "COLUMN" else box[1][1] + 2), LABEL.get(a["kind"], a["kind"]), font=f(13), fill=(90, 90, 90))
            else:
                d.rectangle(box, outline=GREY, width=1)

def arrow(d, a, b, col, t=0.3):
    (x1, y1), (x2, y2) = P(*a), P(*b)
    mx, my = x1 + (x2 - x1) * t, y1 + (y2 - y1) * t; L_ = math.hypot(x2 - x1, y2 - y1); ux, uy = (x2 - x1) / L_, (y2 - y1) / L_
    s = 13
    d.polygon([(mx + ux * s, my + uy * s), (mx - ux * s - uy * s * 0.7, my - uy * s + ux * s * 0.7), (mx - ux * s + uy * s * 0.7, my - uy * s - ux * s * 0.7)], fill=col)

def paths(d, names=True, nodes=False):
    col = rgb["AGV_PATH"]
    for p in L["paths"]:
        d.line([P(*p["start"]), P(*p["end"])], fill=col, width=4)
        arrow(d, p["start"], p["end"], col, 0.45 if p["name"] == "P002" else 0.3)
        if names:
            (x1, y1), (x2, y2) = P(*p["start"]), P(*p["end"])
            mx, my = (x1 + x2) / 2, (y1 + y2) / 2
            off = (8, -26) if y1 == y2 else (10, 12)
            if p["name"] == "P007": off = (10, -40)
            d.text((mx + off[0], my + off[1]), p["name"], font=f(14, True), fill=(60, 60, 60))
    if nodes:
        for x, y, lab in [(0, 0, "A"), (20, 0, "B"), (40, 0, "C"), (40, 16, "D"), (20, 16, "E"), (0, 16, "F")]:
            px, py = P(x, y); d.ellipse([px - 5, py - 5, px + 5, py + 5], fill=(40, 40, 40))
            d.text((px + 6, py + (6 if y == 0 else -24)), f"{lab} ({x},{y})", font=f(14, True), fill=(20, 20, 20))

def cps(d, traffic=True, labels=True):
    for c in L["cps"]:
        if c["type"] == "traffic" and not traffic: continue
        col = rgb.get("AGV_CP_" + c["type"].upper(), (200, 0, 0))
        px, py = P(*c["at"]); r = 8
        d.ellipse([px - r, py - r, px + r, py + r], outline=col, width=3, fill="white")
        d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=col)
        if labels:
            x, y = c["at"]
            dx, dy = (12, 8) if y == 0 else (12, -26)
            if c["name"] in ("S001", "F001", "T002"): dx, dy = (12, -10)
            if c["name"] == "T001": dx, dy = (-30, -28)
            d.text((px + dx, py + dy), c["name"], font=f(14, True), fill=col)

def blocks(d):
    col = rgb["AGV_BLOCK"]
    for b in L["blocks"]:
        a, c = P(b["min"][0], b["max"][1]), P(b["max"][0], b["min"][1])
        d.rectangle([a, c], outline=col, width=3)
        d.text((a[0] - 44, a[1] - 20 if b["min"][1] > 5 else c[1] + 2), b["name"], font=f(14, True), fill=col)

def legend(d, items):
    x, y = M + 4, H - 30
    for name, col in items:
        d.rectangle([x, y + 4, x + 14, y + 16], fill=col); d.text((x + 20, y), name, font=f(13), fill=(60, 60, 60))
        x += 32 + d.textlength(name, font=f(13))

def save(im, name): im.save(os.path.join(out, name), optimize=True); print(name)

im, d = base("1단계  배경 도면", "벽·기둥·랙·설비 (background.dxf)"); background(d); save(im, "step0-background.png")
im, d = base("2단계  경로 그리기", "AGVPATH 단방향 7개 · 화살표 = 진행 방향"); background(d); paths(d, nodes=True)
legend(d, [("AGV_PATH (단방향 경로)", rgb["AGV_PATH"])]); save(im, "step1-paths.png")
im, d = base("3단계  CP 놓기", "standby·finish·load·unload (GROUP G1)"); background(d); paths(d, names=False); cps(d, traffic=False)
legend(d, [(t + " CP", rgb["AGV_CP_" + t.upper()]) for t in ("standby", "finish", "load", "unload")]); save(im, "step2-cps.png")
im, d = base("4단계  교통제어", "AGVTRAFFIC: block B001·B002 + traffic CP T001~T003"); background(d); paths(d, names=False); cps(d); blocks(d)
legend(d, [("block", rgb["AGV_BLOCK"]), ("traffic CP", rgb["AGV_CP_TRAFFIC"])]); save(im, "step3-traffic.png")
im, d = base("5단계  3D 꾸미기 (선택)", "벽 AGV_3D_WALL · 기둥·랙·설비 ASH_3D_* 블록 — 결과에 영향 없음"); background(d, layer3d=True); paths(d, names=False); cps(d, labels=False); blocks(d)
legend(d, [("AGV_3D_WALL (벽)", rgb["AGV_3D_WALL"]), ("AGV_3D (3D 블록)", rgb["AGV_3D"])]); save(im, "step4-3d.png")
