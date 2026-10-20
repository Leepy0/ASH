using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ASH.Core.AutoMod
{
    public static class DialogCloser
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern int GetWindowTextLengthW(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
        const uint WM_CLOSE = 0x0010, BM_CLICK = 0x00F5;

        static string Text(IntPtr h)
        {
            var sb = new StringBuilder(GetWindowTextLengthW(h) + 1);
            GetWindowTextW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // shortcut: matches windows of ownerPid only (no child-process tree); extend if am264 spawns a dialog-owning child.
        /// <summary>buttonText null: click "OK" (WM_CLOSE fallback), skipping Authorization/Error windows. Otherwise click the child button with that text (mnemonic & ignored) and nothing else.</summary>
        public static IDisposable Start(int ownerPid, string[] titleContains, Action<string> onClosed, string? buttonText = null)
        {
            var seen = new System.Collections.Generic.HashSet<IntPtr>();
            Timer? timer = null;
            // one-shot, re-armed after each poll: polls never overlap
            timer = new Timer(_ =>
            {
                try { Poll(ownerPid, titleContains, buttonText, onClosed, seen); }
                catch (Exception) { } // an exception escaping a Timer callback kills the host process (inside ZWCAD: CAD itself)
                try { timer!.Change(250, Timeout.Infinite); } catch (ObjectDisposedException) { }
            }, null, Timeout.Infinite, Timeout.Infinite);
            timer.Change(0, Timeout.Infinite);
            return timer;
        }

        static void Poll(int pid, string[] needles, string? button, Action<string> onClosed, System.Collections.Generic.HashSet<IntPtr> seen)
        {
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out var p);
                if (p != (uint)pid || !IsWindowVisible(h)) return true;
                var t = Text(h);
                if (t.Length == 0) return true;
                if (button == null && (t.IndexOf("Authorization", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
                foreach (var n in needles)
                    if (t.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // AM's dialog ignores WM_CLOSE; click its button, WM_CLOSE as fallback for the OK case only.
                        IntPtr ok = IntPtr.Zero;
                        var want = button ?? "OK";
                        EnumChildWindows(h, (c, _) => { if (Text(c).Replace("&", "") == want) ok = c; return true; }, IntPtr.Zero);
                        if (ok != IntPtr.Zero) SendMessageW(ok, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        else if (button == null) SendMessageW(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        else break; // button not created yet: retry next poll
                        bool first;
                        lock (seen) first = seen.Add(h);
                        if (first) onClosed(t);
                        break;
                    }
                return true;
            }, IntPtr.Zero);
        }
    }
}
