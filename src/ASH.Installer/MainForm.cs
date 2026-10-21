using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Install;
using ASH.Core.Settings;

namespace ASH.Installer;

sealed class MainForm : Form
{
    static readonly Color Accent = ColorTranslator.FromHtml("#2F6FED");
    static readonly Color Back = ColorTranslator.FromHtml("#F7F8FA");
    static readonly Color Hover = ColorTranslator.FromHtml("#E8EEFB");
    static readonly Color Line = ColorTranslator.FromHtml("#D9DDE3");
    static readonly Color Muted = ColorTranslator.FromHtml("#6B7280");
    // same text as README 설치 section
    static readonly string[] NextSteps =
    {
        "ZWCAD를 완전히 종료한 뒤 다시 시작하세요.",
        "탭이 안 보이면 명령줄에 AGVRIBBON 또는 AGVPALETTE.",
        "첫 사용: 도면 저장 → AGVINIT. mm 도면이면 ash.settings.tsv의 Scale을 0.001로.",
    };
    const string NotFound = "ZWCAD 설치를 찾지 못했습니다. 직접 지정을 누르세요.";

    readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, CheckOnClick = true, IntegralHeight = false };
    readonly Label _notice = new() { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 6, 0, 0) };
    readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9f) };
    readonly Button _install, _uninstall;
    readonly TextBox _server = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };

    /// <summary>Exit code of the last 설치/제거 (0 ok, 1 failure, 2 warnings), same as /silent.</summary>
    public int ExitCode { get; private set; }

    sealed class Item
    {
        public readonly ZwcadInstall Install;
        public Item(ZwcadInstall i) => Install = i;
        // unknown version stays selectable: the planner copies DLLs and warns that autoload was not registered
        public bool Supported => Install.Version.Length == 0 || Install.Version == Program.SupportedVersion;
        public override string ToString() =>
            "ZWCAD " + (Install.Version.Length > 0 ? Install.Version : "(버전 모름)")
            + (Install.Language.Length > 0 ? " (" + Install.Language + ")" : "") + " — " + Install.InstallDir
            + (Supported ? "" : " (미지원: ASH는 ZWCAD " + Program.SupportedVersion + "용)");
    }

    public MainForm(List<ZwcadInstall> found, bool uninstallMode, string serverUrl)
    {
        Text = "ASH 설치 — AGV Simul Helper";
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Back;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 520);
        MinimumSize = new Size(520, 420);
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label { Text = "ASH 설치", AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), Margin = new Padding(0) };
        var sub = new Label { Text = "ZWCAD에 ASH 플러그인을 넣고 시작할 때 자동으로 불러오게 등록합니다.", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 2, 0, 12) };

        var add = MakeButton("직접 지정…", false);
        add.Click += (_, _) => AddManual();
        _install = MakeButton("설치", true);
        _install.Click += (_, _) => RunSelected(false);
        _uninstall = MakeButton("제거", false);
        _uninstall.Click += (_, _) => RunSelected(true);
        var close = MakeButton("닫기", false);
        close.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
        buttons.Controls.AddRange(new Control[] { close, _uninstall, _install });
        add.Margin = new Padding(0);
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 10, 0, 10) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.Controls.Add(add, 0, 0);
        bar.Controls.Add(buttons, 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 16), ColumnCount = 1 };
        _server.Text = serverUrl;
        foreach (var h in new float[] { -1, -1, -1, 55, -1, -1, -1, -1, -1, 45 })
            root.RowStyles.Add(h < 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, h));
        root.Controls.Add(title);
        root.Controls.Add(sub);
        root.Controls.Add(Section("찾은 ZWCAD"));
        root.Controls.Add(Framed(_list));
        root.Controls.Add(_notice);
        root.Controls.Add(Section("서버 주소 (선택) — 업데이트 확인용, 예: http://server:8082. 비워 두면 기존 설정 유지"));
        root.Controls.Add(_server);
        root.Controls.Add(bar);
        root.Controls.Add(Section("로그"));
        root.Controls.Add(Framed(_log));
        Controls.Add(root);

        foreach (var i in found) { var it = new Item(i); _list.Items.Add(it, it.Supported); }
        UpdateState();
        var main = uninstallMode ? _uninstall : _install;
        AcceptButton = main;
        ActiveControl = main;
    }

    static Label Section(string text) => new()
    { Text = text, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI Semibold", 8.25f), Margin = new Padding(0, 4, 0, 4) };

    // 1px light border around a borderless control
    static Panel Framed(Control c)
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Line, Margin = new Padding(0) };
        var inner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 4), BackColor = Color.White };
        c.BackColor = Color.White;
        inner.Controls.Add(c);
        p.Controls.Add(inner);
        return p;
    }

    static Button MakeButton(string text, bool primary)
    {
        var b = new Button
        {
            Text = text, FlatStyle = FlatStyle.Flat, AutoSize = true, MinimumSize = new Size(88, 36),
            Padding = new Padding(10, 0, 10, 0), Margin = new Padding(8, 0, 0, 0), Cursor = Cursors.Hand,
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(31, 41, 55),
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = Line;
        b.FlatAppearance.MouseOverBackColor = primary ? ColorTranslator.FromHtml("#2560D6") : Hover;
        return b;
    }

    void UpdateState()
    {
        _notice.Text = _list.Items.Count == 0 ? NotFound : _list.Items.Count + "개 찾음. 설치할 ZWCAD를 선택하세요.";
        _install.Enabled = _uninstall.Enabled = true; // empty selection is answered by RunSelected
    }

    void AddManual()
    {
        using var d = new FolderBrowserDialog { Description = "ZWCAD.exe가 있는 ZWCAD 설치 폴더를 고르세요." };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var m = Program.Manual(d.SelectedPath);
        if (m == null) { MessageBox.Show(this, "이 폴더에 ZWCAD.exe가 없습니다.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        // without a 20xx version autoload cannot be registered, so do not add the folder until one is given
        var prompt = "ZWCAD 버전(연도)을 입력하세요. 예: 2026";
        while (m.Version.Length == 0)
        {
            var v = Microsoft.VisualBasic.Interaction.InputBox(prompt, Text, "2026").Trim();
            if (v.Length == 0) return; // cancelled
            if (System.Text.RegularExpressions.Regex.IsMatch(v, @"^20\d{2}$")) m = m with { Version = v, ProfileKeyPath = @"Software\ZWSoft\ZWCAD\" + v };
            else prompt = "버전은 20xx 형식이어야 합니다(예: 2026). 다시 입력하세요.";
        }
        var item = new Item(m);
        _list.Items.Add(item, item.Supported);
        UpdateState();
    }

    void RunSelected(bool uninstall)
    {
        var targets = _list.CheckedItems.Cast<Item>().Select(i => i.Install).Distinct().ToList();
        if (targets.Count == 0) { MessageBox.Show(this, "ZWCAD를 하나 이상 선택하세요.", Text); return; }
        if (StepRunner.ZwcadRunning()) { MessageBox.Show(this, StepRunner.CloseZwcad, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        string? server = null;
        if (!uninstall && _server.Text.Trim().Length > 0)
        {
            if (!UserSettings.TryNormalizeUrl(_server.Text, out server)) { MessageBox.Show(this, "서버 주소는 http:// 또는 https://로 시작해야 합니다. 예: http://server:8082", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); _server.Focus(); return; }
        }
        if (uninstall && MessageBox.Show(this, "선택한 ZWCAD에서 ASH를 제거할까요?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        UseWaitCursor = true;
        _install.Enabled = _uninstall.Enabled = false;
        StepRunner? r = null;
        try
        {
            r = new StepRunner(line => { _log.AppendText(line + Environment.NewLine); _log.Update(); });
            if (uninstall) r.Uninstall(targets);
            else { r.Install(targets); if (server != null && r.ExitCode != 1) r.SaveServerUrl(server); }
            ExitCode = r.ExitCode;
        }
        catch (Exception ex)
        {
            if (r != null) r.Log("오류: " + ex.Message); else _log.AppendText("오류: " + ex.Message + Environment.NewLine);
            ExitCode = 1;
        }
        finally { r?.Dispose(); UseWaitCursor = false; UpdateState(); }

        if (ExitCode == 1)
        {
            MessageBox.Show(this, "일부 작업이 실패했습니다. 로그를 확인하세요: " + StepRunner.LogPath, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var notes = new List<string>();
        if (r!.AutoloadWarnings.Count > 0)
            notes.Add("경고:" + Environment.NewLine + string.Join(Environment.NewLine, r.AutoloadWarnings));
        notes.AddRange(r.SessionWarnings);
        notes.AddRange(r.FallbackDirs.Select(d => "ZWCAD 폴더에 쓸 수 없어 DLL을 " + d + "에 설치했습니다."));
        var done = uninstall ? "제거 완료. ZWCAD를 다시 시작하세요." : "완료. ZWCAD를 다시 시작하면 ASH 탭과 AGV* 명령을 쓸 수 있습니다.";
        var text = notes.Count == 0 ? done : string.Join(Environment.NewLine + Environment.NewLine, notes);
        if (!uninstall) text += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, NextSteps);
        MessageBox.Show(this, text, Text, MessageBoxButtons.OK, notes.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }
}
