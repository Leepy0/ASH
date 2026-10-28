using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Input;
using ClosedXML.Excel;

namespace ASH.Plugin.Ui;

/// <summary>
/// AGVINPUT's supply settings table ("공급 설정"): one tab per input sheet, a grid whose columns are the sheet's own row-1 headers (labels from <see cref="InputSchema"/> when it knows the
/// column), so filling input.xlsx needs no Excel. Auto columns are read-only, advanced ones hidden until "고급 열", list and CP columns drop down (free text stays
/// allowed: a CP may not be drawn yet). A pause after an edit re-runs the input checks on the workbook with the edits laid over it and colours the cells
/// (red = error, orange = warning, blue = changed, the message as tooltip). "저장" hands the cell edits to the caller, which writes the file and gives the
/// workbook back as saved. Rows are deleted, copied and pasted (tab-separated text, as Excel does) in the grid; "엑셀로 내보내기" / "엑셀에서 불러오기" write and read
/// the table as a plain workbook. Touches no ZWCAD type and no Excel: the caller owns both.
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
    readonly Func<string, XLWorkbook> _readOther;
    readonly string _path;

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
    /// <param name="readOther">Reads another workbook (plain or encrypted) for "엑셀에서 불러오기".</param>
    public InputDialog(string path, XLWorkbook wb, IReadOnlyDictionary<string, List<string>> cpsByType, IReadOnlyCollection<string> cpNames,
        Action<IReadOnlyList<CellEdit>> apply, Func<XLWorkbook> read, Func<string, XLWorkbook> readOther, string note = "")
    {
        _wb = wb; _cps = cpsByType; _cpNames = cpNames; _apply = apply; _read = read; _readOther = readOther; _note = note; _path = path;
        Text = "ASH 공급 설정 · " + System.IO.Path.GetFileName(path);
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
        var count = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(4, 7, 0, 0) };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Panel, Padding = new Padding(8, 6, 8, 0), WrapContents = false };
        top.Controls.AddRange(new Control[]
        {
            Tool("행 추가", () => AddRow(sheet)), Tool("행 삭제", () => DeleteRows(sheet)),
            Tool("엑셀로 내보내기", ExportExcel), Tool("엑셀에서 불러오기", ImportExcel), count,
        });
        _grids[sheet] = g; _counts[sheet] = count;
        g.ContextMenuStrip = MakeMenu(sheet);
        page.Controls.Add(g); // Fill first, then the top bar docks before it
        page.Controls.Add(top);
        return page;
    }

    static Button Tool(string text, Action click)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(88, 28), Padding = new Padding(8, 0, 8, 0),
            Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Base, Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        b.FlatAppearance.BorderSize = 1;
        b.Click += (o, e) => click();
        return b;
    }

    ContextMenuStrip MakeMenu(string sheet)
    {
        var m = new ContextMenuStrip { Font = Theme.Base };
        var del = m.Items.Add("행 삭제"); del.Click += (o, e) => DeleteRows(sheet);
        var copy = m.Items.Add("복사"); copy.Click += (o, e) => CopyCells(sheet);
        var paste = m.Items.Add("붙여넣기"); paste.Click += (o, e) => PasteCells(sheet);
        var add = m.Items.Add("행 추가"); add.Click += (o, e) => AddRow(sheet);
        m.Opening += (o, e) => paste.Enabled = ClipboardHasText();
        return m;
    }

    static bool ClipboardHasText()
    {
        try { return Clipboard.ContainsText(); }
        catch (System.Runtime.InteropServices.ExternalException) { return false; } // another program holds the clipboard
    }

    DataGridView MakeGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill, Font = Theme.Base, BackgroundColor = Theme.Panel, BorderStyle = BorderStyle.None, GridColor = Color.FromArgb(229, 231, 235),
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AllowUserToOrderColumns = false,
            RowHeadersVisible = true, RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect, MultiSelect = true, ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable, // copy and paste are ours (ProcessCmdKey)
            EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        };
        g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Bg;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Bg;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Text;
        g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        g.RowHeadersDefaultCellStyle.BackColor = Theme.Bg;
        g.RowHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        g.RowHeadersDefaultCellStyle.SelectionBackColor = Theme.Hover;
        g.RowHeadersDefaultCellStyle.SelectionForeColor = Theme.Text;
        g.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        g.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        g.DefaultCellStyle.SelectionBackColor = Theme.Hover;
        g.DefaultCellStyle.SelectionForeColor = Theme.Text;
        g.CellMouseDown += OnRightDown;
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
            // headers of two lines (the labels wrap, so the columns can be narrow) and a row-number column to select rows by
            g.ColumnHeadersHeight = TextRenderer.MeasureText("가", Theme.Base).Height * 2 + Px(12);
            g.RowHeadersWidth = Px(44);
            foreach (var c in t.Columns) g.Columns.Add(MakeColumn(c));
            g.Rows.AddRange(t.Rows.Select(r => NewRow(g, t, r.Row, r.Values)).ToArray());
            ApplyAdvanced(g);
            FitColumns(g, t);
            var first = g.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
            if (first != null) first.Frozen = true; // the name column stays in view while scrolling right
            Counted(sheet);
        }
        finally { g.ResumeLayout(); _busy = false; }
    }

    // widths from the text (measured here: AutoResizeColumns does nothing for a grid on a tab that was never shown); the header wraps on two lines, so it asks for
    // half its label (but never less than its longest word)
    void FitColumns(DataGridView g, InputTable t)
    {
        int Text(string s) => TextRenderer.MeasureText(s, Theme.Base).Width;
        for (int i = 0; i < g.Columns.Count; i++)
        {
            var col = g.Columns[i];
            int cells = t.Rows.Take(300).Select(r => Text(r.Values[i])).DefaultIfEmpty(0).Max() + Px(((ColTag)col.Tag).Combo ? 36 : 18);
            int word = col.HeaderText.Split(' ').Max(w => Text(w));
            int head = Math.Max(Text(col.HeaderText) / 2 + Px(22), word + Px(16));
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
        Counted(sheet);
        var col = g.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible && !c.ReadOnly);
        if (col != null)
        {
            g.CurrentCell = g[col.Index, g.Rows.Count - 1]; // scrolls it into view
            g.BeginEdit(true);
        }
        Touch();
    }

    /// <summary>The row count of the tab and the numbers in the row headers, after the rows changed.</summary>
    void Counted(string sheet)
    {
        var g = _grids[sheet];
        for (int i = 0; i < g.Rows.Count; i++) g.Rows[i].HeaderCell.Value = (i + 1).ToString();
        _counts[sheet].Text = "행 " + g.Rows.Count;
    }

    string SheetOf(DataGridView g) => _grids.First(kv => kv.Value == g).Key;

    // ---------- delete / copy / paste ----------

    static List<int> SelectedRowIndexes(DataGridView g)
    {
        var rows = new SortedSet<int>(g.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex));
        if (rows.Count == 0 && g.CurrentCell != null) rows.Add(g.CurrentCell.RowIndex);
        return rows.ToList();
    }

    /// <summary>Takes out the rows of the selected cells. Nothing is written before "저장": a row that was in the file is then deleted there (the rows below move up).</summary>
    void DeleteRows(string sheet)
    {
        var g = _grids[sheet];
        g.EndEdit();
        var rows = SelectedRowIndexes(g);
        if (rows.Count == 0) return;
        _busy = true;
        try { for (int k = rows.Count - 1; k >= 0; k--) g.Rows.RemoveAt(rows[k]); }
        finally { _busy = false; }
        Counted(sheet);
        if (g.Rows.Count > 0)
        {
            var col = g.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
            if (col != null) g.CurrentCell = g[col.Index, Math.Min(rows[0], g.Rows.Count - 1)];
        }
        Touch();
        _note = $"행 {rows.Count} 삭제";
    }

    /// <summary>The selected cells as tab-separated text (the visible columns of the selection), the way Excel copies.</summary>
    void CopyCells(string sheet)
    {
        var g = _grids[sheet];
        var cells = g.SelectedCells.Cast<DataGridViewCell>().Where(c => c.OwningColumn.Visible).ToList();
        if (cells.Count == 0 && g.CurrentCell != null) cells.Add(g.CurrentCell);
        if (cells.Count == 0) return;
        var rows = cells.Select(c => c.RowIndex).Distinct().OrderBy(r => r).ToList();
        var cols = cells.Select(c => c.ColumnIndex).Distinct().OrderBy(c => g.Columns[c].DisplayIndex).ToList();
        var sel = new HashSet<DataGridViewCell>(cells);
        var matrix = rows.Select(r => (IReadOnlyList<string>)cols.Select(c => sel.Contains(g[c, r]) ? Convert.ToString(g[c, r].Value) ?? "" : "").ToList()).ToList();
        try { Clipboard.SetText(GridText.Format(matrix)); }
        catch (System.Runtime.InteropServices.ExternalException) { _note = "클립보드 사용 중"; RunCheck(); return; }
        _note = $"복사 {cells.Count}칸";
        RunCheck();
    }

    /// <summary>Puts clipboard text (tab-separated rows) into the grid from the current cell on; rows missing at the bottom are added, read-only (Auto) columns skipped.</summary>
    void PasteCells(string sheet)
    {
        var g = _grids[sheet]; var t = _tables[sheet];
        string text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
        catch (System.Runtime.InteropServices.ExternalException) { _note = "클립보드 사용 중"; RunCheck(); return; }
        var data = GridText.Parse(text);
        if (data.Count == 0) { _note = "붙여넣을 내용 없음"; RunCheck(); return; }
        g.EndEdit();
        var cols = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).Select(c => c.Index).ToList();
        if (cols.Count == 0) return;
        var cur = g.CurrentCell;
        int startRow = cur?.RowIndex ?? 0, startPos = cur == null ? 0 : Math.Max(0, cols.IndexOf(cur.ColumnIndex));
        var (cells, need, skipped) = GridText.Plan(data, startRow, startPos, cols, c => !g.Columns[c].ReadOnly);
        int added = Math.Max(0, need - g.Rows.Count);
        _busy = true;
        g.SuspendLayout();
        try
        {
            while (g.Rows.Count < need) g.Rows.Add(NewRow(g, t, 0, new string[t.Columns.Count]));
            g.ClearSelection();
            foreach (var p in cells)
            {
                var cell = g[p.Col, p.Row];
                if (cell is DataGridViewComboBoxCell cb && !cb.Items.Contains(p.Value)) cb.Items.Add(p.Value); // a value outside the list cannot be held otherwise
                cell.Value = p.Value;
                cell.Selected = true;
            }
        }
        finally { g.ResumeLayout(); _busy = false; }
        Counted(sheet);
        Touch();
        _note = $"붙여넣기 {cells.Count}칸" + (added > 0 ? $" · 행 {added} 추가" : "") + (skipped > 0 ? $" · 건너뜀 {skipped}" : "");
    }

    // a right click on a cell outside the selection selects it (a row header: the row) before the menu opens
    void OnRightDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var g = (DataGridView)sender!;
        if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
        if (e.ColumnIndex >= 0 ? g[e.ColumnIndex, e.RowIndex].Selected : g.Rows[e.RowIndex].Selected) return;
        var first = g.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
        int col = e.ColumnIndex >= 0 ? e.ColumnIndex : first?.Index ?? -1;
        if (col < 0) return;
        g.CurrentCell = g[col, e.RowIndex]; // clears the selection
        if (e.ColumnIndex < 0) g.Rows[e.RowIndex].Selected = true;
    }

    // Ctrl+C / Ctrl+V on the grid (not while a cell is being edited: the cell's own text box has them)
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData == (Keys.Control | Keys.C) || keyData == (Keys.Control | Keys.V)) && ActiveControl is DataGridView g && _grids.ContainsValue(g) && !g.IsCurrentCellInEditMode)
        {
            if (keyData == (Keys.Control | Keys.C)) CopyCells(SheetOf(g)); else PasteCells(SheetOf(g));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------- export / import ----------

    List<InputTable> Current() => _order.Where(s => _tables.ContainsKey(s) && _grids[s].Columns.Count > 0).Select(FromGrid).ToList();

    /// <summary>All tabs as they are now (unsaved edits too) to a plain .xlsx the user picks: names in row 1, labels in row 2, as in input.xlsx.</summary>
    void ExportExcel()
    {
        CommitAll();
        using var dlg = new SaveFileDialog
        {
            Title = "엑셀로 내보내기", Filter = "Excel (*.xlsx)|*.xlsx", DefaultExt = "xlsx", AddExtension = true, OverwritePrompt = true,
            FileName = System.IO.Path.GetFileNameWithoutExtension(_path) + "_공급 설정.xlsx", InitialDirectory = System.IO.Path.GetDirectoryName(_path) ?? "",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (string.Equals(System.IO.Path.GetFullPath(dlg.FileName), System.IO.Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "작업 중인 파일입니다. 다른 이름으로 내보내세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Cursor = Cursors.WaitCursor;
        try
        {
            var tables = Current();
            using (PluginLog.Time("AGVINPUT 내보내기")) InputTable.ExportFile(dlg.FileName, tables);
            _note = $"내보냄 {System.IO.Path.GetFileName(dlg.FileName)} · 시트 {tables.Count} · 행 {tables.Sum(t => t.Rows.Count)}";
            RunCheck();
        }
        catch (Exception ex)
        {
            PluginLog.Write("AGVINPUT export failed", ex);
            MessageBox.Show(this, "내보내지 못했습니다: " + ex.Message.Split('\n')[0].Trim(), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
    }

    /// <summary>The values of a chosen workbook into the grids by sheet and column name; they stay unsaved edits until "저장".</summary>
    void ImportExcel()
    {
        CommitAll();
        using var dlg = new OpenFileDialog
        {
            Title = "엑셀에서 불러오기", Filter = "Excel (*.xlsx)|*.xlsx", CheckFileExists = true,
            InitialDirectory = System.IO.Path.GetDirectoryName(_path) ?? "",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Cursor = Cursors.WaitCursor;
        try
        {
            var current = Current();
            InputTable.ImportReport rep; List<InputTable> result;
            using (PluginLog.Time("AGVINPUT 불러오기"))
            {
                using var src = _readOther(dlg.FileName);
                (result, rep) = InputTable.Import(current, src);
            }
            if (rep.Sheets > 0)
            {
                _busy = true;
                try
                {
                    for (int i = 0; i < current.Count; i++)
                        if (!ReferenceEquals(result[i], current[i])) ReplaceRows(current[i].Sheet, result[i]);
                }
                finally { _busy = false; }
                Touch();
            }
            _note = rep.Sheets == 0 ? $"불러올 시트·열 없음 · {System.IO.Path.GetFileName(dlg.FileName)}"
                : $"불러옴 {System.IO.Path.GetFileName(dlg.FileName)} · 시트 {rep.Sheets}/{rep.SheetsAsked} · 행 {rep.Rows} · 열 {rep.ColumnsMatched}"
                  + (rep.ColumnsMissing > 0 ? $" · 없는 열 {rep.ColumnsMissing}" : "") + (rep.ColumnsIgnored > 0 ? $" · 무시한 열 {rep.ColumnsIgnored}" : "");
            RunCheck();
        }
        catch (Exception ex)
        {
            PluginLog.Write("AGVINPUT import failed", ex);
            MessageBox.Show(this, "불러오지 못했습니다: " + ex.Message.Split('\n')[0].Trim(), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
    }

    // the grid's rows become the table's rows (tags = Excel rows, 0 = added); the saved state stays what every diff is against
    void ReplaceRows(string sheet, InputTable table)
    {
        var g = _grids[sheet]; var saved = _tables[sheet];
        g.SuspendLayout();
        try
        {
            g.Rows.Clear();
            g.Rows.AddRange(table.Rows.Select(r => NewRow(g, saved, r.Row, r.Values)).ToArray());
            FitColumns(g, table);
        }
        finally { g.ResumeLayout(); }
        Counted(sheet);
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

    // Delete on whole rows (selected by their number) deletes the rows; on cells it empties them (a cleared cell is saved as empty)
    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var g = (DataGridView)sender!;
        if (e.KeyCode != Keys.Delete || g.IsCurrentCellInEditMode) return;
        if (g.SelectedRows.Count > 0) DeleteRows(SheetOf(g));
        else foreach (DataGridViewCell c in g.SelectedCells) if (!c.OwningColumn.ReadOnly && !c.ReadOnly) c.Value = "";
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
        // the Excel row a grid row lands on once saved (rows below a deleted one move up)
        var where = edited.ToDictionary(kv => kv.Key, kv => _tables[kv.Key].Positions(kv.Value));
        DataGridViewCell? At(string sheet, int row, int col)
        {
            if (!edited.TryGetValue(sheet, out var t)) return null;
            int r = Array.IndexOf(where[sheet], row), c = t.Columns.ToList().FindIndex(x => x.Number == col);
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
