using System.Collections.Concurrent;
using System.Diagnostics;
using MemoApp.Core.Documents;
internal static class MarkdownQueueChecks
{
    internal static async Task Run()
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var updates=new ConcurrentQueue<MarkdownPreviewUpdate>();var parsed=new ConcurrentQueue<string>();int active=0,maxActive=0;
        using(var queue=new MarkdownPreviewQueue(updates.Enqueue,source=>
        {
            int count=Interlocked.Increment(ref active);Interlocked.Exchange(ref maxActive,Math.Max(maxActive,count));parsed.Enqueue(source);
            try{if(source=="BLOCK_A"){entered.Set();if(!release.Wait(TimeSpan.FromSeconds(10)))throw new Exception("synthetic parser gate timeout");}return SafeMarkdown.Preview(source);}finally{Interlocked.Decrement(ref active);}
        }))
        {
            _=queue.Request("BLOCK_A");VaultChecks.Require(entered.Wait(TimeSpan.FromSeconds(5)),"actual first parser begins");long latest=0;for(int i=0;i<100;i++)latest=queue.Request("LATEST_"+i);release.Set();await queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));
            VaultChecks.Require(maxActive==1&&parsed.ToArray().SequenceEqual(new[]{"BLOCK_A","LATEST_99"})&&updates.Count==1&&updates.Single().Generation==latest&&queue.IsCurrent(latest),"one running parse plus latest pending source; stale A result never publishes");
            VaultChecks.Require(queue.Request(new string('x',65537))==0&&queue.Request("malformed\uD800")==0,"oversized/malformed request refused before retaining parser input");
        }
        entered.Reset();release.Reset();updates.Clear();
        var revoked=new MarkdownPreviewQueue(updates.Enqueue,source=>{entered.Set();if(!release.Wait(TimeSpan.FromSeconds(10)))throw new Exception("synthetic revoke gate timeout");return SafeMarkdown.Preview(source);});long version=revoked.Request("SYNTHETIC_REVOKED");VaultChecks.Require(entered.Wait(TimeSpan.FromSeconds(5)),"revoke parser begins");var activeWork=revoked.WhenIdle;var clock=Stopwatch.StartNew();revoked.Dispose();VaultChecks.Require(clock.Elapsed<TimeSpan.FromSeconds(1)&&!activeWork.IsCompleted&&!revoked.IsCurrent(version)&&revoked.Request("LATE")==0,"revoke drops authority without blocking executing synchronous parser");release.Set();await activeWork.WaitAsync(TimeSpan.FromSeconds(10));VaultChecks.Require(updates.IsEmpty,"revoked parse result discarded");revoked.Dispose();
        MarkdownPreviewQueue? reentrant=null;var observed=new List<long>();reentrant=new(update=>{observed.Add(update.Generation);if(observed.Count==1)reentrant!.Request("CALLBACK_LATEST");});reentrant.Request("FIRST");await reentrant.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));VaultChecks.Require(observed.Count==2&&reentrant.IsCurrent(observed[^1]),"ready callback reentrant request is coalesced by the same worker");reentrant.Dispose();
        int faults=0;using var resilient=new MarkdownPreviewQueue(_=>{if(Interlocked.Increment(ref faults)==1)throw new ArgumentException("synthetic callback failure");},source=>source=="PARSE_FAIL"?throw new InvalidDataException("synthetic parser failure"):SafeMarkdown.Preview(source));resilient.Request("PARSE_FAIL");await resilient.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));long final=resilient.Request("NEXT_SAFE");await resilient.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));VaultChecks.Require(faults==2&&resilient.IsCurrent(final),"recoverable parse/callback failure cannot strand worker or later requests");
        entered.Reset();release.Reset();int secondParses=0;using var globalFirst=new MarkdownPreviewQueue(_=>{},source=>{entered.Set();if(!release.Wait(TimeSpan.FromSeconds(10)))throw new Exception("global gate timeout");return SafeMarkdown.Preview(source);});globalFirst.Request("GLOBAL_A");VaultChecks.Require(entered.Wait(TimeSpan.FromSeconds(5)),"global first parser enters");var globalSecond=new MarkdownPreviewQueue(_=>{},source=>{Interlocked.Increment(ref secondParses);return SafeMarkdown.Preview(source);});globalSecond.Request("GLOBAL_B");var admission=globalSecond.WhenIdle;globalSecond.Dispose();await admission.WaitAsync(TimeSpan.FromSeconds(5));VaultChecks.Require(secondParses==0,"application-wide parser admission serializes queues and revoked waiting parser never executes");release.Set();await globalFirst.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));
        MarkdownPreviewQueue? callbackDisposed=null;callbackDisposed=new(_=>callbackDisposed!.Dispose());long authority=callbackDisposed.Request("CALLBACK_REVOKE");await callbackDisposed.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));VaultChecks.Require(!callbackDisposed.IsCurrent(authority)&&callbackDisposed.Request("LATE")==0,"callback can revoke queue without lock inversion or resurrecting authority");callbackDisposed.Dispose();
        Console.WriteLine("PASS: actual parser single-flight/latest-only queue, revoke without waiting, stale discard, bounded request, reentrant callback and parser/callback fault recovery");
    }
}
