using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
namespace DshProbe {
    // Suppression only: never schedules or replays an alert. Monotonic time keeps
    // clock adjustments from ending the protection period early.
    public sealed class QuietTransitionGate {
        readonly object sync=new object();
        readonly long holdMilliseconds;
        long quietUntil, lastTime;
        bool observedQuiet;
        public QuietTransitionGate(long milliseconds) {
            if(milliseconds<0 || milliseconds>10000)throw new ArgumentOutOfRangeException("milliseconds");
            holdMilliseconds=milliseconds;
        }
        public bool Evaluate(bool quiet,string rawReason,long now,out string reason) {
            lock(sync) {
                now=Math.Max(lastTime,now);lastTime=now;
                if(quiet){observedQuiet=true;quietUntil=now+holdMilliseconds;reason=rawReason;return true;}
                if(observedQuiet && now<quietUntil){reason="transition-hold";return true;}
                reason=rawReason;return false;
            }
        }
    }
    // Read-only public Win32 checks. This does NOT claim to read modern Windows DND.
    public static class QuietPolicy {
        public const int TransitionHoldMilliseconds=2000;
        static readonly QuietTransitionGate transition=new QuietTransitionGate(TransitionHoldMilliseconds);
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor,Work; public int Flags; }
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder b,int size);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h,int flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr h,ref MonitorInfo info);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attr,out Rect r,int size);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        public static bool ShouldSuppress(out string reason) {
            string rawReason;bool quiet=CheckEnvironment(out rawReason);
            return transition.Evaluate(quiet,rawReason,(long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency),out reason);
        }
        static bool CheckEnvironment(out string reason) {
            reason="unknown";
            try {
                int state;
                if(SHQueryUserNotificationState(out state)<0){reason="shell-query-unavailable";return true;}
                if(state!=5){reason="shell-state-"+state;return true;}
                IntPtr window=GetForegroundWindow();
                if(window==IntPtr.Zero){reason="foreground-unavailable";return true;}
                if(window==GetShellWindow() || window==GetDesktopWindow()){reason="desktop";return false;}
                var name=new StringBuilder(256);GetClassName(window,name,name.Capacity);
                if(name.ToString()=="Progman" || name.ToString()=="WorkerW"){reason="desktop";return false;}
                // Auto-hidden taskbars let normal maximized captioned windows fill
                // the monitor. Preserve shell-game suppression above, but do not
                // mistake an ordinary maximized browser/editor for borderless play.
                if(IsZoomed(window) && (GetWindowLong(window,-16)&0x00C00000)==0x00C00000){reason="available-maximized";return false;}
                IntPtr prior=SetThreadDpiAwarenessContext(new IntPtr(-4));
                try {
                    Rect bounds;var info=new MonitorInfo();info.Size=Marshal.SizeOf(typeof(MonitorInfo));
                    if(DwmGetWindowAttribute(window,9,out bounds,Marshal.SizeOf(typeof(Rect)))<0 ||
                       !GetMonitorInfo(MonitorFromWindow(window,2),ref info)){reason="geometry-unavailable";return true;}
                    bool fullscreen=Math.Abs(bounds.Left-info.Monitor.Left)<=1 && Math.Abs(bounds.Top-info.Monitor.Top)<=1 &&
                        Math.Abs(bounds.Right-info.Monitor.Right)<=1 && Math.Abs(bounds.Bottom-info.Monitor.Bottom)<=1;
                    reason=fullscreen?"foreground-fullscreen":"available";return fullscreen;
                } finally {if(prior!=IntPtr.Zero)SetThreadDpiAwarenessContext(prior);}
            } catch {reason="quiet-query-unavailable";return true;}
        }
    }
}
