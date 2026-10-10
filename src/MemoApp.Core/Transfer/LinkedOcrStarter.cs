namespace MemoApp.Core.Transfer;

// Preallocated detached operation. The issuer registers Operation before Start can queue work.
// No arbitrary launch delegate, note/session/key/registry owner or publication callback enters this state.
internal sealed class LinkedOcrStarter : IDisposable
{
    private readonly object gate=new();
    private readonly WindowsOcrBundle bundle;
    private readonly AttachmentOcrInput input;
    private readonly string models;
    private readonly CancellationToken token;
    private readonly TaskCompletionSource<OwnedOcrDerivation> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource settled=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool started,disposed;
    internal OcrSourceDescriptor Source=>input.Source;
    internal OcrGrantStamp Stamp=>input.Stamp;
    internal LinkedOcrOperation Operation{get;}
    internal LinkedOcrStarter(WindowsOcrBundle bundle,AttachmentOcrInput input,string models,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(bundle);ArgumentNullException.ThrowIfNull(input);ArgumentNullException.ThrowIfNull(models);
        this.bundle=bundle;this.input=input;this.models=models;this.token=token;Operation=new(completion.Task,settled.Task);
    }
    internal void Start()
    {
        lock(gate)
        {
            if(disposed)throw new ObjectDisposedException(nameof(LinkedOcrStarter));
            if(started)throw new InvalidOperationException("Linked OCR starter already launched");
            started=true;
        }
        try
        {
            // No ExecutionContext or owner closure flows into this fixed worker entry.
            if(!ThreadPool.UnsafeQueueUserWorkItem(static (LinkedOcrStarter state)=>state.RunWorker(),this,false))
                throw new InvalidOperationException("Linked OCR worker queue refused");
        }
        catch(Exception error)
        {
            // Queue refusal has no worker/native operation; this already registered skeleton settles cleanup.
            try{input.Dispose();completion.TrySetException(error);settled.TrySetResult();}
            catch(Exception cleanup){completion.TrySetException(error);settled.TrySetException(cleanup);}
            throw;
        }
    }
    private void RunWorker()=>_ = RunAsync();
    private async Task RunAsync()
    {
        LinkedOcrOperation? inner=null;OwnedOcrDerivation? owned=null;
        try
        {
            token.ThrowIfCancellationRequested();inner=bundle.StartLinked(input,models,token);
            owned=await inner.Completion.ConfigureAwait(false);
            lock(gate)
            {
                if(disposed){owned.Dispose();owned=null;completion.TrySetCanceled();}
                else if(completion.TrySetResult(owned))owned=null;
            }
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
    public void Dispose()
    {
        bool noWorker;OwnedOcrDerivation? published;
        lock(gate){if(disposed)return;disposed=true;noWorker=!started;published=completion.Task.IsCompletedSuccessfully?completion.Task.Result:null;}
        published?.Dispose();
        if(noWorker)
        {
            try{input.Dispose();completion.TrySetCanceled();settled.TrySetResult();}
            catch(Exception error){completion.TrySetException(error);settled.TrySetException(error);}
        }
        // Running process/input/result ownership is retired by RunAsync, never by early disposal.
    }
}
