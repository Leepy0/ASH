# Builds manual.html (one self-contained page, images embedded) from README.md and the chapter files.
# Only the Markdown this manual uses: headings, paragraphs, lists, tables, code blocks, inline code, bold, italic, links, images.
# Usage: python build_html.py   (run in docs/manual; no packages needed)
import base64, html, mimetypes, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
FILES = ["README.md", "01-start.md", "02-tutorial.md", "03-drawing.md", "04-input-model.md", "05-results.md", "06-faq.md"]
CHAPTER = {f: "ch-" + f[:-3].lower() for f in FILES}


def slug(text):
    t = re.sub(r"<[^>]+>", "", text).strip().lower()
    t = re.sub(r"[^\w\- ]", "", t)
    return t.replace(" ", "-")


def data_uri(path):
    mime = mimetypes.guess_type(path)[0] or "application/octet-stream"
    with open(path, "rb") as f:
        return f"data:{mime};base64," + base64.b64encode(f.read()).decode("ascii")


def link(url):
    if re.match(r"^[a-z]+:", url) or url.startswith("#"):
        return url
    path, _, frag = url.partition("#")
    if path in CHAPTER:  # another chapter of this manual: an anchor in this page
        return "#" + (frag or CHAPTER[path])
    return url  # anything else stays relative to docs/manual


