using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using MemoApp.Core.Storage;

internal static class PngMetadataChecks
{
    private delegate object Inspect(ReadOnlySpan<byte> source);
    private delegate object Decode(ReadOnlySpan<byte> source,CancellationToken cancellation,Action<byte[]>? allocations);

    internal static void Run()
    {
        var assembly=typeof(EncryptedVault).Assembly;
        var profile=assembly.GetType("MemoApp.Core.Transfer.PngPreviewProfile");
        var decoder=assembly.GetType("MemoApp.Core.Transfer.PngPixelDecoder");
        VaultChecks.Require(profile is not null && decoder is not null,"Bounded PNG metadata profile/decoder types must exist");
        var inspect=profile!.GetMethod("Inspect",BindingFlags.NonPublic|BindingFlags.Static)!.CreateDelegate<Inspect>();
        var decode=decoder!.GetMethod("Decode",BindingFlags.NonPublic|BindingFlags.Static)!.CreateDelegate<Decode>();
        byte[] ihdr=new byte[13];BinaryPrimitives.WriteUInt32BigEndian(ihdr,1);BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4),1);ihdr[8]=8;ihdr[9]=6;
        byte[] pixels=[1,2,3,4],expected=[3,2,1,4];
        byte[] compressed;
        using(var memory=new MemoryStream())
        {
            using(var zlib=new ZLibStream(memory,CompressionLevel.SmallestSize,true)){zlib.Write([0]);zlib.Write(pixels);}
            compressed=memory.ToArray();
        }
        var metadata=new (string Type,byte[] Data)[]{("sRGB",[0]),("gAMA",Unsigned(45455)),("pHYs",Density(3779,3779,1))};
        VaultChecks.Require(Crc("IEND"u8)==0xae426082U,"Independent bitwise CRC oracle matches known PNG IEND vector");

        byte[] WithMetadata(params (string Type,byte[] Data)[] chunks)=>Container([("IHDR",ihdr),..chunks,("IDAT",compressed),("IEND",[])]);
        void Accept(byte[] source,int expectedChunks,int expectedStructure,string reason)
        {
            var original=(byte[])source.Clone();var header=inspect(source);
            VaultChecks.Require(Scalar(header,"Width")==1 && Scalar(header,"Height")==1 && Scalar(header,"Channels")==4 &&
                Scalar(header,"SourcePixels")==1 && Scalar(header,"BgraBytes")==4 && Scalar(header,"PreviewPixels")==1 &&
                Scalar(header,"ChunkCount")==expectedChunks && Scalar(header,"StructuralBytes")==expectedStructure && Scalar(header,"DataBytes")==compressed.Length,
                reason+": scalar header/chunk/structural budgets include metadata but only IDAT supplies compressed bytes");
            VaultChecks.Require(header.GetType().GetProperties().All(property=>property.PropertyType==typeof(int)),"Metadata preflight retains only scalar data");
            var allocations=new List<byte[]>();var raster=(IDisposable)decode(source,default,allocations.Add);
            byte[] owned=(byte[])raster.GetType().GetField("pixels",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(raster)!;
            try
            {
                VaultChecks.Require(Scalar(raster,"Width")==1 && Scalar(raster,"Height")==1 && Scalar(raster,"Stride")==4 && owned.SequenceEqual(expected),
                    reason+": ignored scalar metadata leaves original RGBA pixel values unchanged in BGRA output");
                VaultChecks.Require(allocations.Any(bytes=>ReferenceEquals(bytes,owned)) && allocations.Where(bytes=>!ReferenceEquals(bytes,owned)).All(bytes=>bytes.All(value=>value==0)),
                    "Successful metadata decode zeroes all compressed/row scratch before result ownership transfers");
                VaultChecks.Require(source.SequenceEqual(original) && pixels.SequenceEqual(new byte[]{1,2,3,4}),"Metadata inspection/decode never changes source or original pixel oracle");
            }
            finally{raster.Dispose();}
            raster.Dispose();
            VaultChecks.Require(owned.All(value=>value==0) && allocations.All(bytes=>bytes.All(value=>value==0)),"Metadata raster disposal idempotently zeroes actual owned output and scratch");
        }
        void Reject(byte[] source,string reason)
        {
            var original=(byte[])source.Clone();bool structureRefused=false;
            try{inspect(source);}catch(InvalidDataException error){structureRefused=true;VaultChecks.Require(error.Message=="Unsupported PNG preview structure profile","Metadata refusal uses fixed structural diagnostic");}
            VaultChecks.Require(structureRefused,reason+": structure must refuse");
            var allocations=new List<byte[]>();bool pixelsRefused=false;
            try{using var raster=(IDisposable)decode(source,default,allocations.Add);}
            catch(InvalidDataException error){pixelsRefused=true;VaultChecks.Require(error.Message=="Unsupported PNG preview pixels","Metadata refusal uses fixed pixel diagnostic");}
            VaultChecks.Require(pixelsRefused && source.SequenceEqual(original) && allocations.All(bytes=>bytes.All(value=>value==0)),
                reason+": decode must refuse without source mutation or abandoned owned allocations");
        }

        Accept(WithMetadata(),3,57,"Unchanged base RGBA profile");
        foreach(var chunk in metadata)Accept(WithMetadata(chunk),4,57+12+chunk.Data.Length,"One approved scalar metadata chunk");
        foreach(byte intent in new byte[]{0,1,2,3})Accept(WithMetadata(("sRGB",[intent])),4,70,"Each permitted sRGB rendering intent");
        foreach(uint gamma in new uint[]{1,45455,int.MaxValue})Accept(WithMetadata(("gAMA",Unsigned(gamma))),4,73,"Unsigned nonzero gAMA range");
        foreach(byte unit in new byte[]{0,1})foreach(var density in new[]{(0U,0U),(1U,(uint)int.MaxValue)})
            Accept(WithMetadata(("pHYs",Density(density.Item1,density.Item2,unit))),4,78,"Unsigned pHYs values and both permitted units");
        foreach(var order in new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}})
            Accept(WithMetadata(order.Select(index=>metadata[index]).ToArray()),6,107,"Each scalar chunk once before IDAT in any order");

        // Independent stored-block zlib vector, rather than relying exclusively on BCL compression.
        byte[] stored=[0x78,0x01,0x01,0x05,0x00,0xfa,0xff,0,1,2,3,4,0,0x19,0,0x0b];
        byte[] generated=compressed;compressed=stored;
        Accept(WithMetadata(metadata),6,107,"Approved scalar metadata with independent stored zlib/Adler vector");compressed=generated;

        foreach(var chunk in metadata)
        {
            foreach(int length in new[]{0,chunk.Data.Length-1,chunk.Data.Length+1})
                Reject(WithMetadata((chunk.Type,new byte[length])),"Exact scalar metadata length required");
            Reject(WithMetadata(chunk,chunk),"Duplicate scalar metadata type refused");
            Reject(Container([("IHDR",ihdr),("IDAT",compressed),chunk,("IEND",[])]),"Scalar metadata after complete IDAT refused");
            Reject(Container([chunk,("IHDR",ihdr),("IDAT",compressed),("IEND",[])]),"Scalar metadata before IHDR refused");
            byte[] corrupt=WithMetadata(chunk);int metadataOffset=8+12+ihdr.Length;
            corrupt[metadataOffset+8+chunk.Data.Length]^=1;
            Reject(corrupt,"Scalar metadata CRC mutation refused");
            corrupt=WithMetadata(chunk);corrupt[metadataOffset+8]^=1;
            Reject(corrupt,"Scalar metadata payload mutation without CRC update refused");
            Reject(Container([("IHDR",ihdr),("IDAT",compressed[..1]),chunk,("IDAT",compressed[1..]),("IEND",[])]),
                "Scalar metadata cannot interrupt contiguous IDAT sequence");
            Reject(Container([("IHDR",ihdr),("IDAT",compressed),("IEND",[]),chunk]),"Scalar metadata after IEND refused");
        }
        foreach(byte intent in new byte[]{4,255})Reject(WithMetadata(("sRGB",[intent])),"Out-of-range sRGB intent refused");
        Reject(WithMetadata(("gAMA",Unsigned(0))),"Zero gAMA refused");
        foreach(uint invalid in new[]{0x80000000U,uint.MaxValue}){Reject(WithMetadata(("gAMA",Unsigned(invalid))),"PNG four-byte integer upper bit refused");Reject(WithMetadata(("pHYs",Density(invalid,1,1))),"PNG X density upper bit refused");Reject(WithMetadata(("pHYs",Density(1,invalid,1))),"PNG Y density upper bit refused");}
        foreach(byte unit in new byte[]{2,255})Reject(WithMetadata(("pHYs",Density(1,1,unit))),"Out-of-range pHYs unit refused");

        // Contiguous IDAT chunks remain legal, including empty chunks, with metadata only beforehand.
        Accept(Container([("IHDR",ihdr),metadata[0],("IDAT",compressed[..1]),("IDAT",[]),("IDAT",compressed[1..]),("IEND",[])]),6,94,
            "Scalar metadata before contiguous split IDAT is transparent to decoding");
        foreach(string type in new[]{"tEXt","zTXt","iTXt","iCCP","eXIf","acTL","fcTL","fdAT","PLTE","tRNS","cHRM","bKGD","tIME","JUNK","srgb","gama","phys"})
            Reject(WithMetadata((type,[0])),"Unapproved text/profile/animation/unknown/case-variant chunk remains refused");
        Reject([0xff,0xd8,0xff,0xe0,0,2,0xff,0xd9],"JPEG remains outside this PNG primitive");
        Console.WriteLine("PASS: bounded PNG scalar sRGB/gAMA/pHYs pre-IDAT grammar, CRC/duplicate/value/ordering refusal, exact unchanged pixels/source and owned disposal zero; other formats remain refused");
    }
    private static byte[] Unsigned(uint value){var bytes=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(bytes,value);return bytes;}
    private static byte[] Density(uint x,uint y,byte unit)
    {var bytes=new byte[9];BinaryPrimitives.WriteUInt32BigEndian(bytes,x);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4),y);bytes[8]=unit;return bytes;}
    private static int Scalar(object value,string name)=>(int)value.GetType().GetProperty(name)!.GetValue(value)!;
    private static byte[] Container((string Type,byte[] Data)[] chunks)
    {
        using var stream=new MemoryStream();stream.Write([137,80,78,71,13,10,26,10]);
        foreach(var (type,data) in chunks)
        {
            var chunk=new byte[data.Length+12];BinaryPrimitives.WriteUInt32BigEndian(chunk,(uint)data.Length);
            System.Text.Encoding.ASCII.GetBytes(type).CopyTo(chunk,4);data.CopyTo(chunk,8);
            BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(data.Length+8),Crc(chunk.AsSpan(4,data.Length+4)));stream.Write(chunk);
        }
        return stream.ToArray();
    }
    private static uint Crc(ReadOnlySpan<byte> input)
    {uint crc=uint.MaxValue;foreach(byte value in input){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320U:crc>>1;}return crc^uint.MaxValue;}
}
