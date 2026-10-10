using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
namespace MemoApp.Core.Transfer;
internal static class LocalRegularFile
{
    internal static FileStream OpenCreationLock(string path)
    {
        LocalFilePath.CheckAncestors(path, false);
        if (OperatingSystem.IsWindows())
        {
            try { if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new IOException("Invalid UI identity creation lock"); }
            catch (FileNotFoundException) { }
            return new(path, new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite, BufferSize = 1 });
        }
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) throw new IOException("UI identity creation locking is unsupported");
        try
        {
            int status = Statx(-100, path, 0x100, 1, out var before);
            if (status == 0 ? !Regular(before) : Marshal.GetLastPInvokeError() != 2) throw new IOException("Invalid UI identity creation lock");
            int fd = NativeOpenCreate(path, 2 | 0x40 | 0x80000 | 0x20000 | 0x800 | 0x100, 0x180); // RDWR|CREAT|CLOEXEC|NOFOLLOW|NONBLOCK|NOCTTY, 0600; never truncate.
            if (fd < 0) throw new IOException("UI identity creation lock cannot be opened");
            var handle = new SafeFileHandle(new IntPtr(fd), true);
            try
            {
                if (Statx(fd, "", 0x1000, 1, out var actual) != 0 || !Regular(actual)) throw new IOException("Opened UI identity lock is not regular");
                return new FileStream(handle, FileAccess.ReadWrite, 1, false);
            }
            catch { handle.Dispose(); throw; }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        { throw new IOException("UI identity regular-file validation is unavailable"); }
    }
    internal static FileStream Open(string path)
    {
        if(OperatingSystem.IsWindows())return new(path,new FileStreamOptions{Mode=FileMode.Open,Access=FileAccess.Read,Share=FileShare.Read,BufferSize=1,Options=FileOptions.SequentialScan});
        if(!OperatingSystem.IsLinux()||OperatingSystem.IsAndroid())throw new IOException("Opaque source reader is unsupported on this platform");
        try
        {
            // Linux stable statx ABI: type bit1, mode at28, complete struct256 bytes.
            // Check path without following the leaf, then open nonblocking and verify the actual descriptor.
            if(Statx(-100,path,0x100,1,out var before)!=0||!Regular(before))throw new IOException("Source is not a regular file");
            int fd=NativeOpen(path,0x80000|0x20000|0x800); // CLOEXEC | NOFOLLOW | NONBLOCK | RDONLY
            if(fd<0)throw new IOException("Regular source cannot be opened");
            var handle=new SafeFileHandle(new IntPtr(fd),true);
            try
            {
                if(Statx(fd,"",0x1000,1,out var actual)!=0||!Regular(actual))throw new IOException("Opened source is not a regular file");
                return new FileStream(handle,FileAccess.Read,1,false);
            }
            catch{handle.Dispose();throw;}
        }
        catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException)
        {throw new IOException("Regular-file source validation is unavailable");}
    }
    private static bool Regular(LinuxStatx value)=>(value.Mask&1)!=0&&(value.Mode&0xf000)==0x8000;
    [StructLayout(LayoutKind.Explicit,Size=256)]
    private struct LinuxStatx
    { [FieldOffset(0)]public uint Mask;[FieldOffset(28)]public ushort Mode; }
    [DllImport("libc.so.6",EntryPoint="statx",SetLastError=true,CallingConvention=CallingConvention.Cdecl)]
    private static extern int Statx(int directory,[MarshalAs(UnmanagedType.LPUTF8Str)]string path,int flags,uint mask,out LinuxStatx result);
    [DllImport("libc.so.6",EntryPoint="open",SetLastError=true,CallingConvention=CallingConvention.Cdecl)]
    private static extern int NativeOpen([MarshalAs(UnmanagedType.LPUTF8Str)]string path,int flags);
    [DllImport("libc.so.6",EntryPoint="open",SetLastError=true,CallingConvention=CallingConvention.Cdecl)]
    private static extern int NativeOpenCreate([MarshalAs(UnmanagedType.LPUTF8Str)]string path,int flags,uint mode);
}
