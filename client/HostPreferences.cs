using System;
using System.IO;
using System.Text.RegularExpressions;
namespace DshClient {
 public static class HostPreferences {
        public static string Rewrite(string text,string expected,string target){
            if(text.Length>2097152||text.Contains("\"\"\"")||text.Contains("'''"))throw new InvalidDataException("无法安全识别原应用通知配置。");
            bool desktop=false;int count=0,section=0;string[] lines=text.Split('\n');
            for(int i=0;i<lines.Length;i++){string line=lines[i].Trim();if(line.StartsWith("[")){desktop=Regex.IsMatch(line,@"\A\[desktop\]\s*(?:#.*)?\z");if(desktop)section++;continue;}if(!desktop||line.StartsWith("#")||!line.Contains("notifications-sound"))continue;
                var match=Regex.Match(lines[i],"\\A(\\s*notifications-sound\\s*=\\s*)\"(none|default)\"(\\s*(?:#.*)?\\r?)\\z");if(!match.Success||++count>1||match.Groups[2].Value!=expected)throw new InvalidDataException("原应用声音设置已变化，未覆盖。");lines[i]=match.Groups[1].Value+"\""+target+"\""+match.Groups[3].Value;}
            if(count!=1||section!=1)throw new InvalidDataException("请先在原应用设置中选择一次通知提示音，再重试。");return String.Join("\n",lines);
        }
        public static string TurnValue(string text){bool desktop=false;string result=null;int count=0,sections=0;foreach(string raw in text.Split('\n')){string line=raw.Trim();if(line.StartsWith("[")){desktop=Regex.IsMatch(line,@"\A\[desktop\]\s*(?:#.*)?\z");if(desktop)sections++;continue;}if(!desktop||line.StartsWith("#")||!line.Contains("notifications-turn-mode"))continue;var m=Regex.Match(line,"\\Anotifications-turn-mode\\s*=\\s*\"(always|unfocused|off)\"\\s*(?:#.*)?\\z");if(!m.Success||++count>1)throw new InvalidDataException("无法安全识别完成通知设置。");result=m.Groups[1].Value;}if(sections!=1)throw new InvalidDataException("通知配置分组不明确。");return result;}
        public static string RewriteTurn(string text,string expected,string target){
            if(TurnValue(text)!=expected)throw new InvalidDataException("完成通知设置已变化。");string newline=text.Contains("\r\n")?"\r\n":"\n";
            if(expected==null){if(target==null)return text;return Regex.Replace(text,@"(?m)^(\[desktop\][^\n]*\n)",m=>m.Value+"notifications-turn-mode = \""+target+"\""+newline,RegexOptions.None,TimeSpan.FromSeconds(1));}
            bool desktop=false;var result=new System.Collections.Generic.List<string>();foreach(string line in text.Split('\n')){if(line.TrimStart().StartsWith("["))desktop=Regex.IsMatch(line.Trim(),@"\A\[desktop\]\s*(?:#.*)?\z");if(desktop&&Regex.IsMatch(line,@"\A\s*notifications-turn-mode\s*=")){if(target==null)continue;result.Add(Regex.Replace(line,"\"(always|unfocused|off)\"","\""+target+"\"",RegexOptions.None,TimeSpan.FromSeconds(1)));}else result.Add(line);}return String.Join("\n",result);
        }
 }
}
