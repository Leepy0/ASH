using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Layout;
using ASH.Core.Settings;
using ASH.Plugin.Commands;
using CoreLayout = ASH.Core.Layout.Layout;

namespace ASH.Plugin.Ui;

/// <summary>"다음 할 일": the list NextSteps.Of makes, for the top of the palette (<see cref="Create"/>). Filled once ZWCAD is idle after an ASH command
/// that changed the drawing (PathCommands.QueueNextSteps calls <see cref="Update"/>) and after a save (SaveCheck). A double click on an item that names a command sends it.</summary>
internal static class NextStepsPanel
{
    private const int MaxRows = 6;
    private static Panel? _root;
    private static ListBox? _list;
    private static Label? _head;
    private static List<NextStep> _steps = new List<NextStep>();

    /// <summary>True once the palette made the panel, so the commands skip the work when nobody shows it.</summary>
    internal static bool Wanted => _list != null && !_list.IsDisposed;

    /// <summary>Header and list in one panel, docked to the top; its height follows the number of items (1 to 6 rows).</summary>
    internal static Control Create()
    {
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Base, ForeColor = Theme.Text, BackColor = Theme.Panel };
        var head = new Label { Dock = DockStyle.Top, Height = 20, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = new Padding(2, 0, 0, 0) };
        var root = new Panel { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4) };
        root.Controls.Add(list); // Fill first: the docked edge then takes its space before it
        root.Controls.Add(head);
        new ToolTip().SetToolTip(list, "더블클릭하면 그 명령을 실행 · 도면을 바꾼 명령 뒤·저장 때 갱신");
        list.DoubleClick += (o, e) =>
        {
            int i = list.SelectedIndex;
            if (i >= 0 && i < _steps.Count && _steps[i].Command.Length > 0) AgvPalette.Send(_steps[i].Command);
        };
        _root = root; _list = list; _head = head;
        Show(new List<NextStep>(), "명령을 실행하면 갱신");
        return root;
    }

    /// <summary>Reads what the folder of <paramref name="dwgPath"/> holds, makes the items and shows them. errors = blocking findings when the caller counted them already, else counted here
    /// with DrawingUnits.PerMeter. Never throws.</summary>
    internal static void Update(string dwgPath, CoreLayout layout, int? errors = null)
    {
        if (!Wanted) return;
        try { Show(NextSteps.Of(layout, Facts(dwgPath, layout, errors)), null); }
        catch (System.Exception ex) { PluginLog.Write("다음 할 일", ex); }
    }

    private static NextStepFacts Facts(string dwgPath, CoreLayout layout, int? errors)
    {
        int err = errors ?? CheckCommands.Count(layout).Errors;
        if (!Path.IsPathRooted(dwgPath)) return new NextStepFacts(false, false, false, false, err); // "Drawing1.dwg": not saved yet
        var dir = Path.GetDirectoryName(dwgPath) ?? "";
        var sf = Path.Combine(dir, ExportCommands.SettingsFileName);
        bool hasSettings = File.Exists(sf), hasInput = File.Exists(Path.Combine(dir, ModelCommands.ExcelFileName)), built = false;
        IReadOnlyCollection<string>? extra = null;
        try
        {
            var s = hasSettings ? SettingsFile.Load(sf) : new ProjectSettings();
            built = Directory.Exists(Path.Combine(dir, s.ModelName + ".arc"));
            // with an input.xlsx its groups win and the workbook is not read here (slow, maybe encrypted): the CPs' groups have to do
            if (hasSettings && !hasInput) extra = SettingsFile.ParseGroups(s.Groups).Select(g => g.Group).ToList();
        }
        catch (System.Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { }
        return new NextStepFacts(true, hasSettings, hasInput, built, err, extra);
    }

    private static void Show(List<NextStep> steps, string? placeholder)
    {
        var list = _list; var head = _head; var root = _root;
        if (list == null || head == null || root == null || list.IsDisposed) return;
        if (list.InvokeRequired) { list.BeginInvoke(new Action(() => Show(steps, placeholder))); return; }
        _steps = steps;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var s in steps) list.Items.Add(s.Label);
        if (steps.Count == 0) list.Items.Add(placeholder ?? "할 일 없음");
        list.EndUpdate();
        head.Text = steps.Count == 0 ? "다음 할 일" : $"다음 할 일 {steps.Count}";
        root.Height = head.Height + Math.Min(Math.Max(steps.Count, 1), MaxRows) * list.ItemHeight + 6;
    }
}
