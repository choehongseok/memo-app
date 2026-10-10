using System.Buffers.Binary;
using System.Reflection;
using MemoApp.Core.Transfer;
internal static class DibToPngChecks
{
    private delegate ClipboardPngSource Convert(ReadOnlySpan<byte> source, CancellationToken cancellation, Action<byte[]>? allocations);
    internal static void Run()
    {
        var type=typeof(ClipboardPngSource).Assembly.GetType("MemoApp.Core.Transfer.DibToPng");
        VaultChecks.Require(type is not null,"Bounded DIB-to-PNG converter is missing");
        var convert=type!.GetMethod("Convert",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<Convert>();
        foreach(int width in new[]{1,3}) foreach(int height in new[]{2,-2}) foreach(int bits in new[]{24,32})
        {
            byte[] source=Dib(width,height,bits), original=(byte[])source.Clone();var observed=new List<byte[]>();
            using var png=convert(source,default,observed.Add);
            var header=PngPreviewProfile.Inspect(png.Content);
            VaultChecks.Require(header.Width==width&&header.Height==2&&header.Channels==3&&header.ChunkCount==3,"Generated strict RGB8 PNG contains only IHDR/IDAT/IEND");
            using var raster=PngPixelDecoder.Decode(png.Content);byte[] expected=Expected(width,2);
            VaultChecks.Require(raster.ConsumePixels(p=>VaultChecks.Require(p.SequenceEqual(expected),"Independent exact BGR/orientation/padding/high-byte opaque pixel oracle")),"Pixels consumed");
            VaultChecks.Require(source.SequenceEqual(original),"Borrowed DIB remains exact");
            png.Dispose(); VaultChecks.Require(observed.All(a=>a.All(b=>b==0)),"Success/disposal zero all observed owned buffers");
        }
        byte[] valid=Dib(3,2,24);
        for(int length=0;length<valid.Length;length++) Refuse(convert,valid[..length]);
        foreach(var mutation in new (int offset,int value,int bytes)[]{(0,12,4),(0,108,4),(0,124,4),(4,0,4),(4,-1,4),(4,4097,4),(4,int.MaxValue,4),(8,0,4),(8,int.MinValue,4),(8,4097,4),(8,-4097,4),(12,0,2),(12,2,2),(14,1,2),(14,8,2),(14,16,2),(14,48,2),(16,1,4),(16,3,4),(16,4,4),(16,5,4),(20,1,4),(20,-1,4),(32,1,4),(36,1,4)})
        {var bad=(byte[])valid.Clone();if(mutation.bytes==2)BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(mutation.offset),(ushort)mutation.value);else BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(mutation.offset),mutation.value);Refuse(convert,bad);}
        var pixels=(byte[])valid.Clone();BinaryPrimitives.WriteInt32LittleEndian(pixels.AsSpan(4),4096);BinaryPrimitives.WriteInt32LittleEndian(pixels.AsSpan(8),4096);Refuse(convert,pixels);
        byte[] tail=new byte[valid.Length+12];valid.CopyTo(tail,0);using(var accepted=convert(tail,default,null)){}tail[^1]=1;Refuse(convert,tail);
        byte[] exact=new byte[4194304];valid.CopyTo(exact,0);using(var accepted=convert(exact,default,null)){}Refuse(convert,new byte[4194305]);
        var actualSize=(byte[])valid.Clone();BinaryPrimitives.WriteInt32LittleEndian(actualSize.AsSpan(20),valid.Length-40);using(var accepted=convert(actualSize,default,null)){}
        foreach(int width in new[]{4096}) foreach(int height in new[]{1,-4096}){byte[] boundary=Dib(width==4096&&height==-4096?1:width,height,24);using var png=convert(boundary,default,null);}
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();Fail<OperationCanceledException>(()=>convert(valid,canceled.Token,null));}
        var allocationCount=new List<byte[]>();using(var counted=convert(valid,default,allocationCount.Add)){};
        for(int stop=1;stop<=allocationCount.Count;stop++)
        {
            var seen=new List<byte[]>();int count=0;Fail<IOException>(()=>convert(valid,default,b=>{seen.Add(b);if(++count==stop)throw new IOException("Synthetic allocation observer failure");}));
            VaultChecks.Require(seen.All(a=>a.All(b=>b==0)),"Observer exception clears allocated buffer and every earlier buffer");
            using var cancellation=new CancellationTokenSource();seen.Clear();count=0;Fail<OperationCanceledException>(()=>convert(valid,cancellation.Token,b=>{seen.Add(b);if(++count==stop)cancellation.Cancel();}));
            VaultChecks.Require(seen.All(a=>a.All(b=>b==0)),"Allocation cancellation clears every owned buffer");
        }
        // Near-capacity incompressible RGB source exercises bounded stream writes and zlib teardown.
        byte[] noisy=Dib(2720,514,24);uint seed=0x12345678;for(int i=40;i<noisy.Length;i++){seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;noisy[i]=(byte)seed;}
        var noiseBuffers=new List<byte[]>();Fail<InvalidDataException>(()=>convert(noisy,default,noiseBuffers.Add));
        VaultChecks.Require(noiseBuffers.All(a=>a.All(b=>b==0)),"Near-capacity encoding/finalization zeroes all observed ownership");
        Console.WriteLine("PASS: bounded DIB24/32 RGB8 pixels, strict grammar/length/slack/budgets, cancellation and owned-buffer failure cleanup");
    }
    private static byte[] Dib(int width,int signedHeight,int bits)
    {
        int height=Math.Abs(signedHeight),stride=((width*bits+31)/32)*4;byte[] value=new byte[40+stride*height];
        BinaryPrimitives.WriteInt32LittleEndian(value,40);BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(4),width);BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(8),signedHeight);BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(12),1);BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(14),(ushort)bits);
        for(int y=0;y<height;y++){int row=signedHeight>0?height-1-y:y;for(int x=0;x<width;x++){int p=40+row*stride+x*(bits/8);value[p]=(byte)(11+x+y*7);value[p+1]=(byte)(35+x*3+y);value[p+2]=(byte)(91+x+y*5);if(bits==32)value[p+3]=new byte[]{0,128,255}[x%3];}for(int p=width*(bits/8);p<stride;p++)value[40+row*stride+p]=0xdd;}
        return value;
    }
    private static byte[] Expected(int width,int height){byte[] result=new byte[width*height*4];for(int y=0;y<height;y++)for(int x=0;x<width;x++){int p=(y*width+x)*4;result[p]=(byte)(11+x+y*7);result[p+1]=(byte)(35+x*3+y);result[p+2]=(byte)(91+x+y*5);result[p+3]=255;}return result;}
    private static void Refuse(Convert convert,byte[] bad){byte[] original=(byte[])bad.Clone();Fail<InvalidDataException>(()=>convert(bad,default,null));VaultChecks.Require(bad.SequenceEqual(original),"Refusal preserves borrowed source");}
    private static void Fail<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
}
