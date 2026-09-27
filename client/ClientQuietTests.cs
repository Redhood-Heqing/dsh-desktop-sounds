using System;using System.IO;using System.Collections.Generic;using System.Threading;using System.Windows.Forms;using System.Web.Script.Serialization;using System.Runtime.InteropServices;using DshClient;
class ClientQuietTests {
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int n);
 [STAThread]static void Main(string[] args){
  if(args.Length<2||File.Exists(args[0]))throw new ArgumentException("Fresh output and asset directory required");
  int hold=args.Length>2?Int32.Parse(args[2]):2;if(hold<2||hold>60)throw new ArgumentException();
  int allowed=1,stage=0,ticks=0,focusRetries=0,emissions=0;bool failed=false;var checks=new List<object>();var timeline=new List<object>();
  Action<string,bool,string> check=(name,pass,detail)=>{checks.Add(new{name,pass,detail});failed|=!pass;};
  var form=new Form{Text="DSH 自动静音验收",Width=540,Height=260,StartPosition=FormStartPosition.CenterScreen,TopMost=true};
  form.Controls.Add(new Label{Text="正在自动核对全屏与恢复后的静音行为，测试结束后自动关闭。",Dock=DockStyle.Fill,TextAlign=System.Drawing.ContentAlignment.MiddleCenter});
  using(var player=new PcmPlayer((state,kind,v)=>Interlocked.Increment(ref emissions),()=>Thread.VolatileRead(ref allowed)==1,kind=>Path.Combine(args[1],kind+".wav"),()=>0.25)){
   var timer=new System.Windows.Forms.Timer{Interval=500};
   timer.Tick+=(s,e)=>{try{
    if(GetForegroundWindow()!=form.Handle){if(++focusRetries>20){check("focus",false,"Own window could not be foreground");timer.Stop();form.Close();return;}form.Activate();SetForegroundWindow(form.Handle);return;}focusRetries=0;
    string reason;bool quiet=player.GetObservedQuietState(out reason);
    if(stage==0){check("normal-window",!quiet,reason);Interlocked.Exchange(ref allowed,0);foreach(string k in new[]{"done","approval","error"})check("paused-"+k,!player.Enqueue(k),"Actual player rejected");stage++;}
    else if(stage==1){Interlocked.Exchange(ref allowed,1);form.FormBorderStyle=FormBorderStyle.None;form.Bounds=Screen.FromHandle(form.Handle).Bounds;form.Activate();timeline.Add(new{at=DateTime.UtcNow.ToString("o"),state="fullscreen-start"});stage++;}
    else if(stage==2){if(ticks++==0){check("fullscreen",quiet,reason);foreach(string k in new[]{"done","approval","error"})check("fullscreen-"+k,!player.Enqueue(k),"Actual player rejected");}if(ticks>=hold*2){form.FormBorderStyle=FormBorderStyle.Sizable;form.Size=new System.Drawing.Size(540,260);form.Location=new System.Drawing.Point(100,100);timeline.Add(new{at=DateTime.UtcNow.ToString("o"),state="fullscreen-end"});stage++;ticks=0;}}
    else if(stage==3){if(ticks++==0)foreach(string k in new[]{"done","approval","error"})check("transition-"+k,!player.Enqueue(k),"Monotonic transition hold");if(ticks>=6){check("normal-resumed",!quiet,reason);check("no-playback-or-late-replay",emissions==0,"Player emission count="+emissions);timer.Stop();form.Close();}}
   }catch(Exception ex){check("runtime",false,ex.GetType().Name);timer.Stop();form.Close();}};
   form.Shown+=(s,e)=>{ShowWindow(form.Handle,5);form.Activate();SetForegroundWindow(form.Handle);timer.Start();};Application.Run(form);timer.Dispose();
  }
  File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{status=failed?"FAIL":"PASS",scope="Real production PCM player with own native fullscreen window; not an actual game/exclusive-fullscreen/lock test",checks,timeline}));Environment.ExitCode=failed?1:0;
 }
}
