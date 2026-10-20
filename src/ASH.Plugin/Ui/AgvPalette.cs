using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.Windows;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Ui.AgvPalette))]

namespace ASH.Plugin.Ui;

public sealed class AgvPalette
{
    private static PaletteSet? _set;

    [CommandMethod("AGVPALETTE")]
    public void Show()
    {
        if (_set == null)
        {
            _set = new PaletteSet("ASH", new Guid("6f1a6c2e-5b7d-4e7a-9c3b-a5b000000001")) { MinimumSize = new Size(220, 400) };
            _set.Add("ASH", new AgvPaletteControl());
        }
        _set.Visible = true;
    }
}

/// <summary>Side palette: one button per command plus the CP placement settings.</summary>
public sealed class AgvPaletteControl : UserControl
{
    private static readonly string[] CpTypes = { "traffic", "load", "unload", "rtnload", "rtnunload", "standby", "buffer" };

    public AgvPaletteControl()
    {
        var s = CpSettings.Current;
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        root.Controls.Add(Group("레이아웃", Btn("양식 초기화", "AGVINIT")));
        root.Controls.Add(Group("경로",
            Btn("경로 그리기", "AGVPATH"), Btn("방향 반전", "AGVREVERSE"), Btn("평행 복제", "AGVOFFSET"), Btn("배열 복제", "AGVARRAY"),
            Btn("교차 분할", "AGVSPLIT"), Btn("경로 잇기", "AGVJOIN"), Btn("필렛", "AGVFILLET")));

        var detailed = new CheckBox { Text = "상세 모드", Checked = s.DetailedMode, AutoSize = true };
        detailed.CheckedChanged += (o, e) => s.DetailedMode = detailed.Checked;
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        type.Items.AddRange(CpTypes);
        type.SelectedItem = s.Type;
        var prefix = new TextBox { Text = s.Prefix, Width = 150 };
        type.TextChanged += (o, e) => { s.Type = type.Text.Trim(); prefix.Text = s.Prefix; };
        prefix.TextChanged += (o, e) => s.Prefix = prefix.Text.Trim();
        var group = Field(s.Group, v => s.Group = v);
        var station = Field(s.Station, v => s.Station = v);
        var cap = Field(s.Cap.ToString(CultureInfo.InvariantCulture),
            v => s.Cap = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) && c > 0 ? c : 1);
        cap.Leave += (o, e) => cap.Text = s.Cap.ToString(CultureInfo.InvariantCulture); // show the value AGVCP will actually use
        root.Controls.Add(Group("CP", detailed,
            Label("TYPE"), type, Label("GROUP"), group, Label("STATION"), station, Label("접두어"), prefix, Label("CAP"), cap,
            Btn("CP 배치", "AGVCP")));

        root.Controls.Add(Group("교통제어",
            Btn("교통제어 자동 생성", "AGVTRAFFIC"), Btn("block 자동 생성", "AGVBLOCKAUTO"), Btn("traffic CP 자동 생성", "AGVCPAUTO")));
        root.Controls.Add(Group("검증·내보내기", Btn("검증", "AGVCHECK"), Btn("내보내기", "AGVEXPORT")));
        Controls.Add(root);
    }

    private static GroupBox Group(string title, params Control[] items)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        flow.Controls.AddRange(items);
        var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(190, 0) };
        g.Controls.Add(flow);
        return g;
    }

    private static Button Btn(string text, string cmd)
    {
        var b = new Button { Text = text, Width = 170, Height = 28 };
        b.Click += (o, e) =>
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            // a command waiting for input would take the command name as its answer
            if (!string.IsNullOrEmpty(Application.GetSystemVariable("CMDNAMES") as string)) { doc.Editor.WriteMessage("\n실행 중인 명령을 먼저 끝내세요."); return; }
            doc.SendStringToExecute(cmd + " ", true, false, true);
        };
        return b;
    }

    private static Label Label(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

    private static TextBox Field(string value, Action<string> set)
    {
        var t = new TextBox { Text = value, Width = 150 };
        t.TextChanged += (o, e) => set(t.Text.Trim());
        return t;
    }
}
