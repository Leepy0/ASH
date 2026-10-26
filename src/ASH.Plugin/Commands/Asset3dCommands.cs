using System;
using System.Linq;
using ASH.Core.Cel;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Plugin.Cad;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.Asset3dCommands))]

namespace ASH.Plugin.Commands;

/// <summary>AGV3D: places ASH_3D_* symbols (design 2026-10-26 §8.2). The model build turns them into the 3D static system ash_3d.</summary>
public sealed class Asset3dCommands
{
    /// <summary>Command-line keyword of a kind: ROBOT_L -> ROBOTL (keywords take no underscore). The palette sends "AGV3D &lt;keyword&gt;".</summary>
    public static string Keyword(string kind) => kind.Replace("_", "");

    [CommandMethod("AGV3D")]
    public void Agv3d()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        PathCommands.Run(doc, "AGV3D", (db, tr) =>
        {
            var ko = new PromptKeywordOptions("\n3D 에셋 종류 [" + string.Join("/", AssetKinds.Blocks.Select(Keyword)) + "]");
            foreach (var k in AssetKinds.Blocks) ko.Keywords.Add(Keyword(k));
            ko.Keywords.Default = Keyword(AssetKinds.Blocks[0]);
            var kr = ed.GetKeywords(ko);
            if (kr.Status != PromptStatus.OK) return false;
            var kind = AssetKinds.Blocks.First(k => string.Equals(Keyword(k), kr.StringResult, StringComparison.OrdinalIgnoreCase));
            int added = 0; double rot = 0;
            while (true)
            {
                var pr = ed.GetPoint(new PromptPointOptions($"\n{AssetShapes.Label(kind)} 위치(바닥 중심, Enter 종료)") { AllowNone = true });
                if (pr.Status != PromptStatus.OK) break;
                var ar = ed.GetAngle(new PromptAngleOptions("\n회전 각도") { BasePoint = pr.Value, UseBasePoint = true, AllowNone = true, DefaultValue = rot, UseDefaultValue = true });
                if (ar.Status == PromptStatus.OK) rot = ar.Value;
                else if (ar.Status != PromptStatus.None) break;
                DrawingWriter.InsertBlock(db, tr, Conventions.Asset3dBlockPrefix + kind, Conventions.Layer3d, new Point2(pr.Value.X, pr.Value.Y), rot, null);
                tr.TransactionManager.QueueForGraphicsFlush();
                doc.TransactionManager.FlushGraphics();
                added++;
            }
            if (added > 0) ed.WriteMessage($"\n{AssetShapes.Label(kind)} {added}개 배치. 크기는 특성 창의 X/Y 배율로 바꿉니다(배율 1 = 기본 크기).");
            return added > 0;
        }, refreshArrows: false);
    }

    /// <summary>A TrueType text style for the Korean block labels (the drawing's default SHX font may not have Hangul).</summary>
    internal static ObjectId KoreanTextStyle(Database db, Transaction tr)
    {
        const string name = "ASH_KO";
        var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        if (st.Has(name)) return st[name];
        st.UpgradeOpen();
        var r = new TextStyleTableRecord { Name = name, FileName = "malgun.ttf" };
        var id = st.Add(r);
        tr.AddNewlyCreatedDBObject(r, true);
        return id;
    }
}
