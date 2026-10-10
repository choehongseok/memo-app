using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
 private sealed record CorpusPng(string Id,byte[] Bytes,int Width,int Height,int PreviewWidth,int PreviewHeight);
 private static async Task OcrSyntheticCorpusRun()
 {
  CorpusScalarOracles();
  string root=Path.Combine(Path.GetTempPath(),"memo-ocr-corpus-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  byte[] secret=EncryptedVault.GenerateRecoverySecret();var owned=new List<byte[]>();SaveCoordinator? owner=null;
  bool actualSettled=true,ownerDisposed=false;byte[]? cipher=null;Exception? primary=null;
  try
  {
   byte[] fixture=CorpusOwn(owned,CorpusReadFile("tests/fixtures/ocr-synthetic-png.base64",40000));
   byte[] original=CorpusOwn(owned,Convert.FromBase64String(Encoding.ASCII.GetString(fixture)));
   Require(original.Length==25955&&Convert.ToHexStringLower(SHA256.HashData(original))=="21f8894d43b5c5bfa13db5aaf1503ad726daf4675a01623e5c5778b0a5f89ca8","Corpus exact existing public synthetic PNG");
   byte[] reference=CorpusDecode(original,1000,450,owned);
   var variants=new List<CorpusPng>{new("original",original,1000,450,1000,450)};
   foreach(string id in new[]{"gray","twice","low-contrast"})
   {
    int width=id=="twice"?2000:1000,height=id=="twice"?900:450;
    byte[] png=CorpusEncode(id,reference,1000,450,owned);
    byte[] decoded=CorpusDecode(png,width,height,owned);
    try
    {
     CorpusInspectEncoding(png,id,reference,1000,450);
     Require(CorpusPixelsMatch(id,reference,1000,450,decoded,width,height),"Independent full WPF pixels match declared corpus transform: "+id);
     decoded[0]^=1;Require(!CorpusPixelsMatch(id,reference,1000,450,decoded,width,height),"Corpus oracle detects an actual changed decoded pixel");decoded[0]^=1;
    }
    finally{CryptographicOperations.ZeroMemory(decoded);}
    variants.Add(new(id,png,width,height,id=="twice"?1024:1000,id=="twice"?460:450));
   }
   CryptographicOperations.ZeroMemory(reference);
   byte[] manifest=new byte[16385];owned.Add(manifest);
   using(var source=typeof(AttachmentPanel).Assembly.GetManifestResourceStream("MemoApp.OcrManifest")??throw new Exception("Fixed OCR manifest missing"))
   {
    int count=0,read;while(count<manifest.Length&&(read=source.Read(manifest,count,manifest.Length-count))!=0)count+=read;
    Require(count is >0 and <=16384&&source.ReadByte()==-1,"Corpus trusted manifest bounded before JSON");manifest=CorpusOwn(owned,manifest.AsSpan(0,count).ToArray());
   }
   using var json=JsonDocument.Parse(manifest);var bundle=new WindowsOcrBundle(Path.Combine(AppContext.BaseDirectory,"ocr"),manifest);
   string models=bundle.InstallModels(root);bundle.CheckModels(models);
   owner=new(EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret),TimeProvider.System);
   var note=owner.Workspace.CreateNote();note.Title="Public synthetic OCR corpus";note.Text="Original public source retained";owner.Workspace.ConvertMode(note,"rich",true);
   Require(note.Document is not null,"Corpus retains a genuine rich source document reference");
   Require(await owner.PrepareAttachmentsAsync(),"Corpus genuine encrypted source root");
   var ids=new List<Guid>();foreach(var variant in variants)ids.Add(owner.AttachBytes(note,variant.Bytes,variant.Id+".png","image/png",note.EditVersion));
   Require(await owner.SaveAsync()&&!owner.IsDirty,"Corpus committed clean invariant basis");
   string baseline=JsonSerializer.Serialize(owner.Workspace.Capture());long version=note.EditVersion,contentVersion=note.ContentVersion,epoch=owner.AttachmentPreviewEpoch;var modified=note.ModifiedAt;var document=note.Document;
   cipher=CorpusOwn(owned,CorpusReadFile(Path.Combine(root,"vault","current.vault"),16778000));
   for(int i=0;i<variants.Count;i++)
   {
    var variant=variants[i];actualSettled=false;
    await CorpusInference(owner,note,ids[i],variant,bundle,models,json.RootElement,()=>actualSettled=true);
    Require(actualSettled&&!owner.IsDirty&&note.EditVersion==version&&note.ContentVersion==contentVersion&&note.ModifiedAt==modified&&ReferenceEquals(note.Document,document)&&owner.AttachmentPreviewEpoch==epoch&&JsonSerializer.Serialize(owner.Workspace.Capture())==baseline,"Corpus runtime preserves complete clean source/history/objects/document/version/time/epoch: "+variant.Id);
    byte[] current=CorpusReadFile(Path.Combine(root,"vault","current.vault"),16778000);
    try{Require(cipher.SequenceEqual(current),"Corpus runtime leaves exact encrypted source ciphertext unchanged");}finally{CryptographicOperations.ZeroMemory(current);}
   }
  }
  catch(Exception error){primary=error;throw;}
  finally
  {
   var failures=new List<Exception>();
   if(owner is not null)
   {
    try{await owner.LockAsync();Require(owner.KeysReleased&&!owner.IsBusy,"Corpus owner key release and writer settlement");}catch(Exception error){failures.Add(error);}
    try{owner.Dispose();ownerDisposed=true;}catch(Exception error){failures.Add(error);}
   }
   CryptographicOperations.ZeroMemory(secret);foreach(byte[] bytes in owned)CryptographicOperations.ZeroMemory(bytes);
   if(owned.Any(bytes=>bytes.Any(value=>value!=0)))failures.Add(new Exception("Corpus fixture owned buffer cleanup failed"));
   if(actualSettled&&(owner is null||ownerDisposed))try{Directory.Delete(root,true);}catch(Exception error){failures.Add(error);}
   if(failures.Count!=0){if(primary is not null)failures.Insert(0,primary);throw new AggregateException("Corpus owner cleanup failed",failures);}
  }
 }

 // A deliberately synthetic stamp tests this verified detached runtime only, never publication grants.
 private static async Task CorpusInference(SaveCoordinator owner,NoteDraft note,Guid id,CorpusPng variant,WindowsOcrBundle bundle,string models,JsonElement manifest,Action markActuallySettled)
 {
  var allocated=new List<byte[]>();AttachmentOcrInput? input=null;LinkedOcrStarter? starter=null;LinkedOcrOperation? running=null;OwnedOcrDerivation? result=null;
  byte[]? utf8=null;using var cancel=new CancellationTokenSource();var elapsed=new Stopwatch();Exception? primary=null;
  try
  {
   string originalHash=Convert.ToHexStringLower(SHA256.HashData(variant.Bytes));
   var descriptor=new OcrSourceDescriptor(id,owner.Workspace.Capture().AttachmentRootId,originalHash,variant.Bytes.Length);
   var stamp=new OcrGrantStamp(Guid.NewGuid(),Guid.NewGuid(),note.Id,note.EditVersion,owner.AttachmentPreviewEpoch);
   string expectedPpm=IndependentLinkedPpmHash(variant.Bytes,out int sw,out int sh,out int pw,out int ph);
   Require(sw==variant.Width&&sh==variant.Height&&pw==variant.PreviewWidth&&ph==variant.PreviewHeight,"Independent corpus source and floor-preview geometry");
   elapsed.Start();input=AttachmentOcrInput.Capture(owner.CreateAttachmentReadLease(note,id,note.EditVersion),descriptor,stamp,cancel.Token,allocated.Add);
   starter=new(bundle,input,models,cancel.Token);running=starter.Operation;
   Require(!running.Completion.IsCompleted&&!running.Settled.IsCompleted,"Corpus fixed starter handles preallocated before launch");starter.Start();
   result=await running.Completion.WaitAsync(TimeSpan.FromSeconds(30));utf8=Field<byte[]>(result,"utf8");await running.Settled.WaitAsync(TimeSpan.FromSeconds(30));markActuallySettled();elapsed.Stop();
   var facts=result.Provenance;facts.Validate();
   Require(result.Source==descriptor&&result.Stamp==stamp&&facts.SourceWidth==sw&&facts.SourceHeight==sh&&facts.PreviewWidth==pw&&facts.PreviewHeight==ph&&facts.PpmSha256==expectedPpm,"Corpus genuine verified source/stamp/independent exact PPM provenance");
   Require(facts.EngineSha256==manifest.GetProperty("engineSha256").GetString()!.ToLowerInvariant()&&facts.EngineSha256==FileSha(Path.Combine(bundle.Root,"tesseract.exe"))&&facts.KorModelSha256==FileSha(Path.Combine(models,"kor.traineddata"))&&facts.EngModelSha256==FileSha(Path.Combine(models,"eng.traineddata")),"Corpus exact pinned installed engine/model bytes");
   Require(manifest.GetProperty("tesseractCommit").GetString()==OcrProvenanceFacts.TesseractCommit&&manifest.GetProperty("leptonicaCommit").GetString()==OcrProvenanceFacts.LeptonicaCommit&&manifest.GetProperty("modelsCommit").GetString()==OcrProvenanceFacts.ModelsCommit&&OcrProvenanceFacts.TransformProfile=="png-nearest-1024-straight-alpha-white-ppm-v1"&&OcrProvenanceFacts.Languages=="kor+eng"&&OcrProvenanceFacts.Oem==1&&OcrProvenanceFacts.Psm==6,"Corpus fixed source commits/transform/engine options");
   string text=result.Text;Require(text.Length is >0 and <=65536&&!text.Contains('\0'),"Corpus actual nonempty bounded linked result required");
   if(variant.Id=="original")Require(OcrComparable(text)==OcrComparable(OcrExpected),"Corpus preserves existing exact normalized baseline content oracle");
   string hash=Convert.ToHexStringLower(SHA256.HashData(utf8));
   Require(result.TextSha256==hash,"Corpus genuine exact owned UTF8 TextSHA");
   int raw=CorpusDistance(OcrExpected,text),normalized=CorpusDistance(OcrComparable(OcrExpected),OcrComparable(text));
   Require(result.ConsumeUtf8(bytes=>Require(new UTF8Encoding(false,true).GetString(bytes)==text,"Corpus owned single read preserves exact text"))&&utf8.All(value=>value==0),"Corpus actual result ownership consumes once and zeroes");
   Require(allocated.Count>0&&allocated.All(bytes=>bytes.All(value=>value==0)),"Corpus genuine source/decode/raster allocations cleared by actual settlement");
   Require(originalHash==Convert.ToHexStringLower(SHA256.HashData(variant.Bytes)),"Corpus original PNG bytes preserved by runtime");
   Console.WriteLine($"OCR_CORPUS id={variant.Id} sha256={originalHash} source={sw}x{sh} preview={pw}x{ph} rawExact={text==OcrExpected} expectedWhitespace={CorpusWhitespace(OcrExpected)} actualWhitespace={CorpusWhitespace(text)} rawScalarErrors={raw} rawExpectedScalars={OcrExpected.EnumerateRunes().Count()} normalizedScalarErrors={normalized} normalizedExpectedScalars={OcrComparable(OcrExpected).EnumerateRunes().Count()} elapsedCaptureToSettlementMs={elapsed.ElapsedMilliseconds} cpu=not-measured nativePeakWorkingSet=not-measured accuracy=diagnostic-only stamp=synthetic-no-publication");
  }
  catch(Exception error){primary=error;throw;}
  finally
  {
   var failures=new List<Exception>();
   void Attempt(Action action){try{action();}catch(Exception error){failures.Add(error);}}
   Attempt(cancel.Cancel);Attempt(()=>starter?.Dispose());Attempt(()=>result?.Dispose());
   if(running is not null)
   {
    try{using var late=await running.Completion;}catch(Exception error){failures.Add(error);}
    try{await running.Settled;markActuallySettled();}catch(Exception error){failures.Add(error);}
   }
   else markActuallySettled();
   Attempt(()=>input?.Dispose());
   if(utf8 is not null&&utf8.Any(value=>value!=0))failures.Add(new Exception("Corpus result actual UTF8 not zero"));
   if(allocated.Any(bytes=>bytes.Any(value=>value!=0)))failures.Add(new Exception("Corpus input allocation not zero"));
   if(failures.Count!=0){if(primary is not null)failures.Insert(0,primary);throw new AggregateException("Corpus actual worker cleanup failed",failures);}
  }
 }

 private static byte[] CorpusOwn(List<byte[]> owned,byte[] bytes)
 {try{owned.Add(bytes);return bytes;}catch{CryptographicOperations.ZeroMemory(bytes);throw;}}
 private static byte[] CorpusReadFile(string path,int limit)
 {
  using var stream=File.OpenRead(path);Require(stream.Length is >0&&stream.Length<=limit,"Corpus file bytes bounded before allocation");byte[] bytes=new byte[checked((int)stream.Length)];
  try{stream.ReadExactly(bytes);Require(stream.ReadByte()==-1,"Corpus exact bounded file length");return bytes;}
  catch{CryptographicOperations.ZeroMemory(bytes);throw;}
 }
 private static byte[] CorpusDecode(byte[] png,int width,int height,List<byte[]> owned)
 {
  Require(png.Length<=4194304&&width is >0 and <=4096&&height is >0 and <=4096&&(long)width*height<=4194304,"Corpus WPF decode bounded beforehand");
  using var stream=new MemoryStream(png,false);var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat|BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad);
  Require(decoder.Frames.Count==1&&decoder.Frames[0].PixelWidth==width&&decoder.Frames[0].PixelHeight==height,"Corpus WPF exact expected geometry");
  var converted=new FormatConvertedBitmap(decoder.Frames[0],PixelFormats.Bgra32,null,0);byte[] bytes=CorpusOwn(owned,new byte[checked(width*height*4)]);converted.CopyPixels(bytes,checked(width*4),0);return bytes;
 }
 private static byte CorpusWhite(byte channel,byte alpha)=>(byte)((channel*alpha+255*(255-alpha)+127)/255);
 private static byte CorpusGray(ReadOnlySpan<byte> pixel)
 {int b=CorpusWhite(pixel[0],pixel[3]),g=CorpusWhite(pixel[1],pixel[3]),r=CorpusWhite(pixel[2],pixel[3]);return (byte)((77*r+150*g+29*b+128)>>8);}
 private static byte CorpusLow(byte gray)=>(byte)(160+(gray*80+127)/255);
 private static bool CorpusPixelsMatch(string id,byte[] reference,int width,int height,byte[] decoded,int outWidth,int outHeight)
 {
  bool twice=id=="twice";if(outWidth!=width*(twice?2:1)||outHeight!=height*(twice?2:1)||decoded.Length!=checked(outWidth*outHeight*4))return false;
  for(int y=0;y<outHeight;y++)for(int x=0;x<outWidth;x++)
  {
   var source=reference.AsSpan(((y/(twice?2:1))*width+x/(twice?2:1))*4,4);int at=(y*outWidth+x)*4;
   byte gray=CorpusGray(source);if(id=="low-contrast")gray=CorpusLow(gray);
   for(int c=0;c<3;c++)if(decoded[at+c]!=(twice?CorpusWhite(source[c],source[3]):gray))return false;
   if(decoded[at+3]!=255)return false;
  }
  return true;
 }

 private static byte[] CorpusEncode(string id,byte[] reference,int width,int height,List<byte[]> owned)
 {
  Require(id is "gray" or "twice" or "low-contrast"&&width is >0 and <=4096&&height is >0 and <=4096&&(long)width*height<=4194304&&reference.Length==checked(width*height*4),"Closed corpus transform grammar");
  int w=width*(id=="twice"?2:1),h=height*(id=="twice"?2:1),channels=id=="twice"?3:1;
  Require(w<=4096&&h<=4096&&(long)w*h<=4194304,"Corpus transformed source geometry bound");
  byte[] raw=CorpusOwn(owned,new byte[checked((w*channels+1)*h)]);
  byte[] compressedBytes=CorpusOwn(owned,new byte[4194304]),outputBytes=CorpusOwn(owned,new byte[4194304]),header=CorpusOwn(owned,new byte[13]);
  var compressed=new MemoryStream(compressedBytes,0,compressedBytes.Length,true,true);var output=new MemoryStream(outputBytes,0,outputBytes.Length,true,true);
  try
  {
   compressed.SetLength(0);output.SetLength(0);
   for(int y=0;y<h;y++)for(int x=0;x<w;x++)
   {
    int at=y*(w*channels+1)+1+x*channels;var pixel=reference.AsSpan(((y/(id=="twice"?2:1))*width+x/(id=="twice"?2:1))*4,4);
    if(channels==3){raw[at]=CorpusWhite(pixel[2],pixel[3]);raw[at+1]=CorpusWhite(pixel[1],pixel[3]);raw[at+2]=CorpusWhite(pixel[0],pixel[3]);}
    else{byte gray=CorpusGray(pixel);raw[at]=id=="low-contrast"?CorpusLow(gray):gray;}
   }
   using(var zlib=new ZLibStream(compressed,CompressionLevel.SmallestSize,true))zlib.Write(raw);
   BinaryPrimitives.WriteUInt32BigEndian(header,(uint)w);BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4),(uint)h);header[8]=8;header[9]=(byte)(channels==3?2:0);
   output.Write([137,80,78,71,13,10,26,10]);CorpusChunk(output,"IHDR"u8,header);CorpusChunk(output,"IDAT"u8,compressed.GetBuffer().AsSpan(0,checked((int)compressed.Length)));CorpusChunk(output,"IEND"u8,[]);
   Require(output.Length<=4194304,"Corpus encoded PNG source byte cap");return CorpusOwn(owned,output.ToArray());
  }
  finally{CryptographicOperations.ZeroMemory(raw);CryptographicOperations.ZeroMemory(header);try{CryptographicOperations.ZeroMemory(compressed.GetBuffer());compressed.Dispose();}finally{CryptographicOperations.ZeroMemory(output.GetBuffer());output.Dispose();}}
 }
 private static uint CorpusCrc(ReadOnlySpan<byte> bytes)
 {uint crc=uint.MaxValue;foreach(byte value in bytes){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320U:crc>>1;}return crc^uint.MaxValue;}
 private static void CorpusChunk(MemoryStream output,ReadOnlySpan<byte> type,ReadOnlySpan<byte> payload)
 {
  Require(payload.Length<=4194304&&output.Length+payload.Length+12<=4194304,"Corpus chunk allocation/output bounded");
  byte[] chunk=new byte[checked(payload.Length+12)];
  try{BinaryPrimitives.WriteUInt32BigEndian(chunk,(uint)payload.Length);type.CopyTo(chunk.AsSpan(4));payload.CopyTo(chunk.AsSpan(8));BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(payload.Length+8),CorpusCrc(chunk.AsSpan(4,payload.Length+4)));output.Write(chunk);}
  finally{CryptographicOperations.ZeroMemory(chunk);}
 }
 private static void CorpusInspectEncoding(byte[] png,string id,byte[] reference,int width,int height)
 {
  int channels=id=="twice"?3:1,w=width*(channels==3?2:1),h=height*(channels==3?2:1);int at=8;
  Require(png.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),"Corpus exact PNG signature");
  byte[]? raw=null;
  try
  {
   foreach(string name in new[]{"IHDR","IDAT","IEND"})
   {
    Require(at+12<=png.Length,"Corpus chunk extent");int length=checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at,4)));
    Require(length>=0&&length<=png.Length-at-12&&Encoding.ASCII.GetString(png,at+4,4)==name,"Corpus exact closed chunk order");
    Require(CorpusCrc(png.AsSpan(at+4,length+4))==BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at+8+length,4)),"Corpus chunk CRC");
    if(name=="IHDR")Require(length==13&&BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at+8,4))==w&&BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at+12,4))==h&&png[at+16]==8&&png[at+17]==(channels==3?2:0)&&png[at+18]==0&&png[at+19]==0&&png[at+20]==0,"Corpus exact PNG geometry/type/profile");
    if(name=="IDAT")
    {
     raw=new byte[checked((w*channels+1)*h)];using var compressed=new MemoryStream(png,at+8,length,false);using var inflater=new ZLibStream(compressed,CompressionMode.Decompress);inflater.ReadExactly(raw);Require(inflater.ReadByte()==-1,"Corpus exact independent inflated row budget");
     for(int y=0;y<h;y++){Require(raw[y*(w*channels+1)]==0,"Corpus filter0 row");for(int x=0;x<w;x++){var source=reference.AsSpan(((y/(channels==3?2:1))*width+x/(channels==3?2:1))*4,4);int pixel=y*(w*channels+1)+1+x*channels;if(channels==3){for(int c=0;c<3;c++)Require(raw[pixel+c]==CorpusWhite(source[2-c],source[3]),"Corpus exact independently inflated RGB");}else{byte gray=CorpusGray(source);Require(raw[pixel]==(id=="low-contrast"?CorpusLow(gray):gray),"Corpus exact independently inflated gray");}}}
    }
    if(name=="IEND")Require(length==0,"Corpus IEND empty");at+=length+12;
   }
   Require(at==png.Length,"Corpus no trailing PNG bytes");
  }
  finally{if(raw is not null)CryptographicOperations.ZeroMemory(raw);}
 }
 private static int CorpusWhitespace(string text)=>text.EnumerateRunes().Count(Rune.IsWhiteSpace);
 private static int CorpusDistance(string expected,string actual)
 {
  Require(expected.Length<=256&&actual.Length<=65536,"Corpus metric bounds before allocation");var target=expected.EnumerateRunes().ToArray();int[] previous=Enumerable.Range(0,target.Length+1).ToArray(),current=new int[target.Length+1];int row=0;
  try{foreach(var value in actual.EnumerateRunes()){current[0]=++row;for(int column=1;column<=target.Length;column++)current[column]=Math.Min(Math.Min(previous[column]+1,current[column-1]+1),previous[column-1]+(target[column-1]==value?0:1));(previous,current)=(current,previous);}return previous[target.Length];}
  finally{Array.Clear(target);Array.Clear(previous);Array.Clear(current);}
 }
 private static void CorpusScalarOracles()
 {
  byte[][] pixels=[[0,0,0,0],[255,255,255,255],[0,0,0,255],[0,0,255,255],[0,255,0,255],[255,0,0,255],[0,0,0,128]];
  byte[] gray=[255,255,0,77,149,29,127],low=[240,240,160,184,207,169,200];
  for(int i=0;i<pixels.Length;i++)Require(CorpusGray(pixels[i])==gray[i]&&CorpusLow(gray[i])==low[i],"Corpus hand-authored alpha/luminance/contrast vector");
  Require(CorpusCrc("123456789"u8)==0xcbf43926,"Corpus independent standard CRC32 vector");
  foreach(var vector in new[]{("합성 😀","합성 😀",0),("abc","abxc",1),("abc","ac",1),("abc","axc",1),("😀","😁",1),("a b","ab",1),("a\nb","a b",1),("", "😀",1)})Require(CorpusDistance(vector.Item1,vector.Item2)==vector.Item3,"Corpus hand-authored Unicode-scalar edit distance");
  Require(CorpusDistance(OcrExpected,OcrExpected+" altered")>0&&CorpusDistance(OcrComparable(OcrExpected),OcrComparable(OcrExpected+" altered"))>0,"Corpus raw and normalized metrics detect real added content");
  Require(CorpusWhitespace("a \n\t😀")==3,"Corpus whitespace counts preserve newline and tabs");
  var owned=new List<byte[]>();
  try
  {
   byte[] samples=pixels.SelectMany(p=>p).ToArray();
   foreach(string id in new[]{"gray","low-contrast"})
   {
    byte[] encoded=CorpusEncode(id,samples,7,1,owned),decoded=CorpusDecode(encoded,7,1,owned);byte[] expected=id=="gray"?gray:low;
    for(int x=0;x<7;x++)Require(decoded.AsSpan(x*4,4).SequenceEqual(new byte[]{expected[x],expected[x],expected[x],255}),"Independent hand-authored encoded gray/alpha/contrast pixels");
   }
   byte[] pair=[0,0,255,255,255,0,0,255];byte[] enlarged=CorpusEncode("twice",pair,2,1,owned),actual=CorpusDecode(enlarged,4,2,owned);
   byte[] row=[0,0,255,255,0,0,255,255,255,0,0,255,255,0,0,255];
   Require(actual.AsSpan(0,16).SequenceEqual(row)&&actual.AsSpan(16,16).SequenceEqual(row),"Independent hand-authored 2x2 duplication geometry/pixels");
   Require(!CorpusPixelsMatch("twice",pair,2,1,actual,5,2),"Corpus pixel oracle rejects altered geometry");
  }
  finally{foreach(byte[] bytes in owned)CryptographicOperations.ZeroMemory(bytes);}
 }
}
