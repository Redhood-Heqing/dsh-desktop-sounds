using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Collections.Generic;
namespace DshClient {
 public static class HostLocation {
  public static string ResolveConfig(string baseDirectory){
   foreach(string name in new[]{"host-location.json","host-setup.json"}){
    string file=Path.Combine(baseDirectory,"data",name);
    if(!File.Exists(file))continue;
    try{var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));object value;
     if(d.TryGetValue("Config",out value)&&value is string&&Path.IsPathRooted((string)value)&&File.Exists((string)value))return Path.GetFullPath((string)value);
    }catch{}
   }
   foreach(string home in new[]{Environment.GetEnvironmentVariable("CODEX_HOME"),Environment.GetEnvironmentVariable("CODEX_HOME",EnvironmentVariableTarget.User),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex")}){
    if(String.IsNullOrWhiteSpace(home)||!Path.IsPathRooted(home))continue;
    string config=Path.Combine(home,"config.toml");if(File.Exists(config))return Path.GetFullPath(config);
   }
   return null;
  }
 }
}
