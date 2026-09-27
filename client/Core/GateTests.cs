using System;
using DshProbe;
class GateTests {
    sealed class CountingSink : ISoundSink {public int Calls;public bool Accept=true;public bool Enqueue(string kind){Calls++;return Accept;}}
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static Signal S(string turn,string source="A") {return new Signal{Entry="chat",Source=source,Thread="thread",Turn=turn,Kind="done",Verified=true};}
    static void Main() {
        var gate=new EventGate();gate.SetSource("chat","A");
        for(int i=0;i<200;i++) {
            var s=S("t"+i);
            Check(gate.Accept(s),"new event rejected");
            Check(!gate.Accept(s),"duplicate passed");
            gate.SetSource("chat","B");s.Source="B";
            Check(!gate.Accept(s),"same event replayed after A/B switch");
            gate.SetSource("chat","A");
        }
        var c=S("cancel");c.Cancelled=true;Check(!gate.Accept(c),"cancelled sounded");
        var p=S("continue");p.PendingContinuation=true;Check(!gate.Accept(p),"continuation sounded");
        var h=S("history");h.Historical=true;Check(!gate.Accept(h),"history sounded");
        var u=S("unknown");u.Verified=false;Check(!gate.Accept(u),"unverified sounded");
        var t=S(null);Check(!gate.Accept(t),"missing turn identity accepted");
        Check(!gate.Accept(S("wrong-source","B")),"inactive adapter sounded");
        var q=S("question");q.Kind="approval";q.Request="q1";
        Check(!gate.Accept(q),"optional question sounded");q.Blocking=true;
        Check(gate.Accept(q),"blocking request suppressed");
        Check(!gate.Accept(q),"request duplicate sounded");
        q.Request="q2";q.Resolved=true;Check(!gate.Accept(q),"resolved approval sounded");
        q.Resolved=false;Check(gate.Accept(q),"new blocking request suppressed");
        for(int i=0;i<5000;i++)Check(gate.Accept(S("bound"+i)),"bounded state regression");
        Check(!gate.Accept(S("bound4999")),"recent dedup lost");
        var sink=new CountingSink();var router=new SoundRouter(sink);router.SelectSource("chat","A");
        Check(router.Submit(S("route")),"router did not deliver");
        router.SelectSource("chat","B");
        Check(!router.Submit(S("route","B")) && sink.Calls==1,"cross-adapter double playback");
        Check(!router.Submit(S("other","A")) && sink.Calls==1,"inactive adapter reached player");
        sink.Accept=false;Check(!router.Submit(S("full","B")),"queue refusal not reported");
        sink.Accept=true;Check(!router.Submit(S("full","B")) && sink.Calls==2,"discarded alert replayed");
        router.SelectSource("chat","C");
        Check(!router.Submit(S("route","C")) && sink.Calls==2,"C replayed A/B event");
        Check(router.Submit(S("new-web","C")) && sink.Calls==3,"C fresh event not routed");
        Check(!router.Submit(S("wrong-web","B")) && sink.Calls==3,"B bypassed C ownership");
        var quiet=new QuietTransitionGate(2000);string reason;
        Check(!quiet.Evaluate(false,"available",100,out reason),"cold normal desktop suppressed");
        Check(quiet.Evaluate(true,"foreground-fullscreen",200,out reason),"fullscreen not suppressed");
        Check(quiet.Evaluate(false,"available",201,out reason) && reason=="transition-hold","mode switch leaked");
        Check(quiet.Evaluate(false,"available",2199,out reason),"hold ended early");
        Check(!quiet.Evaluate(false,"available",2200,out reason),"hold never released");
        Check(!quiet.Evaluate(false,"available",3000,out reason),"normal polls extended hold");
        Check(quiet.Evaluate(true,"foreground-fullscreen",4000,out reason),"reentry not suppressed");
        Check(quiet.Evaluate(false,"available",5000,out reason),"second transition leaked");
        Check(quiet.Evaluate(true,"foreground-unavailable",5500,out reason),"unavailable foreground not suppressed");
        Check(quiet.Evaluate(false,"available",7499,out reason),"unavailable foreground hold lost");
        Check(!quiet.Evaluate(false,"available",7500,out reason),"recovery hold failed to expire");
        Check(!quiet.Evaluate(false,"available",7400,out reason),"out-of-order poll revived expired hold");
        Check(quiet.Evaluate(true,"foreground-fullscreen",8000,out reason),"third fullscreen missed");
        Check(quiet.Evaluate(false,"available",7900,out reason),"out-of-order poll bypassed hold");
        var discardSink=new CountingSink();var quietRouter=new SoundRouter(discardSink);quietRouter.SelectSource("chat","B");
        discardSink.Accept=false;
        Check(!quietRouter.Submit(S("mode-switch-discard","B")),"quiet refusal lost");
        discardSink.Accept=true;
        Check(!quietRouter.Submit(S("mode-switch-discard","B")) && discardSink.Calls==1,"transition-discarded event replayed");
        Check(quietRouter.Submit(S("new-after-game","B")) && discardSink.Calls==2,"new post-game event lost");
        Console.WriteLine("PASS "+checks+" classification/dedup/quiet-transition assertions; synthetic component tests, not live product acceptance.");
    }
}
