using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("DSH 桌面提示音安装程序")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
namespace DshInstaller {
 public sealed class FileRow {public string Path, Sha256; public long Bytes;}
 public sealed class Inventory {public string Product, Version; public FileRow[] Files;}
 public static class Engine {
  public const string Product="DSHDesktopSounds", Exe="DSH桌面提示音.exe";
  public static string Quote(string value){return "\""+value.Replace("\"", "")+"\"";}
  public static string Hash(string path){using(var h=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
  public static Inventory Manifest(string dir){var m=new JavaScriptSerializer().Deserialize<Inventory>(File.ReadAllText(Path.Combine(dir,"install-manifest.json"),Encoding.UTF8));if(m.Product!=Product||m.Files==null||m.Files.Length<10||m.Files.Length>80)throw new InvalidDataException("安装清单无效。");return m;}
  public static string Child(string root,string relative){
   if(String.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.Contains(":")||relative.Split('/','\\').Any(x=>x==".."||x=="."))throw new InvalidDataException("安装文件路径无效。");
   string full=Path.GetFullPath(Path.Combine(root,relative));if(!full.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("文件超出安装目录。");return full;
  }
  public static void NoLinks(string target){for(var p=new DirectoryInfo(target);p!=null;p=p.Parent)if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("请选择不经过目录链接的安装位置。");}
  public static void Verify(string root){var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var row in Manifest(root).Files){if(!names.Add(row.Path))throw new InvalidDataException("安装清单重复。");string file=Child(root,row.Path);NoLinks(Path.GetDirectoryName(file));if(!File.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0||new FileInfo(file).Length!=row.Bytes||Hash(file)!=row.Sha256)throw new InvalidDataException("安装包文件校验失败："+row.Path);}}
  public static void Extract(string root){
   root=Path.GetFullPath(root);NoLinks(root);if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("解包目录必须为空。");Directory.CreateDirectory(root);
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
    long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach(var entry in zip.Entries){if(!names.Add(entry.FullName)||entry.FullName.EndsWith("/")||entry.Length<1||entry.Length>10000000||(total+=entry.Length)>25000000)throw new InvalidDataException("安装包结构无效。");string path=Child(root,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(path));using(var input=entry.Open())using(var output=new FileStream(path,FileMode.CreateNew))input.CopyTo(output);}
   }
   Verify(root);
  }
  public static string DefaultTarget(){
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DSHDesktopSounds")){var p=key==null?null:key.GetValue("InstallLocation") as string;if(!String.IsNullOrEmpty(p)&&File.Exists(Path.Combine(p,"install-manifest.json")))return p;}
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){var s=key==null?null:key.GetValue("DSHDesktopSounds") as string;if(s!=null&&s.StartsWith("\"")){int end=s.IndexOf('"',1);if(end>1){string exe=s.Substring(1,end-1);if(Path.GetFileName(exe)==Exe&&File.Exists(exe)&&File.Exists(Path.Combine(Path.GetDirectoryName(exe),"Setup.ps1")))return Path.GetDirectoryName(exe);}}}
   return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs",Product);
  }
  public static void PowerShell(string script,string args){
   string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
   var info=new ProcessStartInfo(shell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "+Quote(script)+" "+args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
   info.EnvironmentVariables["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\Modules")+";"+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"WindowsPowerShell\Modules");
   using(var process=Process.Start(info)){var stderr=process.StandardError.ReadToEndAsync();var stdout=process.StandardOutput.ReadToEndAsync();process.WaitForExit();Task.WaitAll(stderr,stdout);if(process.ExitCode!=0)throw new IOException(stderr.Result.Length>0?stderr.Result:"设置程序未完成，原 GPT 任务未被停止。");}
  }
  public static void StopOwn(string path){
   foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Exe)))using(p){if(!String.Equals(p.MainModule.FileName,path,StringComparison.OrdinalIgnoreCase))throw new IOException("另一目录的提示音正在运行，请先退出该提示音程序。");}
   try{using(var signal=EventWaitHandle.OpenExisting(@"Local\DSH.Client.Stop"))signal.Set();}catch(WaitHandleCannotBeOpenedException){}
   var deadline=DateTime.UtcNow.AddSeconds(10);do{bool active=false;foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Exe)))using(p){try{if(String.Equals(p.MainModule.FileName,path,StringComparison.OrdinalIgnoreCase))active=true;}catch{throw new IOException("无法核对已有提示音进程。");}}if(!active)return;Thread.Sleep(150);}while(DateTime.UtcNow<deadline);throw new IOException("提示音尚未退出，请关闭它的设置窗口后重试。");
  }
  public static void Install(string target,Action<string> status){
   if(!Environment.Is64BitOperatingSystem||Environment.OSVersion.Version.Major<10)throw new IOException("需要 Windows 11 x64。");
   using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")){int build;if(key==null||!Int32.TryParse(key.GetValue("CurrentBuildNumber") as string,out build)||build<22000)throw new IOException("需要 Windows 11 x64。");}
   target=Path.GetFullPath(target).TrimEnd('\\');NoLinks(target);if(target.Length>180||target==Path.GetPathRoot(target).TrimEnd('\\'))throw new IOException("请选择较短的独立安装目录。");
   string stage=Path.Combine(Path.GetTempPath(),"DSH-install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   try{
    status("正在校验安装包和 GPT 客户端…");Extract(stage);
    string configArg="";
    foreach(string name in new[]{"host-location.json","host-setup.json"}){
     string receipt=Path.Combine(target,"data",name);if(!File.Exists(receipt))continue;
     var record=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(receipt,Encoding.UTF8));object config;
     if(record.TryGetValue("Config",out config)&&config is string&&Path.IsPathRooted((string)config)&&File.Exists((string)config)){configArg=" -ConfigPath "+Quote((string)config);break;}
    }
    PowerShell(Path.Combine(stage,"Setup.ps1"),"-Mode Check -NonInteractive"+configArg);
    bool owned=File.Exists(Path.Combine(target,"install-manifest.json"));
    bool retained=false;string identity=Path.Combine(target,"data","install-identity.json");
    if(File.Exists(identity)){var record=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(identity,Encoding.UTF8));retained=record.ContainsKey("Product")&&Object.Equals(record["Product"],Product)&&record.ContainsKey("Directory")&&String.Equals(record["Directory"] as string,target,StringComparison.OrdinalIgnoreCase);}
    bool legacy=String.Equals(target,DefaultTarget(),StringComparison.OrdinalIgnoreCase)&&File.Exists(Path.Combine(target,Exe))&&File.Exists(Path.Combine(target,"data","host-setup.json"));
    if(Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any()&&!owned&&!legacy&&!retained)throw new IOException("该目录已有其他文件，请选择空目录。");
    if(owned)Manifest(target);
    string current=DefaultTarget();if(!String.Equals(current,target,StringComparison.OrdinalIgnoreCase)&&File.Exists(Path.Combine(current,Exe)))throw new IOException("已在另一目录安装。请先使用原安装的卸载入口，再选择新目录，以保留正确的通知恢复记录。");
    Directory.CreateDirectory(target);string probe=Path.Combine(target,".write-"+Guid.NewGuid().ToString("N"));File.WriteAllText(probe,"ok");File.Delete(probe);
    status("正在安装文件…");StopOwn(Path.Combine(target,Exe));
    var installed=new List<string>();var backups=new Dictionary<string,string>();bool setupStarted=false;
    try{
     var rows=Manifest(stage).Files.Select(x=>x.Path).Concat(new[]{"install-manifest.json"}).ToArray();
     foreach(string relative in rows){string dest=Child(target,relative);NoLinks(Path.GetDirectoryName(dest));Directory.CreateDirectory(Path.GetDirectoryName(dest));if(File.Exists(dest)){if((File.GetAttributes(dest)&FileAttributes.ReparsePoint)!=0)throw new IOException("安装文件不能是链接。");string saved=Child(stage,"rollback/"+relative);Directory.CreateDirectory(Path.GetDirectoryName(saved));File.Copy(dest,saved);backups[dest]=saved;}File.Copy(Child(stage,relative),dest,true);installed.Add(dest);}
     Verify(target);status("正在设置通知、自启动和卸载入口…");setupStarted=true;
     PowerShell(Path.Combine(target,"Setup.ps1"),"-Mode Install -NonInteractive");
     PowerShell(Path.Combine(target,"RegisterInstall.ps1"),"");
     status("安装完成，提示音已在后台运行。");
    }catch{
     // Before preferences are touched, restore exact prior program files.
     // After setup begins, retain repairable files + its restoration receipt;
     // never erase notification recovery information or claim success.
     if(!setupStarted){foreach(string dest in installed){string backup;if(backups.TryGetValue(dest,out backup))File.Copy(backup,dest,true);else File.Delete(dest);}}
     throw;
    }
   }finally{CleanStage(stage);}
  }
  static void CleanStage(string stage){
   string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\')+"\\";string full=Path.GetFullPath(stage);
   if(!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("DSH-install-"))return;
   try{NoLinks(full);foreach(string file in Directory.GetFiles(full,"*",SearchOption.AllDirectories))File.Delete(file);foreach(string dir in Directory.GetDirectories(full,"*",SearchOption.AllDirectories).OrderByDescending(x=>x.Length))Directory.Delete(dir);Directory.Delete(full);}catch{}
  }
  public static void Report(string action,string target,bool passed,string error){string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Product);Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"installer-last.json"),new JavaScriptSerializer().Serialize(new{at=DateTime.UtcNow.ToString("o"),action=action,target=target,status=passed?"PASS":"FAIL",error=error}),new UTF8Encoding(false));}
 }
 sealed class SetupForm:Form {
  TextBox destination;Label status;Button install,browse;bool busy;
  public SetupForm(){Text="DSH 桌面提示音 · 安装";ClientSize=new Size(620,350);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
   Controls.Add(new Label{Text="为 GPT Work / Codex 添加塔科夫提示音",AutoSize=true,Location=new Point(24,24),Font=new Font(Font.FontFamily,15,FontStyle.Bold)});
   Controls.Add(new Label{Text="安装后自动运行，无需每天打开本程序。\r\n三种音频已包含；无需下载音效或安装开发工具。\r\n需先安装并登录兼容的 GPT 桌面客户端。",AutoSize=true,Location=new Point(24,72)});
   destination=new TextBox{Text=Engine.DefaultTarget(),Location=new Point(24,163),Width=480,ReadOnly=true};Controls.Add(destination);
   browse=new Button{Text="更改…",Location=new Point(516,160),Size=new Size(80,32)};browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog{Description="选择独立安装目录；默认位置无需管理员权限。",SelectedPath=destination.Text})if(d.ShowDialog(this)==DialogResult.OK)destination.Text=d.SelectedPath;};Controls.Add(browse);
   status=new Label{Text="仅设置本用户的通知提示音和登录自启动。",Location=new Point(24,210),Size=new Size(572,60)};Controls.Add(status);
   install=new Button{Text="一键安装 / 修复",Location=new Point(370,288),Size=new Size(226,40)};install.Click+=async(s,e)=>{if(install.Text=="完成"){Close();return;}busy=true;install.Enabled=browse.Enabled=false;string target=destination.Text;
    try{await Task.Run(()=>Engine.Install(target,message=>BeginInvoke((Action)(()=>status.Text=message))));Engine.Report("install",target,true,null);install.Text="完成";}
    catch(Exception ex){Engine.Report("install",target,false,ex.Message);status.Text="安装未完成。";MessageBox.Show(this,ex.Message,"未完成安装",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    finally{busy=false;install.Enabled=browse.Enabled=true;}};Controls.Add(install);FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
  }
 }
 static class Program {
  [STAThread]static int Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);bool first;using(var mutex=new Mutex(true,@"Local\DSH.Installer",out first)){if(!first)return 3;try{
   if(args.Length==0){Application.Run(new SetupForm());return 0;}
   if(args.Length==2&&args[0]=="--extract"){Engine.Extract(args[1]);Engine.Report("extract",args[1],true,null);return 0;}
   if(args.Length==2&&args[0]=="--install"){Engine.Install(args[1],s=>{});Engine.Report("install",args[1],true,null);return 0;}
   throw new ArgumentException("支持 --extract 目录 或 --install 目录。");
  }catch(Exception ex){Engine.Report(args.Length>0?args[0]:"launch",args.Length>1?args[1]:null,false,ex.Message);return 1;}finally{mutex.ReleaseMutex();}}}
 }
}
