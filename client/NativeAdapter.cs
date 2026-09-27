using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using DshProbe;
namespace DshClient {
    // Global host log only supplies fresh candidates. Every candidate must be
    // joined to read-only canonical state from that exact desktop host/turn.
    // No turn-start, permission-decision or owner claim is ever transmitted.
    public sealed class NativeAdapter : IDisposable {
        public static bool SupportedHash(string hash){return hash=="96b6aa6e1ea46dd8a30b3fa5166be12284ba66bd3901241a81a60684f150189d"||hash=="89fba67324ffb8dd54ccf13b6f097172e697549eeb1f26396f86f972c10c5b0c";}
        sealed class Pending {public string Thread,Turn,Request;public DateTime At;}
        readonly object sync=new object(),sendLock=new object();
        readonly Dictionary<string,CodexState> states=new Dictionary<string,CodexState>();
        readonly Dictionary<string,DateTime> followed=new Dictionary<string,DateTime>();
        readonly Dictionary<string,HashSet<string>> requestSeen=new Dictionary<string,HashSet<string>>();
        readonly Dictionary<string,Pending> pending=new Dictionary<string,Pending>();
        readonly Dictionary<string,long> positions=new Dictionary<string,long>();
        readonly Dictionary<string,string> tails=new Dictionary<string,string>();
        readonly Action<Signal> emit;readonly Action<string> log;
        readonly CancellationTokenSource stopping=new CancellationTokenSource();
        volatile bool stopped;NamedPipeClientStream pipe;Thread worker;string client,folder,hostPid;DateTime baseline;
        DateTimeOffset hostStarted;FileSystemWatcher watcher;readonly System.Threading.Timer changedTimer;int tickQueued,terminalFollowup;
        string checkedArchive,checkedHash;long checkedLength;DateTime checkedWrite;
        string cachedLogDay;DateTime logPathsChecked;string[] cachedLogPaths;int logPathsDirty=1;
        public volatile string Status="等待应用启动";
        public NativeAdapter(Action<Signal> signal,Action<string> diagnostic){emit=signal;log=diagnostic;changedTimer=new System.Threading.Timer(_=>{Interlocked.Exchange(ref tickQueued,0);Tick();if(Interlocked.Exchange(ref terminalFollowup,0)==1)LogChanged(120);},null,Timeout.Infinite,Timeout.Infinite);worker=new Thread(Run){IsBackground=true,Name="DSH native events"};worker.Start();}
        void LogChanged(int delay=20){if(stopped||Interlocked.CompareExchange(ref tickQueued,1,0)!=0)return;try{changedTimer.Change(delay,Timeout.Infinite);}catch(ObjectDisposedException){}}
        static string Str(object o,string key){return CodexState.Text(CodexState.Get(o,key));}
        void Send(object message){lock(sendLock){var b=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(message));pipe.Write(BitConverter.GetBytes(b.Length),0,4);pipe.Write(b,0,b.Length);pipe.Flush();}}
        void Follow(string id,bool value){Send(new{type="broadcast",method="thread-stream-following-changed",version=1,sourceClientId=client,@params=new{hostId="local",conversationId=id,following=value}});}
        byte[] Read(int size){byte[] data=new byte[size];int at=0;while(at<size){int n=pipe.ReadAsync(data,at,size-at,stopping.Token).GetAwaiter().GetResult();if(n==0)throw new EndOfStreamException();at+=n;}return data;}
        void ReadInto(byte[] data,int size){int at=0;while(at<size){int n=pipe.ReadAsync(data,at,size-at,stopping.Token).GetAwaiter().GetResult();if(n==0)throw new EndOfStreamException();at+=n;}}
        void Run(){int backoff=1000;while(!stopped){try{
            Process host=null;foreach(var p in Process.GetProcessesByName("ChatGPT"))if(p.MainWindowHandle!=IntPtr.Zero){if(host!=null)throw new InvalidDataException("multiple-hosts");host=p;}
            if(host==null){Status="等待应用启动";if(stopping.Token.WaitHandle.WaitOne(3000))break;continue;}
            string archive=Path.Combine(Path.GetDirectoryName(host.MainModule.FileName),"resources","app.asar");var info=new FileInfo(archive);
            if(checkedArchive!=archive||checkedLength!=info.Length||checkedWrite!=info.LastWriteTimeUtc){
                using(var sha=System.Security.Cryptography.SHA256.Create())using(var f=File.OpenRead(archive))checkedHash=BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();
                checkedArchive=archive;checkedLength=info.Length;checkedWrite=info.LastWriteTimeUtc;
            }
            string hash=checkedHash;
            if(!SupportedHash(hash)){Status="应用版本变化，已停止接入；请更新提示音安装包";if(stopping.Token.WaitHandle.WaitOne(30000))break;continue;}
            hostPid=host.Id.ToString();hostStarted=new DateTimeOffset(host.StartTime.ToUniversalTime());folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages","OpenAI.Codex_2p2nqsd0c76g0","LocalCache","Local","Codex","Logs");Interlocked.Exchange(ref logPathsDirty,1);
            lock(sync){states.Clear();followed.Clear();requestSeen.Clear();pending.Clear();positions.Clear();tails.Clear();baseline=DateTime.UtcNow;foreach(string path in LogFiles()){using(var current=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))positions[path]=current.Length;tails[path]="";}}
            if(Directory.Exists(folder)){watcher=new FileSystemWatcher(folder,"*-"+hostPid+"-*.log"){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.Size|NotifyFilters.LastWrite|NotifyFilters.FileName,InternalBufferSize=32768};watcher.Changed+=(s,e)=>LogChanged();watcher.Created+=(s,e)=>{Interlocked.Exchange(ref logPathsDirty,1);LogChanged();};watcher.Renamed+=(s,e)=>{Interlocked.Exchange(ref logPathsDirty,1);LogChanged();};watcher.Error+=(s,e)=>{Interlocked.Exchange(ref logPathsDirty,1);log("log-watch-rescan");LogChanged();};watcher.EnableRaisingEvents=true;}
            pipe=new NamedPipeClientStream(".","codex-ipc",PipeDirection.InOut,PipeOptions.Asynchronous);pipe.Connect(5000);Send(new{type="request",requestId=Guid.NewGuid().ToString(),method="initialize",version=0,@params=new{clientType="dsh-native-sound-readonly"}});
            byte[] frame=new byte[65536];
            while(!stopped){int size=BitConverter.ToInt32(Read(4),0);if(size<1||size>16777216)throw new InvalidDataException("frame-limit");if(size>frame.Length)Array.Resize(ref frame,Math.Min(16777216,Math.Max(size,frame.Length*2)));ReadInto(frame,size);object message=NativeFrame.Parse(frame,size);string type=Str(message,"type"),method=Str(message,"method");
                if(method=="initialize"&&Str(message,"resultType")=="success"){client=Str(CodexState.Get(message,"result"),"clientId");if(client==null)throw new InvalidDataException();Status="已连接 · Codex / GPT Work 原生事件";log("adapter-connected");backoff=1000;continue;}
                if(type=="client-discovery-request"){Send(new{type="client-discovery-response",requestId=CodexState.Get(message,"requestId"),response=new{canHandle=false}});continue;}
                if(type=="request"){Send(new{type="response",requestId=CodexState.Get(message,"requestId"),resultType="error",error="read-only-adapter"});continue;}
                if(method=="thread-stream-following-status-requested"&&Object.Equals(CodexState.Get(message,"version"),1)){lock(sync){foreach(string followedThread in followed.Keys)Follow(followedThread,true);}continue;}
                if(method=="thread-read-state-changed"&&Object.Equals(CodexState.Get(message,"version"),3)||method=="thread-queued-followups-changed"&&Object.Equals(CodexState.Get(message,"version"),2)){
                    object discovery=CodexState.Get(message,"params");string thread=Str(discovery,"conversationId");Guid guid;
                    if(Str(discovery,"hostId")=="local"&&Guid.TryParse(thread,out guid))lock(sync){Subscribe(thread);Interlocked.Exchange(ref terminalFollowup,1);LogChanged();}continue;
                }
                if(method!="thread-stream-state-changed"||!Object.Equals(CodexState.Get(message,"version"),11))continue;
                object p=CodexState.Get(message,"params");string id=Str(p,"conversationId");if(Str(p,"hostId")!="local"||id==null)continue;
                lock(sync){CodexState state;if(!states.TryGetValue(id,out state))continue;string priorRuntime=state.Runtime;state.Apply(CodexState.Get(p,"change"));NewRequests(id,state);if(state.Runtime=="active")followed[id]=DateTime.UtcNow;Evaluate();
                    // Windows may defer file-change notifications while the host
                    // keeps its log open. A real active-to-terminal transition wakes
                    // the existing log/canonical join; it never emits a sound itself.
                    // One bounded retry covers host logging just after the IPC delta.
                    if(state.Valid&&priorRuntime=="active"&&(state.Runtime=="idle"||state.Runtime=="systemError")){Interlocked.Exchange(ref terminalFollowup,1);LogChanged();}
                }
            }
        }catch(Exception ex){if(!stopped){Status="连接恢复中";log("adapter-reconnect:"+ex.GetType().Name);}}finally{client=null;if(watcher!=null){watcher.Dispose();watcher=null;}if(pipe!=null)pipe.Dispose();}
            if(stopping.Token.WaitHandle.WaitOne(backoff))break;backoff=Math.Min(30000,backoff*2);
        }}
        public static string LogDirectory(string root,DateTimeOffset instant){var utc=instant.UtcDateTime;var culture=System.Globalization.CultureInfo.InvariantCulture;return Path.Combine(root,utc.ToString("yyyy",culture),utc.ToString("MM",culture),utc.ToString("dd",culture));}
        IEnumerable<string> LogFiles(){var now=DateTimeOffset.UtcNow;string today=LogDirectory(folder,now);if(cachedLogPaths!=null&&cachedLogDay==today&&now.UtcDateTime-logPathsChecked<TimeSpan.FromSeconds(30)&&Interlocked.CompareExchange(ref logPathsDirty,0,0)==0)return cachedLogPaths;
            Interlocked.Exchange(ref logPathsDirty,0);var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var days=new HashSet<string>{today,LogDirectory(folder,now.AddDays(-1)),LogDirectory(folder,hostStarted)};foreach(string day in days)if(Directory.Exists(day))foreach(string path in Directory.GetFiles(day,"*-"+hostPid+"-*.log"))result.Add(path);cachedLogPaths=new string[result.Count];result.CopyTo(cachedLogPaths);cachedLogDay=today;logPathsChecked=now.UtcDateTime;return cachedLogPaths;}
        static string Field(string line,string field){var m=Regex.Match(line,"\\b"+field+"=([^\\s]+)");return m.Success?m.Groups[1].Value:null;}
        public void Tick(){if(stopped||client==null)return;try{lock(sync){
            foreach(string path in LogFiles()){
                long offset;if(!positions.TryGetValue(path,out offset)){offset=0;positions[path]=0;tails[path]="";}
                using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                    if(f.Length<offset){offset=0;tails[path]="";}long count=f.Length-offset;if(count==0)continue;if(count>1024*1024){positions[path]=f.Length;tails[path]="";log("log-backlog-discarded");continue;}
                    byte[] buffer=new byte[(int)count];f.Position=offset;int n=f.Read(buffer,0,buffer.Length);positions[path]=offset+n;string[] lines=(tails[path]+Encoding.UTF8.GetString(buffer,0,n)).Split('\n');tails[path]=lines[lines.Length-1];if(tails[path].Length>65536)tails[path]="";
                    for(int i=0;i<lines.Length-1;i++)Candidate(lines[i]);
                }
            }
            Evaluate();
            foreach(string id in new List<string>(followed.Keys))if(states[id].Runtime!="active"&&DateTime.UtcNow-followed[id]>TimeSpan.FromSeconds(15)){Follow(id,false);followed.Remove(id);states.Remove(id);requestSeen.Remove(id);}
        }}catch(Exception ex){log("observation-error:"+ex.GetType().Name);}}
        void Subscribe(string id){if(states.ContainsKey(id)){followed[id]=DateTime.UtcNow;return;}if(states.Count>=16){log("follow-capacity-discarded");return;}states[id]=new CodexState();followed[id]=DateTime.UtcNow;Follow(id,true);}
        void NewRequests(string id,CodexState state){if(!state.Valid||state.Snapshots==0||state.Entry=="unknown")return;HashSet<string> seen;if(!requestSeen.TryGetValue(id,out seen)){seen=new HashSet<string>();requestSeen[id]=seen;foreach(var r in state.Requests)if(r.Id!=null)seen.Add(r.Id);return;}foreach(var r in state.Requests)if(state.IsBlockingRequest(r)&&seen.Add(r.Id)){emit(new Signal{Entry=state.Entry,Source="B",Thread=Files.Hash(id),Turn=Files.Hash(r.TurnId),Kind="approval",Request=Files.Hash(r.Id),Blocking=true,Verified=true});}}
        void Candidate(string line){
            bool done=line.Contains("[desktop-notifications] received turn-complete");bool question=line.Contains("[desktop-notifications] show notification")&&(Field(line,"kind")=="question"||Field(line,"kind")=="permission");
            if(!done&&!question)return;int sep=line.IndexOf(' ');DateTime at;if(sep<0||!DateTime.TryParse(line.Substring(0,sep),null,System.Globalization.DateTimeStyles.RoundtripKind,out at)||at.ToUniversalTime()<baseline||DateTime.UtcNow-at.ToUniversalTime()>TimeSpan.FromSeconds(10))return;
            string id=Field(line,"conversationId"),turn=done?Field(line,"turnId"):null,request=question?Field(line,"requestId"):null;Guid uuid;
            if(!Guid.TryParse(id,out uuid)||(done&&!Guid.TryParse(turn,out uuid))||(question&&String.IsNullOrEmpty(request)))return;
            string key=id+"/"+(turn??request);if(pending.ContainsKey(key))return;if(pending.Count>=32||(!states.ContainsKey(id)&&states.Count>=16)){log("candidate-capacity-discarded");return;}
            pending[key]=new Pending{Thread=id,Turn=turn,Request=request,At=at.ToUniversalTime()};
            Subscribe(id);
            log("fresh-candidate:"+(done?"terminal":"input")+":"+Files.Hash(key));
        }
        void Evaluate(){foreach(string key in new List<string>(pending.Keys)){
            Pending p=pending[key];CodexState s;if(!states.TryGetValue(p.Thread,out s))continue;string kind=null,turn=p.Turn;
            if(p.Request!=null){foreach(var r in s.Requests)if(r.Id==p.Request&&s.IsBlockingRequest(r)){kind="approval";turn=r.TurnId;break;}}
            else{string outcome=s.Outcome(p.Turn);if(outcome=="completed")kind="done";else if(outcome=="failed")kind="error";else if(outcome=="cancelled"){pending.Remove(key);log("cancelled-silent");continue;}}
            if(kind!=null){pending.Remove(key);if(s.Entry=="unknown"){log("unknown-native-entry-suppressed");continue;}emit(new Signal{Entry=s.Entry,Source="B",Thread=Files.Hash(p.Thread),Turn=Files.Hash(turn),Kind=kind,Request=p.Request==null?null:Files.Hash(p.Request),Blocking=p.Request!=null,Verified=true});}
            else if(DateTime.UtcNow-p.At>TimeSpan.FromSeconds(10)){pending.Remove(key);log("unverified-expired");}
        }}
        public void Dispose(){stopped=true;changedTimer.Dispose();lock(sync){try{if(client!=null)foreach(string id in followed.Keys)Follow(id,false);}catch{}stopping.Cancel();if(pipe!=null)pipe.Dispose();}worker.Join(1000);}
    }
}
