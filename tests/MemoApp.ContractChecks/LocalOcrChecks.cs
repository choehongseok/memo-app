using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using MemoApp.Core.Transfer;
namespace MemoApp.Core;
internal static class LocalOcrChecks
{
 internal static void Input()
 {
  byte[] pixels=[0,0,255,255,0,0,0,0,40,60,80,128];
  using var raster=new OwnedBgraRaster(3,1,pixels);
  byte[] owned;
  using(var prepared=PpmOcrInput.Capture(raster))
  {
   owned=prepared.Bytes;byte[] header=Encoding.ASCII.GetBytes("P6\n3 1\n255\n");
   VaultChecks.Require(owned.AsSpan(0,header.Length).SequenceEqual(header),"OCR fixed inert PPM header");
   VaultChecks.Require(owned.AsSpan(header.Length).SequenceEqual(new byte[]{255,0,0,255,255,255,167,157,147}),"OCR RGB order and white alpha composite");
   VaultChecks.Require(pixels.All(b=>b==0),"OCR raster source consumed and cleared");
  }
  VaultChecks.Require(owned.All(b=>b==0),"OCR owned PPM zero on disposal");
  foreach(var shape in new[]{(0,1,4),(1025,1,4),(1,1025,4),(1,1,5),(1,1,0)})
  {
   byte[] bad=Enumerable.Repeat((byte)42,shape.Item3).ToArray();using var invalid=new OwnedBgraRaster(shape.Item1,shape.Item2,bad);
   VaultChecks.ExpectFailure(()=>PpmOcrInput.Capture(invalid),"OCR malformed raster rejected");VaultChecks.Require(bad.All(b=>b==0),"Rejected OCR raster disposed without child");
  }
  using var cancel=new CancellationTokenSource();cancel.Cancel();byte[] raw=[42,42,42,255];using var canceled=new OwnedBgraRaster(1,1,raw);
  bool refused=false;try{using var never=PpmOcrInput.Capture(canceled,cancel.Token);}catch(OperationCanceledException){refused=true;}
  VaultChecks.Require(refused&&raw.All(b=>b==0),"OCR pre-cancel clears raster without output");
  string root=Path.Combine(Path.GetTempPath(),"memo-ocr-root-boundary-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   // Synthetic metadata probes path refusal only; it never executes/authenticates a native binary.
   string manifest=System.Text.Json.JsonSerializer.Serialize(new{tesseractCommit="db0ec62f81b0737fbbe184d8fea40af5738f8eef",leptonicaCommit="13275a278eb55b5746e33f95fbf5a2c8f604b3ab",modelsCommit="87416418657359cb625c412a48b6e1d6d41c29bd",engineLength=1,engineSha256=new string('0',64),modelsArchiveLength=1,modelsArchiveSha256=new string('0',64),peDependencies=new[]{"KERNEL32.dll"}});
   var bundle=new WindowsOcrBundle(root,Encoding.UTF8.GetBytes(manifest));
   Directory.CreateDirectory(Path.Combine(root,"unexpected-child"));VaultChecks.ExpectFailure(()=>bundle.CheckNativeRoot(),"OCR refuses additional directory without traversing children");Directory.Delete(Path.Combine(root,"unexpected-child"));
   Directory.CreateDirectory(Path.Combine(root,"tesseract.exe"));VaultChecks.ExpectFailure(()=>bundle.CheckNativeRoot(),"OCR fixed component name cannot be a directory");Directory.Delete(Path.Combine(root,"tesseract.exe"));
   if(!OperatingSystem.IsWindows())
   {Directory.CreateSymbolicLink(Path.Combine(root,"unexpected-link"),Path.GetTempPath());VaultChecks.ExpectFailure(()=>bundle.CheckNativeRoot(),"OCR refuses linked directory without following it");Directory.Delete(Path.Combine(root,"unexpected-link"));}
  }
  finally{Directory.Delete(root,true);}
  Console.WriteLine("PASS: bounded owned OCR PPM input RGB/alpha/source zero/refusal/cancel; engine validation separate");
 }

