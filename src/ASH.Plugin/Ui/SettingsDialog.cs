using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Model;
using ASH.Core.Settings;

namespace ASH.Plugin.Ui;

/// <summary>Form for &lt;drawing folder&gt;\ash.settings.tsv plus this PC's server address (ash.user.tsv, as AGVSERVER saves it).
/// OK checks every field with SettingsFile.Parse, the rules the file is read with, and saves both files; Cancel writes nothing. Touches no ZWCAD type.</summary>
public sealed class SettingsDialog : Form
{
    private const string ServerKey = "ServerUrl";

    // key (as in the file), label, hint; a null key starts a section
    private static readonly (string? Key, string Label, string Hint)[] Fields =
    {
        (null, "모델 · AutoMod", ""),
        ("ModelName", "모델 이름", "도면 폴더에 <이름>.arc(모델 파일)와 <이름>.dir(빌드 결과)을 만듭니다. 공백 없이"),
        ("AmHome", "AutoMod 설치 폴더", @"bin\am264.exe가 있는 폴더. 이 PC에 없으면 모델 파일만 만들고 빌드는 서버가 합니다"),
        ("Units", "AutoMod 거리 단위", "모델 안의 거리 단위. 보통 Meters"),
        (null, "도면 좌표", ""),
        ("Scale", "도면 단위 → m 배율", "도면 1단위가 몇 m인지. mm 도면 0.001, m 도면 1"),
        ("OriginX", "원점 X", "이 도면 좌표(도면 단위)가 모델의 (0, 0)이 됩니다"),
        ("OriginY", "원점 Y", ""),
        ("ArrowSize", "방향표 길이 (m)", "경로 방향 화살표 크기. 다음 경로 편집 때 반영됩니다"),
        (null, "교통제어 자동 생성 (단위 m)", ""),
        ("VehicleLength", "차량 길이", "block 크기·합치기 판단에 씁니다"),
        ("VehicleWidth", "차량 폭", "block을 경로 양옆으로 폭/2 + 안전 거리만큼 넓힙니다"),
        ("SafetyGap", "안전 거리", "차량끼리 두는 여유"),
        ("ClaimDist", "점유 거리", "빈칸 = 안전 거리. traffic CP를 block 앞 이 거리에 둡니다"),
        ("BlockReach", "block 길이", "빈칸 = 교차 차량이 비켜 갈 만큼만(자동)"),
        ("CpSpacing", "CP 간격", "일정 간격 CP 생성용(지금은 쓰지 않음)"),
        (null, "차량 그룹", ""),
        ("Groups", "Groups (읽기 전용)", "Excel(input.xlsx)의 AGV_Groups가 대신합니다. Excel 없이 내보낼 때만 쓰며, 고치려면 텍스트로 여세요"),
        (null, "서버 (이 PC 공통)", ""),
        (ServerKey, "서버 주소", "예: http://server:8082. 서버로 보내기·업데이트 확인에 씁니다. 빈칸 = 설정 안 함"),
    };

    private static readonly Font HintFont = new Font("Segoe UI", 8.25f);

    private readonly string _path;
    private readonly string _url;
    private readonly Dictionary<string, Control> _boxes = new Dictionary<string, Control>();

    public SettingsDialog(string path, ProjectSettings current, string serverUrl)
    {
        _path = path;
        _url = serverUrl;
        Text = "ASH 설정";
        Font = Theme.Base;
        ForeColor = Theme.Text;
        BackColor = Theme.Panel;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 660);

        var values = SettingsFile.Lines(current).Select(l => l.Split(new[] { '\t' }, 2)).ToDictionary(a => a[0], a => a[1]);
        values[ServerKey] = serverUrl;

        // right padding leaves room for the vertical scroll bar, so no horizontal one appears
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, BackColor = Theme.Panel, Padding = new Padding(16, 4, 16 + SystemInformation.VerticalScrollBarWidth, 12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        void Span(Control c) { table.Controls.Add(c, 0, row++); table.SetColumnSpan(c, 2); }
        Span(new Label { Text = path, Font = HintFont, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(2, 8, 0, 0) });
        foreach (var (key, label, hint) in Fields)
        {
            if (key == null) { Span(Theme.Section(label)); continue; }
            Control box = key == "Units"
                ? new ComboBox { Items = { "Meters", "Feet" }, DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.System }
                : new TextBox { BorderStyle = BorderStyle.FixedSingle, ReadOnly = key == "Groups" };
            box.Text = values[key];
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 4, 0, 0);
            _boxes[key] = box;
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 6, 8, 0) }, 0, row);
            table.Controls.Add(box, 1, row++);
            // the file key too: Parse's error messages name it
            table.Controls.Add(new Label { Text = key + (hint.Length > 0 ? " · " + hint : ""), Font = HintFont, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(360, 0), Margin = new Padding(0, 1, 0, 2) }, 1, row++);
        }

        var ok = Flat("확인", Theme.Accent, Color.White);
        var cancel = Flat("취소", Theme.Panel, Theme.Text);
        cancel.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        cancel.FlatAppearance.BorderSize = 1;
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (o, e) => Save();
        var text = new LinkLabel { Text = "텍스트로 열기", AutoSize = true, LinkColor = Theme.Accent, Location = new Point(16, 18) };
        new ToolTip().SetToolTip(text, "메모장으로 엽니다. 이 창에서 바꾼 값은 저장하지 않습니다.");
        text.LinkClicked += (o, e) =>
        {
            try { Process.Start("notepad.exe", "\"" + _path + "\""); Close(); }
            catch (System.ComponentModel.Win32Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Bg };
        bar.Controls.AddRange(new Control[] { text, ok, cancel });
        bar.Layout += (o, e) =>
        {
            cancel.Location = new Point(bar.ClientSize.Width - 16 - cancel.Width, 11);
            ok.Location = new Point(cancel.Left - 8 - ok.Width, 11);
        };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(table); // Fill first, then the bottom bar docks before it
        Controls.Add(bar);
    }

    private void Save()
    {
        try
        {
            var s = SettingsFile.Parse(_boxes.Where(kv => kv.Key != ServerKey).Select(kv => kv.Key + "\t" + kv.Value.Text.Trim()));
            if (!ModelBuilder.IsValidModelName(s.ModelName)) throw new FormatException($"모델 이름 '{s.ModelName}'은(는) 폴더 이름으로 쓸 수 없습니다(공백, / \\ :, .. 없이).");
            if (s.Units.Length == 0) throw new FormatException("AutoMod 거리 단위를 고르세요(보통 Meters).");
            var url = _boxes[ServerKey].Text.Trim();
            if (url.Length > 0 && !UserSettings.TryNormalizeUrl(url, out url)) throw new FormatException("서버 주소는 http:// 또는 https://로 시작해야 합니다(예: http://server:8082).");
            SettingsFile.Save(s, _path);
            // only a changed address is written: an unchanged one may come from the machine-wide file and should keep following it
            if (url != _url) (UserSettings.Load() with { ServerUrl = url }).Save();
            DialogResult = DialogResult.OK;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _boxes.FirstOrDefault(kv => ex.Message.Contains(" " + kv.Key + " ")).Value?.Focus();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, "저장하지 못했습니다: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Button Flat(string text, Color back, Color fore)
    {
        var b = new Button { Text = text, Size = new Size(88, 32), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore, Font = Theme.Base, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
