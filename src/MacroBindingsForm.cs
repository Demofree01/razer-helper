using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RazerHelper
{
    internal sealed class MacroBindingsForm : Form
    {
        private sealed class BindingView { public MacroBinding Binding; public string Name; public override string ToString(){return Name+" · "+Binding;} }
        public List<MacroBinding> Result { get; private set; }
        public FnSignal FnResult { get; private set; }
        private readonly Settings settings;
        private readonly List<MacroBinding> draft;
        private readonly ListBox list;
        private readonly ComboBox macro,key,modifier,playback;
        private readonly NumericUpDown repeat;
        private readonly CheckBox enabled;
        private readonly Label message;
        private bool fieldsDirty, loadingFields;
        public MacroBindingsForm(Settings settings)
        {
            this.settings=settings;draft=Store.Serializer().Deserialize<List<MacroBinding>>(Store.Serializer().Serialize(settings.MacroBindings));FnResult=settings.FnSignal;
            Text="按键绑定 · Hypershift";ClientSize=new Size(690,620);MinimumSize=new Size(650,560);BackColor=Theme.Background;ForeColor=Theme.Text;Font=new Font("Microsoft YaHei UI",9);Padding=new Padding(16);StartPosition=FormStartPosition.CenterParent;
            list=new ListBox{Dock=DockStyle.Top,Height=170,BackColor=Theme.Raised,ForeColor=Theme.Text};
            macro=Theme.Combo(settings.Macros.Select(m=>new Choice(m.Id,m.Name)).ToArray());macro.Width=240;if(macro.Items.Count>0)macro.SelectedIndex=0;
            var keys=new List<Choice>();foreach(char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"){uint scan=Native.MapVirtualKey(c,4);keys.Add(new Choice((scan&255)+":"+((scan&0xff00)==0xe000),c.ToString()));}keys.Add(new Choice("26:False","["));keys.Add(new Choice("27:False","]"));key=Theme.Combo(keys.ToArray());key.SelectedIndex=0;
            modifier=Theme.Combo(new Choice("Fn","Fn / Hypershift"),new Choice("CtrlAlt","Ctrl + Alt"));modifier.Width=185;modifier.SelectedIndex=FnResult==null?1:0;
            playback=Theme.Combo(new Choice("Once","播放一次"),new Choice("Repeat","指定次数"),new Choice("WhileHeld","按住时播放"),new Choice("Toggle","再次按下停止"));playback.SelectedIndex=0;
            repeat=new NumericUpDown{Minimum=1,Maximum=1000,Value=1,Width=80,Margin=new Padding(0,8,8,4)};enabled=Theme.Check("启用此绑定",false);
            macro.Name="BindingMacro";key.Name="BindingKey";modifier.Name="BindingModifier";enabled.Name="BindingEnabled";
            message=Theme.Label(FnResult==null?"Fn 尚未识别：绑定可以保存，识别前不能启用 Fn。":"已保存 Fn 信号；需手动验证在雷云退出后仍可用。",true);message.MaximumSize=new Size(650,0);
            var fields=Theme.Card("设置绑定",Theme.Row(macro,key),Theme.Row(modifier,playback,repeat),enabled,Theme.Row(Theme.Button("保存为新绑定",()=>Commit(false),true),Theme.Button("替换所选绑定",()=>Commit(true),false),Theme.Button("删除所选",Delete,false)),Theme.Row(Theme.Button("识别 Fn 信号",Calibrate,false),Theme.Button("参考雷云绑定记录",References,false)),message,Theme.Label("导入宏和查看记录均不自动启用绑定。F12 可停止宏。\n按住 / 再次按下停止也受宏运行时长上限约束。Fn 信号失效两秒后暂停识别。",true));
            var footer=Theme.Row(Theme.Button("保存并生效",Save,true),Theme.Button("取消",()=>Close(),false));footer.Dock=DockStyle.Bottom;fields.Dock=DockStyle.Fill;Controls.Add(fields);Controls.Add(list);Controls.Add(footer);
            EventHandler edited=delegate{if(!loadingFields)fieldsDirty=true;};macro.SelectedIndexChanged+=edited;key.SelectedIndexChanged+=edited;modifier.SelectedIndexChanged+=edited;playback.SelectedIndexChanged+=edited;repeat.ValueChanged+=edited;enabled.CheckedChanged+=edited;
            list.SelectedIndexChanged+=delegate{var item=list.SelectedItem as BindingView;if(item==null)return;loadingFields=true;try{var b=item.Binding;Theme.Select(macro,b.MacroId);Theme.Select(key,b.Scan+":"+b.Extended);Theme.Select(modifier,b.Modifier);Theme.Select(playback,b.Playback);repeat.Value=b.Repeat;enabled.Checked=b.Enabled;fieldsDirty=false;}finally{loadingFields=false;}};Reload();
        }
        private void Reload(){list.Items.Clear();foreach(var b in draft){var m=settings.Macros.FirstOrDefault(x=>String.Equals(x.Id,b.MacroId,StringComparison.OrdinalIgnoreCase));list.Items.Add(new BindingView{Binding=b,Name=m==null?"宏不存在":m.Name});}if(list.Items.Count>0)list.SelectedIndex=0; }
        private bool Commit(bool replace)
        {
            try{if(macro.SelectedItem==null)throw new InvalidOperationException("先创建或导入宏。");var k=Theme.Value(key).Split(':');var b=new MacroBinding{MacroId=Theme.Value(macro),Modifier=Theme.Value(modifier),Scan=Int32.Parse(k[0]),Extended=Boolean.Parse(k[1]),Playback=Theme.Value(playback),Repeat=(int)repeat.Value,Enabled=enabled.Checked};b.Validate(settings.Macros);
                if(b.Enabled && b.Modifier=="Fn" && FnResult==null)throw new InvalidOperationException("先识别并保存 Fn 信号；也可以使用 Ctrl+Alt。");
                if(b.Enabled)MacroPlan.Compile(settings.Macros.First(x=>String.Equals(x.Id,b.MacroId,StringComparison.OrdinalIgnoreCase)),settings.Macros);
                int index=replace?list.SelectedIndex:-1;if(replace && index<0)throw new InvalidOperationException("先选择要替换的绑定。");
                if(draft.Where((x,i)=>i!=index).Any(x=>x.Enabled && b.Enabled && x.Scan==b.Scan && x.Extended==b.Extended && x.Modifier==b.Modifier))throw new InvalidOperationException("该组合键已启用其他宏。");
                if(index>=0)draft[index]=b;else{if(draft.Count>=64)throw new InvalidOperationException("绑定上限 64。");draft.Add(b);}Reload();list.SelectedIndex=index>=0?index:draft.Count-1;fieldsDirty=false;message.Text="绑定已加入列表，点击“保存并生效”完成保存。";return true;
            }catch(Exception ex){message.Text=ex.Message;return false;}
        }
        private void Delete(){var b=list.SelectedItem as BindingView;if(b!=null){draft.Remove(b.Binding);fieldsDirty=false;Reload();}}
        private void Calibrate(){using(var form=new FnCalibrationForm())if(form.ShowDialog(this)==DialogResult.OK){FnResult=form.Result;message.Text="Fn 信号已加入草稿；请只启用需要的组合键并手动测试。";}}
        private async void References(){try{var scan=await Task.Run(()=>SynapseImport.Scan());if(IsDisposed)return;string text=String.Join("\r\n",scan.Bindings.Select(b=>b.MacroName+" → "+(b.Hypershift?"Fn + ":"")+b.Key+" · "+b.Playback+" · "+b.Profile));using(var form=new Form{Text="雷云绑定日志参考（可能旧）",Size=new Size(660,430),StartPosition=FormStartPosition.CenterParent}){form.Controls.Add(new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,Text=text.Length==0?"未找到绑定记录。":text,ScrollBars=ScrollBars.Both});form.ShowDialog(this);}}catch(Exception ex){message.Text=ex.Message;}}
        internal void Save(){try{if(fieldsDirty && !Commit(list.SelectedIndex>=0))return;foreach(var b in draft){b.Validate(settings.Macros);if(b.Enabled && b.Modifier=="Fn" && FnResult==null)throw new InvalidOperationException("Fn 尚未识别，此绑定不能启用。");if(b.Enabled)MacroPlan.Compile(settings.Macros.First(x=>String.Equals(x.Id,b.MacroId,StringComparison.OrdinalIgnoreCase)),settings.Macros);}if(FnResult!=null)FnResult.Validate();Result=draft;DialogResult=DialogResult.OK;Close();}catch(Exception ex){message.Text=ex.Message;}}
    }
}
