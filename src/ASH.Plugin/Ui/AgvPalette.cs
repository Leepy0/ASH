using System;
using System.Drawing;
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
        try
        {
            if (_set == null)
            {
                // assigned only after Add succeeded: a failed build is retried by the next call instead of leaving an empty palette
                var set = new PaletteSet("ASH", new Guid("6f1a6c2e-5b7d-4e7a-9c3b-a5b000000001")) { MinimumSize = new Size(260, 400) };
                set.Add("ASH", new AgvPaletteControl());
                _set = set;
                PluginLog.Write("palette created");
            }
            _set.Visible = true;
        }
        catch (System.Exception ex)
        {
            PluginLog.Write("AGVPALETTE failed", ex);
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nASH 팔레트를 열지 못했습니다: " + ex.Message + "\n로그: " + PluginLog.Path);
        }
    }

    /// <summary>Manual recovery: rebuilds the ASH ribbon tab if it is missing and brings it to front.</summary>
    [CommandMethod("AGVRIBBON")]
    public void Ribbon()
    {
        if (!AgvRibbon.Activate())
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n리본이 아직 없습니다. 리본 화면(작업공간)으로 바꾼 뒤 다시 실행하세요.");
    }

    /// <summary>Sends "cmd " to the active document unless a command is running.</summary>
    internal static void Send(string cmd)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) { PluginLog.Write("send " + cmd + ": no active drawing"); return; }
        try
        {
            // a command waiting for input would take the command name as its answer
            var running = Application.GetSystemVariable("CMDNAMES") as string;
            if (!string.IsNullOrEmpty(running)) { PluginLog.Write("send " + cmd + ": blocked by running command " + running); doc.Editor.WriteMessage("\n실행 중인 명령을 먼저 끝내세요."); return; }
            doc.SendStringToExecute(cmd + " ", true, false, true);
            PluginLog.Write("sent " + cmd);
        }
        catch (System.Exception ex) { PluginLog.Write("send " + cmd + " failed", ex); doc.Editor.WriteMessage("\nASH: " + ex.Message); } // never let it reach the WinForms/WPF click handler
    }
}
