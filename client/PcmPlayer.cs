using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using DshProbe;
namespace DshClient {
    // One native waveOut stream. Gain is applied to this clip's samples only;
    // never changes system, device or application-session volume.
    public sealed class PcmPlayer : ISoundSink,IDisposable {
        [StructLayout(LayoutKind.Sequential,Pack=2)] struct Format {public ushort Tag,Channels;public uint Rate,ByteRate;public ushort Align,Bits,Extra;}
        [StructLayout(LayoutKind.Sequential)] struct Header {public IntPtr Data;public uint Length,Recorded;public IntPtr User;public uint Flags,Loops;public IntPtr Next,Reserved;}
        [DllImport("winmm.dll")]static extern uint waveOutOpen(out IntPtr h,uint id,ref Format format,IntPtr callback,IntPtr instance,uint flags);
        [DllImport("winmm.dll")]static extern uint waveOutPrepareHeader(IntPtr h,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveOutWrite(IntPtr h,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveOutReset(IntPtr h);
        [DllImport("winmm.dll")]static extern uint waveOutUnprepareHeader(IntPtr h,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveOutClose(IntPtr h);
        sealed class Item {public string Kind;public long At;}
        readonly List<Item> queue=new List<Item>();readonly Dictionary<string,long> last=new Dictionary<string,long>();readonly object sync=new object();
        readonly Func<bool> permit;readonly Func<string,string> path;readonly Func<double> volume;readonly Action<string,string,double> report;readonly Thread worker;readonly AutoResetEvent wake=new AutoResetEvent(false);
        volatile bool stopped,locked,discard;bool observed=true;string quietReason="starting";
        public PcmPlayer(Action<string,string,double> log,Func<bool> permitted,Func<string,string> file,Func<double> gain){report=log;permit=permitted;path=file;volume=gain;SystemEvents.SessionSwitch+=Session;SystemEvents.PowerModeChanged+=Power;worker=new Thread(Run){IsBackground=true,Name="DSH PCM audio"};worker.Start();}
        static long Clock(){return (long)(System.Diagnostics.Stopwatch.GetTimestamp()*1000.0/System.Diagnostics.Stopwatch.Frequency);}
        void Emit(string state,string kind,double value){try{report(state,kind,value);}catch{}}
        bool Quiet(){string reason;bool quiet=QuietPolicy.ShouldSuppress(out reason);if(stopped||locked||!permit()){quiet=true;reason=stopped?"stopped":locked?"locked":"playback-guard";}lock(sync){observed=quiet;quietReason=reason;}return quiet;}
        public bool GetObservedQuietState(out string reason){lock(sync){reason=quietReason;return observed;}}
        public bool Enqueue(string kind){if(kind!="done"&&kind!="approval"&&kind!="error")return false;if(Quiet())return false;lock(sync){long now=Clock(),prior;if(last.TryGetValue(kind,out prior)&&now-prior<1000){Emit("coalesced",kind,1);return true;}queue.RemoveAll(p=>now-p.At>10000);if(queue.Count>=8){int at=queue.FindIndex(p=>p.Kind=="done");if(kind=="done"||at<0)return false;queue.RemoveAt(at);}last[kind]=now;queue.Add(new Item{Kind=kind,At=now});wake.Set();return true;}}
        void Run(){while(!stopped){try{if(Quiet()){lock(sync)queue.Clear();}else{Item item=null;lock(sync){queue.RemoveAll(p=>Clock()-p.At>10000);if(queue.Count>0){int i=queue.FindIndex(p=>p.Kind=="error");if(i<0)i=queue.FindIndex(p=>p.Kind=="approval");if(i<0)i=0;item=queue[i];queue.RemoveAt(i);discard=false;}}if(item!=null){Play(item.Kind);continue;}}}catch(Exception){Emit("failed",null,0);}wake.WaitOne(250);}}
        void Play(string kind){IntPtr handle=IntPtr.Zero,data=IntPtr.Zero,header=IntPtr.Zero;bool prepared=false;uint headerSize=(uint)Marshal.SizeOf(typeof(Header));
            try{string file=path(kind);double duration=Files.ValidateWav(file);Format format=new Format();byte[] bytes=null;
                using(var r=new BinaryReader(File.OpenRead(file))){r.BaseStream.Position=12;while(r.BaseStream.Position+8<=r.BaseStream.Length){string type=System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));uint size=r.ReadUInt32();long end=r.BaseStream.Position+size;if(type=="fmt "){format.Tag=r.ReadUInt16();format.Channels=r.ReadUInt16();format.Rate=r.ReadUInt32();format.ByteRate=r.ReadUInt32();format.Align=r.ReadUInt16();format.Bits=r.ReadUInt16();}if(type=="data"){if(bytes!=null)throw new InvalidDataException();bytes=r.ReadBytes((int)size);}r.BaseStream.Position=end+size%2;}}
                if(bytes==null||bytes.Length==0)throw new InvalidDataException();double gain=Math.Max(0,Math.Min(1,volume()));Scale(bytes,format.Bits,gain);if(Quiet()||discard){Emit("discarded",kind,0);return;}
                using(var done=new AutoResetEvent(false)){
                    if(waveOutOpen(out handle,0xFFFFFFFF,ref format,done.SafeWaitHandle.DangerousGetHandle(),IntPtr.Zero,0x50000)!=0)throw new IOException("audio-device");
                    data=Marshal.AllocHGlobal(bytes.Length);Marshal.Copy(bytes,0,data,bytes.Length);header=Marshal.AllocHGlobal((int)headerSize);Marshal.StructureToPtr(new Header{Data=data,Length=(uint)bytes.Length},header,false);
                    if(waveOutPrepareHeader(handle,header,headerSize)!=0)throw new IOException("prepare");prepared=true;
                    Emit("opened",kind,duration);Emit("volume",kind,gain);done.Reset();if(waveOutWrite(handle,header,headerSize)!=0)throw new IOException("play");Emit("play-requested",kind,0);
                    long started=Clock();while(true){done.WaitOne(50);if(Quiet()||discard){waveOutReset(handle);Emit("discarded",kind,0);break;}var h=(Header)Marshal.PtrToStructure(header,typeof(Header));if((h.Flags&1)!=0){Emit("ended",kind,0);break;}if(Clock()-started>12000){waveOutReset(handle);Emit("timeout",kind,0);break;}}
                    if(handle!=IntPtr.Zero){waveOutReset(handle);if(prepared){waveOutUnprepareHeader(handle,header,headerSize);prepared=false;}waveOutClose(handle);handle=IntPtr.Zero;}
                }
            }catch(Exception){Emit("failed",kind,0);}finally{if(handle!=IntPtr.Zero){waveOutReset(handle);if(prepared)waveOutUnprepareHeader(handle,header,headerSize);waveOutClose(handle);}if(header!=IntPtr.Zero)Marshal.FreeHGlobal(header);if(data!=IntPtr.Zero)Marshal.FreeHGlobal(data);}
        }
        public static void Scale(byte[] data,int bits,double gain){int step=bits/8;for(int i=0;i<data.Length;i+=step){long value;if(bits==8)value=data[i]-128;else if(bits==16)value=(short)(data[i]|data[i+1]<<8);else if(bits==24)value=(data[i]|data[i+1]<<8|data[i+2]<<16)<<8>>8;else value=BitConverter.ToInt32(data,i);long scaled=(long)(value*gain);if(bits==8)data[i]=(byte)(scaled+128);else for(int j=0;j<step;j++)data[i+j]=(byte)(scaled>>(8*j));}}
        public void PauseAndDiscard(){lock(sync){queue.Clear();discard=true;}wake.Set();}
        void Session(object sender,SessionSwitchEventArgs e){if(e.Reason==SessionSwitchReason.SessionLock){locked=true;PauseAndDiscard();}else if(e.Reason==SessionSwitchReason.SessionUnlock)locked=false;}
        void Power(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Suspend)PauseAndDiscard();}
        public void Dispose(){if(stopped)return;stopped=true;SystemEvents.SessionSwitch-=Session;SystemEvents.PowerModeChanged-=Power;PauseAndDiscard();if(worker.Join(1500))wake.Dispose();}
    }
}
