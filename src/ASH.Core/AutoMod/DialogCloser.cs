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
        [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr h, int id);
        const uint WM_CLOSE = 0x0010, BM_CLICK = 0x00F5;
        const int IDOK = 1;
        static readonly string[] OkTexts = { "OK", "확인" }, CancelTexts = { "Cancel", "취소" };
        static bool Is(string text, string[] names) => Array.Exists(names, n => string.Equals(text.Replace("&", "").Trim(), n, StringComparison.OrdinalIgnoreCase));

        static string Text(IntPtr h)
        {
            var sb = new StringBuilder(GetWindowTextLengthW(h) + 1);
            GetWindowTextW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // shortcut: matches windows of ownerPid only (no child-process tree); extend if am264 spawns a dialog-owning child.
        /// <summary>buttonText null: click OK — a button "OK"/"확인" (Korean Windows' Video Compression dialog), else the IDOK control unless it reads Cancel/취소 —
        /// skipping Authorization/Error windows; WM_CLOSE (= Cancel) only for a window with no Cancel button, so a dialog with OK/Cancel is never cancelled.
        /// Otherwise click the child button with that text (mnemonic & ignored) and nothing else.</summary>
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
                        var cancel = false;
                        EnumChildWindows(h, (c, _) =>
                        {
                            var ct = Text(c);
                            if (button != null ? ct.Replace("&", "") == button : Is(ct, OkTexts)) ok = c;
                            cancel |= Is(ct, CancelTexts);
                            return true;
                        }, IntPtr.Zero);
                        if (ok == IntPtr.Zero && button == null && GetDlgItem(h, IDOK) is var id && id != IntPtr.Zero && !Is(Text(id), CancelTexts)) ok = id;
                        if (ok != IntPtr.Zero) SendMessageW(ok, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        else if (button == null && !cancel) SendMessageW(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        else break; // button not created yet (or only Cancel-like buttons): retry next poll, never cancel
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
