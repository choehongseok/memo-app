using System.Diagnostics;
using System.Security.Cryptography;
namespace MemoApp.Core.Transfer;
internal sealed partial class WindowsOcrBundle
{
    internal LinkedOcrOperation StartLinked(AttachmentOcrInput input,string models,CancellationToken token=default)=>OwnedOcrDerivation.StartVerified(this,input,models,token);
    internal (OcrOperation Operation,OcrProvenanceFacts Facts) StartVerifiedLinked(AttachmentOcrInput source,string models,CancellationToken token)
    {
        var files=new List<FileStream>();PreparedTextExport? input=null;OwnedBgraRaster? raster=null;
        try
        {
            token.ThrowIfCancellationRequested();if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows OCR engine");
            source.Source.Validate();source.Stamp.Validate();LocalFilePath.CheckDataRoot(Root);models=LocalFilePath.Resolve(models);LocalFilePath.CheckDataRoot(models);CheckNativeRoot(token);
            string engine=Path.Combine(Root,"tesseract.exe");files.Add(Verify(engine,engineLength,engineHash,token));foreach(var pin in Models)files.Add(Verify(Path.Combine(models,pin.Name),pin.Length,pin.Hash,token));
            var start=new ProcessStartInfo(engine){WorkingDirectory=models};foreach(string arg in new[]{"stdin","stdout","--tessdata-dir",models,"-l",OcrProvenanceFacts.Languages,"--oem","1","--psm","6"})start.ArgumentList.Add(arg);
            string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);start.Environment.Clear();start.Environment.Add("SystemRoot",windows);start.Environment.Add("WINDIR",windows);start.Environment.Add("OMP_THREAD_LIMIT","1");
            raster=source.TakeRaster();input=PpmOcrInput.Capture(raster,token);raster=null;
            var facts=new OcrProvenanceFacts(engineHash.ToLowerInvariant(),Models.Single(p=>p.Name=="kor.traineddata").Hash,Models.Single(p=>p.Name=="eng.traineddata").Hash,source.SourceWidth,source.SourceHeight,source.PreviewWidth,source.PreviewHeight,Convert.ToHexStringLower(SHA256.HashData(input.Bytes)));facts.Validate();token.ThrowIfCancellationRequested();
            var operation=LocalOcrProcess.StartPrepared(input,start,TimeSpan.FromSeconds(20),token);input=null;
            return (new(operation.Completion,Settle(operation.Settled,files)),facts);
        }
        catch{input?.Dispose();raster?.Dispose();foreach(var file in files)file.Dispose();throw;}
    }
}
