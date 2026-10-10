using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace MemoApp.Windows;

// CF_DIB only. Windows may synthesize this format in GetClipboardData.
// The returned HGLOBAL belongs to Windows; this adapter never frees or modifies it.
internal interface INativeClipboardDib
{
    uint GetSequence();bool Open();nint GetDib();nuint GetSize(nint handle);nint Lock(nint handle);
    void Copy(nint pointer,byte[] destination,int offset,int count);bool Unlock(nint handle);bool Close();
}
internal sealed class NativeClipboardDib:INativeClipboardDib
{
    internal static readonly NativeClipboardDib Instance=new();
    public uint GetSequence()=>GetClipboardSequenceNumber();
    public bool Open()=>OpenClipboard(0);
    public nint GetDib()=>GetClipboardData(8);
    public nuint GetSize(nint handle)=>GlobalSize(handle);
    public nint Lock(nint handle)=>GlobalLock(handle);
    public void Copy(nint pointer,byte[] destination,int offset,int count)=>Marshal.Copy(pointer+offset,destination,offset,count);
    public bool Unlock(nint handle)
    {
        SetLastError(0);bool result=GlobalUnlock(handle);int error=Marshal.GetLastPInvokeError();return result||error==0;
    }
    public bool Close()=>CloseClipboard();
    [DllImport("user32.dll",SetLastError=true)] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll",SetLastError=true)] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll",SetLastError=true)] private static extern nuint GlobalSize(nint handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
}
internal sealed class NativeDibCapture:IDisposable
{
    private byte[] bytes;internal uint Sequence{get;}internal ReadOnlyMemory<byte> Content=>bytes;
    internal NativeDibCapture(byte[] bytes,uint sequence){this.bytes=bytes;Sequence=sequence;}
    public void Dispose(){CryptographicOperations.ZeroMemory(bytes);bytes=[];}
}
internal static class NativeClipboardDibCapture
{
    internal static NativeDibCapture Capture(INativeClipboardDib native,Func<bool> allowed,CancellationToken token,Action<byte[]>? allocated=null)
    {
        byte[]? owned=null;nint handle=0;bool opened=false,locked=false,clean=true;uint sequence=0;Exception? failure=null;
        void Authority(){token.ThrowIfCancellationRequested();if(!allowed())throw new OperationCanceledException();}
        void Check(){Authority();uint now=native.GetSequence();Authority();if(now==0||now!=sequence)throw new OperationCanceledException();}
        try
        {
            Authority();sequence=native.GetSequence();Authority();if(sequence==0)throw new OperationCanceledException();
            Check();opened=native.Open();Check();if(!opened)throw new IOException("Clipboard open failed");
            Check();handle=native.GetDib();Check();if(handle==0)throw new InvalidDataException("CF_DIB unavailable");
            Check();nuint size=native.GetSize(handle);Check();if(size<40||size>4194304)throw new InvalidDataException("DIB native allocation limit");
            Check();nint pointer=native.Lock(handle);locked=pointer!=0;Check();if(!locked)throw new IOException("DIB lock failed");
            owned=new byte[checked((int)size)];allocated?.Invoke(owned);Check();
            for(int offset=0;offset<owned.Length;){Check();int count=Math.Min(65536,owned.Length-offset);native.Copy(pointer,owned,offset,count);Check();offset+=count;}
            Check();
        }
        catch(Exception error){failure=error;}
        finally
        {
            if(locked)try{if(!native.Unlock(handle))clean=false;Check();}catch(Exception error){clean=false;failure??=error;}
            if(opened)try{if(!native.Close())clean=false;Check();}catch(Exception error){clean=false;failure??=error;}
        }
        try
        {
            if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            if(!clean||owned is null)throw new IOException("DIB native cleanup failed");Check();
            var result=new NativeDibCapture(owned,sequence);owned=null;return result;
        }
        finally{if(owned is not null)CryptographicOperations.ZeroMemory(owned);}
    }
}
