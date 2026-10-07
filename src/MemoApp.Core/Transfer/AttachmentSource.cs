using System.Security.Cryptography;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Transfer;

// Opaque bytes only: no document/image parsing, clipboard, network, export or process launch.
public sealed class AttachmentSource : IDisposable
{
    private byte[] bytes;
    private bool disposed;
    private AttachmentSource(byte[] bytes,string name,string mime,string hash)
    {this.bytes=bytes;Name=name;Mime=mime;Sha256=hash;}
    public string Name {get;private set;}
    public string Mime {get;private set;}
    public string Sha256 {get;private set;}
    public ReadOnlySpan<byte> Content=>disposed?throw new InvalidOperationException("Attachment input is disposed"):bytes;
    public static AttachmentSource Read(string source,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();source=LocalFilePath.Resolve(source);LocalFilePath.CheckAncestors(source,true);
        string name=Path.GetFileName(source),mime=ExtensionMime(Path.GetExtension(source));
        AttachmentValidation.Description(name,mime,0,new string('0',64));
        cancellationToken.ThrowIfCancellationRequested();
        using var input=LocalRegularFile.Open(source);
        return ReadCaptured(input,input.Length,name,mime,cancellationToken);
    }
    internal static AttachmentSource ReadCaptured(Stream input,long length,string name,string mime,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(length<0||length>AttachmentValidation.MaxObject)throw new InvalidDataException("Attachment source size limit");
        AttachmentValidation.Description(name,mime,checked((int)length),new string('0',64));
        var bytes=new byte[checked((int)length)];
        try
        {
            for(int offset=0;offset<bytes.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read=input.Read(bytes,offset,Math.Min(65536,bytes.Length-offset));
                if(read==0)throw new EndOfStreamException("Attachment source changed while reading");offset+=read;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if(input.ReadByte()!=-1)throw new IOException("Attachment source changed while reading");
            cancellationToken.ThrowIfCancellationRequested();var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
            cancellationToken.ThrowIfCancellationRequested();return new(bytes,name,mime,hash);
        }
        catch{CryptographicOperations.ZeroMemory(bytes);throw;}
    }
    private static string ExtensionMime(string extension)=>extension.ToLowerInvariant() switch
    {
        ".pdf"=>"application/pdf",
        ".doc"=>"application/msword",
        ".docx"=>"application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls"=>"application/vnd.ms-excel",
        ".xlsx"=>"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt"=>"application/vnd.ms-powerpoint",
        ".pptx"=>"application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".hwp"=>"application/x-hwp",
        ".hwpx"=>"application/vnd.hancom.hwpx",
        ".png"=>"image/png",
        ".jpg" or ".jpeg"=>"image/jpeg",
        ".gif"=>"image/gif",
        ".bmp"=>"image/bmp",
        _=>"application/octet-stream"
    };
    public void Dispose()
    {if(disposed)return;CryptographicOperations.ZeroMemory(bytes);bytes=[];Name=Mime=Sha256="";disposed=true;}
}
