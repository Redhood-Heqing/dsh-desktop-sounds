using System;
using System.Collections;
using System.Collections.Generic;
namespace DshProbe {
    // Keeps outcome/identity only. Text, tool arguments and response bodies are
    // never retained. Unknown protocol updates invalidate rather than guess.
    public sealed class CodexState {
        public sealed class Turn { public string Id,Status; public bool HasError; }
        public sealed class Request { public string Id,TurnId,Method; public bool Blocking; }
        readonly Dictionary<string,Turn> turns=new Dictionary<string,Turn>();
        readonly List<Request> requests=new List<Request>();
        readonly List<string> flags=new List<string>();
        public long Revision=-1;
        public string Runtime="unknown";
        public string Entry="unknown";
        string origin,threadSource;
        void UpdateEntry(){Entry=threadSource!="user"?"unknown":origin=="codex_work_desktop"?"work":origin=="Codex Desktop"?"codex":"unknown";}
        public bool Valid;
        public int Snapshots,Patches;
        public static object Get(object value,string key){var d=value as Dictionary<string,object>;object v;return d!=null&&d.TryGetValue(key,out v)?v:null;}
        public static string Text(object value){return value as string;}
        static Turn Project(object value) {
            string id=Text(Get(value,"turnId")),status=Text(Get(value,"status"));
            if(id==null || (status!="completed"&&status!="failed"&&status!="interrupted"&&status!="inProgress"))return null;
            return new Turn {Id=id,Status=status,HasError=Get(value,"error")!=null};
        }
        void ReadTurns(object history) {
            turns.Clear();var map=Get(Get(history,"history"),"entitiesByKey") as Dictionary<string,object>;
            if(Text(Get(history,"kind"))!="canonical" || map==null || map.Count>128){Valid=false;return;}
            foreach(var entry in map){var t=Project(entry.Value);if(t!=null)turns[entry.Key]=t;}
        }
        static Request ProjectRequest(object r){object p=Get(r,"params"),id=Get(r,"id");return new Request {Id=id==null?null:Convert.ToString(id,System.Globalization.CultureInfo.InvariantCulture),TurnId=Text(Get(p,"turnId")),Method=Text(Get(r,"method")),Blocking=Object.Equals(Get(p,"isBlocking"),true)};}
        void ReadRequests(object value){requests.Clear();var list=value as IList;if(list==null)return;if(list.Count>128){Valid=false;return;}foreach(object r in list)requests.Add(ProjectRequest(r));}
        void ReadFlags(object value){flags.Clear();var list=value as IList;if(list!=null)foreach(object f in list)if(f is string)flags.Add((string)f);}
        void ReadRuntime(object value){string s=Text(Get(value,"type"));Runtime=s=="idle"||s=="active"||s=="systemError"||s=="notLoaded"?s:"unknown";ReadFlags(Get(value,"activeFlags"));}
        public Request[] Requests {get{return requests.ToArray();}}
        public bool IsBlockingRequest(Request r){
            if(IsBlockingInput(r))return true;
            if(!Valid||Runtime!="active"||!flags.Contains("waitingOnApproval")||r==null||String.IsNullOrEmpty(r.Id)||String.IsNullOrEmpty(r.TurnId))return false;
            if(r.Method!="item/commandExecution/requestApproval"&&r.Method!="item/fileChange/requestApproval"&&r.Method!="item/permissions/requestApproval")return false;
            int matches=0,identities=0;foreach(var t in turns.Values)if(t.Id==r.TurnId&&t.Status=="inProgress")matches++;foreach(var p in requests)if(p.Id==r.Id)identities++;
            return matches==1&&identities==1;
        }
        public bool IsBlockingInput(Request r){
            if(!Valid||Runtime!="active"||!flags.Contains("waitingOnUserInput")||r==null||!r.Blocking||r.Method!="item/tool/requestUserInput"||String.IsNullOrEmpty(r.Id)||String.IsNullOrEmpty(r.TurnId))return false;
            int matches=0;foreach(var t in turns.Values)if(t.Id==r.TurnId&&t.Status=="inProgress")matches++;
            int identities=0;foreach(var p in requests)if(p.Id==r.Id)identities++;
            return matches==1&&identities==1;
        }
        public void Apply(object change) {
            string type=Text(Get(change,"type"));
            object revision=Get(change,"revision");
            if(!(revision is int)&&!(revision is long)){Valid=false;return;}
            long next=Convert.ToInt64(revision);if(next<0){Valid=false;return;}
            if(type=="snapshot") {
                Snapshots++;Revision=next;Valid=true;object state=Get(change,"conversationState");origin=Text(Get(state,"originator"));threadSource=Text(Get(state,"threadSource"));UpdateEntry();ReadRuntime(Get(state,"threadRuntimeStatus"));ReadTurns(Get(state,"turnHistory"));ReadRequests(Get(state,"requests"));return;
            }
            if(type!="patches" || !Valid)return;
            object baseRevision=Get(change,"baseRevision");
            if((!(baseRevision is int)&&!(baseRevision is long))||Convert.ToInt64(baseRevision)!=Revision||next<=Revision){Valid=false;return;}Revision=next;
            var patches=Get(change,"patches") as IList;if(patches==null||patches.Count>10000){Valid=false;return;}
            foreach(object patch in patches) {
                Patches++;var path=Get(patch,"path") as IList;object value=Get(patch,"value");string op=Text(Get(patch,"op"));
                if(path==null||path.Count==0||op!="add"&&op!="replace"&&op!="remove"){Valid=false;return;}
                string root=Text(path[0]);
                if(root=="originator"){origin=Text(value);UpdateEntry();continue;}
                if(root=="threadSource"){threadSource=Text(value);UpdateEntry();continue;}
                if(root=="requests"){
                    if(path.Count==1)ReadRequests(value);
                    else if(path.Count==2){int index; if(!Int32.TryParse(Convert.ToString(path[1]),out index)||index<0||index>requests.Count){Valid=false;return;}
                        if(op=="add")requests.Insert(index,ProjectRequest(value));
                        else if(index>=requests.Count){Valid=false;return;}
                        else if(op=="remove")requests.RemoveAt(index);else requests[index]=ProjectRequest(value);
                    }else {Valid=false;return;}
                    continue;
                }
                if(root=="threadRuntimeStatus") {
                    if(path.Count==1)ReadRuntime(value);
                    else if(path.Count==2&&Text(path[1])=="type"){Runtime=Text(value);if(Runtime!="active")flags.Clear();}
                    else if(path.Count==2&&Text(path[1])=="activeFlags")ReadFlags(value);
                    else {Valid=false;return;}
                    continue;
                }
                if(root!="turnHistory")continue;
                if(path.Count==1){ReadTurns(value);continue;}
                if(path.Count==2&&Text(path[1])=="history") {ReadTurns(new Dictionary<string,object>{{"kind","canonical"},{"history",value}});continue;}
                if(path.Count==3&&Text(path[1])=="history"&&Text(path[2])=="entitiesByKey") {
                    ReadTurns(new Dictionary<string,object>{{"kind","canonical"},{"history",new Dictionary<string,object>{{"entitiesByKey",value}}}});continue;
                }
                if(path.Count<4||Text(path[1])!="history"||Text(path[2])!="entitiesByKey") {
                    if(path.Count==2&&Text(path[1])=="kind")Valid=false;
                    continue;
                }
                string key=Text(path[3]);if(key==null){Valid=false;return;}
                if(path.Count==4) {
                    if(op=="remove")turns.Remove(key);
                    else {var t=Project(value);if(t==null)turns.Remove(key);else turns[key]=t;}
                }else if(path.Count==5) {
                    Turn t;if(!turns.TryGetValue(key,out t))continue;
                    string field=Text(path[4]);
                    if(field=="status")t.Status=Text(value);
                    else if(field=="turnId")t.Id=Text(value);
                    else if(field=="error")t.HasError=op!="remove"&&value!=null;
                }
                if(turns.Count>128){Valid=false;return;}
            }
        }
        public string Outcome(string turnId) {
            if(!Valid)return "unverified-state";
            Turn found=null;foreach(var t in turns.Values)if(t.Id==turnId){if(found!=null)return "ambiguous-turn";found=t;}
            if(found==null)return "missing-turn";
            if(found.Status=="interrupted")return "cancelled";
            if(found.Status=="failed")return Runtime=="idle"||Runtime=="systemError"?"failed":"waiting-state";
            if(found.Status!="completed"||Runtime!="idle")return "waiting-state";
            return found.HasError?"inconsistent-error":"completed";
        }
    }
}
