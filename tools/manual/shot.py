# shot.py name url [w h] : Edge headless screenshot into web/<name>.png (retries: headless Edge sometimes exits without a file)
import subprocess, sys, os, time, uuid
here = os.path.dirname(os.path.abspath(__file__))
edge = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
name, url = sys.argv[1], sys.argv[2]
w, h = (sys.argv[3], sys.argv[4]) if len(sys.argv) > 4 else ("1440", "1000")
out = os.path.join(here, "web", name + ".png")
if os.path.exists(out): os.remove(out)
for attempt in range(5):
    prof = os.path.join(here, "edgeprof", uuid.uuid4().hex[:8])
    r = subprocess.run([edge, "--headless=new", "--disable-gpu", f"--user-data-dir={prof}", f"--window-size={w},{h}", f"--screenshot={out}", url],
                       timeout=120, capture_output=True, text=True, errors="replace")
    if os.path.exists(out): break
    time.sleep(2)
print(name, os.path.getsize(out) if os.path.exists(out) else "FAILED", attempt)