def inline(text, base):
    codes = []
    def keep(m):
        codes.append("<code>" + html.escape(m.group(1)) + "</code>")
        return f"\x00{len(codes) - 1}\x00"
    text = re.sub(r"`([^`]+)`", keep, text)
    text = html.escape(text, quote=False)
    def img(m):
        src = os.path.normpath(os.path.join(base, m.group(2)))
        return f'<img alt="{html.escape(m.group(1))}" src="{data_uri(src)}">'
    text = re.sub(r"!\[([^\]]*)\]\(([^)]+)\)", img, text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", lambda m: f'<a href="{html.escape(link(m.group(2)))}">{m.group(1)}</a>', text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"<em>\1</em>", text)
    return re.sub(r"\x00(\d+)\x00", lambda m: codes[int(m.group(1))], text)


def cells(row):
    # split on | outside code spans
    out, cur, code = [], "", False
    for ch in row.strip().strip("|"):
        if ch == "`": code = not code
        if ch == "|" and not code:
            out.append(cur.strip()); cur = ""
        else:
            cur += ch
    out.append(cur.strip())
    return out


def convert(md, base, chapter_id):
    lines, out, i, first = md.split("\n"), [], 0, True
    while i < len(lines):
        line = lines[i].rstrip()
        if not line.strip():
            i += 1; continue
        if line.startswith("```"):
            j = i + 1
            while j < len(lines) and not lines[j].startswith("```"): j += 1
            out.append("<pre>" + html.escape("\n".join(lines[i + 1:j])) + "</pre>")
            i = j + 1; continue
        m = re.match(r"^(#{1,4}) (.*)$", line)
        if m:
            n, text = len(m.group(1)), m.group(2)
            hid = chapter_id if first and n == 1 else slug(text.replace("`", ""))
            first = False
            out.append(f'<h{n} id="{html.escape(hid)}">{inline(text, base)}</h{n}>')
            i += 1; continue
        if line.startswith("|"):
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append(cells(lines[i])); i += 1
            head, body = rows[0], [r for r in rows[1:] if not all(re.fullmatch(r":?-{3,}:?", c) for c in r)]
            t = "<table><thead><tr>" + "".join(f"<th>{inline(c, base)}</th>" for c in head) + "</tr></thead><tbody>"
            t += "".join("<tr>" + "".join(f"<td>{inline(c, base)}</td>" for c in r) + "</tr>" for r in body)
            out.append(t + "</tbody></table>"); continue
        if re.match(r"^(\s*)([-*]|\d+\.) ", line):
            tag = "ol" if re.match(r"^\s*\d+\.", line) else "ul"
            items = []
            while i < len(lines) and re.match(r"^\s*([-*]|\d+\.) ", lines[i]):
                items.append(re.sub(r"^\s*([-*]|\d+\.) ", "", lines[i].rstrip())); i += 1
            out.append(f"<{tag}>" + "".join(f"<li>{inline(x, base)}</li>" for x in items) + f"</{tag}>"); continue
        if line.startswith(">"):
            block = []
            while i < len(lines) and lines[i].startswith(">"):
                block.append(lines[i].lstrip("> ").rstrip()); i += 1
            out.append("<blockquote>" + inline(" ".join(block), base) + "</blockquote>"); continue
        para = []
        while i < len(lines) and lines[i].strip() and not re.match(r"^(#{1,4} |\||```|\s*([-*]|\d+\.) |>)", lines[i]):
            para.append(lines[i].strip()); i += 1
        text = " ".join(para)
        cls = ' class="cap"' if re.fullmatch(r"\*[^*].*\*", text) else ""
        only_img = re.fullmatch(r"!\[[^\]]*\]\([^)]+\)", text)
        out.append(f'<p class="fig">{inline(text, base)}</p>' if only_img else f"<p{cls}>{inline(text, base)}</p>")
    return "\n".join(out)


CSS = """
:root{--fg:#1d2330;--mut:#5b6474;--line:#d9dee7;--acc:#0b63ce;--bg:#fff;--soft:#f4f6fa}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.65 "Malgun Gothic","맑은 고딕","Segoe UI",sans-serif}
main{max-width:1000px;margin:0 auto;padding:24px 20px 80px}
nav.toc{background:var(--soft);border:1px solid var(--line);border-radius:8px;padding:12px 18px;margin:16px 0 28px}
nav.toc a{display:block;color:var(--acc);text-decoration:none;padding:2px 0}
h1{font-size:26px;margin:48px 0 12px;padding-bottom:6px;border-bottom:2px solid var(--fg)}
h2{font-size:20px;margin:34px 0 10px;padding-bottom:4px;border-bottom:1px solid var(--line)}
h3{font-size:17px;margin:24px 0 8px}h4{font-size:15px;margin:18px 0 6px}
a{color:var(--acc)}
code{font:13px Consolas,"D2Coding",monospace;background:var(--soft);border:1px solid var(--line);border-radius:4px;padding:0 4px}
pre{background:var(--soft);border:1px solid var(--line);border-radius:6px;padding:10px 14px;overflow-x:auto;font:13px/1.5 Consolas,monospace}
table{border-collapse:collapse;margin:10px 0 16px;width:100%;font-size:14px}
th,td{border:1px solid var(--line);padding:5px 9px;vertical-align:top;text-align:left}
th{background:var(--soft)}
td img{max-width:100%}
img{max-width:100%;height:auto;border:1px solid var(--line);border-radius:4px}
p.fig{margin:12px 0 4px}
p.cap{color:var(--mut);font-size:13px;margin-top:2px}
blockquote{margin:10px 0;padding:6px 14px;border-left:4px solid var(--acc);background:var(--soft)}
@media print{
 body{font-size:11pt}main{max-width:none;padding:0}
 nav.toc{break-after:page}
 h1{break-before:page}h1,h2,h3{break-after:avoid}
 img,table,pre{break-inside:avoid}
 a{color:inherit;text-decoration:none}
}
"""


def main():
    parts, toc = [], []
    for f in FILES:
        md = open(os.path.join(HERE, f), encoding="utf-8").read()
        title = re.search(r"^# (.+)$", md, re.M).group(1)
        if f != "README.md": toc.append(f'<a href="#{CHAPTER[f]}">{html.escape(title)}</a>')
        parts.append(f'<section>{convert(md, HERE, CHAPTER[f])}</section>')
    body = parts[0] + '<nav class="toc"><b>차례</b>' + "".join(toc) + "</nav>" + "".join(parts[1:])
    page = f'<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">' \
           f"<title>ASH 사용 설명서</title><style>{CSS}</style></head><body><main>{body}</main></body></html>"
    out = os.path.join(HERE, "manual.html")
    with open(out, "w", encoding="utf-8", newline="\n") as fh: fh.write(page)
    # self-check: every in-page link has a target
    ids = set(re.findall(r'id="([^"]+)"', page))
    missing = sorted({h[1:] for h in re.findall(r'href="(#[^"]+)"', page)} - {html.escape(x) for x in ids})
    assert not missing, "links without a target: " + ", ".join(missing)
    print(f"{out} ({os.path.getsize(out) // 1024} KB)")


if __name__ == "__main__":
    main()
