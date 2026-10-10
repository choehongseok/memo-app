using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
namespace MemoApp.Core.Transfer;

// Inert writer for caller-owned, already rendered RGB pages. No text/font/PDF input parsing.
internal sealed class PdfRasterDocumentBuilder : IDisposable
{
    internal const int PageWidth=794,PageHeight=1123,MaxPages=256,MaxBytes=16*1024*1024;
    private const int RawBytes=PageWidth*PageHeight*3;
    private const string A4Width="595.2755905511812",A4Height="841.8897637795276";
    private readonly CancellationToken token;
    private readonly Action<byte[]>? allocations;
    private readonly int[] offsets=new int[3+MaxPages*3];
    private byte[]? document;
    private int length,pages;
    private bool faulted,finished,disposed;
    internal PdfRasterDocumentBuilder(CancellationToken token=default,Action<byte[]>? allocations=null)
    {this.token=token;this.allocations=allocations;}
    internal void AddRgbPage(int width,int height,ReadOnlySpan<byte> rgb)
    {
        byte[]? compressed=null;
        try
        {
            Active();
            if(width!=PageWidth||height!=PageHeight||rgb.Length!=RawBytes||pages>=MaxPages)throw Refused();
            int pageId=3+pages*3;
            // Reserve the complete final Pages/Catalog/xref/trailer before compression allocates or writes.
            string page=PageObject(pageId),content=ContentObject(pageId+2);
            int overhead=checked(page.Length+ImageHeader(pageId+1,RawBytes+65536).Length+"\nendstream\nendobj\n".Length+content.Length);
            int remaining=checked(MaxBytes-length-(document is null?9:0)-FinalReserve(pages+1)-overhead);
            if(remaining<1)throw Refused();
            if(document is null){Allocate(ref document,MaxBytes);Ascii("%PDF-1.7\n");}
            Allocate(ref compressed,Math.Min(RawBytes+65536,remaining));int compressedLength;
            using(var output=new BoundedCompressionOutput(compressed!,token))
            {
                using(var encoder=new ZLibStream(output,CompressionLevel.SmallestSize,true))
                {
                    int copied=0;while(copied<rgb.Length){token.ThrowIfCancellationRequested();int count=Math.Min(65536,rgb.Length-copied);encoder.Write(rgb.Slice(copied,count));copied=checked(copied+count);}
                    token.ThrowIfCancellationRequested();
                } // Disposal/finalization writes retain the same hard capacity and cancellation checks.
                token.ThrowIfCancellationRequested();compressedLength=checked((int)output.Length);
            }
            offsets[pageId]=length;Ascii(page);offsets[pageId+1]=length;Ascii(ImageHeader(pageId+1,compressedLength));Write(compressed.AsSpan(0,compressedLength));Ascii("\nendstream\nendobj\n");offsets[pageId+2]=length;Ascii(content);
            token.ThrowIfCancellationRequested();pages++;
        }
        catch{Fault();throw;}
        finally{if(compressed is not null)CryptographicOperations.ZeroMemory(compressed);}
    }
    internal PreparedTextExport Finish()
    {
        byte[]? result=null;
        try
        {
            Active();if(pages==0)throw Refused();int objects=3+pages*3;
            offsets[1]=length;Ascii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");offsets[2]=length;Ascii(PagesObject(pages));
            int xref=length;Ascii($"xref\n0 {Number(objects)}\n0000000000 65535 f \n");for(int id=1;id<objects;id++){token.ThrowIfCancellationRequested();Ascii(offsets[id].ToString("D10",CultureInfo.InvariantCulture)+" 00000 n \n");}
            Ascii($"trailer\n<< /Size {Number(objects)} /Root 1 0 R >>\nstartxref\n{Number(xref)}\n%%EOF\n");
            Allocate(ref result,length);int copied=0;while(copied<length){token.ThrowIfCancellationRequested();int count=Math.Min(65536,length-copied);document!.AsSpan(copied,count).CopyTo(result.AsSpan(copied,count));copied=checked(copied+count);}
            token.ThrowIfCancellationRequested();var prepared=new PreparedTextExport(result!);result=null;finished=true;ClearDocument();return prepared;
        }
        catch{Fault();throw;}
        finally{if(result is not null)CryptographicOperations.ZeroMemory(result);}
    }
    private void Active(){if(disposed||faulted||finished)throw new InvalidOperationException("Raster PDF builder lifetime ended");token.ThrowIfCancellationRequested();}
    private void Allocate(ref byte[]? owned,int size){token.ThrowIfCancellationRequested();owned=new byte[size];allocations?.Invoke(owned);token.ThrowIfCancellationRequested();}
    private void Write(ReadOnlySpan<byte> source)
    {
        token.ThrowIfCancellationRequested();if(source.Length>MaxBytes-length)throw Refused();
        while(!source.IsEmpty){token.ThrowIfCancellationRequested();int count=Math.Min(source.Length,65536);source[..count].CopyTo(document!.AsSpan(length,count));length=checked(length+count);source=source[count..];}
    }
    private void Ascii(string text)
    {
        Span<byte> scratch=stackalloc byte[1024];
        try{int position=0;while(position<text.Length){token.ThrowIfCancellationRequested();int count=Math.Min(scratch.Length,text.Length-position);for(int i=0;i<count;i++){char value=text[position+i];if(value>127)throw new InvalidOperationException("Fixed PDF grammar must be ASCII");scratch[i]=(byte)value;}Write(scratch[..count]);position+=count;}}
        finally{scratch.Clear();}
    }
    private static string PageObject(int id)=>$"{Number(id)} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {A4Width} {A4Height}] /Resources << /XObject << /Im0 {Number(id+1)} 0 R >> >> /Contents {Number(id+2)} 0 R >>\nendobj\n";
    private static string ImageHeader(int id,int bytes)=>$"{Number(id)} 0 obj\n<< /Type /XObject /Subtype /Image /Width 794 /Height 1123 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {Number(bytes)} >>\nstream\n";
    private static string ContentObject(int id)
    {string content=$"q\n{A4Width} 0 0 {A4Height} 0 0 cm\n/Im0 Do\nQ\n";return $"{Number(id)} 0 obj\n<< /Length {Number(content.Length)} >>\nstream\n{content}\nendstream\nendobj\n";}
    private static string PagesObject(int count)=>$"2 0 obj\n<< /Type /Pages /Count {Number(count)} /Kids ["+string.Join(" ",Enumerable.Range(0,count).Select(i=>$"{Number(3+i*3)} 0 R"))+"] >>\nendobj\n";
    private static int FinalReserve(int count)
    {
        int objects=3+count*3;
        return checked("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n".Length+PagesObject(count).Length+$"xref\n0 {Number(objects)}\n".Length+objects*20+$"trailer\n<< /Size {Number(objects)} /Root 1 0 R >>\nstartxref\n16777216\n%%EOF\n".Length);
    }
    private static string Number(int value)=>value.ToString(CultureInfo.InvariantCulture);
    private static InvalidDataException Refused()=>new("Raster PDF page or document limit");
    private void ClearDocument(){if(document is not null)CryptographicOperations.ZeroMemory(document);document=null;length=0;Array.Clear(offsets);}
    private void Fault(){faulted=true;ClearDocument();}
    public void Dispose(){if(disposed)return;disposed=true;ClearDocument();}
    private sealed class BoundedCompressionOutput(byte[] bytes,CancellationToken cancellation):Stream
    {
        private int position;
        public override bool CanRead=>false;public override bool CanWrite=>true;public override bool CanSeek=>false;public override long Length=>position;public override long Position{get=>position;set=>throw new NotSupportedException();}
        public override void Write(byte[] buffer,int offset,int count){ValidateBufferArguments(buffer,offset,count);Write(buffer.AsSpan(offset,count));}
        public override void Write(ReadOnlySpan<byte> source){cancellation.ThrowIfCancellationRequested();if(source.Length>bytes.Length-position)throw Refused();source.CopyTo(bytes.AsSpan(position));position=checked(position+source.Length);}
        public override void Flush()=>cancellation.ThrowIfCancellationRequested();public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
    }
}
