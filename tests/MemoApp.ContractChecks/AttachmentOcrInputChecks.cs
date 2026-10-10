using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
internal static class AttachmentOcrInputChecks
{
    internal static async Task Run()
    {
        var assembly=typeof(PreparedTextExport).Assembly;Type? inputType=assembly.GetType("MemoApp.Core.Transfer.AttachmentOcrInput");VaultChecks.Require(inputType is not null,"Source-bound owned OCR input is missing");
        Type sourceType=assembly.GetType("MemoApp.Core.Transfer.OcrSourceDescriptor")!,stampType=assembly.GetType("MemoApp.Core.Transfer.OcrGrantStamp")!;
        string root=Path.Combine(Path.GetTempPath(),"memo-linked-ocr-input-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        // Fixed PNG stored-block vector: source RGBA [1,2,3,4], preview BGRA [3,2,1,4].
        byte[] png=(byte[])typeof(PngDecoderChecks).GetMethod("Png",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[1,1,4,new byte[][]{[0x78,0x01,0x01,0x05,0x00,0xfa,0xff,0,1,2,3,4,0,0x19,0,0x0b]}])!;byte[] copy=(byte[])png.Clone();
        try
        {
            using var owner=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=owner.Workspace.CreateNote();note.Title="합성 OCR 원본 보존";note.Text="합성 원본 body\r\n공백과 Unicode e\u0301 보존";VaultChecks.Require(await owner.PrepareAttachmentsAsync(),"OCR input genuine anchored encrypted root");Guid objectId=owner.AttachBytes(note,png,"synthetic.png","image/png",note.EditVersion);VaultChecks.Require(await owner.SaveAsync(),"OCR input genuine encrypted object saved");
            string beforeWorkspace=JsonSerializer.Serialize(owner.Workspace.Capture());byte[] beforeCipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));long beforeVersion=note.EditVersion;
            object Descriptor(string? hash=null,int? length=null)=>Activator.CreateInstance(sourceType,[objectId,owner.Workspace.Capture().AttachmentRootId,hash??Convert.ToHexStringLower(SHA256.HashData(png)),length??png.Length])!;
            object Stamp()=>Activator.CreateInstance(stampType,[Guid.NewGuid(),Guid.NewGuid(),note.Id,note.EditVersion,owner.AttachmentPreviewEpoch])!;
            IDisposable Capture(AttachmentReadLease lease,object descriptor,object stamp,CancellationToken token,List<byte[]>? observed=null)=>(IDisposable)Call(inputType!,"Capture",null,[lease,descriptor,stamp,token,observed is null?null:(Action<byte[]>)observed.Add])!;
            var observed=new List<byte[]>();using(var lease=owner.CreateAttachmentReadLease(note,objectId,note.EditVersion))
            {
                byte[] leased=LeaseBytes(lease);object source=Descriptor(),stamp=Stamp();using var input=Capture(lease,source,stamp,default,observed);
                VaultChecks.Require(leased.All(b=>b==0)&&owner.WhenAttachmentReadsIdle.IsCompleted,"Same single lease is consumed/disposed and authenticated source ownership ends");
                VaultChecks.Require(Equals(Property(input,"Source"),source)&&Equals(Property(input,"Stamp"),stamp)&&Scalar(input,"SourceWidth")==1&&Scalar(input,"SourceHeight")==1&&Scalar(input,"PreviewWidth")==1&&Scalar(input,"PreviewHeight")==1,"Original descriptor/stamp and actual source/preview dimensions preserved without granting application authority");
                using var raster=(OwnedBgraRaster)Call(inputType!,"TakeRaster",input,[])!;using var ppm=PpmOcrInput.Capture(raster);
                VaultChecks.Require(ppm.Bytes.SequenceEqual(new byte[]{80,54,10,49,32,49,10,50,53,53,10,251,251,251}),"Exact actual prepared PPM alpha-over-white integer policy");
                Fail<InvalidOperationException>(()=>Call(inputType!,"TakeRaster",input,[]));input.Dispose();VaultChecks.Require(observed.All(b=>b.All(v=>v==0)),"Source/decode scratch and consumed raster zero");
            }
            foreach(var bad in new[]{Descriptor(new string('0',64)),Descriptor(length:png.Length-1),Descriptor(new string('F',64))})
            {observed.Clear();using var lease=owner.CreateAttachmentReadLease(note,objectId,note.EditVersion);byte[] owned=LeaseBytes(lease);Fail<InvalidDataException>(()=>Capture(lease,bad,Stamp(),default,observed));VaultChecks.Require(owned.All(b=>b==0)&&observed.Count==0,"Wrong descriptor/source digest/length refused before decoder allocations and lease zeroed");}
            using(var lease=owner.CreateAttachmentReadLease(note,objectId,note.EditVersion)){lease.Dispose();Fail<InvalidOperationException>(()=>Capture(lease,Descriptor(),Stamp(),default));}
            using(var token=new CancellationTokenSource()){using var lease=owner.CreateAttachmentReadLease(note,objectId,note.EditVersion);byte[] owned=LeaseBytes(lease);token.Cancel();Fail<OperationCanceledException>(()=>Capture(lease,Descriptor(),Stamp(),token.Token));VaultChecks.Require(owned.All(b=>b==0),"Pre-capture cancellation disposes acquired owned lease");}
            observed.Clear();using(var lease=owner.CreateAttachmentReadLease(note,objectId,note.EditVersion))using(var input=Capture(lease,Descriptor(),Stamp(),default,observed)){input.Dispose();Fail<ObjectDisposedException>(()=>Call(inputType!,"TakeRaster",input,[]));VaultChecks.Require(observed.All(b=>b.All(v=>v==0)),"Disposing unused owned input zeroes raster and scratch");}
            int allocations=observed.Count;
            for(int stop=1;stop<=allocations;stop++)
            {
                int count=0;var failed=new List<byte[]>();using var lease=owner.CreateAttachmentReadLease(note,objectId,note.EditVersion);byte[] owned=LeaseBytes(lease);
                Action<byte[]> observer=b=>{failed.Add(b);if(++count==stop)throw new IOException("Synthetic decoder allocation failure");};
                Fail<IOException>(()=>Call(inputType!,"Capture",null,[lease,Descriptor(),Stamp(),default(CancellationToken),observer]));VaultChecks.Require(owned.All(b=>b==0)&&failed.All(b=>b.All(v=>v==0)),"Capture observer failure zeroes every allocated buffer and leased source");
            }
            VaultChecks.Require(png.SequenceEqual(copy),"Capture never alters borrowed original PNG fixture");
            await OwnedResultChecks(assembly);
            WorkerFields(assembly);
            VaultChecks.Require(note.EditVersion==beforeVersion&&!owner.IsDirty&&JsonSerializer.Serialize(owner.Workspace.Capture())==beforeWorkspace&&File.ReadAllBytes(Path.Combine(root,"current.vault")).SequenceEqual(beforeCipher),"All workspace fields/history/objects/versions and original ciphertext remain exact and clean after OCR runtime success/failures");
            Console.WriteLine("PASS: authenticated source/stamp/PPM, exact original workspace+cipher invariance, OCR input/result single-use reentrant/self-dispose/throw ownership and UTF8 limits, compiled keyless fields; synthetic result boundary only");
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static Task OwnedResultChecks(Assembly assembly)
    {
        Type? type=assembly.GetType("MemoApp.Core.Transfer.OwnedOcrDerivation");VaultChecks.Require(type is not null,"Owned source-bound OCR result is missing");
        VaultChecks.Require(type!.GetConstructors(BindingFlags.Instance|BindingFlags.Public).Length==0&&!type.GetMethods(BindingFlags.Public|BindingFlags.Static).Any(m=>m.ReturnType==type),"No public constructor or raw text/provenance factory mints trusted OCR results");
        // Private construction checks ownership mechanics only; no verified engine execution is claimed.
        byte[] text=System.Text.Encoding.UTF8.GetBytes("합성 OCR\r\n");using var output=new OwnedOcrText(text);
        Type factsType=assembly.GetType("MemoApp.Core.Transfer.OcrProvenanceFacts")!,sourceType=assembly.GetType("MemoApp.Core.Transfer.OcrSourceDescriptor")!,stampType=assembly.GetType("MemoApp.Core.Transfer.OcrGrantStamp")!;
        object facts=Activator.CreateInstance(factsType,[new string('a',64),new string('b',64),new string('c',64),1,1,1,1,new string('d',64)])!;
        object source=Activator.CreateInstance(sourceType,[Guid.NewGuid(),Guid.NewGuid(),new string('e',64),72])!,stamp=Activator.CreateInstance(stampType,[Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),1L,1L])!;
        var observed=new List<byte[]>();using var result=(IDisposable)Construct(type,[output,source,stamp,facts,default(CancellationToken),(Action<byte[]>)observed.Add])!;
        VaultChecks.Require(text.All(b=>b==0)&&Property(result,"Text") is string actual&&actual=="합성 OCR\r\n"&&Property(result,"TextSha256") is string hash&&hash==Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(actual))),"Owned result consumes exact UTF8 once, source output zeroed, exact text/hash preserved");
        result.Dispose();VaultChecks.Require(observed.All(b=>b.All(v=>v==0)),"Result dispose zeroes owned UTF8");Fail<ObjectDisposedException>(()=>Property(result,"Text"));
        using var canceled=new CancellationTokenSource();canceled.Cancel();byte[] second=System.Text.Encoding.UTF8.GetBytes("synthetic");using var secondOutput=new OwnedOcrText(second);Fail<OperationCanceledException>(()=>Construct(type,[secondOutput,source,stamp,facts,canceled.Token,null]));VaultChecks.Require(second.All(b=>b==0),"Canceled result construction disposes incoming owned output");
        OwnershipCases(()=>(new OwnedOcrText(Encoding.UTF8.GetBytes("합성 owner\r\n")),null));
        OwnershipCases(()=>
        {
            byte[] incoming=Encoding.UTF8.GetBytes("합성 owner\r\n");var outputText=new OwnedOcrText(incoming);var allocated=new List<byte[]>();
            var value=(OwnedOcrDerivation)Construct(type,[outputText,source,stamp,facts,default(CancellationToken),(Action<byte[]>)allocated.Add])!;
            VaultChecks.Require(incoming.All(b=>b==0),"Synthetic result construction ended original owned output lifetime");return (value,allocated.Single());
        });
        foreach(byte[] invalid in new[]{Array.Empty<byte>(),new byte[]{0xc0,0xaf},new byte[]{0xf0,0x9f,0x98},new byte[]{65,0,66},Enumerable.Repeat((byte)65,262145).ToArray(),Enumerable.Repeat((byte)65,65537).ToArray()})
        {
            byte[] incoming=(byte[])invalid.Clone();using var malformed=new OwnedOcrText(incoming);observed.Clear();
            Fail<InvalidDataException>(()=>Construct(type,[malformed,source,stamp,facts,default(CancellationToken),(Action<byte[]>)observed.Add]));
            VaultChecks.Require(incoming.All(b=>b==0)&&observed.Count==0,"Empty/malformedUTF8/NUL/byte/UTF16 overflow refused before result allocation and owned incoming output zeroed");
        }
        byte[] exact=Enumerable.Repeat((byte)65,65536).ToArray();using(var exactOutput=new OwnedOcrText(exact))using(var bounded=(OwnedOcrDerivation)Construct(type,[exactOutput,source,stamp,facts,default(CancellationToken),null])!)
        {VaultChecks.Require(bounded.Text.Length==65536&&exact.All(b=>b==0),"Exact UTF16 limit succeeds with input ownership ended");}
        foreach(bool cancelAfterAllocation in new[]{false,true})
        {
            using var token=new CancellationTokenSource();byte[] incoming=Encoding.UTF8.GetBytes("synthetic allocation boundary");using var textOutput=new OwnedOcrText(incoming);observed.Clear();
            Action<byte[]> allocation=b=>{observed.Add(b);if(cancelAfterAllocation)token.Cancel();else throw new IOException("Synthetic result allocation observer failure");};
            if(cancelAfterAllocation)Fail<OperationCanceledException>(()=>Construct(type,[textOutput,source,stamp,facts,token.Token,allocation]));else Fail<IOException>(()=>Construct(type,[textOutput,source,stamp,facts,token.Token,allocation]));
            VaultChecks.Require(incoming.All(b=>b==0)&&observed.Count==1&&observed.All(b=>b.All(v=>v==0)),"Result allocation observer exception/cancellation zeroes incoming and newly allocated owned bytes");
        }
        return Task.CompletedTask;
    }
    private static void OwnershipCases(Func<(IDisposable Owner,byte[]? KnownBytes)> create)
    {
        (IDisposable Owner,byte[] Bytes,Func<OcrUtf8Reader,bool> Consume) Fresh()
        {
            var created=create();if(created.Owner is OwnedOcrText text)return (text,(byte[])typeof(OwnedOcrText).GetField("bytes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(text)!,text.ConsumeUtf8);
            var result=(OwnedOcrDerivation)created.Owner;return (result,created.KnownBytes!,result.ConsumeUtf8);
        }
        var repeated=Fresh();using(repeated.Owner)
        {byte[] expected=(byte[])repeated.Bytes.Clone();VaultChecks.Require(repeated.Consume(b=>VaultChecks.Require(b.SequenceEqual(expected),"Owned UTF8 exact first consume"))&&repeated.Bytes.All(b=>b==0),"First consume zeroes owned UTF8");Fail<InvalidOperationException>(()=>repeated.Consume(_=>{}));}
        var reentrant=Fresh();using(reentrant.Owner)
        {byte[] expected=(byte[])reentrant.Bytes.Clone();VaultChecks.Require(reentrant.Consume(b=>{Fail<InvalidOperationException>(()=>reentrant.Consume(_=>{}));VaultChecks.Require(b.SequenceEqual(expected)&&reentrant.Bytes.SequenceEqual(expected),"Reentrant refusal leaves outer borrowed bytes exact until callback finishes");})&&reentrant.Bytes.All(b=>b==0),"Reentrant outer consume ends ownership and zeroes");}
        var selfDispose=Fresh();using(selfDispose.Owner)
        {byte[] expected=(byte[])selfDispose.Bytes.Clone();VaultChecks.Require(!selfDispose.Consume(b=>{selfDispose.Owner.Dispose();VaultChecks.Require(b.SequenceEqual(expected)&&selfDispose.Bytes.SequenceEqual(expected),"Self Dispose defers clearing exclusive borrowed bytes until callback finally");})&&selfDispose.Bytes.All(b=>b==0),"Self Dispose returns false then zeroes owned UTF8");Fail<InvalidOperationException>(()=>selfDispose.Consume(_=>{}));}
        var throwing=Fresh();using(throwing.Owner)
        {Fail<IOException>(()=>throwing.Consume(_=>throw new IOException("Synthetic callback failure")));VaultChecks.Require(throwing.Bytes.All(b=>b==0),"Callback exception propagates only after owned UTF8 zero");Fail<InvalidOperationException>(()=>throwing.Consume(_=>{}));}
    }
    private static void WorkerFields(Assembly assembly)
    {
        // Inspect compiled DTO/input/result/linked-completion state and display classes, not a hypothetical delegate-free worker.
        Type[] roots=[typeof(AttachmentOcrInput),typeof(OwnedOcrDerivation),typeof(OcrSourceDescriptor),typeof(OcrGrantStamp),typeof(OcrProvenanceFacts),typeof(WindowsOcrBundle),typeof(OwnedOcrText)];
        var graph=new HashSet<Type>();void Visit(Type type){if(!graph.Add(type))return;foreach(var nested in type.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic))Visit(nested);}
        foreach(var root in roots)Visit(root);
        foreach(var type in graph)foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
        {
            Type value=field.FieldType;VaultChecks.Require(!HasAuthorityType(value),"Compiled OCR worker/DTO field graph has no note/coordinator/workspace/vault/key/UI owner");
            if(typeof(Delegate).IsAssignableFrom(value)&&!field.IsStatic)VaultChecks.Require(value==typeof(Action<byte[]>),"Instance runtime observer closure is the explicit allocation test seam; static compiler LINQ caches may contain pure data delegates");
        }
        foreach(var method in new[]{typeof(AttachmentOcrInput).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static)!,typeof(OwnedOcrDerivation).GetMethod("StartVerified",BindingFlags.NonPublic|BindingFlags.Static)!})
        {var observer=method.GetParameters().Single(p=>p.ParameterType==typeof(Action<byte[]>));VaultChecks.Require(observer.IsOptional&&observer.DefaultValue is null,"Allocation observer test seam defaults to null in production");}
        VaultChecks.Require(typeof(WindowsOcrBundle).GetMethod("StartLinked",BindingFlags.NonPublic|BindingFlags.Instance)!.GetParameters().All(p=>!typeof(Delegate).IsAssignableFrom(p.ParameterType)),"Production linked bundle entry accepts only detached input/models/token");
        bool HasAuthorityType(Type value)
        {
            if(value.IsGenericType&&value.GetGenericArguments().Any(HasAuthorityType))return true;
            if(value.IsArray)return HasAuthorityType(value.GetElementType()!);
            string name=value.FullName??"";return value==typeof(NoteDraft)||value==typeof(SaveCoordinator)||value==typeof(EditingWorkspace)||value==typeof(EncryptedVault)||name.StartsWith("System.Windows.",StringComparison.Ordinal)||name.Contains("KeyOwner",StringComparison.Ordinal)||name.Contains("SessionOwner",StringComparison.Ordinal);
        }
    }
    private static byte[] LeaseBytes(AttachmentReadLease lease)=>(byte[])typeof(AttachmentReadLease).GetField("bytes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(lease)!;
    private static object? Property(object target,string name){try{return target.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target);}catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
    private static int Scalar(object target,string name)=>(int)Property(target,name)!;
    private static object? Call(Type type,string name,object? target,object?[] args){try{return type.GetMethod(name,BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);}catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
    private static object? Construct(Type type,object?[] args){try{return Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,args,null);}catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
    private static void Fail<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
}
