using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RazerHelper
{
    internal sealed class AdvancedPowerForm : Form
    {
        private readonly Controller controller;
        private readonly ComboBox supply, boost;
        private readonly NumericUpDown min, max, epp, watts;
        private readonly Label cpuStatus, gpuStatus, notice;
        private readonly Button applyCpu, applyGpu, restore;
        private ProcessorPolicyState current; private ProcessorPolicyChange last;
        private NvidiaSnapshot gpu;
        private string BackupPath { get { return Path.Combine(Store.DirectoryPath,"advanced","last-cpu-change.json"); } }
        public AdvancedPowerForm(Controller controller) : this(controller,false) { }
        internal AdvancedPowerForm(Controller controller, bool preview)
        {
            this.controller=controller;Text="进阶能耗调节 · 手动应用";ClientSize=new Size(620,680);MinimumSize=new Size(590,600);BackColor=Theme.Background;ForeColor=Theme.Text;Font=new Font("Microsoft YaHei UI",9);StartPosition=FormStartPosition.CenterParent;Padding=new Padding(18);AutoScroll=true;
            supply=Theme.Combo(new Choice("AC","接电参数"),new Choice("DC","电池参数"));supply.SelectedIndex=0;
            min=Number(0,100,5);max=Number(1,100,100);epp=Number(0,100,50);
            boost=Theme.Combo(new Choice("0","关闭睿频"),new Choice("1","启用"),new Choice("2","积极"),new Choice("3","能效优先启用"),new Choice("4","能效优先积极"),new Choice("5","积极（保障性能）"),new Choice("6","能效积极（保障性能）"));boost.Width=215;boost.SelectedIndex=0;
            cpuStatus=Theme.Label("待读取当前 Windows CPU 策略。",true);gpuStatus=Theme.Label("GPU 尚未查询；点击读取以避免后台唤醒独显。",true);notice=Theme.Label("打开窗口和读取均不应用参数。",true);
            applyCpu=Theme.Button("手动应用 CPU 策略",ApplyCpu,true);applyCpu.Enabled=false;restore=Theme.Button("恢复上次 CPU 原值",Restore,false);restore.Enabled=false;
            watts=Number(1,500,45);applyGpu=Theme.Button("手动应用 GPU 上限",ApplyGpu,true);applyGpu.Enabled=false;
            var stack=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=4};stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            stack.Controls.Add(Theme.Card("CPU 能耗策略",cpuStatus,Theme.Row(supply,Theme.Button("只读刷新",ReadCpu,false)),Theme.Row(Theme.Label("最小状态 %",true),min,Theme.Label("最大状态 %",true),max),Theme.Row(Theme.Label("能效偏好 EPP %",true),epp),Theme.Row(boost),Theme.Row(applyCpu,restore),Theme.Label("编辑当前 Windows 模式的保存策略，百分比不是瓦数或降压值。\nEPP：0 优先性能，100 优先节能。Windows 使用保存的接电 / 电池值。",true)),0,0);
            stack.Controls.Add(Theme.Card("NVIDIA 功耗与上限",gpuStatus,Theme.Row(Theme.Button("只读查询 GPU",ReadGpu,false)),Theme.Row(Theme.Label("功率上限 W",true),watts,applyGpu)),0,1);
            stack.Controls.Add(Theme.Card("本机调节能力",Theme.Label("CPU 精确 TDP / PPT、温度墙及降压：尚未接入可验证的 SMU 后端。\nGPU 电压、频率偏移及 TGP：需驱动提供可回读接口。\n进阶参数只在手动点击时写入，Helper 不在开机 / 插拔时重写。",true)),0,2);
            stack.Controls.Add(notice,0,3);Controls.Add(stack);
            supply.SelectedIndexChanged+=delegate{if(!preview)ReadCpu();};Shown+=delegate{if(!preview)ReadCpu();};
            try{if(File.Exists(BackupPath)){last=Store.Serializer().Deserialize<ProcessorPolicyChange>(File.ReadAllText(BackupPath));if(last!=null && last.Before!=null && last.After!=null)restore.Enabled=true;}}catch{notice.Text="旧 CPU 备份不可读，请保留文件并手动检查。";}
        }
        private static NumericUpDown Number(int low,int high,int value){return new NumericUpDown{Minimum=low,Maximum=high,Value=value,Width=74,BackColor=Theme.Raised,ForeColor=Theme.Text,Margin=new Padding(5,6,10,6)};}
        private async void ReadCpu()
        {
            applyCpu.Enabled=false;bool ac=Theme.Value(supply)=="AC";
            try{var state=await Task.Run(()=>new WindowsProcessorPolicy().Read(ac));if(IsDisposed || (Theme.Value(supply)=="AC")!=ac)return;current=state;
                cpuStatus.Text=WindowsPower.Label(state.Overlay.ToString())+" · "+(ac?"接电":"电池")+" · "+(state.Plan==state.ActivePlan?"基础计划":"模式覆盖策略")+"\n策略 "+state.Plan;
                Set(min,state.Minimum);Set(max,state.Maximum);Set(epp,state.Epp);boost.Enabled=state.Boost.HasValue && state.Boost>=0 && state.Boost<=6;if(boost.Enabled)Theme.Select(boost,state.Boost.ToString());
                applyCpu.Enabled=min.Enabled && max.Enabled;
            }catch(Exception ex){if(!IsDisposed)notice.Text="读取失败："+ex.Message;}
        }
        private static void Set(NumericUpDown control,int? value){control.Enabled=value.HasValue && value>=control.Minimum && value<=control.Maximum;if(control.Enabled)control.Value=value.Value;}
        private async void ApplyCpu()
        {
            if(current==null)return;var before=current;var desired=new ProcessorPolicyState{AC=before.AC,Plan=before.Plan,ActivePlan=before.ActivePlan,Overlay=before.Overlay,Minimum=(int)min.Value,Maximum=(int)max.Value,Epp=epp.Enabled?(int?)epp.Value:null,Boost=boost.Enabled?Int32.Parse(Theme.Value(boost)):(int?)null};
            applyCpu.Enabled=false;
            bool success=await controller.Run("手动 CPU 能耗策略",delegate
            {
                // Persist the complete original values before the first write.
                Store.WriteAtomic(BackupPath,Store.Serializer().Serialize(new ProcessorPolicyChange{Before=before,After=before}));
                last=ProcessorPolicyTransaction.Apply(new WindowsProcessorPolicy(),before,desired);
                Store.WriteAtomic(BackupPath,Store.Serializer().Serialize(last));
            });if(IsDisposed)return;restore.Enabled=last!=null;notice.Text=success?"已应用并通过回读；原值保存在本地 advanced 目录。":"应用未完成，请查看主窗口错误；未显示成功。";ReadCpu();
        }
        private async void Restore()
        {
            if(last==null)return;var saved=last;restore.Enabled=false;
            bool success=await controller.Run("恢复手动 CPU 策略原值",delegate{last=ProcessorPolicyTransaction.Apply(new WindowsProcessorPolicy(),saved.After,saved.Before);Store.WriteAtomic(BackupPath,Store.Serializer().Serialize(last));});if(IsDisposed)return;restore.Enabled=last!=null;notice.Text=success?"CPU 原值已恢复并回读。":"恢复未完成，请查看主窗口错误。";ReadCpu();
        }
        private async void ReadGpu()
        {
            applyGpu.Enabled=false;
            try{var state=await Task.Run(()=>NvidiaPower.Read());if(IsDisposed)return;gpu=state;
                gpuStatus.Text=state.Name+"\n功耗 "+Value(state.PowerWatts,"W")+" · 温度 "+Value(state.Temperature,"°C")+" · 占用 "+Value(state.Usage,"%")+"\n核心 "+Value(state.CoreMHz,"MHz")+" · 显存 "+Value(state.MemoryMHz,"MHz")+"\n"+(state.CanLimitPower?"可回读上限 "+state.PowerLimit+" W · 允许 "+state.MinimumLimit+"–"+state.MaximumLimit+" W":"当前上限不可回读，驱动未开放可恢复的瓦数调节。");
                watts.Enabled=state.CanLimitPower;if(state.CanLimitPower){watts.Minimum=(decimal)state.MinimumLimit.Value;watts.Maximum=(decimal)state.MaximumLimit.Value;watts.Value=(decimal)state.PowerLimit.Value;applyGpu.Enabled=true;}
            }catch(Exception ex){if(!IsDisposed)gpuStatus.Text="读取失败："+ex.Message;}
        }
        private static string Value(double? n,string unit){return n.HasValue?n.Value.ToString("0.#")+" "+unit:"不可读";}
        private async void ApplyGpu()
        {
            if(gpu==null)return;var before=gpu;double value=(double)watts.Value;applyGpu.Enabled=false;
            await controller.Run("手动 GPU 功率上限",delegate{Store.WriteAtomic(Path.Combine(Store.DirectoryPath,"advanced","last-gpu-before.json"),Store.Serializer().Serialize(before));NvidiaPower.SetLimit(before,value);});if(!IsDisposed)ReadGpu();
        }
    }
}
