using System.Diagnostics;
using System.Security.Cryptography;
namespace MemoApp.Core.Transfer;
// Internal feasibility harness for the already installed Linux engine. Never a Windows product engine selection.
internal static class LinuxTesseractProbe
{
 internal static async Task<OwnedOcrText> RunAsync(OwnedBgraRaster raster,string modelDirectory,CancellationToken token=default)
 {
  ArgumentNullException.ThrowIfNull(raster);
  try
  {
   if(!OperatingSystem.IsLinux()||OperatingSystem.IsAndroid())throw new PlatformNotSupportedException("Linux OCR probe only");
   token.ThrowIfCancellationRequested();string models=LocalFilePath.Resolve(modelDirectory);LocalFilePath.CheckDataRoot(models);
   using var executable=Verify("/usr/bin/tesseract",51536,"74b76a5b994adcc18be362bf64ed5118082b2401d5d9a35242e92fc4c62e2b4b",token);
   using var korean=Verify(Path.Combine(models,"kor.traineddata"),1677415,"6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2",token);
   using var english=Verify(Path.Combine(models,"eng.traineddata"),4113088,"7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2",token);
   var start=new ProcessStartInfo("/usr/bin/tesseract"){WorkingDirectory=models};
   foreach(string argument in new[]{"stdin","stdout","--tessdata-dir",models,"-l","kor+eng","--oem","1","--psm","6"})start.ArgumentList.Add(argument);
   start.Environment.Clear();start.Environment.Add("LANG","C.UTF-8");start.Environment.Add("LC_ALL","C.UTF-8");start.Environment.Add("OMP_THREAD_LIMIT","1");
   var input=PpmOcrInput.Capture(raster,token); // Ownership transfers to the process until all pipe work ends.
   return await LocalOcrProcess.RunPreparedAsync(input,start,TimeSpan.FromSeconds(20),token);
  }
  finally{raster.Dispose();}
 }
 private static FileStream Verify(string path,long length,string hash,CancellationToken token)
 {
  path=LocalFilePath.Resolve(path);LocalFilePath.CheckAncestors(path,true);FileStream? source=null;byte[] buffer=new byte[65536];bool returned=false;
  try
  {
   source=LocalRegularFile.Open(path);if(source.Length!=length)throw new InvalidDataException("OCR public dependency length");
   using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long remaining=length;
   while(remaining>0){token.ThrowIfCancellationRequested();int read=source.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(read==0)throw new InvalidDataException("OCR public dependency truncated");digest.AppendData(buffer,0,read);remaining-=read;}
   if(source.ReadByte()!=-1||Convert.ToHexStringLower(digest.GetHashAndReset())!=hash)throw new InvalidDataException("OCR public dependency hash");
   token.ThrowIfCancellationRequested();returned=true;return source;
  }
  finally{CryptographicOperations.ZeroMemory(buffer);if(!returned)source?.Dispose();}
 }
}
