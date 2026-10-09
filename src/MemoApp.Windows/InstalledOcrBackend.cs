using System.IO;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
namespace MemoApp.Windows;
internal class InstalledOcrBackend
{
 private readonly WindowsOcrBundle? bundle;
 internal bool Available=>bundle is not null;
 internal InstalledOcrBackend()
 {
  using var stream=typeof(InstalledOcrBackend).Assembly.GetManifestResourceStream("MemoApp.OcrManifest");if(stream is null)return;
  byte[] manifest=new byte[16385];int count=0;
  while(count<manifest.Length){int read=stream.Read(manifest,count,manifest.Length-count);if(read==0)break;count+=read;}
  if(count>16384)throw new InvalidDataException("OCR manifest limit");
  bundle=new(Path.Combine(AppContext.BaseDirectory,"ocr"),manifest.AsSpan(0,count).ToArray());
 }
 internal virtual string Install(string parent,CancellationToken token)=>bundle!.InstallModels(parent,token);
 internal virtual void Check(string directory,CancellationToken token)=>bundle!.CheckModels(directory,token);
 internal virtual OcrOperation Start(AttachmentReadLease lease,string models,CancellationToken token)
 {using var raster=AttachmentPngPreview.Decode(lease,token);return bundle!.Start(raster,models,token);}
}
