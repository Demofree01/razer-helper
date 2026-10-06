using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RazerHelper
{
    internal static class UiPreview
    {
        internal static void Render(string directory)
        {
            Directory.CreateDirectory(directory);
            var macro=new MacroDefinition{Name="示例宏",Events=new List<MacroEvent>{new MacroEvent{Kind="Loop",Repeat=3,Children=new List<MacroEvent>{new MacroEvent{Kind="Delay",DelayMs=250},new MacroEvent{Kind="Key",Code=46},new MacroEvent{Kind="Key",Code=46,Up=true}}},new MacroEvent{Kind="Text",Text="仅用于渲染的示例文本"}}};
            var settings=new Settings();settings.Macros.Add(macro);settings.MacroBindings.Add(new MacroBinding{MacroId=macro.Id,Scan=44});
            using(var controller=new Controller(settings))
            {
                Capture(new AdvancedPowerForm(controller,true),directory,"advanced-power");
                Capture(new MacroEditorForm(macro,settings.Macros),directory,"macro-editor");
                Capture(new MacroActionDialog(new MacroEvent{Kind="Text",Text="仅用于渲染的示例文本"},settings.Macros),directory,"macro-action");
                Capture(new MacroBindingsForm(settings),directory,"macro-bindings");
                Capture(new SynapseScanForm(settings,delegate{return false;},true),directory,"synapse-scan");
                Capture(new FnCalibrationForm(true),directory,"fn-calibration");
            }
        }
        private static void Capture(Form form,string directory,string name)
        {
            using(form){form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(directory,name+".png"));}form.Close();}
        }
    }
}
