using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Input;
using ClosedXML.Excel;

namespace ASH.Plugin.Ui;

/// <summary>
/// AGVINPUT's input table: one tab per input sheet, a grid whose columns are the sheet's own row-1 headers (labels from <see cref="InputSchema"/> when it knows the
/// column), so filling input.xlsx needs no Excel. Auto columns are read-only, advanced ones hidden until "고급 열", list and CP columns drop down (free text stays
/// allowed: a CP may not be drawn yet). A pause after an edit re-runs the input checks on the workbook with the edits laid over it and colours the cells
/// (red = error, orange = warning, blue = changed, the message as tooltip). "저장" hands the cell edits to the caller, which writes the file and gives the
/// workbook back as saved. Touches no ZWCAD type and no Excel: the caller owns both.
/// </summary>
public sealed class InputDialog : Form
{
    static readonly Dictionary<string, string> TabNames = new Dictionary<string, string>
        { ["Materials"] = "자재", ["AGV_Groups"] = "그룹", ["AGV_Types"] = "타입", ["Conveyors"] = "컨베이어" };

    static readonly Color AutoBack = Color.FromArgb(243, 244, 246);
    static readonly Color ErrorBack = Rgb(ExcelMarks.ErrorRgb), WarnBack = Rgb(ExcelMarks.WarnRgb);
    static Color Rgb(int rgb) => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    sealed class ColTag
    {
        public readonly TableColumn Col;
        public readonly bool Combo;
        public ColTag(TableColumn col, bool combo) { Col = col; Combo = combo; }
    }

    readonly IReadOnlyDictionary<string, List<string>> _cps;
    readonly IReadOnlyCollection<string> _cpNames;
    readonly Action<IReadOnlyList<CellEdit>> _apply;
    readonly Func<XLWorkbook> _read;

    XLWorkbook _wb;
    Dictionary<string, InputTable> _tables = new Dictionary<string, InputTable>();  // as read: the saved state every diff is against
    readonly List<string> _order = new List<string>();
    readonly Dictionary<string, DataGridView> _grids = new Dictionary<string, DataGridView>();
    readonly Dictionary<string, Label> _counts = new Dictionary<string, Label>();
    Dictionary<string, List<string>> _otherSheets = new Dictionary<string, List<string>>(); // first-column names of list sources that are not tabs

    readonly TabControl _tabs = new TabControl();
    readonly CheckBox _advanced = new CheckBox();
    readonly Label _status = new Label();
    readonly Button _save, _close;
    readonly ToolTip _tip = new ToolTip();
    readonly Timer _debounce = new Timer { Interval = 400 };
    readonly List<DataGridViewCell> _painted = new List<DataGridViewCell>();
    bool _busy, _stale;
    string _note;

    /// <summary>Cells written by "저장" while the dialog was open.</summary>
    public int SavedCells { get; private set; }

    /// <param name="wb">The workbook as read; the dialog owns it from here on.</param>
    /// <param name="cpsByType">The drawing's CP names by CP type, for the CP drop-downs.</param>
    /// <param name="cpNames">All CP names of the drawing, for the checks.</param>
    /// <param name="apply">Writes the cell edits to the file (throws when it cannot).</param>
    /// <param name="read">Reads the file again after a save.</param>
    public InputDialog(string path, XLWorkbook wb, IReadOnlyDictionary<string, List<string>> cpsByType, IReadOnlyCollection<string> cpNames,
        Action<IReadOnlyList<CellEdit>> apply, Func<XLWorkbook> read, string note = "")
    {
        _wb = wb; _cps = cpsByType; _cpNames = cpNames; _apply = apply; _read = read; _note = note;
        Text = "ASH 입력 표 · " + System.IO.Path.GetFileName(path);
        Font = Theme.Base;
        ForeColor = Theme.Text;
        BackColor = Theme.Panel;
        Theme.ScaleToDpi(this);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true; MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 580); // 96 dpi
        MinimumSize = new Size(560, 360);

