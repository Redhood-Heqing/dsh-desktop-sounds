using System;
using System.Collections.Generic;

namespace DshProbe {
    // Both adapters must normalize to the same stable task/turn/request identity.
    // A toast ID, unread count or UI element address is NOT such an identity.
    public sealed class Signal {
        public string Entry, Source, Thread, Turn, Request, Kind;
        public bool Verified, Historical, PendingContinuation, Cancelled, Blocking, Resolved;
    }
    public sealed class EventGate {
        readonly Dictionary<string, string> owners = new Dictionary<string, string>();
        readonly HashSet<string> seen = new HashSet<string>();
        readonly Queue<string> order = new Queue<string>();
        readonly object sync = new object();
        public void SetSource(string entry, string source) {lock(sync) owners[entry] = source;}
        public bool Accept(Signal s) {
            if(s == null || !s.Verified || s.Historical || s.PendingContinuation || s.Cancelled ||
               String.IsNullOrEmpty(s.Entry) || String.IsNullOrEmpty(s.Thread) || String.IsNullOrEmpty(s.Turn)) return false;
            if(s.Kind != "done" && s.Kind != "approval" && s.Kind != "error") return false;
            if(s.Kind == "approval" && (!s.Blocking || s.Resolved || String.IsNullOrEmpty(s.Request))) return false;
            string key = s.Entry + "\u001f" + s.Thread + "\u001f" + s.Turn + "\u001f" + s.Kind + "\u001f" + (s.Kind == "approval" ? s.Request : "");
            lock(sync) {
                string owner;
                if(!owners.TryGetValue(s.Entry, out owner) || owner != s.Source || seen.Contains(key)) return false;
                seen.Add(key); order.Enqueue(key);
                while(order.Count > 4096) seen.Remove(order.Dequeue());
                return true;
            }
        }
    }
}
