using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RazerHelper
{
    internal sealed class MacroEditorForm : Form
    {
        public MacroDefinition Result { get; private set; }
        private readonly MacroDefinition draft;
        private readonly List<MacroDefinition> library;
        private readonly TreeView tree;
        private readonly TextBox name,target;
        private readonly NumericUpDown duration;
        private readonly CheckBox external,clipboard;
        private readonly Label message;
        public MacroEditorForm(MacroDefinition macro,IEnumerable<MacroDefinition> library)
        {
            draft=Store.Serializer().Deserialize<MacroDefinition>(Store.Serializer().Serialize(macro));this.library=library.ToList();
            Text="宏编辑器 · "+draft.Name;ClientSize=new Size(730,650);MinimumSize=new Size(650,580);BackColor=Theme.Background;ForeColor=Theme.Text;Font=new Font("Microsoft YaHei UI",9);StartPosition=FormStartPosition.CenterParent;Padding=new Padding(16);
            name=Theme.Input(draft.Name,190);target=Theme.Input(draft.TargetProcess,190);duration=new NumericUpDown{Minimum=1,Maximum=600,Value=draft.MaxRunSeconds,Width=80,Margin=new Padding(0,8,8,4)};
            external=Theme.Check("允许外部动作（被调用宏也须单独允许）",draft.AllowExternalActions);clipboard=Theme.Check("允许此宏写剪贴板文本（结束时尝试恢复）",draft.AllowClipboard);
            var header=Theme.Card("宏设置",Theme.Row(Theme.Label("名称",true),name,Theme.Label("目标 EXE（可选）",true),target),Theme.Row(Theme.Label("运行上限（秒）",true),duration),external,clipboard);header.Dock=DockStyle.Top;
            tree=new TreeView{Dock=DockStyle.Fill,BackColor=Theme.Raised,ForeColor=Theme.Text,BorderStyle=BorderStyle.None,HideSelection=false};tree.NodeMouseDoubleClick+=delegate{Edit();};
            var buttons=Theme.Row(Theme.Button("添加动作",()=>Add(false),true),Theme.Button("添加到循环",()=>Add(true),false),Theme.Button("编辑",Edit,false),Theme.Button("上移",()=>MoveAction(-1),false),Theme.Button("下移",()=>MoveAction(1),false),Theme.Button("删除动作",Delete,false));buttons.Dock=DockStyle.Top;
            message=Theme.Label("F12 为停止键。文本和命令参数默认隐藏；编辑时可主动显示。",true);
            var footer=Theme.Card("保存",message,Theme.Row(Theme.Button("保存宏",Save,true),Theme.Button("取消",()=>Close(),false)));footer.Dock=DockStyle.Bottom;
            Controls.Add(tree);Controls.Add(buttons);Controls.Add(header);Controls.Add(footer);RefreshTree(null);
        }
        private void RefreshTree(MacroEvent selected)
        {
            tree.BeginUpdate();tree.Nodes.Clear();AddNodes(tree.Nodes,draft.Events,selected);tree.ExpandAll();tree.EndUpdate();
        }
        private void AddNodes(TreeNodeCollection nodes,List<MacroEvent> events,MacroEvent selected)
        {
            for(int i=0;i<events.Count;i++){var e=events[i];var node=new TreeNode((i+1)+". "+(e.Kind!="Delay" && e.DelayMs>0?"等待 "+e.DelayMs+" ms · ":"")+e.Description()){Tag=e};nodes.Add(node);if(e==selected)tree.SelectedNode=node;if(e.Children!=null)AddNodes(node.Nodes,e.Children,selected);}
        }
        private List<MacroEvent> ActionOwner(TreeNode node){return node==null || node.Parent==null?draft.Events:((MacroEvent)node.Parent.Tag).Children;}
        private void Add(bool child)
        {
            var node=tree.SelectedNode;List<MacroEvent> events=ActionOwner(node);int index=node==null?events.Count:events.IndexOf((MacroEvent)node.Tag)+1;
            if(child){if(node==null || ((MacroEvent)node.Tag).Kind!="Loop"){message.Text="先选择一个循环动作。";return;}events=((MacroEvent)node.Tag).Children;index=events.Count;}
            using(var edit=new MacroActionDialog(new MacroEvent{Kind="Delay",DelayMs=100},library))if(edit.ShowDialog(this)==DialogResult.OK){events.Insert(index,edit.Result);RefreshTree(edit.Result);}
        }
        private void Edit(){var node=tree.SelectedNode;if(node==null)return;var events=ActionOwner(node);int index=events.IndexOf((MacroEvent)node.Tag);using(var edit=new MacroActionDialog(events[index],library))if(edit.ShowDialog(this)==DialogResult.OK){events[index]=edit.Result;RefreshTree(edit.Result);}}
        private void Delete(){var node=tree.SelectedNode;if(node==null)return;ActionOwner(node).Remove((MacroEvent)node.Tag);RefreshTree(null);}
        private void MoveAction(int direction){var node=tree.SelectedNode;if(node==null)return;var events=ActionOwner(node);var e=(MacroEvent)node.Tag;int index=events.IndexOf(e),next=index+direction;if(next<0 || next>=events.Count)return;events.RemoveAt(index);events.Insert(next,e);RefreshTree(e);}
        private void Save()
        {
            try{draft.Name=name.Text;draft.TargetProcess=target.Text.Trim();draft.MaxRunSeconds=(int)duration.Value;draft.AllowExternalActions=external.Checked;draft.AllowClipboard=clipboard.Checked;draft.Validate();
                // Validate structure without executing or granting permissions.
                MacroPlan.Compile(draft,library.Where(x=>x.Id!=draft.Id).Concat(new[]{draft}),false);
                Result=draft;DialogResult=DialogResult.OK;Close();
            }catch(Exception ex){message.Text=ex.Message;}
        }
    }
    internal sealed class MacroActionDialog : Form, IMessageFilter
    {
        public MacroEvent Result { get; private set; }
        private readonly MacroEvent draft;
        private readonly ComboBox kind,button,state,mouseState,reference;
        private readonly NumericUpDown delay,scan,amount,x,y,repeat;
        private readonly CheckBox extended,absolute,showText;
        private readonly TextBox text,path,args;
        private readonly Label error;
        private readonly List<Control> options=new List<Control>();
        private bool capture;
        public MacroActionDialog(MacroEvent action,IEnumerable<MacroDefinition> library)
        {
            draft=Store.Serializer().Deserialize<MacroEvent>(Store.Serializer().Serialize(action));Text="编辑动作";ClientSize=new Size(590,610);MinimumSize=new Size(540,500);StartPosition=FormStartPosition.CenterParent;BackColor=Theme.Background;ForeColor=Theme.Text;Font=new Font("Microsoft YaHei UI",9);Padding=new Padding(16);KeyPreview=true;AutoScroll=true;
            kind=Theme.Combo(new Choice("Delay","延迟"),new Choice("Key","键盘"),new Choice("Mouse","鼠标按钮"),new Choice("Wheel","滚轮"),new Choice("Move","鼠标轨迹"),new Choice("Text","直接输入文本"),new Choice("Clipboard","剪贴板文本"),new Choice("Macro","调用其他宏"),new Choice("Loop","循环"),new Choice("Launch","启动程序"),new Choice("Command","运行命令"));Theme.Select(kind,draft.Kind);
            delay=Number(0,60000,draft.DelayMs);scan=Number(1,127,Math.Max(1,Math.Min(127,draft.Code)));amount=Number(-1200,1200,draft.Kind=="Wheel"?draft.Code:120);x=Number(-65535,65535,draft.X);y=Number(-65535,65535,draft.Y);repeat=Number(1,1000,draft.Repeat);
            state=Theme.Combo(new Choice("Down","按下"),new Choice("Up","释放"));Theme.Select(state,draft.Up?"Up":"Down");extended=Theme.Check("扩展键（右 Ctrl / 导航键等）",draft.Extended);absolute=Theme.Check("绝对屏幕坐标",draft.Absolute);
            mouseState=Theme.Combo(new Choice("Down","按下"),new Choice("Up","释放"));Theme.Select(mouseState,draft.Up?"Up":"Down");
            button=Theme.Combo(new Choice("1","左键"),new Choice("2","右键"),new Choice("3","中键"),new Choice("4","侧键 1"),new Choice("5","侧键 2"));Theme.Select(button,Math.Max(1,Math.Min(5,draft.Code)).ToString());
            reference=Theme.Combo(library.Select(m=>new Choice(m.Id,m.Name)).ToArray());if(reference.Items.Count>0)Theme.Select(reference,draft.MacroId);reference.Width=300;
            text=new TextBox{Multiline=true,Width=520,Height=130,ScrollBars=ScrollBars.Vertical,BackColor=Theme.Raised,ForeColor=Theme.Text,Text="内容已隐藏",Enabled=false};showText=Theme.Check("显示 / 编辑文本内容",false);showText.CheckedChanged+=delegate{if(showText.Checked){text.Text=draft.Text;text.Enabled=true;}else{draft.Text=text.Enabled?text.Text:draft.Text;text.Text="内容已隐藏";text.Enabled=false;}};
            path=Theme.Input(draft.ActionPath,400);args=Theme.Input(draft.Arguments,500);args.UseSystemPasswordChar=true;var reveal=Theme.Check("显示命令参数",false);reveal.CheckedChanged+=delegate{args.UseSystemPasswordChar=!reveal.Checked;};
            var stack=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var top=Theme.Card("动作",Theme.Row(kind),Theme.Row(Theme.Label("动作前延时 ms",true),delay));stack.Controls.Add(top);
            options.Add(Theme.Card("键盘",Theme.Row(Theme.Label("扫描码（十进制）",true),scan,state,Theme.Button("捕获一个按键",delegate{if(!capture)Application.AddMessageFilter(this);capture=true;error.Text="在本窗口按一个键，只用于设置此动作。";},false)),extended));
            options.Add(Theme.Card("鼠标按钮",Theme.Row(button,mouseState)));
            options.Add(Theme.Card("滚轮",Theme.Row(Theme.Label("120 为一格，向下用负数",true),amount)));
            options.Add(Theme.Card("鼠标轨迹",Theme.Row(Theme.Label("X",true),x,Theme.Label("Y",true),y),absolute));
            options.Add(Theme.Card("文本",showText,text,Theme.Label("直接输入文本不改剪贴板；剪贴板动作需在宏设置中单独允许。",true)));
            options.Add(Theme.Card("嵌套宏",Theme.Row(reference)));
            options.Add(Theme.Card("循环",Theme.Row(Theme.Label("次数",true),repeat),Theme.Label("保存后选中循环，点击“添加到循环”编辑子动作。",true)));
            options.Add(Theme.Card("启动 / 命令",Theme.Row(path,Theme.Button("选择 EXE",Browse,false)),args,reveal,Theme.Label("命令由指定 EXE 接收参数，不自动拼接 shell；需在宏设置中允许外部动作。\n启动导致窗口焦点改变时，后续输入会停止。",true)));
            foreach(var control in options)stack.Controls.Add(control);
            error=Theme.Label("",true);stack.Controls.Add(error);stack.Controls.Add(Theme.Row(Theme.Button("保存动作",Save,true),Theme.Button("取消",()=>Close(),false)));Controls.Add(stack);
            kind.SelectedIndexChanged+=delegate{SelectKind();};SelectKind();
        }
        private static NumericUpDown Number(int low,int high,int value){return new NumericUpDown{Minimum=low,Maximum=high,Value=Math.Max(low,Math.Min(high,value)),Width=88,Margin=new Padding(0,7,10,5)};}
        private void SelectKind(){string type=Theme.Value(kind);for(int i=0;i<options.Count;i++)options[i].Visible=i==0 && type=="Key" || i==1 && type=="Mouse" || i==2 && type=="Wheel" || i==3 && type=="Move" || i==4 && (type=="Text" || type=="Clipboard") || i==5 && type=="Macro" || i==6 && type=="Loop" || i==7 && (type=="Launch" || type=="Command");}
        private void Browse(){using(var dialog=new OpenFileDialog{Filter="程序|*.exe"})if(dialog.ShowDialog(this)==DialogResult.OK)path.Text=dialog.FileName;}
        internal static bool CaptureKey(Message message,out int code,out bool isExtended)
        {
            code=(int)((message.LParam.ToInt64()>>16)&0xff);isExtended=(message.LParam.ToInt64()&(1L<<24))!=0;
            return (message.Msg==0x100 || message.Msg==0x104) && code>=1 && code<=127 && code!=0x58;
        }
        public bool PreFilterMessage(ref Message message){int code;bool isExtended;if(capture && ContainsFocus && CaptureKey(message,out code,out isExtended)){scan.Value=code;extended.Checked=isExtended;capture=false;Application.RemoveMessageFilter(this);error.Text="已捕获扫描码 "+code;return true;}return false;}
        protected override void OnFormClosed(FormClosedEventArgs e){Application.RemoveMessageFilter(this);base.OnFormClosed(e);}
        private void Save()
        {
            try
            {
                string type=Theme.Value(kind);var e=new MacroEvent{Kind=type,DelayMs=(int)delay.Value};
                if(type=="Key"){e.Code=(int)scan.Value;e.Extended=extended.Checked;e.Up=Theme.Value(state)=="Up";}
                if(type=="Mouse"){e.Code=Int32.Parse(Theme.Value(button));e.Up=Theme.Value(mouseState)=="Up";}
                if(type=="Wheel")e.Code=(int)amount.Value;
                if(type=="Move"){e.X=(int)x.Value;e.Y=(int)y.Value;e.Absolute=absolute.Checked;}
                if(type=="Text" || type=="Clipboard")e.Text=showText.Checked?text.Text:draft.Text;
                if(type=="Macro"){if(reference.SelectedItem==null)throw new InvalidOperationException("没有可调用的宏。");e.MacroId=Theme.Value(reference);}
                if(type=="Loop"){e.Repeat=(int)repeat.Value;e.Children=draft.Children??new List<MacroEvent>{new MacroEvent{Kind="Delay",DelayMs=100}};}
                if(type=="Launch" || type=="Command"){e.ActionPath=path.Text.Trim();e.Arguments=args.Text;}
                new MacroDefinition{Events=new List<MacroEvent>{e},MaxRunSeconds=600}.Validate();Result=e;DialogResult=DialogResult.OK;Close();
            }catch(Exception ex){error.Text=ex.Message;}
        }
    }
}
