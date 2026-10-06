using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace RazerHelper
{
    internal static partial class Tests
    {
        private static void MacroFixTests()
        {
            Test("synapse-text-function-inserts-without-clipboard",delegate
            {
                var macro=Synapse(Event("clipboard","text","A\u4e2d"),Event("delay","ms",25),Event("keyboard","scancode",15,"flag",0),Event("keyboard","scancode",15,"flag",1),Event("clipboard","text","B"));
                var sent=new List<MacroEvent>();int external=0,waited=0;
                MacroRunner.Run(macro,new[]{macro},CancellationToken.None,()=>true,e=>sent.Add(e),e=>external++,n=>waited+=n);
                Assert(!macro.AllowClipboard && external==0 && waited==25,"Imported text needs no clipboard access");
                Assert(sent.Count==8 && sent[0].Kind=="Unicode" && sent[0].Code=='A' && sent[1].Up && sent[2].Code==0x4e2d && sent[3].Up && sent[4].Kind=="Key" && sent[4].Code==15 && sent[5].Up && sent[6].Code=='B' && sent[7].Up,"Actual input order, not just action labels");
            });
            Test("legacy-synapse-text-repair-exact-events-only",delegate
            {
                var source=new Dictionary<string,object>{{"guid",Guid.NewGuid().ToString()},{"name","fixture"},{"appEngine",new Dictionary<string,object>{{"mouseMoveType","none"},{"events",new[]{Event("clipboard","text","fixture"),Event("delay","ms",30)}}}}};
                var scan=new SynapseScan();SynapseImport.ConvertList(Store.Serializer().Serialize(new[]{source}),"fixture snapshot",scan);
                var candidate=scan.Macros.Single();var original=CopyMacro(candidate.LegacyMacro);original.Name="renamed fixture";original.HotKey=117;original.TargetProcess="fixture.exe";original.MaxRunSeconds=20;
                Assert(SynapseImport.RepairLegacyText(original,scan.Macros)==1 && original.Events[0].Kind=="Text" && original.Events[0].Text=="fixture" && original.Events[1].DelayMs==30,"Known legacy event repaired");
                Assert(original.Name=="renamed fixture" && original.HotKey==117 && original.TargetProcess=="fixture.exe" && original.MaxRunSeconds==20 && !original.AllowClipboard,"User metadata and permissions preserved");
                Assert(SynapseImport.RepairLegacyText(original,scan.Macros)==0,"Repair idempotent");
                var edited=CopyMacro(candidate.LegacyMacro);edited.Events[1].DelayMs=31;string before=Store.Serializer().Serialize(edited);
                Assert(SynapseImport.RepairLegacyText(edited,scan.Macros)==0 && before==Store.Serializer().Serialize(edited),"Edited macro untouched");
                edited=CopyMacro(candidate.LegacyMacro);edited.Id=Guid.NewGuid().ToString();
                Assert(SynapseImport.RepairLegacyText(edited,scan.Macros)==0 && edited.Events[0].Kind=="Clipboard","Unproven origin untouched");
                edited=CopyMacro(candidate.LegacyMacro);
                Assert(SynapseImport.RepairLegacyText(edited,new[]{candidate,candidate})==0,"Ambiguous snapshot untouched");
            });
            Test("binding-save-current-fields-without-add-to-draft",delegate
            {
                var settings=new Settings();settings.Macros.Add(Macro(new MacroEvent{Kind="Text",Text="fixture"}));
                using(var form=new MacroBindingsForm(settings))
                {
                    Assert(Theme.Value((ComboBox)form.Controls.Find("BindingModifier",true).Single())=="CtrlAlt","Use available modifier before Fn calibration");
                    ((ComboBox)form.Controls.Find("BindingKey",true).Single()).SelectedIndex=1;
                    ((CheckBox)form.Controls.Find("BindingEnabled",true).Single()).Checked=true;
                    form.Save();
                    Assert(form.Result!=null && form.Result.Count==1 && form.Result[0].Enabled && form.Result[0].Modifier=="CtrlAlt" && form.Result[0].Scan==(Native.MapVirtualKey('B',4)&255),"One save includes selected fields");
                    Assert(settings.MacroBindings.Count==0,"Original settings remain unchanged until caller persists");
                }
            });
            Test("binding-save-updates-selected-and-keeps-count",delegate
            {
                var settings=new Settings();var macro=Macro(new MacroEvent{Kind="Delay"});settings.Macros.Add(macro);settings.MacroBindings.Add(new MacroBinding{MacroId=macro.Id,Modifier="CtrlAlt",Scan=(int)(Native.MapVirtualKey('A',4)&255),Enabled=true});
                using(var form=new MacroBindingsForm(settings))
                {
                    ((ComboBox)form.Controls.Find("BindingKey",true).Single()).SelectedIndex=2;form.Save();
                    Assert(form.Result.Count==1 && form.Result[0].Scan==(Native.MapVirtualKey('C',4)&255) && settings.MacroBindings[0].Scan==(Native.MapVirtualKey('A',4)&255),"Replace selected binding without duplicates or original mutation");
                }
            });
            Test("binding-save-refuses-unready-macro-and-unknown-Fn",delegate
            {
                var settings=new Settings();settings.Macros.Add(Macro(new MacroEvent{Kind="Clipboard",Text="fixture"}));
                using(var form=new MacroBindingsForm(settings))
                {
                    ((CheckBox)form.Controls.Find("BindingEnabled",true).Single()).Checked=true;form.Save();Assert(form.Result==null,"Clipboard permission enforced before activating a binding");
                }
                settings.Macros[0].Events[0].Kind="Text";
                using(var form=new MacroBindingsForm(settings))
                {
                    Theme.Select((ComboBox)form.Controls.Find("BindingModifier",true).Single(),"Fn");((CheckBox)form.Controls.Find("BindingEnabled",true).Single()).Checked=true;
                    form.Save();Assert(form.Result==null && settings.FnSignal==null && settings.MacroBindings.Count==0,"Uncalibrated Fn not enabled");
                }
            });
        }
    }
}
