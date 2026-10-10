using System.Diagnostics;
using System.Reflection;
using MemoApp.Core.Transfer;
internal static class OcrCleanupFaultChecks
{
    // Run only in an isolated harness invocation: intentionally faulted cleanup retains global admission.
    internal static async Task Run()
    {
        string path=Path.Combine(Path.GetTempPath(),"memo-ocr-cleanup-fault-"+Guid.NewGuid().ToString("N"));File.WriteAllBytes(path,[1]);
        try
        {
            using var source=PpmOcrInput.Capture(new OwnedBgraRaster(1,1,new byte[]{1,2,3,255}));var probe=new OcrProcessOwnershipProbe();
            var operation=LocalOcrProcess.StartPrepared(source,Worker("good"),TimeSpan.FromSeconds(5),verifiedFiles:[new CleanupFaultFile(path)],ownershipProbe:probe);
            try{using var unexpected=await operation.Completion;throw new Exception("Cleanup fault returned an owned OCR candidate");}catch(IOException error)when(error.Message=="Synthetic OCR cleanup fault"){}
            try{await operation.Settled;throw new Exception("Cleanup fault reported successful settlement");}catch(IOException error)when(error.Message=="Synthetic OCR cleanup fault"){}
            VaultChecks.Require(operation.Completion.IsFaulted&&operation.Settled.IsFaulted&&source.IsDisposed&&probe.CandidateBuffer is {Length:>0} bytes&&bytes.All(value=>value==0),"Copied UTF8 candidate is zeroed when Release throws before return transfer; cleanup remains fail closed");
            using var laterInput=PpmOcrInput.Capture(new OwnedBgraRaster(1,1,new byte[]{1,2,3,255}));var later=LocalOcrProcess.StartPrepared(laterInput,Worker("good"),TimeSpan.FromSeconds(5));
            try{using var unexpected=await later.Completion;throw new Exception("Cleanup fault released global OCR admission");}catch(InvalidOperationException){}await later.Settled;
            VaultChecks.Require(laterInput.IsDisposed,"Admission refusal still settles later owned input");
            Console.WriteLine("PASS: actual synthetic child cleanup fault zeroes copied candidate, faults settlement and retains admission; isolated fault fixture, no OCR provenance claim");
        }
        finally{File.Delete(path);}
    }
    private static ProcessStartInfo Worker(string mode)=>(ProcessStartInfo)typeof(MemoApp.Core.LocalOcrChecks).GetMethod("Worker",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[mode])!;
    private sealed class CleanupFaultFile(string path):FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)
    {
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)throw new IOException("Synthetic OCR cleanup fault");}
    }
}
