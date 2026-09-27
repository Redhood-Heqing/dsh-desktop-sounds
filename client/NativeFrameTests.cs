using System;using System.Text;using System.IO;using System.Web.Script.Serialization;using DshClient;using DshProbe;
class NativeFrameTests {
 static int checks;
 static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
 static object Parse(string s){var b=Encoding.UTF8.GetBytes(s.Replace('\'', '"'));return NativeFrame.Parse(b,b.Length);}
 static void Reject(string s){bool rejected=false;try{Parse(s);}catch(InvalidDataException){rejected=true;}Check(rejected,"Malformed frame accepted");}
 static object Change(object m){return CodexState.Get(CodexState.Get(m,"params"),"change");}
 static void Main(){
  var initial=Parse("{ 'type':'response', 'method':'initialize', 'resultType':'success', 'result':{'clientId':'test-client'}, 'unknown':[{'private':'secret'}] }");
  Check(CodexState.Text(CodexState.Get(CodexState.Get(initial,"result"),"clientId"))=="test-client","initialize lost");Check(CodexState.Get(initial,"unknown")==null,"unknown payload retained");
  string prefix="{'method':'thread-stream-state-changed','version':11,'params':{'hostId':'local','conversationId':'test','change':{'type':'snapshot','revision':0,'conversationState':{'originator':'codex_work_desktop','threadSource':'user','threadRuntimeStatus':{'type':'active','activeFlags':[]},'requests':[],'turnHistory':{'kind':'canonical','history':{'entitiesByKey':{'key':{'turnId':'turn-1','status':'inProgress','error':null,'items':[{'text':'";
  var frame=Parse(prefix+new string('x',2000000)+"'}]}}}}}}}}");
  Check(new JavaScriptSerializer().Serialize(frame).Length<1000,"body allocated into projection");var state=new CodexState();state.Apply(Change(frame));Check(state.Valid&&state.Entry=="work","projected native snapshot invalid");
  frame=Parse("{'params':{'change':{'type':'patches','baseRevision':0,'revision':1,'patches':[{'value':'ignored reply','path':['turnHistory','history','entitiesByKey','key','items'], 'op':'replace'},{'value':'completed','op':'replace','path':['turnHistory','history','entitiesByKey','key','status']},{'value':{'type':'idle'},'path':['threadRuntimeStatus'],'op':'replace'}]}}}");
  state.Apply(Change(frame));Check(state.Outcome("turn-1")=="completed","reordered patch lost outcome");
  frame=Parse("{'params':{'change':{'type':'patches','baseRevision':1,'revision':2,'patches':[{'op':'replace','path':['turnHistory','history','entitiesByKey','key','error'],'value':{'message':'private failure','nested':[1,2,3]}}]}}}");state.Apply(Change(frame));Check(state.Outcome("turn-1")=="inconsistent-error","non-null error was discarded");Check(!new JavaScriptSerializer().Serialize(frame).Contains("private failure"),"error text retained");
  frame=Parse("{'params':{'change':{'type':'patches','baseRevision':2,'revision':3,'patches':[{'op':'remove','path':['turnHistory','history','entitiesByKey','key','error']},{'op':'replace','path':['turnHistory','history','entitiesByKey','key','status'],'value':'interrupted'}]}}}");state.Apply(Change(frame));Check(state.Outcome("turn-1")=="cancelled","cancel lost");
  var escaped=Parse("{\"method\":\"a\\u002fb\",\"version\":11}");Check(CodexState.Text(CodexState.Get(escaped,"method"))=="a/b","escaped field lost");Check(Object.Equals(CodexState.Get(escaped,"version"),11),"integer version type changed");
  Reject("{'type':'broadcast','type':'request'}");Reject("{'unused':[1,2,]}");Reject("{'unused':{'x':'unterminated}}");Reject("{} trailing");Reject("{'version':01}");Reject("{'params':{'change':{'patches':[{'op':'replace','path':['x'],'value':'first','value':'second'}]}}}");
  Check(NativeAdapter.LogDirectory("root",new DateTimeOffset(2026,9,27,0,0,0,TimeSpan.FromHours(8)))==Path.Combine("root","2026","09","26"),"Local midnight switched log source too early");
  Check(NativeAdapter.LogDirectory("root",new DateTimeOffset(2026,9,26,17,0,0,TimeSpan.FromHours(-7)))==Path.Combine("root","2026","09","27"),"UTC rollover lost");
  var culture=System.Threading.Thread.CurrentThread.CurrentCulture;try{System.Threading.Thread.CurrentThread.CurrentCulture=new System.Globalization.CultureInfo("th-TH");Check(NativeAdapter.LogDirectory("root",new DateTimeOffset(2026,9,26,12,0,0,TimeSpan.Zero))==Path.Combine("root","2026","09","26"),"Local calendar changed source path");}finally{System.Threading.Thread.CurrentThread.CurrentCulture=culture;}
  Console.WriteLine("PASS "+checks+" UTF-8 metadata projection checks; includes 2 MB discarded body, reordered patches and invalid-frame rejection.");
 }
}
