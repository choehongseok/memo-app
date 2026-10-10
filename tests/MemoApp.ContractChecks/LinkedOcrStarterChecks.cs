using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
internal static class LinkedOcrStarterChecks
{
    internal static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-staged-ocr-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        byte[] secret=EncryptedVault.GenerateRecoverySecret();byte[] png=Convert.FromBase64String(File.ReadAllText("tests/fixtures/ocr-synthetic-png.base64"));
        try
        {
            string components=Path.Combine(root,"empty-components");Directory.CreateDirectory(components);
            byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(new{tesseractCommit=OcrProvenanceFacts.TesseractCommit,leptonicaCommit=OcrProvenanceFacts.LeptonicaCommit,modelsCommit=OcrProvenanceFacts.ModelsCommit,peDependencies=new[]{"KERNEL32.dll"},engineLength=1,modelsArchiveLength=1,engineSha256=new string('a',64),modelsArchiveSha256=new string('b',64)});
            var bundle=new WindowsOcrBundle(components,manifest);
            using var owner=new SaveCoordinator(EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret),TimeProvider.System);var note=owner.Workspace.CreateNote();
            VaultChecks.Require(await owner.PrepareAttachmentsAsync(),"Staged starter anchored synthetic encrypted root");Guid id=owner.AttachBytes(note,png,"synthetic.png","image/png",note.EditVersion);VaultChecks.Require(await owner.SaveAsync(),"Staged starter genuine authenticated source");
            var source=new OcrSourceDescriptor(id,owner.Workspace.Capture().AttachmentRootId,Convert.ToHexStringLower(SHA256.HashData(png)),png.Length);
            var stamp=new OcrGrantStamp(Guid.NewGuid(),Guid.NewGuid(),note.Id,note.EditVersion,owner.AttachmentPreviewEpoch);var allocations=new List<byte[]>();
            AttachmentOcrInput Capture()=>AttachmentOcrInput.Capture(owner.CreateAttachmentReadLease(note,id,note.EditVersion),source,stamp,allocations:allocations.Add);
            using(var input=Capture())using(var starter=new LinkedOcrStarter(bundle,input,root))
            {
                var operation=starter.Operation;VaultChecks.Require(starter.Source==source&&starter.Stamp==stamp&&!operation.Completion.IsCompleted&&!operation.Settled.IsCompleted,"Source/stamp and both pending tasks exist before any launch");
                CheckDetachedGraph(starter,new HashSet<object>(ReferenceEqualityComparer.Instance));
                starter.Dispose();try{using var unexpected=await operation.Completion;throw new Exception("Disposed staged input returned a result");}catch(OperationCanceledException){}await operation.Settled;
                try{starter.Start();throw new Exception("Disposed starter launched");}catch(ObjectDisposedException){}
                VaultChecks.Require(allocations.All(bytes=>bytes.All(value=>value==0)),"Dispose before launch zeroes every captured owned buffer");
            }
            allocations.Clear();using(var canceled=new CancellationTokenSource())using(var input=Capture())using(var starter=new LinkedOcrStarter(bundle,input,root,canceled.Token))
            {
                canceled.Cancel();var operation=starter.Operation;starter.Start();try{starter.Start();throw new Exception("Repeated starter launched");}catch(InvalidOperationException){}
                VaultChecks.Require(ReferenceEquals(operation,starter.Operation),"Repeated start retains the registered operation identity");
                try{using var unexpected=await operation.Completion;throw new Exception("Pre-canceled worker returned a result");}catch(OperationCanceledException){}finally{await operation.Settled;}
                VaultChecks.Require(allocations.All(bytes=>bytes.All(value=>value==0)),"Queued pre-start cancellation settles owned source cleanup");
            }
            allocations.Clear();using(var input=Capture())using(var starter=new LinkedOcrStarter(bundle,input,root))
            {
                var operation=starter.Operation;starter.Start();
                try{using var unexpected=await operation.Completion;throw new Exception("Empty synthetic components accepted");}
                catch(Exception error)when(error is PlatformNotSupportedException or InvalidDataException or IOException){}
                finally{await operation.Settled;}
                VaultChecks.Require(operation.Completion.IsFaulted&&operation.Settled.IsCompletedSuccessfully&&allocations.All(bytes=>bytes.All(value=>value==0)),"Fixed worker startup verification failure retains skeleton until confirmed cleanup; no inference claimed");
            }
            Console.WriteLine("PASS: detached staged OCR prelaunch ownership, cancellation/start failure cleanup, fixed worker field boundaries (synthetic components; no native inference)");
        }
        finally{CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(png);Directory.Delete(root,true);}
    }
    private static void CheckDetachedGraph(object value,HashSet<object> seen)
    {
        if(!seen.Add(value))return;Type type=value.GetType();
        VaultChecks.Require(value is not Delegate&&value is not SaveCoordinator&&value is not NoteDraft,"Detached starter retains no launch delegate/coordinator/note owner");
        if(type.Assembly!=typeof(SaveCoordinator).Assembly)return;
        foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
        {
            VaultChecks.Require(!field.Name.Contains("registry",StringComparison.OrdinalIgnoreCase)&&!field.Name.Contains("issuer",StringComparison.OrdinalIgnoreCase),"Detached custom state has no registry/issuer owner field");
            if(field.GetValue(value) is object child)CheckDetachedGraph(child,seen);
        }
    }
}
