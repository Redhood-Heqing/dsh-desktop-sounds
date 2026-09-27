using System;
using System.Runtime.InteropServices;
namespace DshProbe {
    // Read-only WASAPI session meter. No capture client or microphone is opened.
    public sealed class AudioMeter : IDisposable {
        object enumeratorObject, managerObject, endpointObject;
        Device device;
        Sessions sessions;
        Endpoint endpoint;
        public AudioMeter() {
            enumeratorObject=new DeviceEnumerator();
            Marshal.ThrowExceptionForHR(((Enumerator)enumeratorObject).GetDefaultAudioEndpoint(0,1,out device));
            Guid sid=typeof(Sessions).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref sid,23,IntPtr.Zero,out managerObject));
            sessions=(Sessions)managerObject;
            Guid eid=typeof(Endpoint).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref eid,23,IntPtr.Zero,out endpointObject));
            endpoint=(Endpoint)endpointObject;
        }
        public float EndpointVolume {get {float v;Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out v));return v;}}
        public bool EndpointMuted {get {bool v;Marshal.ThrowExceptionForHR(endpoint.GetMute(out v));return v;}}
        public float ReadProcessPeak(uint pid, out bool matched, out bool muted, out float volume) {
            matched=false;muted=false;volume=0;float peak=0;
            SessionList list;
            Marshal.ThrowExceptionForHR(sessions.GetSessionEnumerator(out list));
            try {
                int count;Marshal.ThrowExceptionForHR(list.GetCount(out count));
                for(int i=0;i<count;i++) {
                    object control;Marshal.ThrowExceptionForHR(list.GetSession(i,out control));
                    try {
                        uint id;Marshal.ThrowExceptionForHR(((Session2)control).GetProcessId(out id));
                        if(id!=pid) continue;
                        matched=true;
                        float p;Marshal.ThrowExceptionForHR(((Meter)control).GetPeakValue(out p));peak=Math.Max(peak,p);
                        bool m;float v;
                        Marshal.ThrowExceptionForHR(((SimpleVolume)control).GetMute(out m));
                        Marshal.ThrowExceptionForHR(((SimpleVolume)control).GetMasterVolume(out v));
                        muted|=m;volume=Math.Max(volume,v);
                    } finally {Marshal.ReleaseComObject(control);}
                }
            } finally {Marshal.ReleaseComObject(list);}
            return peak;
        }
        public void Dispose() {
            if(endpointObject!=null){Marshal.ReleaseComObject(endpointObject);endpointObject=null;}
            if(managerObject!=null){Marshal.ReleaseComObject(managerObject);managerObject=null;}
            if(device!=null){Marshal.ReleaseComObject(device);device=null;}
            if(enumeratorObject!=null){Marshal.ReleaseComObject(enumeratorObject);enumeratorObject=null;}
        }
        [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator {}
        [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Enumerator {
            [PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out IntPtr collection);
            [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out Device device);
        }
        [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Device {
            [PreserveSig]int Activate(ref Guid iid,uint context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object result);
        }
        [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Sessions {
            [PreserveSig]int GetAudioSessionControl(ref Guid id,uint flags,out IntPtr control);
            [PreserveSig]int GetSimpleAudioVolume(ref Guid id,uint flags,out IntPtr volume);
            [PreserveSig]int GetSessionEnumerator(out SessionList list);
        }
        [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface SessionList {
            [PreserveSig]int GetCount(out int count);
            [PreserveSig]int GetSession(int index,[MarshalAs(UnmanagedType.IUnknown)]out object session);
        }
        [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Session2 {
            [PreserveSig]int GetState(out int state);
            [PreserveSig]int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)]out string name);
            [PreserveSig]int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string name,ref Guid context);
            [PreserveSig]int GetIconPath([MarshalAs(UnmanagedType.LPWStr)]out string path);
            [PreserveSig]int SetIconPath([MarshalAs(UnmanagedType.LPWStr)]string path,ref Guid context);
            [PreserveSig]int GetGroupingParam(out Guid grouping);
            [PreserveSig]int SetGroupingParam(ref Guid grouping,ref Guid context);
            [PreserveSig]int RegisterAudioSessionNotification(IntPtr callback);
            [PreserveSig]int UnregisterAudioSessionNotification(IntPtr callback);
            [PreserveSig]int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
            [PreserveSig]int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
            [PreserveSig]int GetProcessId(out uint pid);
        }
        [ComImport,Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Meter {
            [PreserveSig]int GetPeakValue(out float value);
        }
        [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface SimpleVolume {
            [PreserveSig]int SetMasterVolume(float value,ref Guid context);
            [PreserveSig]int GetMasterVolume(out float value);
            [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool value,ref Guid context);
            [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool value);
        }
        [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Endpoint {
            [PreserveSig]int RegisterControlChangeNotify(IntPtr p);
            [PreserveSig]int UnregisterControlChangeNotify(IntPtr p);
            [PreserveSig]int GetChannelCount(out uint count);
            [PreserveSig]int SetMasterVolumeLevel(float value,ref Guid c);
            [PreserveSig]int SetMasterVolumeLevelScalar(float value,ref Guid c);
            [PreserveSig]int GetMasterVolumeLevel(out float value);
            [PreserveSig]int GetMasterVolumeLevelScalar(out float value);
            [PreserveSig]int SetChannelVolumeLevel(uint c,float value,ref Guid context);
            [PreserveSig]int SetChannelVolumeLevelScalar(uint c,float value,ref Guid context);
            [PreserveSig]int GetChannelVolumeLevel(uint c,out float value);
            [PreserveSig]int GetChannelVolumeLevelScalar(uint c,out float value);
            [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool value,ref Guid context);
            [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool value);
        }
    }
}
