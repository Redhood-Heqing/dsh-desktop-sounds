using System;
namespace DshProbe {
    public interface ISoundSink { bool Enqueue(string kind); }
    // A and B supply verified normalized identities to ONE router and ONE sink.
    // Capability probes have no verified adapter yet, and do not emit signals here.
    public sealed class SoundRouter {
        readonly EventGate gate = new EventGate();
        readonly ISoundSink sink;
        readonly object sync = new object();
        public SoundRouter(ISoundSink sharedSink) {
            if(sharedSink==null)throw new ArgumentNullException("sharedSink");
            sink=sharedSink;
        }
        public void SelectSource(string entry,string source) {
            if(String.IsNullOrEmpty(entry) || (source!="A" && source!="B" && source!="C")) throw new ArgumentException("Invalid adapter");
            lock(sync)gate.SetSource(entry,source);
        }
        public bool Submit(Signal signal) {
            lock(sync) {
                if(!gate.Accept(signal))return false;
                // At-most-once: a busy/paused sink discards instead of delaying a stale alert.
                return sink.Enqueue(signal.Kind);
            }
        }
    }
}
