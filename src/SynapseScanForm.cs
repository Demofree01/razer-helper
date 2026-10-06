using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RazerHelper
{
    internal sealed class SynapseScanForm : Form
    {
        private readonly CheckedListBox list;
        private readonly TextBox preview;
        private readonly Label notes;
        private readonly Button import;
        private readonly Func<List<MacroDefinition>,bool> save;
        private readonly Settings settings;
        public SynapseScanForm(Settings settings,Func<List<MacroDefinition>,bool> save) : this(settings,save,false) { }
        internal SynapseScanForm(Settings settings,Func<List<MacroDefinition>,bool> save,bool sample)
        {
            this.settings=settings;this.save=save;Text="只读扫描雷云宏 · 预览后导入";ClientSize=new Size(780,650);MinimumSize=new Size(650,560);BackColor=Theme.Background;ForeColor=Theme.Text;Font=new Font("Microsoft YaHei UI",9);StartPosition=FormStartPosition.CenterParent;Padding=new Padding(16);
            notes=Theme.Label("正在只读扫描本地缓存与宏日志…",true);notes.MaximumSize=new Size(740,0);notes.Dock=DockStyle.Top;
            list=new CheckedListBox{Dock=DockStyle.Top,Height=185,BackColor=Theme.Raised,ForeColor=Theme.Text,CheckOnClick=true};list.SelectedIndexChanged+=delegate{ShowCandidate();};
            preview=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,BackColor=Theme.Surface,ForeColor=Theme.Text};
            import=Theme.Button("导入勾选宏（不启用绑定）",Import,true);import.Enabled=false;var footer=Theme.Row(import,Theme.Button("关闭",()=>Close(),false));footer.Dock=DockStyle.Bottom;
            Controls.Add(preview);Controls.Add(list);Controls.Add(notes);Controls.Add(footer);Shown+=async delegate{if(sample){notes.Text="界面样例 · 历史日志快照需核对后手动导入。\n默认隐藏文本，不自动启用绑定。";foreach(var macro in settings.Macros)list.Items.Add(new SynapseCandidate{Name=macro.Name,Source="样例快照",Macro=macro},false);if(list.Items.Count>0)list.SelectedIndex=0;return;}try{var scan=await Task.Run(()=>SynapseImport.Scan());if(IsDisposed)return;notes.Text=String.Join("\n",scan.Notes);foreach(var item in scan.Macros)list.Items.Add(item,false);import.Enabled=true;if(list.Items.Count>0)list.SelectedIndex=0;}catch(Exception ex){if(!IsDisposed)notes.Text="扫描失败："+ex.Message;}};
        }
        private void ShowCandidate()
        {
            var candidate=list.SelectedItem as SynapseCandidate;if(candidate==null)return;
            preview.Text=candidate.Name+"\r\n来源："+candidate.Source+"\r\n"+(candidate.Macro==null?candidate.Error:"运行上限："+candidate.Macro.MaxRunSeconds+" 秒\r\n"+String.Join("\r\n",candidate.Macro.Events.Select((e,i)=>(i+1)+". "+(e.Kind=="Delay"?"":"等待 "+e.DelayMs+" ms · ")+e.Description())));
        }
        private void Import()
        {
            try
            {
                var chosen=list.CheckedItems.Cast<SynapseCandidate>().ToList();if(chosen.Count==0)throw new InvalidOperationException("先勾选要导入的宏。");
                if(chosen.Any(x=>x.Macro==null))throw new InvalidOperationException("所选包含空宏或未转换的动作，请取消该项；不会部分导入。");
                if(chosen.Any(x=>settings.Macros.Any(m=>String.Equals(m.Id,x.Macro.Id,StringComparison.OrdinalIgnoreCase))))throw new InvalidOperationException("所选宏已经导入；可在本地编辑器中查看和修改。");
                var macros=chosen.Select(x=>Store.Serializer().Deserialize<MacroDefinition>(Store.Serializer().Serialize(x.Macro))).ToList();
                foreach(var macro in macros){macro.HotKey=0;macro.AllowExternalActions=false;macro.AllowClipboard=false;}
                if(save(macros)){DialogResult=DialogResult.OK;Close();}
            }catch(Exception ex){notes.Text=ex.Message;}
        }
    }
    internal sealed class FnCalibrationForm : Form
    {
        public FnSignal Result { get; private set; }
        private RawFnReader reader;private readonly ListBox down,up;private readonly Label message;
        public FnCalibrationForm() : this(false) { }
        internal FnCalibrationForm(bool preview)
        {
            Text="Fn 输入识别 · 不改硬件映射";ClientSize=new Size(710,440);BackColor=Theme.Background;ForeColor=Theme.Text;Font=new Font("Microsoft YaHei UI",9);StartPosition=FormStartPosition.CenterParent;Padding=new Padding(16);
            message=Theme.Label("只按住、放开 Fn，先各重复两次；分别选择稳定的按下与释放信号。\n如果 Fn 单独没有报文，取消：本状态下无法独立识别 Fn，勿选择其他键代替。\n检测只在此窗口打开时显示报文，不保存输入日志。",true);message.MaximumSize=new Size(670,0);message.Dock=DockStyle.Top;
            down=new ListBox{Dock=DockStyle.Fill,BackColor=Theme.Raised,ForeColor=Theme.Text};up=new ListBox{Dock=DockStyle.Fill,BackColor=Theme.Raised,ForeColor=Theme.Text};
            var left=new Panel{Dock=DockStyle.Left,Width=328};var right=new Panel{Dock=DockStyle.Fill};var downTitle=Theme.Label("选择 Fn 按下报文（至少 2 次）",true);downTitle.Dock=DockStyle.Top;var upTitle=Theme.Label("选择 Fn 释放报文（至少 2 次）",true);upTitle.Dock=DockStyle.Top;left.Controls.Add(down);left.Controls.Add(downTitle);right.Controls.Add(up);right.Controls.Add(upTitle);var group=new Panel{Dock=DockStyle.Fill};group.Controls.Add(right);group.Controls.Add(left);
            var footer=Theme.Row(Theme.Button("保存 Fn 按下 / 释放信号",Save,true),Theme.Button("取消",()=>Close(),false));footer.Dock=DockStyle.Bottom;Controls.Add(group);Controls.Add(message);Controls.Add(footer);
            Shown+=delegate{if(preview)return;try{reader=new RawFnReader(Handle);reader.Signal+=delegate(RawSignal signal){var prior=down.Items.Cast<RawSignal>().FirstOrDefault(x=>x.Device==signal.Device && x.Report==signal.Report);if(prior!=null){prior.Seen++;int index=down.Items.IndexOf(prior);down.Items[index]=prior;up.Items[index]=prior;return;}if(down.Items.Count>=64)return;down.Items.Add(signal);up.Items.Add(signal);};}catch(Exception ex){message.Text=ex.Message;}};
        }
        private void Save(){try{var a=down.SelectedItem as RawSignal;var b=up.SelectedItem as RawSignal;if(a==null || b==null || a.Device!=b.Device || a.Seen<2 || b.Seen<2)throw new InvalidOperationException("需要选择同一内置键盘的不同按下 / 释放信号，并各检测到至少两次。");var signal=new FnSignal{Device=a.Device,Down=a.Report,Up=b.Report};signal.Validate();Result=signal;DialogResult=DialogResult.OK;Close();}catch(Exception ex){message.Text=ex.Message;}}
        protected override void WndProc(ref Message m){if(m.Msg==0xff && reader!=null)reader.Message(m.LParam);base.WndProc(ref m);}
        protected override void OnFormClosed(FormClosedEventArgs e){if(reader!=null)reader.Dispose();base.OnFormClosed(e);}
    }
}
