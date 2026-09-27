using System;
using System.Web.Script.Serialization;
using DshProbe;
class CodexStateTests {
    static int checks;
    static readonly JavaScriptSerializer json=new JavaScriptSerializer();
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static object Parse(string value){return json.DeserializeObject(value.Replace('\'', '"'));}
    static void Apply(CodexState s,string text){var c=(System.Collections.Generic.Dictionary<string,object>)Parse(text);c["baseRevision"]=s.Revision;c["revision"]=s.Revision+1;s.Apply(c);}
    static CodexState State(string status,string runtime) {
        var s=new CodexState();s.Apply(Parse("{'type':'snapshot','revision':0,'conversationState':{'threadRuntimeStatus':{'type':'"+runtime+"'},'turnHistory':{'kind':'canonical','history':{'entitiesByKey':{'turn-key':{'turnId':'turn-1','status':'"+status+"','error':null,'items':[{'body':'PRIVATE BODY'}]}}}}}}"));return s;
    }
    static void Main() {
        var s=State("completed","idle");Check(s.Outcome("turn-1")=="completed","success not correlated");
        Check(s.Outcome("turn-other")=="missing-turn","wrong identity accepted");
        Check(State("completed","active").Outcome("turn-1")=="waiting-state","active continuation accepted");
        Check(State("inProgress","idle").Outcome("turn-1")=="waiting-state","intermediate accepted");
        Check(State("interrupted","idle").Outcome("turn-1")=="cancelled","cancelled accepted");
        Check(State("failed","idle").Outcome("turn-1")=="failed","failure became success");
        Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['turnHistory','history','entitiesByKey','turn-key','error'],'value':{'message':'PRIVATE ERROR'}}]}");
        Check(s.Outcome("turn-1")=="inconsistent-error","error outcome accepted");
        Apply(s,"{'type':'patches','patches':[{'op':'remove','path':['turnHistory','history','entitiesByKey','turn-key']}]}");
        Check(s.Outcome("turn-1")=="missing-turn","removed turn retained");
        s=State("inProgress","active");Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['turnHistory','history','entitiesByKey','turn-key','status'],'value':'completed'},{'op':'replace','path':['threadRuntimeStatus'],'value':{'type':'idle'}}]}");
        Check(s.Outcome("turn-1")=="completed","patch completion lost");
        Apply(s,"{'type':'patches','patches':[{'op':'move','path':['turnHistory']}]}");Check(s.Outcome("turn-1")=="unverified-state","unknown mutation guessed");
        var fresh=new CodexState();Check(fresh.Outcome("turn-1")=="unverified-state","cold state accepted");
        s=State("inProgress","active");Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['requests'],'value':[{'id':42,'method':'item/tool/requestUserInput','params':{'turnId':'turn-1','isBlocking':true}}]}]}");
        Check(!s.IsBlockingInput(s.Requests[0]),"missing blocking status allowed");
        Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['threadRuntimeStatus'],'value':{'type':'active','activeFlags':['waitingOnUserInput']}}]}");
        Check(s.IsBlockingInput(s.Requests[0]),"real blocking shape rejected");
        Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['requests'],'value':[{'id':43,'method':'item/tool/requestUserInput','params':{'turnId':'turn-1','isBlocking':false}}]}]}");
        Check(!s.IsBlockingInput(s.Requests[0]),"optional input accepted");
        Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['requests'],'value':[]}]}");Check(s.Requests.Length==0,"resolved request retained");
        s.Apply(Parse("{'type':'patches','baseRevision':0,'revision':99,'patches':[]}"));Check(!s.Valid,"out-of-order revision accepted");
        Check(State("failed","active").Outcome("turn-1")=="waiting-state","active failure sounded early");
        s=State("inProgress","active");Check(s.Entry=="unknown","missing origin guessed");
        Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['originator'],'value':'Codex Desktop'},{'op':'replace','path':['threadSource'],'value':'user'}]}");Check(s.Entry=="codex","native Codex origin missing");
        Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['originator'],'value':'codex_work_desktop'}]}");Check(s.Entry=="work","native Work origin missing");
        Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['threadSource'],'value':'subagent'}]}");Check(s.Entry=="unknown","subagent accepted");
        foreach(string method in new[]{"item/commandExecution/requestApproval","item/fileChange/requestApproval","item/permissions/requestApproval"}){
            s=State("inProgress","active");Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['requests'],'value':[{'id':'req-1','method':'"+method+"','params':{'turnId':'turn-1'}}]}]}");Check(!s.IsBlockingRequest(s.Requests[0]),"automatic approval sounded without waiting flag");
            Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['threadRuntimeStatus'],'value':{'type':'active','activeFlags':['waitingOnApproval']}}]}");Check(s.IsBlockingRequest(s.Requests[0]),"blocking approval rejected");
            Apply(s,"{'type':'patches','patches':[{'op':'replace','path':['threadRuntimeStatus'],'value':{'type':'idle'}}]}");Check(!s.IsBlockingRequest(s.Requests[0]),"resolved approval sounded");
        }
        Console.WriteLine("PASS "+checks+" canonical state identity/outcome assertions; synthetic only.");
    }
}
