using System.Security.Cryptography;
using System.Text;
namespace MemoApp.Core.Transfer;

internal sealed record LinkedOcrOperation(Task<OwnedOcrDerivation> Completion,Task Settled);
// Closed fresh-runtime candidate; the future coordinator must independently consume its issued grant.
internal sealed class OwnedOcrDerivation : IDisposable
{
    private readonly object gate=new();
    private byte[]? utf8;
    private bool running,finished,revoked;
    internal OcrSourceDescriptor Source{get;}
    internal OcrGrantStamp Stamp{get;}
    internal OcrProvenanceFacts Provenance{get;}
    internal string TextSha256{get;private set;}="";
    internal string Text{get{lock(gate){if(utf8 is null||finished||revoked)throw new ObjectDisposedException(nameof(OwnedOcrDerivation));if(running)throw new InvalidOperationException("OCR derivation consumption running");return new UTF8Encoding(false,true).GetString(utf8);}}}
    private OwnedOcrDerivation(OwnedOcrText output,OcrSourceDescriptor source,OcrGrantStamp stamp,OcrProvenanceFacts facts,CancellationToken token,Action<byte[]>? allocations)
    {
        Source=source;Stamp=stamp;Provenance=facts;
        try
        {
            token.ThrowIfCancellationRequested();source.Validate();stamp.Validate();facts.Validate();
            bool consumed=output.ConsumeUtf8(bytes=>
            {
                token.ThrowIfCancellationRequested();if(bytes.Length is <1 or >262144)throw new InvalidDataException("OCR linked output limit");
                string text;try{text=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){throw new InvalidDataException("OCR linked UTF8");}
                if(text.Length>65536||text.Contains('\0'))throw new InvalidDataException("OCR linked text limit");
                utf8=new byte[bytes.Length];allocations?.Invoke(utf8);token.ThrowIfCancellationRequested();bytes.CopyTo(utf8);TextSha256=Convert.ToHexStringLower(SHA256.HashData(bytes));token.ThrowIfCancellationRequested();
            });
            if(!consumed)throw new OperationCanceledException();token.ThrowIfCancellationRequested();
        }
        catch{Dispose();throw;}
        finally{output.Dispose();}
    }
    internal static LinkedOcrOperation StartVerified(WindowsOcrBundle bundle,AttachmentOcrInput input,string models,CancellationToken token,Action<byte[]>? allocations=null)
    {
        var completion=new TaskCompletionSource<OwnedOcrDerivation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation=new LinkedOcrOperation(completion.Task,settled.Task);
        _=RunVerifiedAsync(bundle,input,models,token,allocations,completion,settled);return operation;
    }
    private static async Task RunVerifiedAsync(WindowsOcrBundle bundle,AttachmentOcrInput input,string models,CancellationToken token,Action<byte[]>? allocations,TaskCompletionSource<OwnedOcrDerivation> completion,TaskCompletionSource settled)
    {
        OcrOperation? inner=null;OwnedOcrDerivation? owned=null;
        try
        {
            var source=input.Source;var stamp=input.Stamp;var verified=bundle.StartVerifiedLinked(input,models,token);inner=verified.Operation;
            using var output=await inner.Completion.ConfigureAwait(false);
            owned=new(output,source,stamp,verified.Facts,token,allocations);
            if(completion.TrySetResult(owned))owned=null;
        }
        catch(OperationCanceledException error){completion.TrySetCanceled(error.CancellationToken);}
        catch(Exception error){completion.TrySetException(error);}
        finally
        {
            Exception? failure=null;
            try{owned?.Dispose();input.Dispose();}catch(Exception error){failure=error;}
            try{if(inner is not null)await inner.Settled.ConfigureAwait(false);}catch(Exception error){failure??=error;}
            if(failure is null)settled.TrySetResult();else settled.TrySetException(failure);
        }
    }
    internal bool ConsumeUtf8(OcrUtf8Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);byte[] borrowed;
        lock(gate){if(running||finished||revoked)throw new InvalidOperationException("OCR derivation ownership ended");running=true;borrowed=utf8!;}
        bool success=false;
        try{reader(borrowed);}
        finally{lock(gate){success=!revoked;running=false;finished=true;CryptographicOperations.ZeroMemory(borrowed);utf8=null;}}
        return success;
    }
    public void Dispose(){lock(gate){revoked=true;if(!running&&!finished){finished=true;if(utf8 is not null)CryptographicOperations.ZeroMemory(utf8);utf8=null;}}}
}
