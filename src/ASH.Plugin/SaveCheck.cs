using System;
using System.Collections.Generic;
using System.IO;
using ASH.Core.Settings;
using ASH.Plugin.Cad;
using ASH.Plugin.Commands;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ASH.Plugin;

/// <summary>After a drawing with ASH layers is saved: the layout check (DrawingReader + LayoutChecker) with nothing printed and no zoom, its result on the palette
/// status line. SaveComplete, not BeginSave: the file is written by then, so nothing here can hold up or break the save. Off with "SaveCheck\t0" in ash.user.tsv.</summary>
internal static class SaveCheck
{
    private static readonly HashSet<Database> Hooked = new HashSet<Database>();

    // Initialize may run with drawings already open (demand load): they are hooked here, later ones when created
    internal static void Hook()
    {
        var docs = Application.DocumentManager;
        docs.DocumentCreated += OnCreated;
        docs.DocumentToBeDestroyed += OnDestroyed;
        foreach (Document d in docs) Attach(d);
    }

    internal static void Unhook()
    {
        var docs = Application.DocumentManager;
        docs.DocumentCreated -= OnCreated;
        docs.DocumentToBeDestroyed -= OnDestroyed;
        foreach (var db in new List<Database>(Hooked)) db.SaveComplete -= OnSaved;
        Hooked.Clear();
    }

    private static void OnCreated(object sender, DocumentCollectionEventArgs e)
    {
        try { Attach(e.Document); }
        catch (System.Exception ex) { PluginLog.Write("save check hook", ex); }
    }

    private static void OnDestroyed(object sender, DocumentCollectionEventArgs e)
    {
        try
        {
            var db = e.Document.Database;
            if (Hooked.Remove(db)) db.SaveComplete -= OnSaved;
        }
        catch (System.Exception ex) { PluginLog.Write("save check unhook", ex); }
    }

    private static void Attach(Document d)
    {
        var db = d.Database;
        if (Hooked.Add(db)) db.SaveComplete += OnSaved;
    }

    private static void OnSaved(object sender, DatabaseIOEventArgs e)
    {
        try { if (sender is Database db) Check(db, e.FileName); }
        catch (System.Exception ex) { PluginLog.Write("save check", ex); } // never reaches the save
    }

    private static void Check(Database db, string file)
    {
        // autosave (.sv$), DXF and the like are not the user's save
        if (!file.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) return;
        // an ASH command is waiting for input or working on the drawing: it reports itself, and DrawingUnits belongs to it
        if ((Application.GetSystemVariable("CMDNAMES") as string ?? "").IndexOf("AGV", StringComparison.OrdinalIgnoreCase) >= 0) return;
        if (!UserSettings.Load().SaveCheck) return;
        double perMeter = PerMeter(db, file);
        var before = DrawingUnits.PerMeter;
        DrawingUnits.PerMeter = perMeter;
        try
        {
            using (PluginLog.Time("저장 검증"))
            using (var tr = db.TransactionManager.StartTransaction()) // read only, never committed
            {
                if (!HasAshLayers(db, tr)) return;
                var layout = DrawingReader.Read(db, tr);
                if (layout.Paths.Count + layout.Cps.Count + layout.Blocks.Count == 0) return;
                var (errors, warnings) = CheckCommands.Count(layout);
                PaletteStatus.Set(errors + warnings == 0 ? "검증: 문제 없음" : $"검증: 오류 {errors} · 경고 {warnings} (AGVCHECK)");
                // the palette shows the active drawing; a save of another one (save all) leaves it alone
                if (Application.DocumentManager.MdiActiveDocument?.Database == db) NextStepsPanel.Update(file, layout, errors);
            }
        }
        finally { DrawingUnits.PerMeter = before; }
    }

    // A drawing nobody used ASH in has no AGV_ layer (InitCommands creates them on the first ASH command): no reading of its objects
    private static bool HasAshLayers(Database db, Transaction tr)
    {
        foreach (ObjectId id in (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))
            if (((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name.StartsWith("AGV_", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // As PathCommands.Run: from ash.settings.tsv beside the drawing; without one (nothing is created here) from the drawing's own units
    private static double PerMeter(Database db, string file)
    {
        try
        {
            var sf = Path.Combine(Path.GetDirectoryName(file) ?? "", ExportCommands.SettingsFileName);
            if (File.Exists(sf)) return UnitScale.FromSettings(SettingsFile.Load(sf));
        }
        catch (System.Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { }
        return 1.0 / UnitScale.FromInsUnits((int)db.Insunits);
    }
}
