using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace DshClient {
    public sealed class Settings {
        public bool Enabled=false, Codex=true, Work=true, Done=true, Approval=true, Error=true, AutoStart=false;
        public int Volume=25;
        public Dictionary<string,string> Sounds=new Dictionary<string,string>();
    }
    public static class Files {
        public static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        public static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        public static void Write(string path,string text){Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
        public static Settings Load(string path){if(!File.Exists(path))return new Settings();var s=Json.Deserialize<Settings>(File.ReadAllText(path));if(s==null||s.Volume<0||s.Volume>100||s.Sounds==null)throw new InvalidDataException("设置文件损坏");return s;}
        // Imported PCM WAV is self-contained, bounded and natively decodable.
        // Validate before replacing the active selection; compressed WAV rejected.
        public static double ValidateWav(string path){
            using(var f=File.OpenRead(path))using(var r=new BinaryReader(f)){
                if(f.Length<44||f.Length>5*1024*1024||Encoding.ASCII.GetString(r.ReadBytes(4))!="RIFF")throw new InvalidDataException("请选择 5 MB 以内的 PCM WAV 文件。");
                uint size=r.ReadUInt32();if(size+8L!=f.Length||Encoding.ASCII.GetString(r.ReadBytes(4))!="WAVE")throw new InvalidDataException("WAV 文件长度或标识无效。");
                int channels=0,bits=0,align=0,dataChunks=0;uint rate=0,byteRate=0;long data=0;bool format=false;
                while(f.Position+8<=f.Length){string kind=Encoding.ASCII.GetString(r.ReadBytes(4));uint n=r.ReadUInt32();long end=f.Position+n;if(end>f.Length)throw new InvalidDataException("WAV 数据不完整。");
                    if(kind=="fmt "){if(format||n<16||r.ReadUInt16()!=1)throw new InvalidDataException("仅支持未压缩的 PCM WAV。");format=true;channels=r.ReadUInt16();rate=r.ReadUInt32();byteRate=r.ReadUInt32();align=r.ReadUInt16();bits=r.ReadUInt16();}
                    if(kind=="data"){data+=n;if(++dataChunks>1)throw new InvalidDataException("仅支持单数据块 WAV。");}f.Position=end+(n%2);if(f.Position>f.Length)throw new InvalidDataException("WAV 填充无效。");
                }
                if(f.Position!=f.Length||!format||channels<1||channels>2||rate<8000||rate>192000||(bits!=8&&bits!=16&&bits!=24&&bits!=32)||align!=channels*bits/8||byteRate!=rate*align||data<=0||data%align!=0||data/(double)byteRate>10)throw new InvalidDataException("音频须为单声道或立体声、8–192 kHz，长度不超过 10 秒。");
                return data/(double)byteRate;
            }
        }
    }
    public sealed class Ledger {
        readonly string path;readonly Dictionary<string,DateTime> entries=new Dictionary<string,DateTime>();
        public Ledger(string file){path=file;if(!File.Exists(path))return;foreach(string line in File.ReadAllLines(path)){var p=line.Split('|');DateTime at;if(p.Length!=2||!System.Text.RegularExpressions.Regex.IsMatch(p[0],"\\A[a-f0-9]{64}\\z")||!DateTime.TryParse(p[1],null,System.Globalization.DateTimeStyles.RoundtripKind,out at))throw new InvalidDataException("去重记录损坏，已停止播音。");if(at>DateTime.UtcNow.AddHours(-24))entries[p[0]]=at;}}
        public bool Add(string key){lock(entries){if(entries.ContainsKey(key))return false;DateTime now=DateTime.UtcNow;foreach(string k in new List<string>(entries.Keys))if(entries[k]<now.AddHours(-24))entries.Remove(k);if(entries.Count>=10000)return false;entries[key]=now;var lines=new List<string>();foreach(var p in entries)lines.Add(p.Key+"|"+p.Value.ToString("o"));try{Files.Write(path,String.Join("\n",lines));return true;}catch{entries.Remove(key);throw;}}}
    }
}
