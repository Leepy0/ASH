using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Analysis;
using ASH.Core.Runner;

namespace ASH.Plugin.Ui;

/// <summary>Asks which analysis and plan a job goes to, plus a version note, before AGVSEND/AGVRESULTUP uploads. "새 분석…"/"새 안…" name one the server then creates.
/// <c>analyses</c> null = the server gave no analysis list (0.18 or the call failed): the job goes unclassified and the dialog says so. Touches no ZWCAD type.</summary>
public sealed class SendDialog : Form
{
    private const string NewAnalysis = "새 분석…", NewPlan = "새 안…";

    private readonly IReadOnlyList<AnalysisSummary>? _analyses;
    private readonly ComboBox _analysis = Combo(), _plan = Combo();
    private readonly TextBox _analysisName = Box(), _planName = Box(), _note = Box();
    private readonly Label _next = Theme.Caption("");

    public SendChoice Choice { get; private set; } = new SendChoice();
    public string Note { get; private set; } = "";

    public SendDialog(string title, string info, string okText, IReadOnlyList<AnalysisSummary>? analyses, SendChoice initial)
    {
        _analyses = analyses;
        Text = title;
        Font = Theme.Base;
        ForeColor = Theme.Text;
        BackColor = Theme.Panel;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, analyses == null ? 250 : 400);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Panel, Padding = new Padding(16, 4, 16, 8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control c) { if (!(c is Label)) c.Dock = DockStyle.Fill; table.Controls.Add(c); }
        Add(new Label { Text = info, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(428, 0), Margin = new Padding(2, 8, 0, 0) });
        if (analyses == null)
            Add(new Label { Text = "이 서버에서 분석 목록을 받지 못해 분류 없이 보냅니다.", ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(428, 0), Margin = new Padding(2, 8, 0, 0) });
        else
        {
            Add(Theme.Section("분석"));
            Add(_analysis);
            Add(_analysisName);
            Add(Theme.Section("안"));
            Add(_plan);
            Add(_planName);
            Add(_next);
            _analysis.Items.AddRange(analyses.Select(a => (object)a.Name).Concat(new object[] { NewAnalysis }).ToArray());
            var ai = initial.AnalysisId.Length == 0 ? -1 : analyses.ToList().FindIndex(a => a.Id == initial.AnalysisId);
            _analysis.SelectedIndex = ai >= 0 ? ai : _analysis.Items.Count - 1;
            if (ai < 0) _analysisName.Text = initial.AnalysisName;
            FillPlans(initial.PlanId, initial.PlanName);
            _analysis.SelectedIndexChanged += (o, e) => FillPlans("", "");
            _plan.SelectedIndexChanged += (o, e) => Sync();
        }
        Add(Theme.Section("메모 (선택)"));
        _note.Multiline = true;
        _note.Height = 60;
        Add(_note);

        var ok = Flat(okText, Theme.Accent, Color.White);
        var cancel = Flat("취소", Theme.Panel, Theme.Text);
        cancel.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        cancel.FlatAppearance.BorderSize = 1;
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (o, e) => Accept();
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Bg };
        bar.Controls.AddRange(new Control[] { ok, cancel });
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

    private AnalysisSummary? SelectedAnalysis => _analyses != null && _analysis.SelectedIndex >= 0 && _analysis.SelectedIndex < _analyses.Count ? _analyses[_analysis.SelectedIndex] : null;
    private PlanSummary? SelectedPlan => SelectedAnalysis is { } a && _plan.SelectedIndex >= 0 && _plan.SelectedIndex < a.Plans.Count ? a.Plans[_plan.SelectedIndex] : null;

    // plans of the chosen analysis; keeps the plan with this id, else "새 안…" holding this name
    private void FillPlans(string planId, string planName)
    {
        var plans = SelectedAnalysis?.Plans ?? new List<PlanSummary>();
        _plan.Items.Clear();
        _plan.Items.AddRange(plans.Select(p => (object)p.Name).Concat(new object[] { NewPlan }).ToArray());
        var pi = planId.Length == 0 ? -1 : plans.FindIndex(p => p.Id == planId);
        _plan.SelectedIndex = pi >= 0 ? pi : plans.Count;
        if (pi < 0) _planName.Text = planName;
        Sync();
    }

    private void Sync()
    {
        _analysisName.Visible = SelectedAnalysis == null;
        _planName.Visible = SelectedPlan == null;
        _next.Text = "다음: v" + Math.Max(1, SelectedPlan?.NextVersion ?? 1);
    }

    private void Accept()
    {
        Note = _note.Text.Trim();
        if (_analyses != null)
        {
            var a = SelectedAnalysis;
            var p = SelectedPlan;
            var an = a?.Name ?? _analysisName.Text.Trim();
            var pn = p?.Name ?? _planName.Text.Trim();
            if (an.Length == 0 || pn.Length == 0)
            {
                MessageBox.Show(this, "새 분석·안의 이름을 입력하세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                (an.Length == 0 ? _analysisName : _planName).Focus();
                return;
            }
            Choice = new SendChoice { AnalysisId = a?.Id ?? "", PlanId = p?.Id ?? "", AnalysisName = an, PlanName = pn };
        }
        DialogResult = DialogResult.OK;
    }

    private static ComboBox Combo() => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 0, 0) };
    private static TextBox Box() => new TextBox { BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 0, 0) };

    private static Button Flat(string text, Color back, Color fore)
    {
        var b = new Button { Text = text, Size = new Size(88, 32), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore, Font = Theme.Base, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