 internal static async Task ProcessChecks()
 {
  byte[] pixels=[0,0,0,255];using var raster=new OwnedBgraRaster(1,1,pixels);
  using var input=PpmOcrInput.Capture(raster);
  using var result=await LocalOcrProcess.RunPreparedAsync(input,Worker("good"),TimeSpan.FromSeconds(5));
  VaultChecks.Require(result.Text=="합성 OCR ABC 123\n"&&input.IsDisposed,"Actual child stdio bounded UTF8 result and input cleanup");
  foreach(string mode in new[]{"invalid","overflow","stderr","exit"})
  {
   using var source=PpmOcrInput.Capture(new OwnedBgraRaster(1,1,new byte[]{1,2,3,255}));bool refused=false;
   try{using var invalid=await LocalOcrProcess.RunPreparedAsync(source,Worker(mode),TimeSpan.FromSeconds(5));}catch(InvalidDataException){refused=true;}
   VaultChecks.Require(refused&&source.IsDisposed,"Actual child failure/output refusal and input cleanup "+mode);
  }
  using var blocked=PpmOcrInput.Capture(new OwnedBgraRaster(1024,1024,new byte[1024*1024*4]));using var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(300));bool canceled=false;var elapsed=Stopwatch.StartNew();
  try{using var impossible=await LocalOcrProcess.RunPreparedAsync(blocked,Worker("blocked"),TimeSpan.FromSeconds(5),cancel.Token);}catch(OperationCanceledException){canceled=true;}
  VaultChecks.Require(canceled&&elapsed.Elapsed<TimeSpan.FromSeconds(4)&&blocked.IsDisposed,"Stdin unconsumed/pipe-held child canceled with bounded cleanup");
  using var deadline=PpmOcrInput.Capture(new OwnedBgraRaster(1,1,new byte[]{1,2,3,255}));bool timed=false;
  try{using var impossible=await LocalOcrProcess.RunPreparedAsync(deadline,Worker("blocked"),TimeSpan.FromMilliseconds(200));}catch(TimeoutException){timed=true;}
  VaultChecks.Require(timed&&deadline.IsDisposed,"Actual child overall timeout and cleanup");
  using var settledInput=PpmOcrInput.Capture(new OwnedBgraRaster(1,1,new byte[]{1,2,3,255}));
  var settled=LocalOcrProcess.StartPrepared(settledInput,Worker("good"),TimeSpan.FromSeconds(5));
  using(var text=await settled.Completion)VaultChecks.Require(text.Text=="합성 OCR ABC 123\n","Settlement API retains owned actual child result");
  await settled.Settled.WaitAsync(TimeSpan.FromSeconds(4));VaultChecks.Require(settledInput.IsDisposed,"Settlement waits for input cleanup");
  using var failedInput=PpmOcrInput.Capture(new OwnedBgraRaster(1,1,new byte[]{1,2,3,255}));
  var failed=LocalOcrProcess.StartPrepared(failedInput,Worker("blocked"),TimeSpan.FromMilliseconds(200));bool refusedDeadline=false;
  using var simultaneousInput=PpmOcrInput.Capture(new OwnedBgraRaster(1,1,new byte[]{1,2,3,255}));
  var simultaneous=LocalOcrProcess.StartPrepared(simultaneousInput,Worker("good"),TimeSpan.FromSeconds(5));bool occupied=false;
  try{using var text=await simultaneous.Completion;}catch(InvalidOperationException){occupied=true;}
  await simultaneous.Settled;VaultChecks.Require(occupied&&simultaneousInput.IsDisposed,"Concurrent process refused before starting; refused input ownership settled");
  try{using var text=await failed.Completion;}catch(TimeoutException){refusedDeadline=true;}
  await failed.Settled.WaitAsync(TimeSpan.FromSeconds(4));VaultChecks.Require(refusedDeadline&&failedInput.IsDisposed,"Failure result and child ownership settlement are distinct and both finish");
  using var inheritedInput=PpmOcrInput.Capture(new OwnedBgraRaster(1,1,new byte[]{1,2,3,255}));
  var inherited=LocalOcrProcess.StartPrepared(inheritedInput,Worker("inherit-pipes"),TimeSpan.FromMilliseconds(400));bool inheritedDeadline=false;
  try{using var text=await inherited.Completion;}catch(TimeoutException){inheritedDeadline=true;}
  bool observedLate=!inherited.Settled.IsCompleted;
  if(observedLate)
  {
   VaultChecks.Require(!inheritedInput.IsDisposed,"Actual inherited late pipe retains input before settlement");
   using var lateInput=PpmOcrInput.Capture(new OwnedBgraRaster(1,1,new byte[]{1,2,3,255}));
   var late=LocalOcrProcess.StartPrepared(lateInput,Worker("good"),TimeSpan.FromSeconds(5));bool refusedLate=false;
   try{using var text=await late.Completion;}catch(InvalidOperationException){refusedLate=true;}
   await late.Settled;VaultChecks.Require(refusedLate&&lateInput.IsDisposed,"Actual late pipe retains process admission");
  }
  await inherited.Settled.WaitAsync(TimeSpan.FromSeconds(8));VaultChecks.Require(inheritedDeadline&&inheritedInput.IsDisposed,"Inherited pipe timeout eventually clears actual ownership");
  Console.WriteLine("OCR inherited-pipe fixture: late cleanup transfer observed="+observedLate+"; OS closure can settle earlier");
  Console.WriteLine("PASS: actual OCR child stdio/good/invalid/overflow/stderr/nonzero/cancel/deadline and owned cleanup; synthetic child is a boundary fixture, not OCR accuracy");
 }
 private static ProcessStartInfo Worker(string mode)
 {
  var start=new ProcessStartInfo(Environment.ProcessPath!);if(Path.GetFileNameWithoutExtension(start.FileName)=="dotnet")start.ArgumentList.Add(typeof(LocalOcrChecks).Assembly.Location);
  start.ArgumentList.Add("--ocr-fixture-worker");start.ArgumentList.Add(mode);return start;
 }
 internal static bool TryWorker(string[] args)
 {
  if(args.Length!=2||args[0]!="--ocr-fixture-worker")return false;string mode=args[1];
  if(mode=="blocked"){Thread.Sleep(30000);return true;}
  if(mode=="hold-pipes"){Thread.Sleep(5000);return true;}
  using var input=Console.OpenStandardInput();input.CopyTo(Stream.Null);using var output=Console.OpenStandardOutput();
  if(mode=="inherit-pipes"){using var descendant=Process.Start(Worker("hold-pipes"));return true;}
  if(mode=="good")output.Write(Encoding.UTF8.GetBytes("합성 OCR ABC 123\n"));
  else if(mode=="invalid")output.Write(new byte[]{0xff,0xfe});
  else if(mode=="overflow")output.Write(new byte[262145]);
  else if(mode=="stderr"){using var error=Console.OpenStandardError();error.Write(new byte[4097]);}
  else if(mode=="exit")Environment.ExitCode=7;
  return true;
 }

 internal static async Task NativeProbe(string root)
 {
  VaultChecks.Require(OperatingSystem.IsWindows(),"Native OCR probe requires actual Windows");
  root=Path.GetFullPath(root);using var manifest=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"native-build.json")));
  var value=manifest.RootElement;
  VaultChecks.Require(value.GetProperty("tesseractCommit").GetString()=="db0ec62f81b0737fbbe184d8fea40af5738f8eef"&&value.GetProperty("leptonicaCommit").GetString()=="13275a278eb55b5746e33f95fbf5a2c8f604b3ab"&&value.GetProperty("modelsCommit").GetString()=="87416418657359cb625c412a48b6e1d6d41c29bd","Exact public upstream source provenance");
  VaultChecks.Require(!Directory.EnumerateFiles(root,"*.dll",SearchOption.AllDirectories).Any(),"Probe bundle contains no additional DLL dependencies");
  string engine=Path.Combine(root,"tesseract.exe");byte[] binary=File.ReadAllBytes(engine);
  VaultChecks.Require(binary.Length==value.GetProperty("engineLength").GetInt64()&&Convert.ToHexStringLower(SHA256.HashData(binary))==value.GetProperty("engineSha256").GetString(),"CI source-built engine hash; no production publisher trust claim");
  foreach(var pin in new[]{("kor",1677415,"6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2"),("eng",4113088,"7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2")})
  {
   byte[] model=File.ReadAllBytes(Path.Combine(root,"tessdata",pin.Item1+".traineddata"));
   VaultChecks.Require(model.Length==pin.Item2&&Convert.ToHexStringLower(SHA256.HashData(model))==pin.Item3,"Actual native probe pinned model");
  }
  byte[] png=Convert.FromBase64String(File.ReadAllText("tests/fixtures/ocr-synthetic-png.base64"));
  VaultChecks.Require(Convert.ToHexStringLower(SHA256.HashData(png))=="21f8894d43b5c5bfa13db5aaf1503ad726daf4675a01623e5c5778b0a5f89ca8","Public synthetic OCR fixture hash");
  using var raster=PngPixelDecoder.Decode(png);Array.Clear(png);
  var start=new ProcessStartInfo(engine){WorkingDirectory=root};
  foreach(string arg in new[]{"stdin","stdout","--tessdata-dir",Path.Combine(root,"tessdata"),"-l","kor+eng","--oem","1","--psm","6"})start.ArgumentList.Add(arg);
  string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);start.Environment.Clear();start.Environment.Add("SystemRoot",windows);start.Environment.Add("WINDIR",windows);start.Environment.Add("OMP_THREAD_LIMIT","1");
  var input=PpmOcrInput.Capture(raster);var elapsed=Stopwatch.StartNew();
  using var result=await LocalOcrProcess.RunPreparedAsync(input,start,TimeSpan.FromSeconds(20));
  const string expected="한글 메모 시험\n합성 자료만 사용합니다\n저장 암호 잠금 복구\nABC 123\n";
  VaultChecks.Require(string.Concat(result.Text.Where(c=>!char.IsWhiteSpace(c)))==string.Concat(expected.Where(c=>!char.IsWhiteSpace(c))),"Actual Windows native Korean/English OCR exact public synthetic result");
  Console.WriteLine("PASS: actual Windows source-built Tesseract 5.5.3/static Leptonica 1.87.0/official kor+eng/stdin-only PPM/owned UTF8; elapsed-ms="+elapsed.ElapsedMilliseconds+"; UI/runtime component trust not yet connected");
 }

 internal static async Task LinuxProbe(string root)
 {
  byte[] png=File.ReadAllBytes(Path.Combine(root,"synthetic.png"));using var raster=PngPixelDecoder.Decode(png);Array.Clear(png);
  using var result=await LinuxTesseractProbe.RunAsync(raster,Path.Combine(root,"tessdata"));
  string expected=File.ReadAllText(Path.Combine(root,"expected.txt"));
  VaultChecks.Require(string.Concat(result.Text.Where(c=>!char.IsWhiteSpace(c)))==string.Concat(expected.Where(c=>!char.IsWhiteSpace(c))),"Actual pinned Linux OCR synthetic Korean/English result");
  Console.WriteLine("PASS: actual pinned Linux Tesseract/stdin PPM/official kor+eng/source raster consumed/owned bounded UTF8; Windows engine/UI integration not verified");
 }
}
