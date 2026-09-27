using System;
using System.IO;
using System.Text;
using DshClient;
class ClientStateTests {
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Reject(Action action,string message){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Check(rejected,message);}
    static void Wav(string path,int samples,int format=1,int rate=8000,int byteRate=16000){using(var f=File.Create(path))using(var w=new BinaryWriter(f)){w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+samples*2);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)format);w.Write((short)1);w.Write(rate);w.Write(byteRate);w.Write((short)2);w.Write((short)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(samples*2);w.Write(new byte[samples*2]);}}
    static void Main(string[] args){if(args.Length!=1)throw new Exception("Explicit test directory required");string dir=Path.GetFullPath(args[0]);if(Directory.Exists(dir))throw new Exception("Preserve previous evidence");Directory.CreateDirectory(dir);
        string config=Path.Combine(dir,"中文 设置.json");Check(Files.Load(config).Volume==25,"unsafe default volume");Check(!Files.Load(config).Enabled,"unexpected first-launch sound");var s=new Settings{Enabled=true,Volume=37,Approval=false};Files.Write(config,Files.Json.Serialize(s));var loaded=Files.Load(config);Check(loaded.Enabled&&loaded.Volume==37&&!loaded.Approval,"settings did not persist");s.Volume=49;Files.Write(config,Files.Json.Serialize(s));Check(Files.Load(config).Volume==49,"atomic replacement failed");Check(Directory.GetFiles(dir,"*.tmp").Length==0,"temporary write left behind");
        string ledger=Path.Combine(dir,"seen.txt"),key=Files.Hash("real-event-identity");Check(new Ledger(ledger).Add(key),"fresh identity rejected");Check(!new Ledger(ledger).Add(key),"restart replayed identity");Check(new Ledger(ledger).Add(Files.Hash("other-turn")),"different turn lost");string bad=Path.Combine(dir,"bad-ledger.txt");File.WriteAllText(bad,"private or malformed");Reject(()=>new Ledger(bad),"corrupt ledger accepted");
        string wav=Path.Combine(dir,"一秒.wav");Wav(wav,8000);Check(Files.ValidateWav(wav)==1,"valid PCM rejected");Wav(wav,8000,3);Reject(()=>Files.ValidateWav(wav),"compressed or float accepted");Wav(wav,80001);Reject(()=>Files.ValidateWav(wav),"overlong accepted");Wav(wav,8000,1,8000,15000);Reject(()=>Files.ValidateWav(wav),"invalid byte rate accepted");File.WriteAllText(wav,"not a WAV");Reject(()=>Files.ValidateWav(wav),"corrupt audio accepted");Wav(wav,8000);using(var f=new FileStream(wav,FileMode.Open,FileAccess.Write)){f.SetLength(f.Length-1);}Reject(()=>Files.ValidateWav(wav),"truncation accepted");
        string original="notify = ['existing', 'literal']\r\n[desktop]\r\nnotifications-sound = \"default\" # retain comment\r\n[other]\r\nvalue = 42\r\n";
        string quiet=HostPreferences.Rewrite(original,"default","none");Check(HostPreferences.Rewrite(quiet,"none","default")==original,"sound rollback changed unrelated bytes");
        string always=HostPreferences.RewriteTurn(quiet,null,"always");Check(HostPreferences.TurnValue(always)=="always","foreground completion not enabled");Check(HostPreferences.RewriteTurn(always,"always",null)==quiet,"absent-key rollback changed bytes");
        string off=HostPreferences.RewriteTurn(always,"always","off");Reject(()=>HostPreferences.RewriteTurn(off,"always",null),"user's later preference overwritten");Reject(()=>HostPreferences.Rewrite(original,"none","default"),"compare-before-restore missing");
        byte[] samples={255,127,0,128,0,0};PcmPlayer.Scale(samples,16,0.25);Check(BitConverter.ToInt16(samples,0)==8191&&BitConverter.ToInt16(samples,2)==-8192&&BitConverter.ToInt16(samples,4)==0,"clip gain distorted PCM16");byte[] samples8={0,128,255};PcmPlayer.Scale(samples8,8,0);Check(samples8[0]==128&&samples8[1]==128&&samples8[2]==128,"8-bit silence has DC offset");
        Console.WriteLine("PASS "+checks+" settings, dedup, WAV, gain and host-setting rollback checks. File fixtures only; native-event evidence separate.");
    }
}
