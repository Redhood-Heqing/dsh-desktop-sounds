using System;using System.IO;using System.IO.Pipes;using System.Text;using System.Collections.Generic;using System.Threading;using System.Web.Script.Serialization;using DshClient;using DshProbe;
// One read-only snapshot of the controlling desktop conversation. No bodies are
// serialized to disk, no task/permission methods and no sounds are sent.
class NativeCompatibilityProbe {
 static JavaScriptSerializer json=new JavaScriptSerializer();
 static void Send(NamedPipeClientStream pipe,object value){byte[] b=Encoding.UTF8.GetBytes(json.Serialize(value));pipe.Write(BitConverter.GetBytes(b.Length),0,4);pipe.Write(b,0,b.Length);pipe.Flush();}
 static byte[] Read(NamedPipeClientStream pipe,int count,CancellationToken token){byte[] b=new byte[count];int at=0;while(at<count){int n=pipe.ReadAsync(b,at,count-at,token).GetAwaiter().GetResult();if(n==0)throw new EndOfStreamException();at+=n;}return b;}
 static void Main(string[] args){if(args.Length!=1||File.Exists(args[0]))throw new ArgumentException("Fresh evidence path required");string thread=Environment.GetEnvironmentVariable("CODEX_THREAD_ID"),client=null;Guid parsed;if(!Guid.TryParse(thread,out parsed))throw new ArgumentException("Own current thread required");object report=null;int max=0;
  using(var deadline=new CancellationTokenSource(15000))using(var pipe=new NamedPipeClientStream(".","codex-ipc",PipeDirection.InOut,PipeOptions.Asynchronous)){
   try{pipe.Connect(3000);Send(pipe,new{type="request",requestId=Guid.NewGuid().ToString(),method="initialize",version=0,@params=new{clientType="dsh-own-current-metadata-compatibility"}});
    while(!deadline.IsCancellationRequested){int length=BitConverter.ToInt32(Read(pipe,4,deadline.Token),0);max=Math.Max(max,length);if(length<1||length>16777216)throw new InvalidDataException("Product frame limit exceeded");object frame=NativeFrame.Parse(Read(pipe,length,deadline.Token),length);string method=CodexState.Text(CodexState.Get(frame,"method")),type=CodexState.Text(CodexState.Get(frame,"type"));
     if(method=="initialize"&&CodexState.Text(CodexState.Get(frame,"resultType"))=="success"){client=CodexState.Text(CodexState.Get(CodexState.Get(frame,"result"),"clientId"));Send(pipe,new{type="broadcast",method="thread-stream-following-changed",version=1,sourceClientId=client,@params=new{hostId="local",conversationId=thread,following=true}});continue;}
     if(type=="client-discovery-request"){Send(pipe,new{type="client-discovery-response",requestId=CodexState.Get(frame,"requestId"),response=new{canHandle=false}});continue;}
     if(type=="request"){Send(pipe,new{type="response",requestId=CodexState.Get(frame,"requestId"),resultType="error",error="read-only-probe"});continue;}
     object p=CodexState.Get(frame,"params"),change=CodexState.Get(p,"change");if(method!="thread-stream-state-changed"||CodexState.Text(CodexState.Get(p,"conversationId"))!=thread||CodexState.Text(CodexState.Get(change,"type"))!="snapshot")continue;
     var state=new CodexState();state.Apply(change);object projected=CodexState.Get(change,"conversationState");var turns=CodexState.Get(CodexState.Get(CodexState.Get(projected,"turnHistory"),"history"),"entitiesByKey") as Dictionary<string,object>;
     report=new{at=DateTime.UtcNow.ToString("o"),status=state.Valid&&state.Entry=="codex"?"PASS":"FAIL",entry=state.Entry,runtime=state.Runtime,turnCount=turns==null?0:turns.Count,frameBytes=length,projectedMetadataBytes=Encoding.UTF8.GetByteCount(json.Serialize(frame)),scope="Actual current large desktop conversation, production UTF-8 projector and canonical classifier; no text retained or audio emitted"};break;
    }
   }catch(Exception ex){report=new{at=DateTime.UtcNow.ToString("o"),status="FAIL",errorType=ex.GetType().Name,maxFrameBytes=max};}
   finally{if(client!=null&&pipe.IsConnected)try{Send(pipe,new{type="broadcast",method="thread-stream-following-changed",version=1,sourceClientId=client,@params=new{hostId="local",conversationId=thread,following=false}});}catch{}}
  }
  if(report==null)report=new{status="BLOCKED",reason="no snapshot before deadline"};File.WriteAllText(args[0],json.Serialize(report));Console.WriteLine(json.Serialize(report));
 }
}
