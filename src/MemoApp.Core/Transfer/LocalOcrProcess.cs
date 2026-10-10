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
internal static class LocalOcrProcess
{
 private static readonly SemaphoreSlim admission=new(1,1);
 internal static Task<OwnedOcrText> RunPreparedAsync(PreparedTextExport input,ProcessStartInfo start,TimeSpan timeout,CancellationToken token=default)=>StartPrepared(input,start,timeout,token).Completion;
 // Settled is keyless cleanup evidence. UI/global admission may release only after its own publication work and this task finish.
 internal static OcrOperation StartPrepared(PreparedTextExport input,ProcessStartInfo start,TimeSpan timeout,CancellationToken token=default)
 {
  var settled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  return new(RunOwnedAsync(input,start,timeout,token,settled),settled.Task);
 }
 private static async Task<OwnedOcrText> RunOwnedAsync(PreparedTextExport input,ProcessStartInfo start,TimeSpan timeout,CancellationToken token,TaskCompletionSource settled)
 {
  bool entered=false,transferred=false;Process? child=null;byte[] output=new byte[262145];Task all=Task.CompletedTask;
  try
  {
   if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromSeconds(20)||input.IsDisposed||input.Bytes.Length>3145750)throw new InvalidDataException("OCR input/deadline limit");
   token.ThrowIfCancellationRequested();if(!await admission.WaitAsync(0,token))throw new InvalidOperationException("OCR process work is occupied");entered=true;
   start.UseShellExecute=false;start.RedirectStandardInput=true;start.RedirectStandardOutput=true;start.RedirectStandardError=true;start.CreateNoWindow=true;
   child=new Process{StartInfo=start};if(!child.Start())throw new InvalidDataException("OCR child refused");
   var fault=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int count=0;
   async Task ReadOutput()
   {
    while(true){int read=await child.StandardOutput.BaseStream.ReadAsync(output.AsMemory(count),token);if(read==0)return;count+=read;if(count>262144)throw new InvalidDataException("OCR output byte limit");}
   }
   async Task ReadError()
   {
    byte[] buffer=new byte[1024];int total=0;try{while(true){int read=await child.StandardError.BaseStream.ReadAsync(buffer.AsMemory(0,Math.Min(buffer.Length,4097-total)),token);if(read==0)return;total+=read;if(total>4096)throw new InvalidDataException("OCR error output limit");}}finally{CryptographicOperations.ZeroMemory(buffer);}
   }
   async Task WriteInput(){try{await child.StandardInput.BaseStream.WriteAsync(input.Bytes,token);await child.StandardInput.BaseStream.FlushAsync(token);}finally{child.StandardInput.Close();}}
   Task[] tasks=[ReadOutput(),ReadError(),WriteInput(),child.WaitForExitAsync()];
   foreach(var task in tasks)_=task.ContinueWith(t=>{if(t.IsFaulted||t.IsCanceled)fault.TrySetResult();},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
   all=Task.WhenAll(tasks);Task first=await Task.WhenAny(all,fault.Task).WaitAsync(timeout,token);
   if(first==fault.Task)throw new InvalidDataException("OCR pipe failed");
   try{await all;}catch{throw new InvalidDataException("OCR pipe failed");}
   token.ThrowIfCancellationRequested();if(child.ExitCode!=0)throw new InvalidDataException("OCR child failed");
   string text;try{text=new UTF8Encoding(false,true).GetString(output,0,count);}catch(DecoderFallbackException){throw new InvalidDataException("OCR output UTF8");}
   if(text.Length>65536||text.Contains('\0'))throw new InvalidDataException("OCR output text limit");
   token.ThrowIfCancellationRequested();return new(output.AsSpan(0,count).ToArray());
  }
  finally
  {
   if(child is not null)
   {
    try{if(!child.HasExited)child.Kill(true);}catch(Exception error)when(error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException){}
    try{child.StandardInput.Close();}catch{}try{child.StandardOutput.Close();}catch{}try{child.StandardError.Close();}catch{}
    try{await all.WaitAsync(TimeSpan.FromSeconds(2));}catch{}
   }
   void Release()
   {
    try{input.Dispose();CryptographicOperations.ZeroMemory(output);child?.Dispose();if(entered)admission.Release();settled.TrySetResult();}
    catch(Exception error){settled.TrySetException(error);throw;}
   }
   if(!all.IsCompleted)
   {transferred=true;_=all.ContinueWith(t=>{_=t.Exception;Release();},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
   if(!transferred)Release();
  }
 }
}
