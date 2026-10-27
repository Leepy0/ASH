// node cdp.mjs shots.json : headless Edge driven over the DevTools protocol (Node's built-in WebSocket).
// Each shot: { name, url, w, h, js?, wait?, full? } -> web/<name>.png. js runs after load (may return a promise), then wait ms.
import { spawn } from "node:child_process";
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const shots = JSON.parse(readFileSync(process.argv[2], "utf8"));
const port = 9333;
const edge = "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
const prof = join(here, "edgeprof", "cdp" + Date.now());
mkdirSync(join(here, "web"), { recursive: true });
const proc = spawn(edge, ["--headless=new", "--disable-gpu", `--remote-debugging-port=${port}`, `--user-data-dir=${prof}`, "--window-size=1440,1000", "about:blank"], { stdio: "ignore" });
const sleep = ms => new Promise(r => setTimeout(r, ms));

let targets;
for (let i = 0; i < 50; i++) {
  try { targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json(); if (targets.some(t => t.type === "page")) break; } catch { }
  await sleep(200);
}
const page = targets.find(t => t.type === "page");
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener("open", r, { once: true }));
let id = 0; const pending = new Map(); const waiters = [];
ws.addEventListener("message", ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  else if (m.method) for (const w of waiters.splice(0)) w(m);
});
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const loaded = () => new Promise(r => { const f = m => m.method === "Page.loadEventFired" ? r() : waiters.push(f); waiters.push(f); });
await send("Page.enable");

for (const s of shots) {
  await send("Emulation.setDeviceMetricsOverride", { width: s.w || 1440, height: s.h || 1000, deviceScaleFactor: 1, mobile: false });
  const l = loaded();
  await send("Page.navigate", { url: s.url });
  await Promise.race([l, sleep(15000)]);
  await sleep(s.pre ?? 800);
  if (s.js) {
    const r = await send("Runtime.evaluate", { expression: s.js, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) console.log(s.name, "js error", JSON.stringify(r.result.exceptionDetails).slice(0, 300));
  }
  await sleep(s.wait ?? 600);
  let params = { format: "png" };
  if (s.full) {
    const r = await send("Runtime.evaluate", { returnByValue: true, expression: "Math.min(7000, Math.ceil(Math.max(document.documentElement.scrollHeight, ...[...document.querySelectorAll('*')].filter(e => e.scrollHeight > e.clientHeight + 5 && /auto|scroll/.test(getComputedStyle(e).overflowY)).map(e => e.scrollHeight + e.getBoundingClientRect().top + 24))))" });
    const h = r.result.result.value;
    await send("Emulation.setDeviceMetricsOverride", { width: s.w || 1440, height: h, deviceScaleFactor: 1, mobile: false });
    await sleep(500);
  }
  if (s.marks) {
    // each mark: { t: text the element starts with, up?: css selector of the ancestor to outline, n?: nth match }
    const fn = `(${JSON.stringify(s.marks)}).map(m => {
      const all = [...document.querySelectorAll('body *')].filter(e => e.offsetParent !== null || e.tagName === 'svg');
      const hits = all.filter(e => (e.textContent || '').trim().startsWith(m.t)).sort((a, b) => a.textContent.length - b.textContent.length);
      let e = hits.filter(x => !hits.some(y => y !== x && x.contains(y) && y.textContent.trim().startsWith(m.t)))[m.n || 0];
      if (e && m.up) e = e.closest(m.up) || e;
      if (!e) return null;
      const r = e.getBoundingClientRect();
      return { t: m.t, x: r.left + scrollX, y: r.top + scrollY, w: r.width, h: r.height };
    })`;
    const r = await send("Runtime.evaluate", { expression: fn, returnByValue: true });
    writeFileSync(join(here, "web", s.name + ".json"), JSON.stringify(r.result.result.value, null, 1));
  }
  const shot = await send("Page.captureScreenshot", params);
  writeFileSync(join(here, "web", s.name + ".png"), Buffer.from(shot.result.data, "base64"));
  console.log(s.name, "ok");
}
ws.close();
proc.kill();
process.exit(0);
