using System.Collections.Generic;
using System.Linq;
using ASH.Core.Traffic;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;

namespace ASH.Plugin.Cad;

/// <summary>F1 group view on the drawing: grey (ACI 8) or hide the CP references of the groups that are not selected. Display only, nothing a reader looks at (DrawingReader ignores Visible and colour).</summary>
internal static class CpViewer
{
    private const short Gray = 8;

    private static IEnumerable<BlockReference> CpRefs(Database db, Transaction tr) =>
        DrawingReader.ModelSpace(db, tr, OpenMode.ForRead)
            .Select(id => tr.GetObject(id, OpenMode.ForRead))
            .OfType<BlockReference>()
            .Where(br => Conventions.IsCpLayer(br.Layer) && DrawingReader.Eq(DrawingReader.BlockName(br, tr), Conventions.CpBlockName));

    private static string Group(BlockReference br, Transaction tr) =>
        DrawingReader.Attributes(br, tr).TryGetValue("GROUP", out var g) ? g.Trim() : "";

    private static CpDisplay Display(BlockReference br) => !br.Visible ? CpDisplay.Hidden : br.ColorIndex == Gray ? CpDisplay.Gray : CpDisplay.Normal;

    /// <summary>The groups in the drawing with their CP counts and how many are grey or hidden now.</summary>
    public static IReadOnlyList<CpGroupInfo> Scan(Database db, Transaction tr) =>
        CpGroupView.Summarize(CpRefs(db, tr).Select(br => (Group(br, tr), Display(br))));

    public static int HiddenCount(Database db, Transaction tr) => CpRefs(db, tr).Count(br => !br.Visible);

    /// <summary>Shows the CPs of the selected groups normally (ByLayer, visible) and the others grey or hidden. Null selection shows everything.</summary>
    public static void Apply(Database db, Transaction tr, CpViewMode mode, ISet<string>? selected)
    {
        var refs = CpRefs(db, tr).ToList();
        var plan = CpGroupView.Plan(refs.Select(br => Group(br, tr)).ToList(), selected, mode);
        for (int i = 0; i < refs.Count; i++)
        {
            bool visible = plan[i] != CpDisplay.Hidden;
            short color = plan[i] == CpDisplay.Gray ? Gray : Commands.InitCommands.ByLayer;
            Set(refs[i], visible, color);
            // the reference's Visible should carry its attributes; each one is set too in case ZWCAD does not
            foreach (ObjectId aid in refs[i].AttributeCollection)
                if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar && !ar.IsErased) Set(ar, visible, color);
        }
    }

    // written only when it differs, so a repeated view does not touch (and undo-record) every CP
    private static void Set(Entity e, bool visible, short color)
    {
        if (e.Visible == visible && e.ColorIndex == color) return;
        e.UpgradeOpen();
        e.Visible = visible;
        e.ColorIndex = color;
    }

    /// <summary>One line on the command line when CPs are hidden: they are exported and sent all the same.</summary>
    public static void WarnIfHidden(Editor ed, Database db, Transaction tr)
    {
        int n = HiddenCount(db, tr);
        if (n > 0) ed.WriteMessage($"\n숨긴 CP {n}개 포함 — 숨김은 화면용이라 읽기·내보내기에는 모두 들어갑니다(CP 탭 '모두 표시'로 풀기).");
    }

    public static void WarnIfHidden(Document doc)
    {
        using (var tr = doc.Database.TransactionManager.StartTransaction()) WarnIfHidden(doc.Editor, doc.Database, tr);
    }
}
