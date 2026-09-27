using System;
using DshProbe;
class HostSoundGuardTests {
    static int count;
    static void Check(bool v,string name){count++;if(!v)throw new Exception(name);}
    static void Main(){
        Check(HostSoundGuard.AllowsText("[desktop]\nnotifications-sound = \"none\"\n"),"canonical none");
        Check(HostSoundGuard.AllowsText("[desktop] # section\nnotifications-sound=\"none\" # muted\n[x]\ny=1"),"comments");
        Check(!HostSoundGuard.AllowsText("[desktop]\nnotifications-sound=\"default\""),"default overlaps");
        Check(!HostSoundGuard.AllowsText("[desktop]\nnotifications-sound=\"classic\""),"classic overlaps");
        Check(!HostSoundGuard.AllowsText("[other]\nnotifications-sound=\"none\""),"wrong section");
        Check(!HostSoundGuard.AllowsText("[desktop]\n# notifications-sound=\"none\""),"comment only");
        Check(!HostSoundGuard.AllowsText("[desktop]\nnotifications-sound=\"none\"\nnotifications-sound=\"default\""),"duplicate");
        Check(!HostSoundGuard.AllowsText("[desktop]\nnotifications-sound=\"none\"\n[desktop]"),"duplicate section");
        Check(!HostSoundGuard.AllowsText("[desktop]\nnotifications-sound='none'"),"unsupported representation");
        Check(!HostSoundGuard.AllowsText("x='''\n[desktop]\nnotifications-sound=\"none\"\n'''"),"multiline fake");
        Check(!HostSoundGuard.AllowsText("[desktop]\nother=\"notifications-sound\""),"unrelated value");
        Check(!HostSoundGuard.AllowsText(""),"missing");
        Console.WriteLine("PASS "+count+" default sound guard assertions; synthetic only.");
    }
}
