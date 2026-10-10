using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
namespace MemoApp.Core.Transfer;

// Internal keyless process primitive. Product trust/publication belong to the Windows adapter.
internal delegate void OcrUtf8Reader(ReadOnlySpan<byte> bytes);
internal sealed class OwnedOcrText(byte[] owned):IDisposable
{
 private readonly object gate=new();private byte[]? bytes=owned;private bool running,finished,revoked;
 internal string Text{get{lock(gate){if(bytes is null||revoked||finished)throw new ObjectDisposedException(nameof(OwnedOcrText));if(running)throw new InvalidOperationException("OCR output consumption running");return new UTF8Encoding(false,true).GetString(bytes);}}}
 internal bool ConsumeUtf8(OcrUtf8Reader reader)
 {
  ArgumentNullException.ThrowIfNull(reader);byte[] borrowed;
  lock(gate){if(running||finished||revoked)throw new InvalidOperationException("OCR output ownership ended");running=true;borrowed=bytes!;}
  bool success=false;
  try{reader(borrowed);}
  finally{lock(gate){success=!revoked;running=false;finished=true;CryptographicOperations.ZeroMemory(borrowed);bytes=null;}}
  return success;
 }
 public void Dispose(){lock(gate){revoked=true;if(!running&&!finished){finished=true;if(bytes is not null)CryptographicOperations.ZeroMemory(bytes);bytes=null;}}}
}
internal sealed record OcrOperation(Task<OwnedOcrText> Completion,Task Settled);
// Data-only test observation; null on every product route. No delegate or authority owner is retained.
internal sealed class OcrProcessOwnershipProbe { internal byte[]? CandidateBuffer; }
internal static class LocalOcrProcess
{
 private static readonly SemaphoreSlim admission=new(1,1);
 internal static Task<OwnedOcrText> RunPreparedAsync(PreparedTextExport input,ProcessStartInfo start,TimeSpan timeout,CancellationToken token=default)=>StartPrepared(input,start,timeout,token).Completion;
 // Settled is keyless cleanup evidence. UI/global admission may release only after its own publication work and this task finish.
 internal static OcrOperation StartPrepared(PreparedTextExport input,ProcessStartInfo start,TimeSpan timeout,CancellationToken token=default,List<FileStream>? verifiedFiles=null,OcrProcessOwnershipProbe? ownershipProbe=null)
 {
  var completion=new TaskCompletionSource<OwnedOcrText>(TaskCreationOptions.RunContinuationsAsynchronously);
  var settled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var operation=new OcrOperation(completion.Task,settled.Task); // All returned ownership handles exist before launch.
  _=DeliverOwnedAsync(input,start,timeout,token,completion,settled,verifiedFiles,ownershipProbe);return operation;
 }
 private static async Task DeliverOwnedAsync(PreparedTextExport input,ProcessStartInfo start,TimeSpan timeout,CancellationToken token,TaskCompletionSource<OwnedOcrText> completion,TaskCompletionSource settled,List<FileStream>? verifiedFiles,OcrProcessOwnershipProbe? ownershipProbe)
 {
  OwnedOcrText? owned=null;
  try{owned=await RunOwnedAsync(input,start,timeout,token,settled,verifiedFiles,ownershipProbe).ConfigureAwait(false);if(completion.TrySetResult(owned))owned=null;}
  catch(OperationCanceledException error){completion.TrySetCanceled(error.CancellationToken);}
  catch(Exception error){completion.TrySetException(error);}
  finally{owned?.Dispose();}
 }
 private static async Task<OwnedOcrText> RunOwnedAsync(PreparedTextExport input,ProcessStartInfo start,TimeSpan timeout,CancellationToken token,TaskCompletionSource settled,List<FileStream>? verifiedFiles,OcrProcessOwnershipProbe? ownershipProbe)
 {
  bool entered=false,transferred=false,childStarted=false;Process? child=null;byte[]? output=null;OwnedOcrText? candidate=null;byte[]? candidateBytes=null;Task all=Task.CompletedTask;
  Task[] tasks=Array.Empty<Task>();
  try
  {
   tasks=[Task.CompletedTask,Task.CompletedTask,Task.CompletedTask,Task.CompletedTask];output=new byte[262145];
   if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromSeconds(20)||input.IsDisposed||input.Bytes.Length>3145750)throw new InvalidDataException("OCR input/deadline limit");
   token.ThrowIfCancellationRequested();if(!await admission.WaitAsync(0,token))throw new InvalidOperationException("OCR process work is occupied");entered=true;
   start.UseShellExecute=false;start.RedirectStandardInput=true;start.RedirectStandardOutput=true;start.RedirectStandardError=true;start.CreateNoWindow=true;
   child=new Process{StartInfo=start};if(!child.Start())throw new InvalidDataException("OCR child refused");childStarted=true;
   var fault=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int count=0;
   async Task ReadOutput()
   {
    while(true){int read=await child.StandardOutput.BaseStream.ReadAsync(output!.AsMemory(count),token);if(read==0)return;count+=read;if(count>262144)throw new InvalidDataException("OCR output byte limit");}
   }
   async Task ReadError()
   {
    byte[] buffer=new byte[1024];int total=0;try{while(true){int read=await child.StandardError.BaseStream.ReadAsync(buffer.AsMemory(0,Math.Min(buffer.Length,4097-total)),token);if(read==0)return;total+=read;if(total>4096)throw new InvalidDataException("OCR error output limit");}}finally{CryptographicOperations.ZeroMemory(buffer);}
   }
   async Task WriteInput(){try{await child.StandardInput.BaseStream.WriteAsync(input.Bytes,token);await child.StandardInput.BaseStream.FlushAsync(token);}finally{child.StandardInput.Close();}}
   tasks[0]=ReadOutput();tasks[1]=ReadError();tasks[2]=WriteInput();tasks[3]=child.WaitForExitAsync();
   foreach(var task in tasks)_=task.ContinueWith(t=>{if(t.IsFaulted||t.IsCanceled)fault.TrySetResult();},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
   all=Task.WhenAll(tasks);Task first=await Task.WhenAny(all,fault.Task).WaitAsync(timeout,token);
   if(first==fault.Task)throw new InvalidDataException("OCR pipe failed");
   try{await all;}catch{throw new InvalidDataException("OCR pipe failed");}
   token.ThrowIfCancellationRequested();if(child.ExitCode!=0)throw new InvalidDataException("OCR child failed");
   string text;try{text=new UTF8Encoding(false,true).GetString(output!,0,count);}catch(DecoderFallbackException){throw new InvalidDataException("OCR output UTF8");}
   if(text.Length>65536||text.Contains('\0'))throw new InvalidDataException("OCR output text limit");
   token.ThrowIfCancellationRequested();candidateBytes=output!.AsSpan(0,count).ToArray();
   if(ownershipProbe is not null)ownershipProbe.CandidateBuffer=candidateBytes;
   candidate=new(candidateBytes);return candidate;
  }
  catch{if(candidate is not null)candidate.Dispose();else if(candidateBytes is not null)CryptographicOperations.ZeroMemory(candidateBytes);throw;}
  finally
  {
   if(child is not null)
   {
    try{if(!child.HasExited)child.Kill(true);}catch(Exception error)when(error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException){}
    try{child.StandardInput.Close();}catch{}try{child.StandardOutput.Close();}catch{}try{child.StandardError.Close();}catch{}
    async Task Drain()
    {
     // Slots exist before launch, so partially initialized startup still owns every returned pipe task.
     foreach(var task in tasks)try{await task.ConfigureAwait(false);}catch{}
     if(childStarted)await child.WaitForExitAsync().ConfigureAwait(false); // Exit failure is never successful settlement.
    }
    all=Drain();try{await all.WaitAsync(TimeSpan.FromSeconds(2));}catch{}
   }
   void Release()
   {
    Exception? failure=all.IsFaulted?all.Exception:all.IsCanceled?new OperationCanceledException("OCR child exit cleanup canceled"):null;
    try{input.Dispose();}catch(Exception error){failure??=error;}
    try{if(output is not null)CryptographicOperations.ZeroMemory(output);}catch(Exception error){failure??=error;}
    try{child?.Dispose();}catch(Exception error){failure??=error;}
    if(verifiedFiles is not null)foreach(var file in verifiedFiles)
     try{file.Dispose();}catch(Exception error){failure??=error;}
    if(failure is not null)
    {
     // A return expression is evaluated before finally. Until Release succeeds, its UTF8 copy remains ours.
     // The same local remains captured for deferred cleanup faults after a result was handed to Completion.
     if(candidate is not null)candidate.Dispose();else if(candidateBytes is not null)CryptographicOperations.ZeroMemory(candidateBytes);
     settled.TrySetException(failure);System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    if(entered)admission.Release();settled.TrySetResult();
   }
   if(!all.IsCompleted)
   {transferred=true;_=all.ContinueWith(t=>{_=t.Exception;Release();},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
   if(!transferred)Release();
  }
 }
}
