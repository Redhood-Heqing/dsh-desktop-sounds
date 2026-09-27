using System;using System.IO;using System.Web.Script.Serialization;using DshClient;
class HostLocationTests {
 static void Main(string[] args){string root=Path.GetFullPath(args[0]);if(Directory.Exists(root))throw new Exception("Fresh fixture required");Directory.CreateDirectory(Path.Combine(root,"data"));string home=Path.Combine(root,"配置 空格 & 单引号'");Directory.CreateDirectory(home);string config=Path.Combine(home,"config.toml");File.WriteAllText(config,"# owned fixture");string saved=Environment.GetEnvironmentVariable("CODEX_HOME");
 try{
  Environment.SetEnvironmentVariable("CODEX_HOME",null);File.WriteAllText(Path.Combine(root,"data","host-location.json"),new JavaScriptSerializer().Serialize(new{Config=config}));
  if(HostLocation.ResolveConfig(root)!=config)throw new Exception("Persisted path must work without process environment");
  Environment.SetEnvironmentVariable("CODEX_HOME",Path.Combine(root,"missing"));if(HostLocation.ResolveConfig(root)!=config)throw new Exception("Stored valid path must win over stale environment");
  File.Delete(Path.Combine(root,"data","host-location.json"));File.WriteAllText(Path.Combine(root,"data","host-setup.json"),new JavaScriptSerializer().Serialize(new{Config=config}));if(HostLocation.ResolveConfig(root)!=config)throw new Exception("Legacy location migration failed");
  File.WriteAllText(Path.Combine(root,"data","host-setup.json"),"bad json");Environment.SetEnvironmentVariable("CODEX_HOME",home);if(HostLocation.ResolveConfig(root)!=config)throw new Exception("Environment fallback failed");
  Console.WriteLine("PASS 4 persisted location, no environment, migration and special-character path checks");
 }finally{Environment.SetEnvironmentVariable("CODEX_HOME",saved);}
 }
}
