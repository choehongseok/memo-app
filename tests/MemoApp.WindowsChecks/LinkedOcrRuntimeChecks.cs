using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task LinkedOcrRuntimeRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-linked-ocr-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();byte[] png=WithScalarPngMetadata(Convert.FromBase64String(File.ReadAllText("tests/fixtures/ocr-synthetic-png.base64")));LinkedOcrOperation? running=null;OwnedOcrDerivation? result=null;
        try
        {
            using var manifestStream=typeof(AttachmentPanel).Assembly.GetManifestResourceStream("MemoApp.OcrManifest")??throw new Exception("Fixed product OCR manifest missing");using var manifestCopy=new MemoryStream();manifestStream.CopyTo(manifestCopy);byte[] manifest=manifestCopy.ToArray();Require(manifest.Length<=16384,"Fixed OCR manifest bounded");using var manifestJson=JsonDocument.Parse(manifest);
            var bundle=new WindowsOcrBundle(Path.Combine(AppContext.BaseDirectory,"ocr"),manifest);string models=bundle.InstallModels(root);bundle.CheckModels(models);
            using var active=new SaveCoordinator(EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret),TimeProvider.System);var note=active.Workspace.CreateNote();note.Text="원본 합성 이미지";Require(await active.PrepareAttachmentsAsync(),"Linked OCR genuine encrypted root");Guid id=active.AttachBytes(note,png,"synthetic-linked.png","image/png",note.EditVersion);Require(await active.SaveAsync(),"Linked OCR authenticated source baseline");string baseline=JsonSerializer.Serialize(active.Workspace.Capture());
            var source=new OcrSourceDescriptor(id,active.Workspace.Capture().AttachmentRootId,Convert.ToHexStringLower(SHA256.HashData(png)),png.Length);
            // Deliberately synthetic values: this fixture neither issues nor tests coordinator publication grants.
            var stamp=new OcrGrantStamp(Guid.NewGuid(),Guid.NewGuid(),note.Id,note.EditVersion,active.AttachmentPreviewEpoch);
            var observed=new List<byte[]>();AttachmentOcrInput Capture(CancellationToken token=default)=>AttachmentOcrInput.Capture(active.CreateAttachmentReadLease(note,id,note.EditVersion),source,stamp,token,observed.Add);
            string expectedPpm=IndependentLinkedPpmHash(png,out int sourceWidth,out int sourceHeight,out int previewWidth,out int previewHeight);Require(sourceWidth==1000&&sourceHeight==450,"Known synthetic OCR dimensions");
            using(var input=Capture())
            {
                running=bundle.StartLinked(input,models);Require(observed.All(bytes=>bytes.All(value=>value==0)),"Actual linked start consumes and zeroes source/decode/raster buffers before process completion");
                string[] verifiedPaths=[Path.Combine(bundle.Root,"tesseract.exe"),Path.Combine(models,"kor.traineddata"),Path.Combine(models,"eng.traineddata"),Path.Combine(models,"Apache2.txt")];
                bool heldObserved=false;
                if(!running.Settled.IsCompleted)
                {
                    foreach(string path in verifiedPaths){if(running.Settled.IsCompleted)break;try{using var unexpected=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);Require(running.Settled.IsCompleted,"Verified file handle remains held before settlement: "+Path.GetFileName(path));}catch(IOException){heldObserved=true;}}
                }
                result=await running.Completion.WaitAsync(TimeSpan.FromSeconds(30));await running.Settled.WaitAsync(TimeSpan.FromSeconds(30));
                foreach(string path in verifiedPaths){using var released=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);Require(released.Length>0,"Verified component handles released after actual settlement");}
                Require(heldObserved,"Observe actual verified file retention while native OCR remains unsettled");
                var facts=result.Provenance;facts.Validate();Require(result.Source==source&&result.Stamp==stamp&&facts.SourceWidth==sourceWidth&&facts.SourceHeight==sourceHeight&&facts.PreviewWidth==previewWidth&&facts.PreviewHeight==previewHeight&&facts.PpmSha256==expectedPpm,"Actual linked provenance preserves source descriptor/synthetic stamp and independently computed exact PPM digest/dimensions");
                Require(facts.EngineSha256==manifestJson.RootElement.GetProperty("engineSha256").GetString()!.ToLowerInvariant()&&facts.EngineSha256==FileSha(Path.Combine(bundle.Root,"tesseract.exe"))&&facts.KorModelSha256==FileSha(Path.Combine(models,"kor.traineddata"))&&facts.EngModelSha256==FileSha(Path.Combine(models,"eng.traineddata")),"Actual verified engine/model fingerprints match fixed manifest and installed model bytes");
                Require(manifestJson.RootElement.GetProperty("tesseractCommit").GetString()==OcrProvenanceFacts.TesseractCommit&&manifestJson.RootElement.GetProperty("leptonicaCommit").GetString()==OcrProvenanceFacts.LeptonicaCommit&&manifestJson.RootElement.GetProperty("modelsCommit").GetString()==OcrProvenanceFacts.ModelsCommit&&OcrProvenanceFacts.TransformProfile=="png-nearest-1024-straight-alpha-white-ppm-v1"&&OcrProvenanceFacts.Languages=="kor+eng"&&OcrProvenanceFacts.Oem==1&&OcrProvenanceFacts.Psm==6,"Exact pinned commits/closed transform/language/OEM/PSM policy");
                string text=result.Text;Require(OcrComparable(text)==OcrComparable(OcrExpected),"Actual linked native Korean/English recognition matches existing synthetic fixture");byte[] textBytes=Encoding.UTF8.GetBytes(text);try{Require(result.TextSha256==Convert.ToHexStringLower(SHA256.HashData(textBytes)),"Actual recognized exact UTF8 TextSHA including line endings");}finally{CryptographicOperations.ZeroMemory(textBytes);}
                byte[] owned=Field<byte[]>(result,"utf8");Require(result.ConsumeUtf8(bytes=>Require(Encoding.UTF8.GetString(bytes)==text,"Actual result single owned UTF8 read"))&&owned.All(value=>value==0),"Actual native owned result consumes once and zeroes bytes");try{result.ConsumeUtf8(_=>{});throw new Exception("Native linked result consumed twice");}catch(InvalidOperationException){}result.Dispose();result=null;running=null;
            }
            Require(JsonSerializer.Serialize(active.Workspace.Capture())==baseline,"Linked runtime primitive changes no body/metadata/schema/attachment/history and applies no candidate");
            observed.Clear();using(var canceledInput=Capture())using(var cancel=new CancellationTokenSource())
            {cancel.Cancel();try{bundle.StartLinked(canceledInput,models,cancel.Token);throw new Exception("Pre-canceled linked native input accepted");}catch(OperationCanceledException){}Require(observed.All(bytes=>bytes.All(value=>value==0)),"Pre-start cancellation disposes actual authenticated linked raster");}
            observed.Clear();using(var failedInput=Capture())
            {try{bundle.StartLinked(failedInput,Path.Combine(root,"missing-synthetic-models"));throw new Exception("Missing models accepted");}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}Require(observed.All(bytes=>bytes.All(value=>value==0)),"Actual model verification failure disposes authenticated input before native candidate");}
            observed.Clear();using(var cancel=new CancellationTokenSource())using(var lockInput=Capture())
            {
                running=bundle.StartLinked(lockInput,models,cancel.Token);cancel.Cancel();Task locking=active.LockAsync();Require(active.KeysReleased,"Lock releases keys independently of already-detached native OCR cleanup");
                try{result=await running.Completion.WaitAsync(TimeSpan.FromSeconds(30));}catch(OperationCanceledException){}catch(InvalidDataException){}finally{if(result is not null){byte[] late=Field<byte[]>(result,"utf8");result.Dispose();Require(late.All(value=>value==0),"Any native result winning cancellation race is discarded and zeroed without application");result=null;}}
                await running.Settled.WaitAsync(TimeSpan.FromSeconds(30));running=null;await locking;Require(observed.All(bytes=>bytes.All(value=>value==0)),"Canceled native process settles owned raster cleanup with source session keys released");
            }
            Console.WriteLine("PASS: actual fixed native linked OCR provenance/source/PPM/TextSHA, verified handle settlement, exact owned cleanup; synthetic stamp only, no issued grant/schema11/apply");
        }
        finally
        {
            bool actuallySettled=running is null;
            try
            {
                try
                {
                    result?.Dispose();
                    if(running is not null)
                    {
                        // Assertion timeouts revoke success, never transfer or abandon late result ownership.
                        try{using var late=await running.Completion;}catch(OperationCanceledException){}catch(InvalidDataException){}
                    }
                }
                finally
                {
                    if(running is not null){await running.Settled;actuallySettled=true;}
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(png);
                // Failed settlement is fail-closed: preserve the synthetic directory rather than racing native handles.
                if(actuallySettled)Directory.Delete(root,true);
            }
        }
    }
    private static string FileSha(string path){using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream));}
    private static string IndependentLinkedPpmHash(byte[] png,out int sourceWidth,out int sourceHeight,out int previewWidth,out int previewHeight)
    {
        // Independent WPF decoder and test-owned nearest/white formula; never use production PpmOcrInput.
        sourceWidth=checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16,4)));sourceHeight=checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20,4)));double scale=Math.Min(1.0,1024.0/Math.Max(sourceWidth,sourceHeight));previewWidth=Math.Max(1,(int)Math.Floor(sourceWidth*scale));previewHeight=Math.Max(1,(int)Math.Floor(sourceHeight*scale));
        using var stream=new MemoryStream(png,false);var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat|BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad);var frame=decoder.Frames.Single();Require(frame.PixelWidth==sourceWidth&&frame.PixelHeight==sourceHeight,"Independent PNG source dimensions");var bitmap=new FormatConvertedBitmap(frame,PixelFormats.Bgra32,null,0);byte[] pixels=new byte[checked(sourceWidth*sourceHeight*4)],row=new byte[checked(previewWidth*3)];
        try
        {
            bitmap.CopyPixels(pixels,checked(sourceWidth*4),0);using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);digest.AppendData(Encoding.ASCII.GetBytes("P6\n"+previewWidth.ToString(CultureInfo.InvariantCulture)+" "+previewHeight.ToString(CultureInfo.InvariantCulture)+"\n255\n"));
            for(int y=0;y<previewHeight;y++){int fromY=checked((int)((long)y*sourceHeight/previewHeight));for(int x=0;x<previewWidth;x++){int from=checked((fromY*sourceWidth+(int)((long)x*sourceWidth/previewWidth))*4),alpha=pixels[from+3];for(int channel=0;channel<3;channel++)row[x*3+channel]=(byte)((pixels[from+2-channel]*alpha+255*(255-alpha)+127)/255);}digest.AppendData(row);}return Convert.ToHexStringLower(digest.GetHashAndReset());
        }
        finally{CryptographicOperations.ZeroMemory(pixels);CryptographicOperations.ZeroMemory(row);}
    }
}
