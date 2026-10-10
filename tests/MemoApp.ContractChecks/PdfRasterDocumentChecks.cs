using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using MemoApp.Core.Transfer;
internal static class PdfRasterDocumentChecks
{
    private const int Width=794,Height=1123,RawBytes=Width*Height*3;
    private delegate void Add(int width,int height,ReadOnlySpan<byte> rgb);
    internal static void Run()
    {
        Type? type=typeof(PreparedTextExport).Assembly.GetType("MemoApp.Core.Transfer.PdfRasterDocumentBuilder");VaultChecks.Require(type is not null,"Bounded raster PDF builder is missing");
        byte[] page=new byte[RawBytes];Array.Fill(page,(byte)255);for(int y=0;y<60;y++)for(int x=0;x<60;x++){Paint(page,x,y,255,0,0);Paint(page,Width-1-x,y,0,255,0);Paint(page,x,Height-1-y,0,0,255);Paint(page,Width-1-x,Height-1-y,29,73,137);}byte[] original=(byte[])page.Clone();
        var seen=new List<byte[]>();using(var builder=Create(type!,default,seen.Add))
        {
            AddPage(builder,Width,Height,page);AddPage(builder,Width,Height,page);using var result=Finish(builder);Validate(result.Bytes,page,2);
            VaultChecks.Require(page.SequenceEqual(original),"Borrowed RGB is unchanged");VaultChecks.Require(seen.Where(b=>!ReferenceEquals(b,result.Bytes)).All(b=>b.All(v=>v==0)),"Finish clears document and per-page compressed scratch");
            byte[] owned=result.Bytes;result.Dispose();VaultChecks.Require(owned.All(v=>v==0)&&seen.All(b=>b.All(v=>v==0)),"Prepared output disposal ends every observed owned byte lifetime");
            Fail<InvalidOperationException>(()=>Finish(builder));Fail<InvalidOperationException>(()=>AddPage(builder,Width,Height,page));
        }
        using(var builder=Create(type!,default,null)){AddPage(builder,Width,Height,page);using var result=Finish(builder);Validate(result.Bytes,page,1);if(Environment.GetEnvironmentVariable("MEMO_PDF_RASTER_PROBE") is string path)File.WriteAllBytes(path,result.Bytes);}
        foreach(var shape in new[]{(0,Height,RawBytes),(Width,0,RawBytes),(Width-1,Height,RawBytes),(Width,Height-1,RawBytes),(int.MaxValue,int.MaxValue,RawBytes),(Width,Height,RawBytes-1),(Width,Height,RawBytes+1)})
        {
            seen.Clear();using var builder=Create(type!,default,seen.Add);byte[] bad=new byte[shape.Item3];Fail<InvalidDataException>(()=>AddPage(builder,shape.Item1,shape.Item2,bad));
            VaultChecks.Require(seen.Count==0,"Invalid state/dimensions/exact RGB length refused before any owned allocation");Fail<InvalidOperationException>(()=>Finish(builder));Fail<InvalidOperationException>(()=>AddPage(builder,Width,Height,page));
        }
        using(var empty=Create(type!,default,null)){Fail<InvalidDataException>(()=>Finish(empty));Fail<InvalidOperationException>(()=>AddPage(empty,Width,Height,page));}
        seen.Clear();int limitAllocations=0;using(var builder=Create(type!,default,b=>{if(seen.Count==2){VaultChecks.Require(seen[1].All(v=>v==0),"Earlier page compressed scratch zero before next allocation");seen.RemoveAt(1);}seen.Add(b);limitAllocations++;}))
        {
            for(int i=0;i<256;i++)AddPage(builder,Width,Height,page);int count=limitAllocations;Fail<InvalidDataException>(()=>AddPage(builder,Width,Height,page));VaultChecks.Require(limitAllocations==count,"257th page refused before allocation");Fail<InvalidOperationException>(()=>Finish(builder));VaultChecks.Require(seen.All(b=>b.All(v=>v==0)),"Failed 257th page immediately destroys prior candidate");
        }
        using(var builder=Create(type!,default,null)){for(int i=0;i<256;i++)AddPage(builder,Width,Height,page);using var result=Finish(builder);Validate(result.Bytes,page,256,false);}
        foreach(bool afterPage in new[]{false,true})
        {
            using var token=new CancellationTokenSource();seen.Clear();using var builder=Create(type!,token.Token,seen.Add);if(afterPage)AddPage(builder,Width,Height,page);token.Cancel();
            if(afterPage)Fail<OperationCanceledException>(()=>Finish(builder));else Fail<OperationCanceledException>(()=>AddPage(builder,Width,Height,page));
            Fail<InvalidOperationException>(()=>Finish(builder));VaultChecks.Require(seen.All(b=>b.All(v=>v==0)),"Canceled Add/Finish faults permanently and zeroes candidate");
        }
        var allocationCount=new List<byte[]>();using(var builder=Create(type!,default,allocationCount.Add)){AddPage(builder,Width,Height,page);using var result=Finish(builder);}
        for(int stop=1;stop<=allocationCount.Count;stop++)
        {
            int count=0;seen.Clear();using(var builder=Create(type!,default,b=>{seen.Add(b);if(++count==stop)throw new IOException("Synthetic observer failure");}))
            {Fail<IOException>(()=>{AddPage(builder,Width,Height,page);using var result=Finish(builder);});Fail<InvalidOperationException>(()=>Finish(builder));VaultChecks.Require(seen.All(b=>b.All(v=>v==0)),"Allocation observer failure zeroes new and earlier owned buffers");}
            count=0;seen.Clear();using var token=new CancellationTokenSource();using(var builder=Create(type!,token.Token,b=>{seen.Add(b);if(++count==stop)token.Cancel();}))
            {Fail<OperationCanceledException>(()=>{AddPage(builder,Width,Height,page);using var result=Finish(builder);});Fail<InvalidOperationException>(()=>Finish(builder));VaultChecks.Require(seen.All(b=>b.All(v=>v==0)),"Cancellation at every owned allocation faults and zeroes all buffers");}
        }
        byte[] noise=new byte[RawBytes];uint seed=0x352ac017;for(int i=0;i<noise.Length;i++){seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;noise[i]=(byte)seed;}byte[] digest=SHA256.HashData(noise);seen.Clear();
        using(var builder=Create(type!,default,seen.Add))
        {
            for(int i=0;i<6;i++)AddPage(builder,Width,Height,noise);Fail<InvalidDataException>(()=>AddPage(builder,Width,Height,noise));Fail<InvalidOperationException>(()=>Finish(builder));
            VaultChecks.Require(seen.All(b=>b.All(v=>v==0))&&SHA256.HashData(noise).SequenceEqual(digest),"Incompressible accumulated overflow refuses whole candidate, zeroes ownership, preserves source");
        }
        Console.WriteLine("PASS: bounded A4 raster PDF objects/xref/Flate exact RGB/corners, 256 pages, overflow, permanent failure/cancel fault and owned zero");
    }
    private static IDisposable Create(Type type,CancellationToken token,Action<byte[]>? observer)=>(IDisposable)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,[token,observer],CultureInfo.InvariantCulture)!;
    private static void AddPage(IDisposable builder,int width,int height,ReadOnlySpan<byte> rgb)=>builder.GetType().GetMethod("AddRgbPage",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate<Add>(builder)(width,height,rgb);
    private static PreparedTextExport Finish(IDisposable builder)
    {
        try{return (PreparedTextExport)builder.GetType().GetMethod("Finish",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(builder,null)!;}catch(TargetInvocationException e) when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static void Validate(byte[] bytes,byte[] expected,int pages,bool inflate=true)
    {
        VaultChecks.Require(bytes.Length<=16*1024*1024&&bytes.AsSpan().StartsWith("%PDF-1.7\n"u8),"Complete PDF signature and byte budget");string text=Encoding.Latin1.GetString(bytes);
        VaultChecks.Require(text.Contains("/Count "+pages+" /Kids [")&&text.Contains("/MediaBox [0 0 595.2755905511812 841.8897637795276]"),"Exact physical A4 points and page count");
        foreach(string forbidden in new[]{"/Font","/JavaScript","/URI","/Annots","/Action","/EmbeddedFile","/ToUnicode"})VaultChecks.Require(!text.Contains(forbidden),"Raster PDF has no font/text/action/external objects");
        int start=text.LastIndexOf("startxref\n",StringComparison.Ordinal)+10;int xref=int.Parse(text.AsSpan(start,text.IndexOf('\n',start)-start),CultureInfo.InvariantCulture);VaultChecks.Require(text.AsSpan(xref).StartsWith("xref\n"),"startxref points to actual table");
        string[] entries=text[xref..].Split('\n');int count=int.Parse(entries[1].Split(' ')[1],CultureInfo.InvariantCulture);VaultChecks.Require(count==3+pages*3,"Exactly Catalog/Pages and one page/image/content set per page");
        for(int id=1;id<count;id++){int offset=int.Parse(entries[id+2].AsSpan(0,10),CultureInfo.InvariantCulture);VaultChecks.Require(text.AsSpan(offset).StartsWith(id+" 0 obj\n"),"Every xref offset names exact object");}
        int images=0,position=0;while((position=text.IndexOf("/Subtype /Image",position,StringComparison.Ordinal))>=0)
        {
            int stream=text.IndexOf("stream\n",position,StringComparison.Ordinal)+7;string dictionary=text[position..(stream-7)];VaultChecks.Require(dictionary.Contains("/Width 794 /Height 1123 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode"),"Strict fixed RGB image grammar");int marker=dictionary.IndexOf("/Length ",StringComparison.Ordinal)+8;int length=int.Parse(dictionary.AsSpan(marker,dictionary.IndexOf(' ',marker)-marker),CultureInfo.InvariantCulture);
            VaultChecks.Require(text.AsSpan(stream+length).StartsWith("\nendstream\nendobj\n"),"Image stream length exact");if(inflate){using var input=new MemoryStream(bytes,stream,length,false);using var zlib=new ZLibStream(input,CompressionMode.Decompress);byte[] decoded=new byte[RawBytes];zlib.ReadExactly(decoded);VaultChecks.Require(zlib.ReadByte()==-1&&decoded.SequenceEqual(expected),"Independent exact RGB inflate includes asymmetric four corners");CryptographicOperations.ZeroMemory(decoded);}
            images++;position=stream+length;
        }
        VaultChecks.Require(images==pages,"One RGB image per page");
    }
    private static void Paint(byte[] bytes,int x,int y,byte r,byte g,byte b){int p=(y*Width+x)*3;bytes[p]=r;bytes[p+1]=g;bytes[p+2]=b;}
    private static void Fail<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
}
