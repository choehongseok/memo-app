using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;

internal static class WordImageExportChecks
{
    private static readonly XNamespace W="http://schemas.openxmlformats.org/wordprocessingml/2006/main",WP="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing",A="http://schemas.openxmlformats.org/drawingml/2006/main",P="http://schemas.openxmlformats.org/drawingml/2006/picture",R="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    internal static Task Run()
    {
        Require(typeof(StructuredWordExport).GetMethod("BuildOwned",BindingFlags.NonPublic|BindingFlags.Static) is not null,"Detached Word builder is missing");
        GenuinePackage(); MalformedPixels(); DetachedBounds(); ZeroingAndCancellation(); Geometry();
        Console.WriteLine("PASS: N04 fixed detached Word worker, independently parsed exact PNG/DrawingML/internal relations, sequential pixel refusal, separate media/pixel/XML/ZIP bounds and actual zeroing/settlement");
        return Task.CompletedTask;
    }
    private static void GenuinePackage()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-word-images-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using var owner=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);
            var first=owner.Workspace.CreateNote();first.Title="합성 <그림> 😀";first.Text="앞\t문장\n다음";
            Require(owner.PrepareAttachmentsAsync().GetAwaiter().GetResult(),"Genuine authenticated image root");
            byte[] png=Png(2,1,Compress([0,255,0,0,0,0,128,255,127]),density:true),other=Png(1,2,Compress([0,10,20,30,255,0,40,50,60,255]));
            Guid id=owner.AttachBytes(first,png,"same-name.png","image/png",first.EditVersion);
            Guid id2=owner.AttachBytes(first,other,"same-name.png","image/png",first.EditVersion);
            owner.Workspace.ConvertMode(first,"rich",true);
            owner.Workspace.SetRichDocument(first,new(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"앞\\t문장\\n다음\",\"bold\":true,\"link\":\"https://example.invalid/?x=1&y=2\"}]}]}"));
            owner.Workspace.InsertInlineImage(first,id,1,"투명 <첫> & 그림");owner.Workspace.InsertInlineImage(first,id,2,"반복 그림");owner.Workspace.InsertInlineImage(first,id2,3,"다른 그림");
            var duplicate=owner.Workspace.Duplicate(first);duplicate.Title="공유";
            var plain=owner.Workspace.CreateNote();plain.Title="plain";plain.Text="=HYPERLINK(\"file:///synthetic\")";
            var markdown=owner.Workspace.CreateNote();markdown.Title="markdown";owner.Workspace.ConvertMode(markdown,"markdown",true);markdown.Text="![literal](https://example.invalid/a.png)";
            var rich=owner.Workspace.CreateNote();rich.Title="v1";owner.Workspace.ConvertMode(rich,"rich",true);
            owner.Workspace.SetRichDocument(rich,new(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"rich formatting\",\"underline\":true}]},{\"type\":\"list\",\"ordered\":true,\"items\":[{\"runs\":[{\"text\":\"ordered\"}]}]},{\"type\":\"table\",\"rows\":[[{\"runs\":[{\"text\":\"cell\"}]}]]}]}"));
            var emptyV2=owner.Workspace.Duplicate(first);emptyV2.Title="v2 image-free";
            foreach(int at in new[]{3,2,1})owner.Workspace.RemoveInlineImage(emptyV2,at);
            Require(emptyV2.Document!.SchemaVersion==2,"Actual supported image-free v2");
            Require(owner.SaveAsync().GetAwaiter().GetResult(),"Genuine encrypted source save");
            int thread=Environment.CurrentManagedThreadId;owner.RegisterWordImageExportOwner(()=>Environment.CurrentManagedThreadId==thread);
            NoteDraft[] selected=[first,duplicate,plain,markdown,rich,emptyV2];
            string snapshot=JsonSerializer.Serialize(owner.Workspace.Capture());byte[] cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            var originals=new List<byte[]>();using var context=owner.CaptureWordImageContext(selected,selected.Select(n=>n.EditVersion).ToArray(),owner.AttachmentPreviewEpoch,default,originals.Add);
            var operation=StructuredWordExport.StartOwned(context);PreparedTextExport? package=null;
            try
            {
                package=operation.Completion.GetAwaiter().GetResult();operation.Settled.GetAwaiter().GetResult();
                Require(context.IsSettled&&originals.Count==2&&originals.All(x=>x.All(b=>b==0)),"Actual worker settlement clears exact captured PNGs");
                Require(owner.IsWordImageContextCurrent(context),"Unchanged original owner remains current after worker");
                VaultChecks.ExpectFailure(()=>StructuredWordExport.StartOwned(context),"Actual context build is single-use");
                using var zip=new ZipArchive(new MemoryStream(package.Bytes,false),ZipArchiveMode.Read);
                Require(zip.Entries.Count==7&&zip.Entries.Select(e=>e.FullName).Distinct().Count()==7,"Exactly five legacy parts and two fixed media entries");
                byte[] Read(string path){using var input=zip.GetEntry(path)!.Open();using var m=new MemoryStream();input.CopyTo(m);return m.ToArray();}
                XDocument Xml(string path)=>XDocument.Parse(Encoding.UTF8.GetString(Read(path)));
                Require(Read("word/media/image0001.png").SequenceEqual(png)&&Read("word/media/image0002.png").SequenceEqual(other),"Exact authenticated RGBA PNG bytes preserved, no alpha flatten or transcode");
                var document=Xml("word/document.xml");var rels=Xml("word/_rels/document.xml.rels").Root!.Elements().ToArray();
                Require(rels.Length==3&&rels.All(e=>e.Attribute("TargetMode") is null)&&rels.Skip(1).All(e=>e.Attribute("Type")!.Value.EndsWith("/image",StringComparison.Ordinal)),"Generated image relationships are internal only");
                foreach(var entry in zip.Entries.Where(e=>e.FullName.EndsWith(".rels",StringComparison.Ordinal)))Require(Xml(entry.FullName).Root!.Elements().All(e=>e.Attribute("TargetMode") is null),"No external package relationships");
                var drawings=document.Descendants(WP+"inline").ToArray();Require(drawings.Length==6,"Repeated/shared object retains every independent placement");
                string[] alts=["투명 <첫> & 그림","반복 그림","다른 그림","투명 <첫> & 그림","반복 그림","다른 그림"];
                Require(drawings.Select(e=>e.Element(WP+"docPr")!.Attribute("descr")!.Value).SequenceEqual(alts),"Exact escaped independent alt descriptions");
                Require(drawings.Select(e=>e.Element(WP+"docPr")!.Attribute("id")!.Value).Distinct().Count()==6,"Unique generated drawing IDs across selected notes");
                for(int i=0;i<drawings.Length;i++)
                {
                    string expected=i%3==2?"rIdImage2":"rIdImage1";Require(drawings[i].Descendants(A+"blip").Single().Attribute(R+"embed")!.Value==expected,"Shared media deduplicated with per-placement internal embed");
                    var extent=drawings[i].Element(WP+"extent")!;var transform=drawings[i].Descendants(A+"ext").Single();
                    long cx=i%3==2?9525:19050,cy=i%3==2?19050:9525;
                    Require((long)extent.Attribute("cx")! == cx&&(long)extent.Attribute("cy")! == cy&&extent.Attribute("cx")!.Value==transform.Attribute("cx")!.Value&&extent.Attribute("cy")!.Value==transform.Attribute("cy")!.Value,"96DPI matching inline/shape extent ignoring pHYs");
                }
                Require(document.Descendants(W+"t").Any(e=>e.Value.Contains("https://example.invalid/?x=1&y=2"))&&!document.Descendants(W+"hyperlink").Any()&&!document.Descendants(W+"instrText").Any(),"Rich URL remains visible inert literal");
                Require(document.Descendants(W+"t").Any(e=>e.Value==markdown.Text)&&document.Descendants(W+"t").Any(e=>e.Value==plain.Text),"Plain and Markdown remain literal text");
                Require(document.Descendants(W+"tbl").Count()==1&&document.Descendants(W+"numPr").Count()==1&&document.Descendants(W+"u").Any(),"Mixed v1 rich table/list/run formatting retained");
                Require(document.Descendants(W+"pgSz").Single().Attribute(W+"w")!.Value=="11906"&&document.Descendants(W+"pgMar").Single().Attribute(W+"left")!.Value=="1440","Explicit A4 portrait with one-inch margins");
                Require(Xml("[Content_Types].xml").Root!.Elements().Any(e=>e.Attribute("Extension")?.Value=="png"&&e.Attribute("ContentType")?.Value=="image/png"),"PNG content type registered");
                Require(JsonSerializer.Serialize(owner.Workspace.Capture())==snapshot&&!owner.IsDirty&&File.ReadAllBytes(Path.Combine(root,"current.vault")).SequenceEqual(cipher),"Worker preserves entire source/history/metadata/immutable objects and exact current cipher");
                VaultChecks.ExpectFailure(()=>StructuredWordExport.Capture([first]),"Legacy note-only route still lacks v2 attachment authority");
                string? fixture=Environment.GetEnvironmentVariable("MEMO_WORD_IMAGE_FIXTURE_PATH");if(!string.IsNullOrEmpty(fixture))File.WriteAllBytes(fixture,package.Bytes);
            }
            finally
            {
                try{operation.Settled.GetAwaiter().GetResult();}finally{package?.Dispose();owner.RetireWordImageContext(context);owner.SettleWordImageContext(context);}
            }
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void MalformedPixels()
    {
        byte[] valid=Compress([0,1,2,3,4]);var adler=(byte[])valid.Clone();adler[^1]^=1;
        byte[][] compressed=[[1,2,3],adler,Compress([5,1,2,3,4]),Compress([0,1,2,3]),Compress([0,1,2,3,4,0]),[..valid,0],[..valid,..valid]];
        foreach(var data in compressed)
        {
            byte[] png=Png(1,1,data);Require(PngPreviewProfile.Inspect(png).SourcePixels==1,"CRC-valid malformed pixels pass structure only");
            using var context=Synthetic([png]);var op=StructuredWordExport.StartOwned(context);bool refused=false;
            try{using var output=op.Completion.GetAwaiter().GetResult();}catch(InvalidDataException e){refused=e.Message=="Unsupported PNG preview pixels";}
            finally{op.Settled.GetAwaiter().GetResult();}
            Require(refused&&context.IsSettled,"Fixed detached worker refuses malformed zlib/filter/adler/exact raster before package delivery");
            Require(Owned(context).Length==0,"Malformed worker cleans captured media");
        }
        var scratch=new List<byte[]>();using var probe=Synthetic([Png(1,1,Compress([5,1,2,3,4]))]);
        Direct(probe,()=>VaultChecks.ExpectFailure(()=>StructuredWordExport.BuildOwned(probe,default,scratch.Add),"Malformed pixel zeroing probe"));
        Require(scratch.Count>0&&scratch.All(b=>b.All(v=>v==0)),"Malformed pixel stream clears every observed decoder allocation");
    }
    private static void DetachedBounds()
    {
        // Independent worker repeats bounded source validation even for deliberately test-forged detached contexts.
        var header=Png(4096,1024,[1]);
        using(var pixels=Synthetic(Enumerable.Range(0,16).Select(_=>header).ToArray()))
            Refuses(pixels,"Word unique media or source pixel budget");
        byte[] large=Png(1,1,new byte[3_000_000]);
        using(var media=Synthetic([large,large,large]))Refuses(media,"Word unique media or source pixel budget");
        using(var count=Synthetic(Enumerable.Range(0,129).Select(_=>Png(1,1,[1])).ToArray()))Refuses(count,"Word detached source bounds");
        using(var orphan=Synthetic([Png(1,1,Compress([0,1,2,3,4]))]))
        {
            using var detached=OwnedWordImageContext.TakeCapturedOwnership(ImmutableArray.Create(new WordNoteSource("synthetic","","plain",null)),orphan.Images,[],Owned(orphan).Select(b=>(byte[])b.Clone()).ToArray());
            Refuses(detached,"Word unreferenced detached media");
        }
        using(var mismatch=Synthetic([Png(1,1,Compress([0,1,2,3,4]))],wrongLength:true))Refuses(mismatch,"Word exact media descriptor");
        var dense=new StyledDocument(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":["+string.Join(',',Enumerable.Repeat("{\"text\":\"\",\"bold\":true,\"underline\":true,\"strike\":true,\"fontFamily\":\"D2Coding\",\"fontSize\":16,\"foreground\":\"#123456\",\"background\":\"#ABCDEF\"}",768))+"]}]}");
        Require(RichDocumentCodec.Inspect(dense).Supported,"Independent compressible XML bound fixture canonical");
        using(var xml=Synthetic([],Enumerable.Repeat(new WordNoteSource("bounded","","rich",dense),100).ToImmutableArray()))Refuses(xml,"Word XML construction limit");
        using(var source=Synthetic([],Enumerable.Repeat(new WordNoteSource("bounded",new string('가',65536),"plain",null),100).ToImmutableArray()))Refuses(source,"Word projected source budget");
        // Reflect the actual private bounded ZIP sink, including the end-of-central-directory append.
        var type=typeof(StructuredWordExport).GetNestedType("BoundedWordBuffer",BindingFlags.NonPublic)!;
        byte[] backing;
        using(var stream=(MemoryStream)Activator.CreateInstance(type,true)!)
        {
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
            {
                using(var entry=zip.CreateEntry("a",CompressionLevel.NoCompression).Open())entry.Write(new byte[16*1024*1024-128]);
            }
            long exact=stream.Length;Require(exact<=16*1024*1024&&exact>16*1024*1024-128,"Near-limit ZIP including actual central directory succeeds");backing=stream.GetBuffer();
        }
        Require(backing.All(v=>v==0),"Successful archive teardown then live buffer disposal zeroes ZIP bytes");
        using(var stream=(MemoryStream)Activator.CreateInstance(type,true)!)
        {
            var zip=new ZipArchive(stream,ZipArchiveMode.Create,true);using(var entry=zip.CreateEntry("a",CompressionLevel.NoCompression).Open())entry.Write(new byte[16*1024*1024-64]);
            bool refused=false;try{zip.Dispose();}catch(IOException e){refused=e.Message=="Word package construction limit";}
            Require(refused,"Final central directory itself cannot exceed ZIP hard bound");backing=stream.GetBuffer();
        }
        Require(backing.All(v=>v==0),"Failed archive teardown retains owned backing until disposal then clears");
    }
    private static void ZeroingAndCancellation()
    {
        byte[] png=Png(1,1,Compress([0,1,2,3,4]));
        var allocations=new List<byte[]>();int allocationBoundaries=0;using(var context=Synthetic([png]))
        {
            PreparedTextExport? output=null;Direct(context,()=>output=StructuredWordExport.BuildOwned(context,default,allocations.Add));
            Require(allocations.Count>=6&&allocations.Take(allocations.Count-1).All(a=>a.All(v=>v==0)),"Decoded scratch/raster and retired/live ZIP construction buffers zeroed after success");
            allocationBoundaries=allocations.Count;byte[] final=output!.Bytes;Require(ReferenceEquals(final,allocations[^1])&&final.Any(v=>v!=0),"Only transferred final package stays alive");output.Dispose();Require(final.All(v=>v==0),"Final package actual owned bytes zero on disposal");
        }
        for(int boundary=1;boundary<=allocationBoundaries;boundary++)
        {
            allocations.Clear();using var cancel=new CancellationTokenSource();using var context=Synthetic([png]);bool canceled=false;
            Direct(context,()=>{try{using var output=StructuredWordExport.BuildOwned(context,cancel.Token,a=>{allocations.Add(a);if(allocations.Count==boundary)cancel.Cancel();});}catch(OperationCanceledException){canceled=true;}});
            // Decoder stops allocation immediately; ZIP.Dispose may grow its central-directory buffer during cancellation unwind.
            Require(canceled&&(boundary<=4?allocations.Count==boundary:allocations.Count>=boundary)&&allocations.All(a=>a.All(v=>v==0)),"Cancellation at decode/ZIP allocation boundary clears every actual owned buffer");
        }
        allocations.Clear();using(var context=Synthetic([png]))Direct(context,()=>VaultChecks.ExpectFailure(()=>StructuredWordExport.BuildOwned(context,default,a=>{allocations.Add(a);if(allocations.Count==5)throw new IOException("Synthetic ZIP allocation observer failure");}),"ZIP allocation observer exception"));
        Require(allocations.Count>=5&&allocations.All(a=>a.All(v=>v==0)),"Observer failure clears decoder and ZIP backing");
        using(var context=Synthetic([png]))
        {
            using var cancel=new CancellationTokenSource();cancel.Cancel();var op=StructuredWordExport.StartOwned(context,cancel.Token);bool refused=false;
            try{using var output=op.Completion.GetAwaiter().GetResult();}catch(OperationCanceledException){refused=true;}finally{op.Settled.GetAwaiter().GetResult();}
            Require(refused&&context.IsSettled&&Owned(context).Length==0,"Actual canceled worker settles only after captured media cleanup");
        }
    }
    private static void Geometry()
    {
        Require(WordImageDrawing.Extent(2,1)==(19050L,9525L),"Small image not upscaled");
        var wide=WordImageDrawing.Extent(4096,1);Require(wide.Width==9026*635L&&wide.Height==Math.Max(1,9525L*wide.Width/(4096*9525L)),"Wide landscape image proportionally fits A4 content width");
        var tall=WordImageDrawing.Extent(1,4096);Require(tall.Height==13958*635L&&tall.Width==Math.Max(1,9525L*tall.Height/(4096*9525L)),"Tall image proportionally fits A4 content height");
    }
    private static OwnedWordImageContext Synthetic(byte[][] pngs,ImmutableArray<WordNoteSource>? notes=null,bool wrongLength=false)
    {
        var descriptors=ImmutableArray.CreateBuilder<WordImageDescriptor>();var placements=ImmutableArray.CreateBuilder<WordImagePlacement>();var nodes=new List<object>();
        for(int i=0;i<pngs.Length;i++){var h=PngPreviewProfile.Inspect(pngs[i]);Guid id=Guid.NewGuid();descriptors.Add(new(id,Guid.NewGuid(),Convert.ToHexStringLower(SHA256.HashData(pngs[i])),pngs[i].Length+(wrongLength?1:0),h.Width,h.Height,h.SourcePixels));placements.Add(new(0,i,id,"synthetic"));nodes.Add(new{type="image",attachmentId=id,alt="synthetic"});}
        var doc=new StyledDocument(2,JsonSerializer.Serialize(new{nodes}));string text=RichDocumentCodec.Inspect(doc).Text??"";
        return OwnedWordImageContext.TakeCapturedOwnership(notes??ImmutableArray.Create(new WordNoteSource("synthetic",text,"rich",doc)),descriptors.ToImmutable(),placements.ToImmutable(),pngs.Select(p=>(byte[])p.Clone()).ToArray());
    }
    private static byte[][] Owned(OwnedWordImageContext context)=>(byte[][])typeof(OwnedWordImageContext).GetField("bytes",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(context)!;
    private static void Direct(OwnedWordImageContext context,Action action)
    {context.BeginBuild(Task.CompletedTask,Task.CompletedTask);try{action();}finally{context.CompleteBuild();}}
    private static void Refuses(OwnedWordImageContext context,string expected)
    {bool refused=false;Direct(context,()=>{try{using var output=StructuredWordExport.BuildOwned(context);}catch(Exception e)when(e is InvalidDataException or IOException){refused=e.Message==expected;}});Require(refused,"Independent worker refusal: "+expected);}
    private static void Require(bool condition,string message)=>VaultChecks.Require(condition,message);
    private static byte[] Compress(byte[] rows){using var memory=new MemoryStream();using(var zlib=new ZLibStream(memory,CompressionLevel.SmallestSize,true))zlib.Write(rows);return memory.ToArray();}
    private static byte[] Png(int width,int height,byte[] data,bool density=false)
    {
        byte[] header=new byte[13];BinaryPrimitives.WriteUInt32BigEndian(header,(uint)width);BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4),(uint)height);header[8]=8;header[9]=6;
        using var memory=new MemoryStream();memory.Write([137,80,78,71,13,10,26,10]);Chunk("IHDR",header);
        if(density){byte[] phys=new byte[9];BinaryPrimitives.WriteUInt32BigEndian(phys,1000);BinaryPrimitives.WriteUInt32BigEndian(phys.AsSpan(4),1000);phys[8]=1;Chunk("pHYs",phys);}
        Chunk("IDAT",data);Chunk("IEND",[]);return memory.ToArray();
        void Chunk(string type,byte[] bytes){var chunk=new byte[bytes.Length+12];BinaryPrimitives.WriteUInt32BigEndian(chunk,(uint)bytes.Length);Encoding.ASCII.GetBytes(type).CopyTo(chunk,4);bytes.CopyTo(chunk,8);uint crc=uint.MaxValue;foreach(byte b in chunk.AsSpan(4,bytes.Length+4)){crc^=b;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320U:crc>>1;}BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(bytes.Length+8),crc^uint.MaxValue);memory.Write(chunk);}
    }
}
