using ASH.Core.Layout;
using ASH.Core.Settings;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.LayerColorCommands))]

namespace ASH.Plugin.Commands;

public sealed class LayerColorCommands
{
    /// <summary>The palette's "레이어 색상": asks for the colours (LayerColorDialog), saves the changed ones in this PC's ash.user.tsv and recolours the ASH layers of the
    /// current drawing at once (InitCommands.Init puts defaults + changes on every ASH layer, as every ASH command does). Works on an unsaved drawing too.</summary>
    [CommandMethod("AGVLAYERCOLOR")]
    public void AgvLayerColor()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        try
        {
            var path = UserSettings.DefaultPath;
            var user = UserSettings.Load(path);
            using (var dlg = new LayerColorDialog(LayerColors.Parse(user.LayerColors)))
            {
                if (Application.ShowModalDialog(dlg) != System.Windows.Forms.DialogResult.OK) return;
                (user with { LayerColors = LayerColors.Format(dlg.Overrides) }).Save(path);
            }
            PaletteStatus.Set("레이어 색을 저장했습니다");
            Theme.ReloadLayerColors(); // palette buttons repaint
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                InitCommands.Init(doc.Database, tr, out var recolored);
                tr.Commit();
                ed.WriteMessage($"\nASH 레이어 색상: 저장했고 현재 도면에서 색을 바꾼 레이어 {recolored}개.");
                PaletteStatus.Set($"AGVLAYERCOLOR 완료: 레이어 {recolored}개 색 변경");
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nAGVLAYERCOLOR 오류: {ex.Message}");
            PluginLog.Write("AGVLAYERCOLOR failed", ex);
            PaletteStatus.Set($"AGVLAYERCOLOR 오류: {ex.Message}");
        }
    }
}
