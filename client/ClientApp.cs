using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using DshProbe;
namespace DshClient {
    public sealed class ClientForm : Form {
        readonly string data,config,hostConfig,assets;readonly object fileLock=new object();
        readonly object hostCheckLock=new object();long hostChecked;bool hostReady;
        Settings settings;PcmPlayer player;SoundRouter router;Ledger ledger;NativeAdapter adapter;
        NotifyIcon tray;Label status,summary;CheckBox enabled,codex,work,done,approval,error,autostart;TrackBar volume;
        System.Windows.Forms.Timer uiTimer;System.Threading.Timer observeTimer;
        volatile bool preview,closing;bool loading,allowVisibility;int busy,signals,played;string last="尚未收到新事件";
        readonly ManualResetEvent clipEnded=new ManualResetEvent(false);
        readonly EventWaitHandle stopEvent=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\DSH.Client.Stop");
        readonly EventWaitHandle showEvent=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\DSH.Client.Settings");
        public ClientForm(bool minimized){
            allowVisibility=!minimized;
            data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");Directory.CreateDirectory(data);config=Path.Combine(data,"settings.json");assets=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets");
            hostConfig=HostLocation.ResolveConfig(AppDomain.CurrentDomain.BaseDirectory);
            settings=Files.Load(config);ledger=new Ledger(Path.Combine(data,"seen.txt"));
            if(settings.Enabled&&!HostReady()){settings.Enabled=false;last="通知设置未就绪；请运行安装与修复入口";}
            Text="DSH 桌面提示音";ClientSize=new Size(760,730);MinimumSize=new Size(760,670);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;BackColor=Color.FromArgb(244,245,241);
            var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=10,AutoScroll=true};Controls.Add(panel);
            panel.Controls.Add(new Label{Text="DSH 桌面提示音",Font=new Font(Font.FontFamily,20,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,12)});
            status=new Label{AutoSize=true,MaximumSize=new Size(680,0),Text="连接中…"};panel.Controls.Add(status);
            panel.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(680,0),ForeColor=Color.FromArgb(125,72,10),Text="自动跟随原客户端中的 Codex 与 GPT Work。日常无需打开此窗口。\r\n支持完成、等待确认和最终失败提示；适用版本见安装说明。",Margin=new Padding(0,8,0,12)});
            var switches=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};enabled=Check("启用自动提示音",settings.Enabled,switches);codex=Check("Codex",settings.Codex,switches);work=Check("GPT Work",settings.Work,switches);panel.Controls.Add(switches);
            var rows=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,ColumnCount=4};
            done=SoundRow(rows,0,"done","任务完成",settings.Done);approval=SoundRow(rows,1,"approval","等待确认",settings.Approval);error=SoundRow(rows,2,"error","任务出错",settings.Error);panel.Controls.Add(rows);
            var vol=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};vol.Controls.Add(new Label{Text="音量",AutoSize=true,Margin=new Padding(0,14,10,0)});volume=new TrackBar{Minimum=0,Maximum=100,Value=settings.Volume,TickFrequency=25,Width=320};var percent=new Label{Text=settings.Volume+"%",AutoSize=true,Margin=new Padding(0,14,0,0)};vol.Controls.Add(volume);vol.Controls.Add(percent);panel.Controls.Add(vol);
            autostart=new CheckBox{Text="登录 Windows 时自动运行（可随时取消）",Checked=settings.AutoStart,AutoSize=true};panel.Controls.Add(autostart);
            summary=new Label{AutoSize=true,MaximumSize=new Size(680,0),Margin=new Padding(0,10,0,10)};panel.Controls.Add(summary);
            panel.Controls.Add(new Label{Text="游戏全屏 / 无边框、锁屏和暂停时丢弃声音，不延后补响。\r\n安装时配置原应用通知；暂停和退出不会反复修改这些设置。\r\n需要恢复原通知时，请使用随附的卸载入口。",AutoSize=true,MaximumSize=new Size(680,0),ForeColor=Color.DimGray});
            var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};Button(actions,"查看诊断",()=>Open(Path.Combine(data,"diagnostics.log")));Button(actions,"打开数据目录",()=>Open(data));Button(actions,"安装与修复",()=>Setup("Install"));Button(actions,"卸载并恢复通知",()=>Setup("Uninstall"));Button(actions,"退出",()=>Close());panel.Controls.Add(actions);
            player=new PcmPlayer(Audio,()=>!closing&&(preview||settings.Enabled&&(settings.Codex||settings.Work)&&HostReady()),Resolve,()=>settings.Volume/100.0);router=new SoundRouter(player);router.SelectSource("codex","B");router.SelectSource("work","B");
            // Windows may not publish writes to an open host log through its
            // directory watcher promptly. Check cached file tails every 200 ms;
            // directory discovery is separately cached, no history scan occurs.
            adapter=new NativeAdapter(Signal,Log);observeTimer=new System.Threading.Timer(_=>{if(Interlocked.Exchange(ref busy,1)!=0)return;try{adapter.Tick();}finally{Interlocked.Exchange(ref busy,0);}},null,200,200);
            foreach(var check in new[]{enabled,codex,work,done,approval,error})check.CheckedChanged+=(s,e)=>Changed();
            volume.ValueChanged+=(s,e)=>{player.PauseAndDiscard();percent.Text=volume.Value+"%";Changed();};autostart.CheckedChanged+=(s,e)=>{if(loading)return;try{SetAutoStart(autostart.Checked);Changed();}catch(Exception ex){loading=true;autostart.Checked=!autostart.Checked;loading=false;MessageBox.Show(this,ex.Message,"自启动设置失败");}};
            var menu=new ContextMenuStrip();menu.Items.Add("设置",null,(s,e)=>ShowSettings());menu.Items.Add("暂停提示音",null,(s,e)=>{enabled.Checked=false;});menu.Items.Add("退出",null,(s,e)=>Close());
            tray=new NotifyIcon{Icon=SystemIcons.Information,Text="DSH 桌面提示音",ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=(s,e)=>ShowSettings();
            uiTimer=new System.Windows.Forms.Timer{Interval=1000};uiTimer.Tick+=(s,e)=>RefreshState();uiTimer.Start();
            Shown+=(s,e)=>{if(minimized)Hide();RefreshState();};Resize+=(s,e)=>{if(WindowState==FormWindowState.Minimized)Hide();};
            // Create a hidden HWND so Close() follows the normal FormClosed /
            // ApplicationContext exit path even when started directly to tray.
            IntPtr lifecycleHandle=Handle;
            Log("client-started");
        }
        CheckBox Check(string text,bool value,Control parent){var check=new CheckBox{Text=text,Checked=value,AutoSize=true,Margin=new Padding(0,5,22,8)};parent.Controls.Add(check);return check;}
        CheckBox SoundRow(TableLayoutPanel table,int row,string kind,string text,bool value){var check=new CheckBox{Text=text,Checked=value,AutoSize=true,Margin=new Padding(0,12,30,12)};table.Controls.Add(check,0,row);var listen=new Button{Text="试听",AutoSize=true};listen.Click+=(s,e)=>Preview(kind,text);table.Controls.Add(listen,1,row);var import=new Button{Text="替换 WAV…",AutoSize=true};import.Click+=(s,e)=>Import(kind);table.Controls.Add(import,2,row);var reset=new Button{Text="恢复原声",AutoSize=true};reset.Click+=(s,e)=>{settings.Sounds.Remove(kind);Save();};table.Controls.Add(reset,3,row);return check;}
        void Button(Control parent,string text,Action action){var b=new Button{Text=text,AutoSize=true,Margin=new Padding(0,12,12,0)};b.Click+=(s,e)=>action();parent.Controls.Add(b);}
        protected override void SetVisibleCore(bool value){base.SetVisibleCore(value&&allowVisibility);}
        void ShowSettings(){allowVisibility=true;Show();WindowState=FormWindowState.Normal;Activate();}
        void Open(string path){try{if(File.Exists(path)||Directory.Exists(path))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message,"无法打开");}}
        string Resolve(string kind){lock(fileLock){string name;return settings.Sounds.TryGetValue(kind,out name)&&System.Text.RegularExpressions.Regex.IsMatch(name,"\\A[a-f0-9]{64}\\.wav\\z")?Path.Combine(data,"sounds",name):Path.Combine(assets,kind+".wav");}}
        bool HostReady(){lock(hostCheckLock){long now=Stopwatch.GetTimestamp();if(now-hostChecked<Stopwatch.Frequency)return hostReady;hostChecked=now;try{string text=File.ReadAllText(hostConfig);hostReady=HostSoundGuard.AllowsText(text)&&HostPreferences.TurnValue(text)=="always";}catch{hostReady=false;}return hostReady;}}
        void Changed(){if(loading)return;if(enabled.Checked&&!settings.Enabled&&!HostReady()){loading=true;enabled.Checked=false;loading=false;MessageBox.Show(this,"原应用的通知设置未就绪，请运行随附的安装与修复入口。","自动提示未启用");}settings.Enabled=enabled.Checked;settings.Codex=codex.Checked;settings.Work=work.Checked;settings.Done=done.Checked;settings.Approval=approval.Checked;settings.Error=error.Checked;settings.Volume=volume.Value;settings.AutoStart=autostart.Checked;if(!settings.Enabled)player.PauseAndDiscard();Save();RefreshState();}
        void Save(){try{lock(fileLock)Files.Write(config,Files.Json.Serialize(settings));}catch(Exception ex){settings.Enabled=false;Log("settings-write-failed:"+ex.GetType().Name);MessageBox.Show(this,"设置未保存，已暂停提示音。请检查数据目录是否可写。","保存失败");}}
        void Preview(string kind,string label){if(preview)return;preview=true;player.PauseAndDiscard();last="正在试听："+label;Log("explicit-preview:"+kind);if(!player.Enqueue(kind)){preview=false;last="当前处于静音保护，试听未播放";}RefreshState();}
        void Import(string kind){using(var dialog=new OpenFileDialog{Title="选择本地提示音（PCM WAV，≤5 MB，≤10 秒）",Filter="PCM WAV (*.wav)|*.wav",CheckFileExists=true}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;try{Files.ValidateWav(dialog.FileName);string name;using(var sha=System.Security.Cryptography.SHA256.Create())using(var f=File.OpenRead(dialog.FileName))name=BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant()+".wav";string dir=Path.Combine(data,"sounds");Directory.CreateDirectory(dir);string target=Path.Combine(dir,name);if(!File.Exists(target))File.Copy(dialog.FileName,target);lock(fileLock)settings.Sounds[kind]=name;Save();last="本地 WAV 已替换";}catch(Exception ex){MessageBox.Show(this,ex.Message+"\r\n原有提示音保持不变。","无法导入");}}}
        void Signal(Signal s){Interlocked.Increment(ref signals);Log("verified-event:"+s.Entry+":"+s.Kind+":"+s.Turn);if(!settings.Enabled||s.Entry=="codex"&&!settings.Codex||s.Entry=="work"&&!settings.Work||preview||s.Kind=="done"&&!settings.Done||s.Kind=="approval"&&!settings.Approval||s.Kind=="error"&&!settings.Error)return;if(!HostReady()){Log("host-preference-conflict-suppressed");return;}
            try{string key=Files.Hash(s.Entry+"\u001f"+s.Thread+"\u001f"+s.Turn+"\u001f"+s.Kind+(s.Request==null?"":"\u001f"+s.Request));if(!ledger.Add(key)){Log("duplicate-or-ledger-capacity");return;}if(!router.Submit(s))Log("player-suppressed");}catch(Exception ex){Log("signal-rejected:"+ex.GetType().Name);}
        }
        void Audio(string state,string kind,double value){Log("audio:"+state+":"+kind+":"+value.ToString(System.Globalization.CultureInfo.InvariantCulture));if(state=="play-requested"){Interlocked.Increment(ref played);clipEnded.Reset();ThreadPool.QueueUserWorkItem(_=>Meter());}if(state=="ended"||state=="failed"||state=="timeout"||state=="discarded"||state=="rejected-duration"||state=="missing-or-oversize"||state=="open-failed"||state=="invalid-path"){clipEnded.Set();preview=false;}}
        void Meter(){try{using(var m=new AudioMeter()){float peak=0;bool found=false,muted=false;float volume=0;var end=DateTime.UtcNow.AddSeconds(12);do{bool f,b;float v;peak=Math.Max(peak,m.ReadProcessPeak((uint)Process.GetCurrentProcess().Id,out f,out b,out v));found|=f;muted|=b;volume=Math.Max(volume,v);}while(!clipEnded.WaitOne(20)&&DateTime.UtcNow<end);Log("audio-output:"+(found&&!muted&&volume>0&&!m.EndpointMuted&&m.EndpointVolume>0&&peak>0.0001f?"verified":"unverified")+":peak="+peak.ToString(System.Globalization.CultureInfo.InvariantCulture));}}catch(Exception ex){Log("meter-unavailable:"+ex.GetType().Name);}}
        void RefreshState(){if(stopEvent.WaitOne(0)){Close();return;}if(showEvent.WaitOne(0))ShowSettings();string reason;bool quiet=player.GetObservedQuietState(out reason);status.Text=adapter.Status+"\r\n"+(!settings.Enabled?"自动提示已暂停":!HostReady()?"原应用通知设置已变化，自定义自动声音已拦截":quiet?"静音保护生效："+reason:"已启用 · 等待新的 Codex / GPT Work 事件");summary.Text="已核实事件："+signals+"　播放请求："+played+"\r\n"+last;}
        void SetAutoStart(bool on){using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){string command="\""+Application.ExecutablePath+"\" --tray";if(on)key.SetValue("DSHDesktopSounds",command);else if(Object.Equals(key.GetValue("DSHDesktopSounds"),command))key.DeleteValue("DSHDesktopSounds",false);}}
        void Setup(string mode){try{Program.StartSetup(mode);}catch(Exception ex){MessageBox.Show(this,ex.Message,"安装入口无法启动");}}
        void Log(string value){try{lock(fileLock){string path=Path.Combine(data,"diagnostics.log");if(File.Exists(path)&&new FileInfo(path).Length>1048576){string old=path+".previous";if(File.Exists(old))File.Delete(old);File.Move(path,old);}File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" "+value+Environment.NewLine);}}catch{}}
        protected override void OnFormClosed(FormClosedEventArgs e){closing=true;if(uiTimer!=null)uiTimer.Stop();if(observeTimer!=null)observeTimer.Dispose();if(adapter!=null)adapter.Dispose();if(player!=null)player.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}Log("client-stopped");base.OnFormClosed(e);}
    }
    static class Program {
        public static void StartSetup(string mode){string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,mode=="Uninstall"&&File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Uninstall.ps1"))?"Uninstall.ps1":"Setup.ps1");if(!File.Exists(script))throw new FileNotFoundException("缺少安装脚本");var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),"-NoProfile -ExecutionPolicy Bypass -File \""+script+"\""+(Path.GetFileName(script)=="Setup.ps1"?" -Mode "+mode:"")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};info.EnvironmentVariables["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\Modules")+";"+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"WindowsPowerShell\Modules");Process.Start(info);}
        static bool Signal(string name){try{using(var e=EventWaitHandle.OpenExisting(name))e.Set();return true;}catch(WaitHandleCannotBeOpenedException){return false;}}
        [STAThread] static int Main(string[] args){if(args.Contains("--exit"))return Signal(@"Local\DSH.Client.Stop")?0:1;bool first;using(var mutex=new Mutex(true,@"Local\DSH.SharedSoundRouter.WebPilot",out first)){if(!first){if(!args.Contains("--tray"))Signal(@"Local\DSH.Client.Settings");return 3;}try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new ClientForm(args.Contains("--tray")));return 0;}catch(Exception ex){MessageBox.Show("提示音程序未启动。原应用任务没有被停止。\r\n"+ex.Message,"DSH 启动失败");return 1;}finally{mutex.ReleaseMutex();}}}
    }
}
