using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using MemoApp.Core.Transfer;
internal static class BoundedFileReaderChecks
{
    private static byte[] Read(Stream input,int minimum,int maximum,CancellationToken token=default)
    {
        var type=typeof(TextTransfer).Assembly.GetType("MemoApp.Core.Transfer.BoundedFileReader");VaultChecks.Require(type is not null,"Single captured-length bounded file reader is missing");
        try{return (byte[])type!.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[input,minimum,maximum,token])!;}catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    internal static void Run()
    {
        using(var changing=new Source([1,2,3],3,99)){byte[] bytes=Read(changing,0,3);try{VaultChecks.Require(changing.LengthCalls==1&&bytes.SequenceEqual(new byte[]{1,2,3}),"Allocation uses one checked scalar even if a second Length getter would grow above cap");}finally{CryptographicOperations.ZeroMemory(bytes);}}
        using(var oversized=new Source([1],4,1)){VaultChecks.ExpectFailure(()=>Read(oversized,0,3),"Oversize initial length refused before allocation/read");VaultChecks.Require(oversized.LengthCalls==1&&oversized.Buffer is null,"Limit check reads scalar once and never payload");}
        foreach(bool shorter in new[]{true,false})using(var source=new Source([1,2],shorter?3:1,3)){VaultChecks.ExpectFailure(()=>Read(source,0,3),"Short/trailing source changed after capture refused");VaultChecks.Require(source.LengthCalls==1&&source.Buffer is not null&&source.Buffer.All(b=>b==0)&&source.CanRead,"Failed exact owned buffer zeroed and borrowed source remains open");}
        using(var cancellation=new CancellationTokenSource())using(var source=new Source([1,2],2,99,cancellation)){bool denied=false;try{Read(source,0,3,cancellation.Token);}catch(OperationCanceledException){denied=true;}VaultChecks.Require(denied&&source.LengthCalls==1&&source.Buffer is not null&&source.Buffer.All(b=>b==0),"Cancellation after read zeros exact owned array");}
        using(var cancellation=new CancellationTokenSource())using(var source=new Source([1],1,99)){cancellation.Cancel();bool denied=false;try{Read(source,0,3,cancellation.Token);}catch(OperationCanceledException){denied=true;}VaultChecks.Require(denied&&source.LengthCalls==0&&source.Buffer is null,"Pre-cancel avoids Length and source payload access");}
        using(var empty=new Source([],0,99))VaultChecks.Require(Read(empty,0,3).Length==0&&empty.LengthCalls==1,"Empty supported source reads length once");
        using(var empty=new Source([],0,99))VaultChecks.ExpectFailure(()=>Read(empty,1,3),"Positive minimum rejects empty source");
        Console.WriteLine("PASS: single checked file length before allocation, genuine changing-length stream, bounded payload/short/trailing/cancel refusal, exact owned failure zero and borrowed source preservation");
    }
    private sealed class Source(byte[] physical,long first,long subsequent,CancellationTokenSource? cancel=null):MemoryStream(physical,false)
    {
        internal int LengthCalls;internal byte[]? Buffer;public override long Length=>++LengthCalls==1?first:subsequent;
        public override int Read(byte[] buffer,int offset,int count){Buffer=buffer;int read=base.Read(buffer,offset,count);cancel?.Cancel();return read;}
    }
}
