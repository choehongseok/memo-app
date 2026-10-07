namespace MemoApp.Core.Documents;
public sealed record MarkdownPreviewUpdate(long Generation,MarkdownPreview Preview);
public sealed class MarkdownPreviewQueue:IDisposable
{
    private static readonly SemaphoreSlim ParserSlot=new(1,1);
    private readonly object gate=new();
    private readonly CancellationTokenSource stop=new();
    private Action<MarkdownPreviewUpdate>? ready;
    private Func<string,MarkdownPreview>? parse;
    private (long Generation,string Source)? pending;
    private long generation;
    private bool running,disposed;
    private Task worker=Task.CompletedTask;
    public MarkdownPreviewQueue(Action<MarkdownPreviewUpdate> ready,Func<string,MarkdownPreview>? parse=null)
    {ArgumentNullException.ThrowIfNull(ready);this.ready=ready;this.parse=parse??SafeMarkdown.Preview;}
    public long Request(string source)
    {
        ArgumentNullException.ThrowIfNull(source);bool bounded=source.Length<=SafeMarkdown.MaxSource&&RichDocumentCodec.IsWellFormedUnicode(source);
        lock(gate)
        {
            if(disposed)return 0;generation++;pending=null;
            if(!bounded)return 0;
            pending=(generation,source);if(!running){running=true;worker=Task.Run(WorkLoop);}return generation;
        }
    }
    public bool IsCurrent(long value){lock(gate)return !disposed&&value!=0&&value==generation;}
    public Task WhenIdle{get{lock(gate)return worker;}}
    private async Task WorkLoop()
    {
        while(true)
        {
            (long Generation,string Source) work;
            lock(gate){if(disposed||pending is null){running=false;return;}work=pending.Value;pending=null;}
            try{await ParserSlot.WaitAsync(stop.Token).ConfigureAwait(false);}catch(OperationCanceledException){lock(gate)running=false;return;}
            MarkdownPreview? preview=null;
            try
            {
                Func<string,MarkdownPreview>? parser;lock(gate)parser=!disposed&&work.Generation==generation?parse:null;
                if(parser is not null)
                {
                    try{preview=parser(work.Source);}catch(Exception error) when(error is not OutOfMemoryException){preview=new(false,"미리보기 처리 실패 · 원문은 보존합니다.",[]);}
                }
            }
            finally{ParserSlot.Release();}
            Action<MarkdownPreviewUpdate>? callback;lock(gate)callback=preview is not null&&!disposed&&work.Generation==generation?ready:null;
            // A consumer must recheck IsCurrent/session authority after its own asynchronous dispatch.
            if(callback is not null)try{callback(new(work.Generation,preview!));}catch(Exception error) when(error is not OutOfMemoryException){/* Consumer failure cannot strand the queue. */}
        }
    }
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;generation++;pending=null;ready=null;parse=null;}
        // Cancel waiting admission, not an already executing synchronous parser.
        stop.Cancel();
    }
}
