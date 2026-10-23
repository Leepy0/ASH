using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ASH.Core.AutoMod;
using ASH.Core.Film;

namespace ASH.Tests.Film;

public class AutoViewDiagnosticsTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(System.IntPtr h, string text, string caption, uint type);

    // a dialog AutoView might show (OK/Cancel, "확인"/"취소" on Korean Windows like 12.4's 비디오 압축), opened by this process: the diagnostics list it
    // with its buttons and text, and a title from Film:DialogTitles gets it clicked with OK, never Cancel
    [Fact]
    public void A_Stuck_Dialog_Is_Listed_And_A_Configured_Title_Clicks_Its_OK()
    {
        var title = "ASH 시험 창 " + System.Guid.NewGuid().ToString("N").Substring(0, 6);
        var answer = 0;
        var box = new Thread(() => answer = MessageBoxW(System.IntPtr.Zero, "The specified grid has over 1M lines", title, 1 /* MB_OKCANCEL */)) { IsBackground = true };
        box.Start();
        var pid = Process.GetCurrentProcess().Id;
        string? line = null;
        var sw = Stopwatch.StartNew();
        while (line == null && sw.Elapsed < System.TimeSpan.FromSeconds(10)) { line = AutoViewDiagnostics.Windows(pid).FirstOrDefault(w => w.Contains(title)); Thread.Sleep(100); }
        Assert.NotNull(line);
        Assert.Contains("[#32770]", line);
        Assert.Contains("버튼 ", line);
        Assert.Contains("over 1M lines", line);

        var clicked = new List<string>();
        using (DialogCloser.Start(pid, new[] { "시험 창" }, t => { lock (clicked) clicked.Add(t); }))
            Assert.True(box.Join(System.TimeSpan.FromSeconds(10)), "the dialog was not clicked");
        Assert.Equal(title, Assert.Single(clicked));
        Assert.Equal(1, answer); // IDOK: OK/확인 was pressed, not Cancel/취소 (WM_CLOSE would have given IDCANCEL)
    }

    // AutoView 12.4 writes an AVI, possibly under its own name (its dialog said "model.avi"): taken as the run's movie and kept apart from the next run's
    [Fact]
    public void An_Avi_Is_Taken_When_There_Is_No_Mp4()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_avi_" + System.Guid.NewGuid().ToString("N"));
        var avw = Path.Combine(dir, "m.avw");
        Directory.CreateDirectory(avw);
        try
        {
            string M(string name) => Path.Combine(dir, name);
            var old = M("cut01.avi"); File.WriteAllText(old, "earlier cut");
            File.SetLastWriteTimeUtc(old, System.DateTime.UtcNow.AddMinutes(-5));
            var since = System.DateTime.UtcNow;
            Assert.Null(AutoViewRenderer.Movie(M("cut02.mp4"), M("cut02.avi"), dir, avw, since)); // an older run's AVI is not this run's
            File.WriteAllText(M("model.avi"), "avi");
            Assert.Equal(M("cut02.avi"), AutoViewRenderer.Movie(M("cut02.mp4"), M("cut02.avi"), dir, avw, since));
            Assert.False(File.Exists(M("model.avi"))); // moved: the next run's model.avi cannot overwrite it
            File.WriteAllText(M("cut02.mp4"), "mp4");
            Assert.Equal(M("cut02.mp4"), AutoViewRenderer.Movie(M("cut02.mp4"), M("cut02.avi"), dir, avw, since)); // an MP4 wins
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Program_Names_The_Version_Or_Says_There_Is_None()
    {
        var self = Process.GetCurrentProcess().MainModule!.FileName!;
        Assert.Matches(@"\(버전 \d+\.\d+", AutoViewDiagnostics.Program(self));
        var plain = Path.GetTempFileName();
        try { Assert.EndsWith("(버전 정보 없음)", AutoViewDiagnostics.Program(plain)); }
        finally { File.Delete(plain); }
        Assert.EndsWith("(파일 없음)", AutoViewDiagnostics.Program(@"C:\nowhere\aview.exe"));
    }

    [Fact]
    public void Text_Reports_The_Files_AutoView_Left()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_diag_" + System.Guid.NewGuid().ToString("N"));
        var avw = Path.Combine(dir, "m.avw");
        Directory.CreateDirectory(Path.Combine(dir, "cut01_frames"));
        Directory.CreateDirectory(avw);
        try
        {
            File.WriteAllText(Path.Combine(avw, "camera.des"), "camera description");
            File.WriteAllText(Path.Combine(dir, "aviewout.dat"), "Reading camera\r\nCamera script duration is 1 seconds\r\n");
            File.WriteAllText(Path.Combine(dir, "aviewerr.dat"), "");
            for (int i = 0; i < 3; i++) File.WriteAllText(Path.Combine(dir, "cut01_frames", $"amov{i}.jpg"), "j");
            File.WriteAllText(Path.Combine(dir, "ffmpeg.cmd"), "@echo off");
            var t = AutoViewDiagnostics.Text(@"C:\nowhere\aview.exe", null, dir, avw, Path.Combine(dir, "cut01"), dir);
            Assert.StartsWith(@"진단: AutoView C:\nowhere\aview.exe (파일 없음)", t);
            Assert.DoesNotContain("창", t); // AutoView has ended: no window list
            Assert.Contains("서버 세션 ", t);
            Assert.Contains("camera.des 있음", t);
            Assert.Contains("aviewerr.dat 비어 있음", t);
            Assert.Contains("aviewout.dat \"Reading camera | Camera script duration is 1 seconds\"", t);
            Assert.Contains("convert.bat 없음", t);
            Assert.Contains("cut01.mp4 없음", t);
            Assert.Contains("avi 없음", t);
            Assert.Contains("프레임 JPG 3개", t);
            Assert.Contains("ffmpeg " + Path.Combine(dir, "ffmpeg"), t); // found on the path AutoView gets (here the .cmd unless a real ffmpeg.exe comes first)
        }
        finally { Directory.Delete(dir, true); }
    }
}
