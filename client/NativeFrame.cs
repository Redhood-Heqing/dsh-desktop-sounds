using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace DshClient {
    // Project directly from UTF-8. Assistant bodies, tool arguments and history
    // items are skipped without building strings/dictionaries for their content.
    public sealed class NativeFrame {
        readonly byte[] bytes;readonly int length;int at,depth;
        NativeFrame(byte[] input,int count){bytes=input;length=count;}
        public static object Parse(byte[] input,int count){var r=new NativeFrame(input,count);object value=r.Object("frame");r.White();if(r.at!=count)throw new InvalidDataException("json-tail");return value;}
        void White(){while(at<length&&(bytes[at]==32||bytes[at]==9||bytes[at]==10||bytes[at]==13))at++;}
        byte Peek(){White();if(at>=length)throw new InvalidDataException("json-end");return bytes[at];}
        void Take(byte b){if(Peek()!=b)throw new InvalidDataException("json-token");at++;}
        void Enter(){if(++depth>128)throw new InvalidDataException("json-depth");}
        string String(){White();int start=at;SkipString();int size=at-start;if(size>2048)throw new InvalidDataException("metadata-string-limit");string text=Encoding.UTF8.GetString(bytes,start,size);return text.IndexOf('\\')<0?text.Substring(1,text.Length-2):new JavaScriptSerializer().Deserialize<string>(text);}
        void SkipString(){Take(34);while(at<length){byte b=bytes[at++];if(b==34)return;if(b<32)throw new InvalidDataException("json-control");if(b==92){if(at>=length)break;byte e=bytes[at++];if(e==117){for(int i=0;i<4;i++){if(at>=length)throw new InvalidDataException();byte h=bytes[at++];if(!(h>=48&&h<=57||h>=65&&h<=70||h>=97&&h<=102))throw new InvalidDataException("json-escape");}}else if(e!=34&&e!=92&&e!=47&&e!=98&&e!=102&&e!=110&&e!=114&&e!=116)throw new InvalidDataException("json-escape");}}throw new InvalidDataException("json-string-end");}
        void Literal(string text){foreach(char c in text){if(at>=length||bytes[at++]!=(byte)c)throw new InvalidDataException("json-literal");}}
        object Scalar(){byte b=Peek();if(b==34)return String();if(b==110){Literal("null");return null;}if(b==116){Literal("true");return true;}if(b==102){Literal("false");return false;}int start=at;if(b==45)at++;if(at>=length)throw new InvalidDataException();if(bytes[at]==48)at++;else{if(bytes[at]<49||bytes[at]>57)throw new InvalidDataException("json-number");while(at<length&&bytes[at]>=48&&bytes[at]<=57)at++;}if(at<length&&bytes[at]==46){at++;int before=at;while(at<length&&bytes[at]>=48&&bytes[at]<=57)at++;if(before==at)throw new InvalidDataException();}if(at<length&&(bytes[at]==101||bytes[at]==69)){at++;if(at<length&&(bytes[at]==43||bytes[at]==45))at++;int before=at;while(at<length&&bytes[at]>=48&&bytes[at]<=57)at++;if(before==at)throw new InvalidDataException();}if(at-start>40)throw new InvalidDataException("json-number-limit");string number=Encoding.ASCII.GetString(bytes,start,at-start);long n;if(Int64.TryParse(number,out n)){if(n>=Int32.MinValue&&n<=Int32.MaxValue)return (int)n;return n;}double d;if(!Double.TryParse(number,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out d))throw new InvalidDataException("json-number");return d;}
        void Skip(){byte b=Peek();if(b==34){SkipString();return;}if(b!=123&&b!=91){Scalar();return;}Enter();at++;byte end=b==123?(byte)125:(byte)93;if(Peek()==end){at++;depth--;return;}while(true){if(b==123){SkipString();Take(58);}Skip();byte sep=Peek();at++;if(sep==end)break;if(sep!=44)throw new InvalidDataException("json-separator");}depth--;}
        static string Child(string context,string key){
            switch(context){
                case "frame":if(key=="params")return "params";if(key=="result")return "result";if(key=="type"||key=="method"||key=="version"||key=="requestId"||key=="resultType")return "scalar";break;
                case "result":if(key=="clientId")return "scalar";break;
                case "params":if(key=="hostId"||key=="conversationId")return "scalar";if(key=="change")return "change";break;
                case "change":if(key=="type"||key=="revision"||key=="baseRevision")return "scalar";if(key=="conversationState")return "state";if(key=="patches")return "patches";break;
                case "state":if(key=="originator"||key=="threadSource")return "scalar";if(key=="threadRuntimeStatus")return "runtime";if(key=="turnHistory")return "historyRoot";if(key=="requests")return "requests";break;
                case "runtime":if(key=="type")return "scalar";if(key=="activeFlags")return "scalars";break;
                case "historyRoot":if(key=="kind")return "scalar";if(key=="history")return "history";break;
                case "history":if(key=="entitiesByKey")return "turns";break;
                case "turns":return "turn";
                case "turn":if(key=="turnId"||key=="status")return "scalar";if(key=="error")return "error";break;
                case "request":if(key=="id"||key=="method")return "scalar";if(key=="params")return "requestParams";break;
                case "requestParams":if(key=="turnId"||key=="isBlocking")return "scalar";break;
            }return null;
        }
        object Value(string context){if(context=="error"){bool empty=Peek()==110;Skip();return empty?null:(object)true;}if(context=="scalar")return Scalar();if(Peek()==110){Literal("null");return null;}if(context=="requests")return Array("request",128);if(context=="patches")return Array("patch",10000);if(context=="scalars")return Array("scalar",64);return Object(context);}
        object Object(string context){Take(123);Enter();var result=new Dictionary<string,object>();if(Peek()==125){at++;depth--;return result;}while(true){string key=String();Take(58);string child=Child(context,key);if(child==null)Skip();else {if(result.ContainsKey(key))throw new InvalidDataException("duplicate-metadata");result[key]=Value(child);}if(result.Count>256)throw new InvalidDataException("metadata-count");byte sep=Peek();at++;if(sep==125)break;if(sep!=44)throw new InvalidDataException("json-object-separator");}depth--;return result;}
        object Array(string context,int limit){Take(91);Enter();var result=new List<object>();if(Peek()==93){at++;depth--;return result;}int count=0;while(true){if(++count>limit)throw new InvalidDataException("json-array-limit");object value=context=="patch"?Patch():Value(context);if(context!="patch"||value!=null)result.Add(value);byte sep=Peek();at++;if(sep==93)break;if(sep!=44)throw new InvalidDataException("json-array-separator");}depth--;return result;}
        object Patch(){Take(123);Enter();string op=null;List<object> path=null;int valueStart=-1;var keys=new HashSet<string>();if(Peek()==125){at++;depth--;return new Dictionary<string,object>();}while(true){string key=String();if(!keys.Add(key))throw new InvalidDataException("duplicate-patch-key");Take(58);if(key=="op")op=Scalar() as string;else if(key=="path")path=Array("scalar",64) as List<object>;else{if(key=="value")valueStart=at;Skip();}byte sep=Peek();at++;if(sep==125)break;if(sep!=44)throw new InvalidDataException();}depth--;
            string context=null,root=path!=null&&path.Count>0?path[0] as string:null;
            if(root=="originator"||root=="threadSource")context="scalar";
            if(root=="requests")context=path.Count==1?"requests":path.Count==2?"request":null;
            if(root=="threadRuntimeStatus")context=path.Count==1?"runtime":path.Count==2&&Equals(path[1],"type")?"scalar":path.Count==2&&Equals(path[1],"activeFlags")?"scalars":null;
            if(root=="turnHistory"){
                if(path.Count==1)context="historyRoot";
                else if(path.Count==2&&Equals(path[1],"history"))context="history";
                else if(path.Count==3&&Equals(path[1],"history")&&Equals(path[2],"entitiesByKey"))context="turns";
                else if(path.Count>=4&&Equals(path[1],"history")&&Equals(path[2],"entitiesByKey")){if(path.Count==4)context="turn";else if(path.Count==5&&(Equals(path[4],"status")||Equals(path[4],"turnId")))context="scalar";else if(path.Count==5&&Equals(path[4],"error"))context="error";}
            }
            bool invalid=path==null||path.Count==0||op!="add"&&op!="replace"&&op!="remove";
            if(context==null&&!invalid&&root!="requests"&&root!="threadRuntimeStatus"&&!(root=="turnHistory"&&path.Count==2&&Equals(path[1],"kind")))return null;
            object projected=null;if(context!=null&&valueStart>=0&&op!="remove"){int end=at;at=valueStart;projected=Value(context);at=end;}
            return new Dictionary<string,object>{{"op",op},{"path",path},{"value",projected}};
        }
    }
}
