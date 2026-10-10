using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
namespace MemoApp.Core.Transfer;
// Expected manifest comes from the product assembly resource, never an editable runtime file.
internal sealed partial class WindowsOcrBundle
{
 internal readonly string Root;private readonly string engineHash,archiveHash;private readonly long engineLength,archiveLength;
 internal static readonly (string Name,int Length,string Hash)[] Models=[("kor.traineddata",1677415,"6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2"),("eng.traineddata",4113088,"7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2"),("Apache2.txt",11358,"cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30")];
 internal WindowsOcrBundle(string root,byte[] trustedAssemblyManifest)
 {
  if(trustedAssemblyManifest.Length>16384)throw new InvalidDataException("OCR manifest limit");Root=LocalFilePath.Resolve(root);
  using var json=JsonDocument.Parse(trustedAssemblyManifest,new(){MaxDepth=4});var m=json.RootElement;
  if(m.GetProperty("tesseractCommit").GetString()!="db0ec62f81b0737fbbe184d8fea40af5738f8eef"||m.GetProperty("leptonicaCommit").GetString()!="13275a278eb55b5746e33f95fbf5a2c8f604b3ab"||m.GetProperty("modelsCommit").GetString()!="87416418657359cb625c412a48b6e1d6d41c29bd")throw new InvalidDataException("OCR source provenance");
  var dll=m.GetProperty("peDependencies").EnumerateArray().Select(v=>v.GetString()).ToArray();if(dll.Length!=1||dll[0]!="KERNEL32.dll")throw new InvalidDataException("OCR native dependencies");
  engineLength=m.GetProperty("engineLength").GetInt64();archiveLength=m.GetProperty("modelsArchiveLength").GetInt64();engineHash=m.GetProperty("engineSha256").GetString()!;archiveHash=m.GetProperty("modelsArchiveSha256").GetString()!;
  if(engineLength is <1 or >8388608||archiveLength is <1 or >8388608||!Hash(engineHash)||!Hash(archiveHash))throw new InvalidDataException("OCR component bounds");
 }
 private static bool Hash(string value)=>value is {Length:64}&&value.All(char.IsAsciiHexDigit);
 private static FileStream Verify(string path,long length,string hash,CancellationToken token)
 {
  path=LocalFilePath.Resolve(path);LocalFilePath.CheckAncestors(path,true);var stream=LocalRegularFile.Open(path);byte[] buffer=new byte[65536];bool returned=false;
  try
  {
   if(stream.Length!=length)throw new InvalidDataException("OCR dependency length");using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long remaining=length;
   while(remaining>0){token.ThrowIfCancellationRequested();int count=stream.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(count==0)throw new InvalidDataException("OCR dependency truncated");digest.AppendData(buffer,0,count);remaining-=count;}
   if(stream.ReadByte()!=-1||!Convert.ToHexStringLower(digest.GetHashAndReset()).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("OCR dependency hash");stream.Position=0;returned=true;return stream;
  }
  finally{CryptographicOperations.ZeroMemory(buffer);if(!returned)stream.Dispose();}
 }
 internal void CheckNativeRoot(CancellationToken token=default)
 {
  LocalFilePath.CheckDataRoot(Root);int count=0;
  foreach(string entry in Directory.EnumerateFileSystemEntries(Root))
  {
   token.ThrowIfCancellationRequested();if(++count>4||!new[]{"tesseract.exe","models.zip","Tesseract-Apache2.txt","Leptonica.txt"}.Contains(Path.GetFileName(entry),StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Unexpected OCR component");
   LocalFilePath.CheckAncestors(entry,true);
  }
  if(count!=4)throw new InvalidDataException("OCR components missing");
  using var apache=Verify(Path.Combine(Root,"Tesseract-Apache2.txt"),11358,"cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30",token);
  using var leptonica=Verify(Path.Combine(Root,"Leptonica.txt"),1521,"87829abb5bbb00b55a107365da89e9a33f86c4250169e5a1e5588505be7d5806",token);
 }
 internal void CheckModels(string directory,CancellationToken token=default)
 {
  directory=LocalFilePath.Resolve(directory);LocalFilePath.CheckDataRoot(directory);foreach(var pin in Models){using var file=Verify(Path.Combine(directory,pin.Name),pin.Length,pin.Hash,token);}
 }
 internal string InstallModels(string parent,CancellationToken token=default)
 {
  CheckNativeRoot(token);parent=LocalFilePath.Resolve(parent);LocalFilePath.CheckDataRoot(parent);if(!Directory.Exists(parent))throw new IOException("Choose an existing model parent");
  using var source=Verify(Path.Combine(Root,"models.zip"),archiveLength,archiveHash,token);using var zip=new ZipArchive(source,ZipArchiveMode.Read,true);
  if(zip.Entries.Count!=3||zip.Entries.Select(e=>e.FullName).Distinct(StringComparer.Ordinal).Count()!=3||zip.Entries.Any(e=>!Models.Any(p=>p.Name==e.FullName)))throw new InvalidDataException("OCR archive entries");
  string directory=Path.Combine(parent,"memo-ocr-models-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);LocalFilePath.CheckDataRoot(directory);
  foreach(var pin in Models)
  {
   token.ThrowIfCancellationRequested();var entry=zip.GetEntry(pin.Name)!;if(entry.Length!=pin.Length)throw new InvalidDataException("OCR model size");byte[] bytes=new byte[pin.Length];
   try
   {
    using var input=entry.Open();input.ReadExactly(bytes);if(input.ReadByte()!=-1||Convert.ToHexStringLower(SHA256.HashData(bytes))!=pin.Hash)throw new InvalidDataException("OCR model content");
    token.ThrowIfCancellationRequested();using var output=new FileStream(Path.Combine(directory,pin.Name),FileMode.CreateNew,FileAccess.Write,FileShare.None);output.Write(bytes);output.Flush(true);
   }
   finally{CryptographicOperations.ZeroMemory(bytes);}
  }
  CheckModels(directory,token);return directory; // Only public models; canceled partial folders are preserved and reported.
 }
 internal OcrOperation Start(OwnedBgraRaster raster,string models,CancellationToken token)
 {
  var files=new List<FileStream>();PreparedTextExport? input=null;
  try
  {
   if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows OCR engine");LocalFilePath.CheckDataRoot(Root);models=LocalFilePath.Resolve(models);LocalFilePath.CheckDataRoot(models);
   CheckNativeRoot(token);
   string engine=Path.Combine(Root,"tesseract.exe");files.Add(Verify(engine,engineLength,engineHash,token));foreach(var pin in Models)files.Add(Verify(Path.Combine(models,pin.Name),pin.Length,pin.Hash,token));
   var start=new ProcessStartInfo(engine){WorkingDirectory=models};foreach(string arg in new[]{"stdin","stdout","--tessdata-dir",models,"-l","kor+eng","--oem","1","--psm","6"})start.ArgumentList.Add(arg);
   string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);start.Environment.Clear();start.Environment.Add("SystemRoot",windows);start.Environment.Add("WINDIR",windows);start.Environment.Add("OMP_THREAD_LIMIT","1");
   input=PpmOcrInput.Capture(raster,token);var operation=LocalOcrProcess.StartPrepared(input,start,TimeSpan.FromSeconds(20),token);input=null;
   return new(operation.Completion,Settle(operation.Settled,files));
  }
  catch{input?.Dispose();foreach(var file in files)file.Dispose();throw;}
  finally{raster.Dispose();}
 }
 private static async Task Settle(Task settled,List<FileStream> files)
 {try{await settled.ConfigureAwait(false);}finally{foreach(var file in files)file.Dispose();}}
}