        foreach (var t in InputTable.LoadAll(wb).Where(t => t.Columns.Count > 0)) { _tables[t.Sheet] = t; _order.Add(t.Sheet); }
        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = Theme.Base;
        foreach (var sheet in _order) _tabs.TabPages.Add(MakePage(sheet));

        _advanced.Text = "고급 열";
        _advanced.AutoSize = true;
        _advanced.Visible = _tables.Values.Any(t => t.Columns.Any(c => c.Advanced));
        _advanced.CheckedChanged += (o, e) => { foreach (var g in _grids.Values) ApplyAdvanced(g); };
        _status.AutoEllipsis = true;
        _status.AutoSize = false;
        _status.ForeColor = Theme.Muted;
        _save = Flat("저장", Theme.Accent, Color.White);
        _save.Enabled = false;
        _save.Click += (o, e) => SaveNow();
        _close = Flat("닫기", Theme.Panel, Theme.Text);
        _close.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        _close.FlatAppearance.BorderSize = 1;
        _close.Click += (o, e) => Close();
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Bg };
        bar.Controls.AddRange(new Control[] { _status, _advanced, _save, _close });
        bar.Layout += (o, e) =>
        {
            _close.Location = new Point(bar.ClientSize.Width - 16 - _close.Width, (bar.ClientSize.Height - _close.Height) / 2);
            _save.Location = new Point(_close.Left - 8 - _save.Width, _close.Top);
            int right = _save.Left - 12;
            if (_advanced.Visible) { _advanced.Location = new Point(right - _advanced.Width, (bar.ClientSize.Height - _advanced.Height) / 2); right = _advanced.Left - 12; }
            _status.SetBounds(16, 0, Math.Max(40, right - 16), bar.ClientSize.Height);
            _status.TextAlign = ContentAlignment.MiddleLeft;
        };
        CancelButton = _close;
        Controls.Add(_tabs); // Fill first, then the bottom bar docks before it
        Controls.Add(bar);
        _debounce.Tick += (o, e) => RunCheck();
        Theme.FitHeight(this, null);
    }

    // ---------- build ----------

    TabPage MakePage(string sheet)
    {
        var page = new TabPage(TabNames.TryGetValue(sheet, out var ko) ? $"{ko} ({sheet})" : sheet) { BackColor = Theme.Panel, UseVisualStyleBackColor = false };
        var g = MakeGrid();
        var add = Flat("행 추가", Theme.Panel, Theme.Text);
        add.Size = new Size(88, 28);
        add.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        add.FlatAppearance.BorderSize = 1;
        add.Location = new Point(8, 6);
        add.Click += (o, e) => AddRow(sheet);
        var count = new Label { AutoSize = true, ForeColor = Theme.Muted, Location = new Point(106, 11) };
        var top = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Panel };
        top.Controls.AddRange(new Control[] { add, count });
        _grids[sheet] = g; _counts[sheet] = count;
        page.Controls.Add(g); // Fill first, then the top bar docks before it
        page.Controls.Add(top);
        return page;
    }

    DataGridView MakeGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill, Font = Theme.Base, BackgroundColor = Theme.Panel, BorderStyle = BorderStyle.None, GridColor = Color.FromArgb(229, 231, 235),
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AllowUserToOrderColumns = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = true,
            EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        };
        g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Bg;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Bg;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Text;
        g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        g.DefaultCellStyle.SelectionBackColor = Theme.Hover;
        g.DefaultCellStyle.SelectionForeColor = Theme.Text;
        g.CellValueChanged += (o, e) => { if (!_busy && e.RowIndex >= 0) Touch(); };
        g.CellBeginEdit += OnBeginEdit;
        g.EditingControlShowing += OnEditing;
        g.CellValidating += OnValidating;
        g.DataError += (o, e) => e.ThrowException = false; // a value outside a drop-down's list is allowed, nothing to complain about
        g.CellMouseClick += OnCellMouseClick;
        g.KeyDown += OnKeyDown;
        return g;
    }

    DataGridViewColumn MakeColumn(TableColumn c)
    {
        bool combo = Choices(c) != null;
        DataGridViewColumn col = combo
            ? new DataGridViewComboBoxColumn { FlatStyle = FlatStyle.Flat, ValueType = typeof(string) }
            : (DataGridViewColumn)new DataGridViewTextBoxColumn { ValueType = typeof(string) };
        col.HeaderText = Short(c.Label);
        col.ToolTipText = c.Schema == null ? c.Name : c.Name + "\n" + c.Label + (c.Auto ? "\n자동 계산" : "");
        col.SortMode = DataGridViewColumnSortMode.NotSortable;
        col.Tag = new ColTag(c, combo);
        col.ReadOnly = c.Auto;
        if (c.Auto) { col.DefaultCellStyle.BackColor = AutoBack; col.DefaultCellStyle.ForeColor = Theme.Muted; }
        return col;
    }

    /// <summary>A header short enough for a column: the label up to its long parenthesis ("호출 임계(용기)" stays, "적재 곡선 속도(m/s, 빈칸=직선)" loses the rest, which is in the tooltip).</summary>
    static string Short(string label)
    {
        int i = label.IndexOf('(');
        if (i <= 0) return label;
        int j = label.LastIndexOf(')');
        return j == label.Length - 1 && j - i - 1 <= 8 ? label : label.Substring(0, i).TrimEnd();
    }

    int Px(int v96) => (int)Math.Round(v96 * DeviceDpi / 96.0);

    // ---------- fill ----------

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        using (PluginLog.Time("AGVINPUT 표 채우기")) foreach (var s in _order) Populate(s);
        BeginInvoke(new Action(RunCheck)); // after the window is up
    }

    void Populate(string sheet)
    {
        var g = _grids[sheet]; var t = _tables[sheet];
        _busy = true;
        g.SuspendLayout();
        try
        {
            g.Rows.Clear(); g.Columns.Clear();
            foreach (var c in t.Columns) g.Columns.Add(MakeColumn(c));
            g.Rows.AddRange(t.Rows.Select(r => NewRow(g, t, r.Row, r.Values)).ToArray());
            ApplyAdvanced(g);
            FitColumns(g, t);
            var first = g.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
            if (first != null) first.Frozen = true; // the name column stays in view while scrolling right
            _counts[sheet].Text = "행 " + g.Rows.Count;
        }
        finally { g.ResumeLayout(); _busy = false; }
    }

    // widths from the text (measured here: AutoResizeColumns does nothing for a grid on a tab that was never shown); a long header wraps instead of widening the column
    void FitColumns(DataGridView g, InputTable t)
    {
        int Text(string s) => TextRenderer.MeasureText(s, Theme.Base).Width;
        for (int i = 0; i < g.Columns.Count; i++)
        {
            var col = g.Columns[i];
            int cells = t.Rows.Take(300).Select(r => Text(r.Values[i])).DefaultIfEmpty(0).Max() + Px(((ColTag)col.Tag).Combo ? 36 : 18);
            int head = Math.Min(Text(col.HeaderText) + Px(18), Px(150));
            col.Width = Math.Max(Px(56), Math.Min(Px(260), Math.Max(cells, head)));
        }
    }

    DataGridViewRow NewRow(DataGridView g, InputTable t, int excelRow, string[] values)
    {
        var row = new DataGridViewRow();
        row.CreateCells(g);
        row.Tag = excelRow;
        for (int i = 0; i < t.Columns.Count; i++)
        {
            var v = (i < values.Length ? values[i] : null) ?? "";
            var cell = row.Cells[i];
            // a drop-down cell must hold its value among its items or it cannot paint; the full list is put in when editing starts
            if (cell is DataGridViewComboBoxCell cb) { cb.Items.Add(""); if (v.Length > 0) cb.Items.Add(v); }
            cell.Value = v;
        }
        return row;
    }

    void ApplyAdvanced(DataGridView g)
    {
        foreach (DataGridViewColumn c in g.Columns)
            c.Visible = _advanced.Checked || !(((ColTag)c.Tag).Col.Advanced);
    }

    void AddRow(string sheet)
    {
        var g = _grids[sheet]; var t = _tables[sheet];
        _busy = true;
        try { g.Rows.Add(NewRow(g, t, 0, new string[t.Columns.Count])); }
        finally { _busy = false; }
        _counts[sheet].Text = "행 " + g.Rows.Count;
        var col = g.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible && !c.ReadOnly);
        if (col != null)
        {
            g.CurrentCell = g[col.Index, g.Rows.Count - 1]; // scrolls it into view
            g.BeginEdit(true);
        }
        Touch();
    }

    // ---------- choices ----------

    /// <summary>The values a column drops down: a fixed list, the first-column names of the sheet it points at (as the grid holds them now, so a group typed a moment
    /// ago is offered), or the drawing's CPs of that type. Null = a plain text column (also the multi-value StandbyCPs).</summary>
    IReadOnlyList<string>? Choices(TableColumn c)
    {
        var s = c.Schema;
        if (s == null) return null;
        if (s.CpType != null) return s.CpType == "standby" ? null : _cps.TryGetValue(s.CpType, out var cps) ? cps : (IReadOnlyList<string>)new List<string>();
        if (s.List == null) return null;
        if (_tables.ContainsKey(s.List)) return GridNames(s.List);
        if (_otherSheets.TryGetValue(s.List, out var cached)) return cached;
        if (_wb.TryGetWorksheet(s.List, out _))
        {
            var t = InputTable.Load(_wb, s.List);
            return _otherSheets[s.List] = t == null ? new List<string>()
                : t.Rows.Select(r => r.Values.Length > 0 ? r.Values[0] : "").Where(v => v.Length > 0).Distinct().ToList();
        }
        return s.List.Split(new[] { ',' }).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    }

    List<string> GridNames(string sheet)
    {
        var names = new List<string>();
        if (!_grids.TryGetValue(sheet, out var g) || g.Columns.Count == 0) return names;
        foreach (DataGridViewRow r in g.Rows)
        {
            var v = (Convert.ToString(r.Cells[0].Value) ?? "").Trim();
            if (v.Length > 0 && !names.Contains(v)) names.Add(v);
        }
        return names;
    }

    // ---------- editing ----------

    void OnBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        var g = (DataGridView)sender!;
        if (!(g.Columns[e.ColumnIndex].Tag is ColTag tag) || !(g[e.ColumnIndex, e.RowIndex] is DataGridViewComboBoxCell cell)) return;
        var cur = Convert.ToString(cell.Value) ?? "";
        cell.Items.Clear();
        cell.Items.Add("");
        foreach (var s in Choices(tag.Col) ?? new List<string>()) if (!cell.Items.Contains(s)) cell.Items.Add(s);
        if (cur.Length > 0 && !cell.Items.Contains(cur)) cell.Items.Add(cur);
    }

    void OnEditing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (!(e.Control is ComboBox cb)) return;
        cb.DropDownStyle = ComboBoxStyle.DropDown; // typing stays allowed
        cb.AutoCompleteSource = AutoCompleteSource.ListItems;
        cb.AutoCompleteMode = AutoCompleteMode.Append;
        cb.SelectionChangeCommitted -= CommitCombo;
        cb.SelectionChangeCommitted += CommitCombo;
    }

    static void CommitCombo(object? sender, EventArgs e) => (((Control)sender!).Parent as DataGridView)?.CommitEdit(DataGridViewDataErrorContexts.Commit);

    // Text typed into a drop-down is not committed by the grid (only a pick from the list is) and a value outside the list cannot be held: make it an item of
    // its cell and set it ourselves.
    void OnValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        var g = (DataGridView)sender!;
        if (e.RowIndex < 0 || e.RowIndex >= g.RowCount || e.ColumnIndex < 0 || !g.IsCurrentCellInEditMode) return;
        if (e.FormattedValue is string raw) CommitTyped(g[e.ColumnIndex, e.RowIndex], raw);
    }

    static void CommitTyped(DataGridViewCell c, string raw)
    {
        if (!(c is DataGridViewComboBoxCell cell) || raw == (Convert.ToString(cell.Value) ?? "")) return;
        if (!cell.Items.Contains(raw)) cell.Items.Add(raw);
        cell.Value = raw;
    }

    // a click on the arrow of a drop-down cell opens the list at once (otherwise it takes a click to select the cell and one more to open it)
    void OnCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var g = (DataGridView)sender!;
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || !(g.Columns[e.ColumnIndex].Tag is ColTag tag) || !tag.Combo || g.Columns[e.ColumnIndex].ReadOnly) return;
        var rect = g.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
        if (e.X < rect.Width - SystemInformation.VerticalScrollBarWidth - 2) return;
        g.CurrentCell = g[e.ColumnIndex, e.RowIndex];
        if (g.BeginEdit(true) && g.EditingControl is ComboBox cb) cb.DroppedDown = true;
    }

    // Delete empties the selected cells (a cleared cell is saved as empty)
    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var g = (DataGridView)sender!;
        if (e.KeyCode != Keys.Delete || g.IsCurrentCellInEditMode) return;
        foreach (DataGridViewCell c in g.SelectedCells) if (!c.OwningColumn.ReadOnly && !c.ReadOnly) c.Value = "";
        e.Handled = true;
    }

    void Touch()
    {
        _note = "";
        _debounce.Stop();
        _debounce.Start();
    }

    // ---------- check ----------

    // the cell being edited goes into the table first (a drop-down's typed text is not committed by EndEdit)
    void CommitAll()
    {
        foreach (var g in _grids.Values)
        {
            if (g.IsCurrentCellInEditMode && g.EditingControl is ComboBox cb && g.CurrentCell != null) CommitTyped(g.CurrentCell, cb.Text);
            g.EndEdit();
        }
    }

    InputTable FromGrid(string sheet)
    {
        var g = _grids[sheet]; var t = _tables[sheet];
        var rows = new List<TableRow>(g.Rows.Count);
        foreach (DataGridViewRow gr in g.Rows)
        {
            var v = new string[t.Columns.Count];
            for (int i = 0; i < v.Length; i++) v[i] = Convert.ToString(gr.Cells[i].Value) ?? "";
            rows.Add(new TableRow(gr.Tag is int n ? n : 0, v));
        }
        return t.With(rows);
    }

    /// <summary>Every sheet's cell edits against the saved state. <paramref name="edited"/> holds each sheet's grid as a table, its rows in grid order, appended ones with their Excel row.</summary>
    List<CellEdit> Collect(out Dictionary<string, InputTable> edited)
    {
        edited = new Dictionary<string, InputTable>();
        var all = new List<CellEdit>();
        foreach (var sheet in _order)
        {
            if (!_tables.ContainsKey(sheet) || _grids[sheet].Columns.Count == 0) continue;
            var e = FromGrid(sheet);
            all.AddRange(_tables[sheet].Diff(e));
            edited[sheet] = e;
        }
        return all;
    }

    void RunCheck()
    {
        _debounce.Stop();
        if (IsDisposed) return;
        try
        {
            using (PluginLog.Time("AGVINPUT 검사"))
            {
                var edits = Collect(out var edited);
                var res = InputCheck.Run(_wb, edits, _cpNames);
                PaintMarks(res, edited, edits);
                ShowStatus(res, edits.Count);
            }
        }
        catch (Exception ex)
        {
            PluginLog.Write("AGVINPUT check", ex);
            _status.Text = "검사 오류: " + ex.Message;
            _status.ForeColor = Theme.Error;
        }
    }

    void PaintMarks(InputCheckResult res, Dictionary<string, InputTable> edited, List<CellEdit> edits)
    {
        foreach (var c in _painted)
            if (c.DataGridView != null) { c.Style.BackColor = Color.Empty; c.ToolTipText = ""; }
        _painted.Clear();
        DataGridViewCell? At(string sheet, int row, int col)
        {
            if (!edited.TryGetValue(sheet, out var t)) return null;
            int r = t.Rows.FindIndex(x => x.Row == row), c = t.Columns.ToList().FindIndex(x => x.Number == col);
            return r < 0 || c < 0 ? null : _grids[sheet][c, r];
        }
        foreach (var e in edits)
            if (At(e.Sheet, e.Row, e.Col) is { } cell) { cell.Style.BackColor = Theme.Hover; _painted.Add(cell); }
        foreach (var m in res.Marks)
            if (At(m.Sheet, m.Row, m.Col) is { } cell) { cell.Style.BackColor = m.Error ? ErrorBack : WarnBack; cell.ToolTipText = m.Text; _painted.Add(cell); }
    }

    void ShowStatus(InputCheckResult res, int changed)
    {
        string text = res.Fatal != null ? "읽기 오류 · " + res.Fatal.Split('\n')[0].Trim()
            : res.Errors + res.Warnings == 0 ? "문제 없음" : $"오류 {res.Errors} · 경고 {res.Warnings}";
        if (changed > 0) text += $" · 변경 {changed}칸";
        if (_note.Length > 0) text = _note + " · " + text;
        _status.Text = text;
        _status.ForeColor = res.Errors > 0 ? Theme.Error : res.Warnings > 0 ? Theme.Warning : Theme.Muted;
        _tip.SetToolTip(_status, string.Join("\n", res.Messages.Take(30)));
        _save.Enabled = changed > 0 && !_stale;
    }

    // ---------- save / close ----------

    bool SaveNow()
    {
        _debounce.Stop();
        CommitAll();
        var edits = Collect(out _);
        if (edits.Count == 0) return true;
        Cursor = Cursors.WaitCursor;
        try
        {
            try { using (PluginLog.Time($"AGVINPUT 저장({edits.Count}칸)")) _apply(edits); }
            catch (Exception ex)
            {
                PluginLog.Write("AGVINPUT save failed", ex);
                MessageBox.Show(this, "저장하지 못했습니다: " + ex.Message.Split('\n')[0].Trim() + "\n(Excel에서 셀을 편집 중이면 마치고 다시 저장하세요)", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            SavedCells += edits.Count;
            try
            {
                var wb = _read(); // the saved state: later edits are diffed against it
                _wb.Dispose(); _wb = wb;
                Reload();
            }
            catch (Exception ex)
            {
                PluginLog.Write("AGVINPUT re-read failed", ex);
                _stale = true; // the file has the edits but this table no longer knows its state: saving again would add the new rows twice
                _save.Enabled = false;
                MessageBox.Show(this, "저장했지만 다시 읽지 못했습니다. 닫고 다시 여세요.\n" + ex.Message.Split('\n')[0].Trim(), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            }
            _note = $"저장 {edits.Count}칸";
            RunCheck();
            return true;
        }
        finally { Cursor = Cursors.Default; }
    }

    /// <summary>The tables again from the workbook as saved; every grid is rebuilt (rows added get their Excel row, values show as the file holds them), keeping the scroll position.</summary>
    void Reload()
    {
        _tables = new Dictionary<string, InputTable>();
        _otherSheets = new Dictionary<string, List<string>>();
        foreach (var t in InputTable.LoadAll(_wb).Where(t => t.Columns.Count > 0)) _tables[t.Sheet] = t;
        foreach (var sheet in _order)
        {
            if (!_tables.ContainsKey(sheet)) continue;
            var g = _grids[sheet];
            int top = Math.Max(0, g.FirstDisplayedScrollingRowIndex), curRow = g.CurrentCellAddress.Y, curCol = g.CurrentCellAddress.X;
            Populate(sheet);
            if (top < g.Rows.Count) g.FirstDisplayedScrollingRowIndex = top;
            if (curRow >= 0 && curRow < g.Rows.Count && curCol >= 0 && curCol < g.Columns.Count && g.Columns[curCol].Visible) g.CurrentCell = g[curCol, curRow];
        }
        _painted.Clear();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _stale) return;
        CommitAll();
        int n = Collect(out _).Count;
        if (n == 0) return;
        var r = MessageBox.Show(this, $"저장하지 않은 변경 {n}칸\n저장할까요?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (r == DialogResult.Cancel || (r == DialogResult.Yes && !SaveNow())) e.Cancel = true;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _debounce.Dispose();
        _tip.Dispose();
        _wb.Dispose();
    }

    static Button Flat(string text, Color back, Color fore)
    {
        var b = new Button { Text = text, Size = new Size(88, 32), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore, Font = Theme.Base, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
