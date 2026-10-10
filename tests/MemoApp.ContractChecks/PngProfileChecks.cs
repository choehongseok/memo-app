using System.Buffers.Binary;
using System.Reflection;
using System.IO.Compression;
using MemoApp.Core.Storage;
internal static class PngProfileChecks
{
    private delegate object Inspect(ReadOnlySpan<byte> input);
    internal static void Run()
    {
        var type=typeof(EncryptedVault).Assembly.GetType("MemoApp.Core.Transfer.PngPreviewProfile");VaultChecks.Require(type is not null,"Internal bounded PNG structure preflight is missing");
        var inspect=type!.GetMethod("Inspect",BindingFlags.NonPublic|BindingFlags.Static)!.CreateDelegate<Inspect>();
        byte[] TinyData(int channels){using var memory=new MemoryStream();using(var zlib=new ZLibStream(memory,CompressionLevel.SmallestSize,true)){zlib.Write([0]);zlib.Write(new byte[2*channels]);}return memory.ToArray();}
        foreach(int channels in new[]{1,2,3,4})
        {
            var source=Png(2,1,channels,[TinyData(channels)]);var original=(byte[])source.Clone();var header=inspect(source);int Read(string name)=>(int)header.GetType().GetProperty(name)!.GetValue(header)!;
            VaultChecks.Require(Read("Width")==2&&Read("Height")==1&&Read("Channels")==channels&&Read("SourcePixels")==2&&Read("BgraBytes")==8&&Read("PreviewWidth")==2&&Read("PreviewHeight")==1&&Read("ChunkCount")==3&&Read("StructuralBytes")==57,"Independent synthetic grayscale/gray-alpha/RGB/RGBA header and checked scalar budgets");
            VaultChecks.Require(source.SequenceEqual(original)&&header.GetType().GetProperties().All(p=>p.PropertyType==typeof(int)),"Pure preflight preserves original bytes and returns scalar data only");
            for(int length=0;length<source.Length;length++)Reject(source[..length],"Every byte truncation rejected");
            foreach(int offset in new[]{8+8+13,8+25+8+TinyData(channels).Length,source.Length-1}){var corrupt=(byte[])source.Clone();corrupt[offset]^=1;Reject(corrupt,"CRC mutation in each allowed chunk");}
        }
        byte[] invalidCompressed=Png(1,1,4,[[1,2,3]]);_=inspect(invalidCompressed); // Structure success deliberately does not prove DEFLATE/pixels.
        var tall=inspect(Png(1,4096,3,[[1]]));var wide=inspect(Png(4096,1,4,[[1]]));var square=inspect(Png(2048,2048,4,[[1]]));
        VaultChecks.Require(Scalar(tall,"PreviewWidth")==1&&Scalar(tall,"PreviewHeight")==1024&&Scalar(tall,"PreviewPixels")==1024&&Scalar(tall,"BgraBytes")==16384&&Scalar(wide,"PreviewWidth")==1024&&Scalar(wide,"PreviewHeight")==1&&Scalar(wide,"PreviewPixels")==1024&&Scalar(wide,"BgraBytes")==16384,"Thin dimension fixtures derive exact bounded non-enlarged raster/preview scalars");
        VaultChecks.Require(Scalar(square,"BgraBytes")==16777216&&Scalar(square,"PreviewWidth")==1024&&Scalar(square,"PreviewHeight")==1024&&Scalar(square,"PreviewPixels")==1048576,"Source-pixel boundary derives exact BGRA16MiB and preview4MiB equivalents");
        foreach(var pair in new[]{(0,1),(1,0),(4097,1),(1,4097),(1985,2113)})Reject(Png(pair.Item1,pair.Item2,4,[[1]]),"Dimension/pixel exact cap+1 rejected");
        var full=Png(1,1,3,[new byte[4194247]]);VaultChecks.Require(full.Length==4194304,"Exact source4MiB fixture");_=inspect(full);Reject(Png(1,1,3,[new byte[4194248]]),"Source cap+1 isolates container byte bound");
        var parts=Enumerable.Range(0,254).Select(_=>new byte[]{1}).ToArray();var maximum=Png(1,1,4,parts);var accepted=inspect(maximum);VaultChecks.Require(Scalar(accepted,"ChunkCount")==256&&Scalar(accepted,"StructuralBytes")==3093&&Scalar(accepted,"DataBytes")==254,"Exact256 total chunks, dominating3093 structural bytes and aggregate IDAT bytes");Reject(Png(1,1,4,[..parts,new byte[]{1}]),"Chunk cap+1 includes IHDR/IEND");
        _=inspect(Png(1,1,3,[[],[1],[]]));Reject(Png(1,1,3,[]),"Missing IDAT");Reject(Png(1,1,3,[[],[]]),"Aggregate empty IDAT");
        foreach(string name in new[]{"PLTE","tRNS","tEXt","iCCP","eXIf","acTL","fcTL","fdAT","JUNK"})Reject(WithExtra(name),"Ancillary/unknown/animation/profile chunks refused");
        foreach(var (index,value) in new[]{(8,(byte)16),(9,(byte)3),(10,(byte)1),(11,(byte)1),(12,(byte)1)})
        {var fields=Header(1,1,4);fields[index]=value;Reject(Container([("IHDR",fields),("IDAT",new byte[]{1}),("IEND",Array.Empty<byte>())]),"Unsupported numeric profile with valid CRC");}
        foreach(int channels in new[]{1,2})foreach(byte depth in new byte[]{1,2,4,16}){var fields=Header(2,1,channels);fields[8]=depth;Reject(Container([("IHDR",fields),("IDAT",TinyData(channels)),("IEND",Array.Empty<byte>())]),"Grayscale depths1/2/4/16 remain refused");}
        foreach(int channels in new[]{1,2}){var fields=Header(2,1,channels);fields[12]=1;Reject(Container([("IHDR",fields),("IDAT",TinyData(channels)),("IEND",Array.Empty<byte>())]),"Interlaced grayscale remains refused");foreach(string name in new[]{"PLTE","tRNS"})Reject(Container([("IHDR",Header(2,1,channels)),(name,new byte[]{0,1}),("IDAT",TinyData(channels)),("IEND",Array.Empty<byte>())]),"Grayscale palette/transparency chunks remain refused");}
        var baseline=Png(1,1,4,[[1]]);foreach(uint length in new[]{0x7fffffffU,0x80000000U,0xffffffffU}){var corrupt=(byte[])baseline.Clone();BinaryPrimitives.WriteUInt32BigEndian(corrupt.AsSpan(8),length);Reject(corrupt,"Unsigned PNG length boundary before cast/allocation");}
        var swapped=(byte[])baseline.Clone();BinaryPrimitives.WriteUInt32LittleEndian(swapped.AsSpan(8),13);Reject(swapped,"Wrong-endian length");
        Reject([..baseline,0],"Trailing byte");Reject([..baseline,..baseline],"Concatenated PNG");var badSignature=(byte[])baseline.Clone();badSignature[0]^=1;Reject(badSignature,"Wrong signature");
        var ihdr=Header(1,1,4);Reject(Container([("IHDR",ihdr),("IHDR",ihdr),("IDAT",new byte[]{1}),("IEND",Array.Empty<byte>())]),"Duplicate header");Reject(Container([("IDAT",new byte[]{1}),("IHDR",ihdr),("IEND",Array.Empty<byte>())]),"Header must be first");Reject(Container([("IHDR",ihdr),("IDAT",new byte[]{1}),("IEND",new byte[]{0})]),"Nonempty IEND");Reject(Container([("IHDR",ihdr),("IDAT",new byte[]{1}),("IEND",Array.Empty<byte>()),("IDAT",new byte[]{1})]),"IDAT after end");
        foreach(var malformed in new[]{ihdr[..12],[..ihdr,(byte)0]})Reject(Container([("IHDR",malformed),("IDAT",new byte[]{1}),("IEND",Array.Empty<byte>())]),"IHDR exact13-byte field grammar");
        Reject(Container([("ihdr",ihdr),("IDAT",new byte[]{1}),("IEND",Array.Empty<byte>())]),"Chunk type is exact case-sensitive four-byte value");
        VaultChecks.Require(Crc("IEND"u8)==0xae426082U,"Independent CRC oracle known IEND vector");
        Console.WriteLine("PASS: pure bounded strict grayscale/gray-alpha/RGB/RGBA PNG structure/CRC/header/scalar preflight, exact source/pixel/chunk limits and malformed/metadata/animation/EOF refusal (no decompression/native decoder/preview)");
        void Reject(byte[] source,string reason)
        {
            var copy=(byte[])source.Clone();bool rejected=false;try{inspect(source);}catch(InvalidDataException error){rejected=true;VaultChecks.Require(error.Message=="Unsupported PNG preview structure profile","Invalid input uses a fixed non-sensitive diagnostic");}
            VaultChecks.Require(rejected,reason);VaultChecks.Require(source.SequenceEqual(copy),"Rejected structure never mutates original source");
        }
        byte[] WithExtra(string name)=>Container([("IHDR",Header(1,1,4)),("IDAT",new byte[]{1}),(name,new byte[]{0}),("IEND",Array.Empty<byte>())]);
    }
    private static byte[] Header(int width,int height,int channels)
    {var header=new byte[13];BinaryPrimitives.WriteUInt32BigEndian(header,(uint)width);BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4),(uint)height);header[8]=8;header[9]=(byte)(channels switch{1=>0,2=>4,3=>2,4=>6,_=>throw new ArgumentException()});return header;}
    private static byte[] Png(int width,int height,int channels,byte[][] payloads)=>Container([("IHDR",Header(width,height,channels)),..payloads.Select(x=>("IDAT",x)),("IEND",Array.Empty<byte>())]);
    private static byte[] Container((string Type,byte[] Data)[] chunks)
    {
        using var memory=new MemoryStream();memory.Write([137,80,78,71,13,10,26,10]);
        foreach(var (name,data) in chunks){var chunk=new byte[data.Length+12];BinaryPrimitives.WriteUInt32BigEndian(chunk,(uint)data.Length);System.Text.Encoding.ASCII.GetBytes(name).CopyTo(chunk,4);data.CopyTo(chunk,8);BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(data.Length+8),Crc(chunk.AsSpan(4,data.Length+4)));memory.Write(chunk);}return memory.ToArray();
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {uint value=uint.MaxValue;foreach(byte b in bytes){value^=b;for(int i=0;i<8;i++)value=(value&1)!=0?(value>>1)^0xedb88320U:value>>1;}return value^uint.MaxValue;}
    private static int Scalar(object header,string name)=>(int)header.GetType().GetProperty(name)!.GetValue(header)!;
}
