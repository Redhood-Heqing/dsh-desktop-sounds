using System;
using System.IO;
using System.Text.RegularExpressions;
namespace DshProbe {
    // This is a deliberately narrow acceptance check for the current host's
    // canonical TOML output, not a general TOML parser. Unknown forms fail closed.
    public static class HostSoundGuard {
        public static bool Allows(string path) {
            try {var f=new FileInfo(path);return f.Exists&&f.Length<=2097152&&AllowsText(File.ReadAllText(path));}
            catch{return false;}
        }
        public static bool AllowsText(string text) {
            bool desktop=false,found=false,none=false;int sections=0;
            foreach(string raw in text.Split('\n')) {
                string line=raw.Trim().TrimStart('\uFEFF');
                if(line.Length==0||line.StartsWith("#"))continue;
                // Reject multiline strings: a fake section inside one must never
                // be mistaken for a live setting. Current host output is simple.
                if(line.Contains("\"\"\"")||line.Contains("'''"))return false;
                if(line.StartsWith("[")) {
                    desktop=Regex.IsMatch(line,@"\A\[desktop\]\s*(?:#.*)?\z");
                    if(desktop&&++sections>1)return false;continue;
                }
                if(!desktop)continue;
                if(!line.Contains("notifications-sound"))continue;
                if(found)return false;found=true;
                none=Regex.IsMatch(line,"\\Anotifications-sound\\s*=\\s*\"none\"\\s*(?:#.*)?\\z");
            }
            return sections==1&&found&&none;
        }
    }
}
