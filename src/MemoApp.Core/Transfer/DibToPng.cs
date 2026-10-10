using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
namespace MemoApp.Core.Transfer;

// Pure bounded conversion only. Native capture and application authority remain with the caller.
internal static class DibToPng
{
    private const int Limit = 4194304;
    internal static ClipboardPngSource Convert(ReadOnlySpan<byte> source, CancellationToken cancellation = default, Action<byte[]>? allocations = null)
    {
        byte[]? row = null, compressed = null, png = null;
        Span<byte> ihdr = stackalloc byte[13];
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (source.Length is <40 or >Limit || U32(source,0)!=40) throw Refused();
            int width=BinaryPrimitives.ReadInt32LittleEndian(source[4..]), signedHeight=BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
            if(width is <1 or >4096 || signedHeight is 0 or int.MinValue || signedHeight is < -4096 or >4096) throw Refused();
            int height=Math.Abs(signedHeight),bits=BinaryPrimitives.ReadUInt16LittleEndian(source[14..]);
            if(BinaryPrimitives.ReadUInt16LittleEndian(source[12..])!=1 || bits is not (24 or 32) || U32(source,16)!=0 || U32(source,32)!=0 || U32(source,36)!=0)throw Refused();
            long pixelCount=checked((long)width*height), stride=checked(((checked((long)width*bits)+31)/32)*4), payload=checked(stride*height), end=checked(40+payload);
            if(pixelCount>Limit || end>source.Length || (U32(source,20)!=0 && U32(source,20)!=payload))throw Refused();
            for(int i=(int)end;i<source.Length;i++){if((i&65535)==0)cancellation.ThrowIfCancellationRequested();if(source[i]!=0)throw Refused();}
            Allocate(ref row,checked(width*3+1),allocations,cancellation);
            Allocate(ref compressed,Limit,allocations,cancellation);
            int compressedLength;
            using(var sink=new BoundedOutput(compressed!,cancellation))
            {
                using(var encoder=new ZLibStream(sink,CompressionLevel.SmallestSize,true))
                {
                    for(int y=0;y<height;y++)
                    {
                        cancellation.ThrowIfCancellationRequested();int sourceY=signedHeight>0?height-1-y:y;
                        var input=source.Slice(checked(40+(int)stride*sourceY),(int)stride);row![0]=0;
                        for(int x=0;x<width;x++){int from=x*(bits/8),to=1+x*3;row[to]=input[from+2];row[to+1]=input[from+1];row[to+2]=input[from];}
                        encoder.Write(row);
                    }
                    cancellation.ThrowIfCancellationRequested();
                } // Final zlib writes still enter the bounded, cancellation-aware sink.
                cancellation.ThrowIfCancellationRequested();compressedLength=checked((int)sink.Length);
            }
            int finalLength=checked(compressedLength+57);if(finalLength>Limit)throw Refused();
            Allocate(ref png,finalLength,allocations,cancellation);
            using(var output=new BoundedOutput(png!,cancellation))
            {
                output.Write(new byte[]{137,80,78,71,13,10,26,10});
                ihdr.Clear();BinaryPrimitives.WriteUInt32BigEndian(ihdr,(uint)width);BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..],(uint)height);ihdr[8]=8;ihdr[9]=2;
                Chunk(output,"IHDR"u8,ihdr,cancellation);Chunk(output,"IDAT"u8,compressed.AsSpan(0,compressedLength),cancellation);Chunk(output,"IEND"u8,ReadOnlySpan<byte>.Empty,cancellation);
                if(output.Length!=finalLength)throw Refused();
            }
            _=PngPreviewProfile.Inspect(png!);cancellation.ThrowIfCancellationRequested();
            var result=ClipboardPngSource.TakeValidatedOwnership(png!);png=null;return result;
        }
        finally
        {
            ihdr.Clear();if(row is not null)CryptographicOperations.ZeroMemory(row);if(compressed is not null)CryptographicOperations.ZeroMemory(compressed);if(png is not null)CryptographicOperations.ZeroMemory(png);
        }
    }
    private static uint U32(ReadOnlySpan<byte> source,int offset)=>BinaryPrimitives.ReadUInt32LittleEndian(source[offset..]);
    private static void Allocate(ref byte[]? owned,int count,Action<byte[]>? observer,CancellationToken cancellation)
    { cancellation.ThrowIfCancellationRequested();owned=new byte[count];observer?.Invoke(owned);cancellation.ThrowIfCancellationRequested(); }
    private static void Chunk(Stream target,ReadOnlySpan<byte> type,ReadOnlySpan<byte> data,CancellationToken cancellation)
    {
        Span<byte> scalar=stackalloc byte[4];
        try
        {
            BinaryPrimitives.WriteUInt32BigEndian(scalar,(uint)data.Length);target.Write(scalar);target.Write(type);target.Write(data);
            uint crc=uint.MaxValue;foreach(byte b in type)crc=AddCrc(crc,b);
            for(int i=0;i<data.Length;i++){if((i&65535)==0)cancellation.ThrowIfCancellationRequested();crc=AddCrc(crc,data[i]);}
            BinaryPrimitives.WriteUInt32BigEndian(scalar,crc^uint.MaxValue);target.Write(scalar);
        }
        finally{scalar.Clear();}
    }
    private static uint AddCrc(uint crc,byte value){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320U:crc>>1;return crc;}
    private static InvalidDataException Refused()=>new("Unsupported bounded clipboard DIB");
    private sealed class BoundedOutput(byte[] buffer,CancellationToken cancellation):Stream
    {
        private int position;
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>position;public override long Position{get=>position;set=>throw new NotSupportedException();}
        public override void Write(byte[] source,int offset,int count){ValidateBufferArguments(source,offset,count);Write(source.AsSpan(offset,count));}
        public override void Write(ReadOnlySpan<byte> source)
        {
            cancellation.ThrowIfCancellationRequested();if(source.Length>buffer.Length-position)throw Refused();
            while(!source.IsEmpty){cancellation.ThrowIfCancellationRequested();int count=Math.Min(source.Length,65536);source[..count].CopyTo(buffer.AsSpan(position,count));position=checked(position+count);source=source[count..];}
        }
        public override void Flush()=>cancellation.ThrowIfCancellationRequested();
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
    }
}
