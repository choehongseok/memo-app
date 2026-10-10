using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;

namespace MemoApp.Windows;

// Metadata only: no vault bytes, recovery secrets, or key access.
internal static class AutomaticBackupRootBinding
{
    internal static StoredSourceRootBinding Capture(string root)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Native source root identity requires Windows");
        try
        {
            root=Normalize(root);
            // A virtual child lets the shared ancestor audit inspect the root itself,
            // including a drive root. No child is opened or created. As in the shared
            // preflight, ancestors are not pinned against hostile concurrent replacement.
            LocalFilePath.CheckAncestors(Path.Combine(root,".memo-source-root-audit"),false);
            using SafeFileHandle handle=CreateFileW(root,FileReadAttributes,ShareReadWriteDelete,IntPtr.Zero,OpenExisting,
                OpenReparsePoint|BackupSemantics,IntPtr.Zero);
            if(handle.IsInvalid)throw NativeFailure();
            if(!GetAttributeTagInformation(handle,FileAttributeTagInfo,out var attributes,(uint)Marshal.SizeOf<AttributeTagInformation>()))throw NativeFailure();
            if((attributes.Attributes&(uint)FileAttributes.Directory)==0||(attributes.Attributes&(uint)FileAttributes.ReparsePoint)!=0||attributes.ReparseTag!=0)
                throw new IOException("Source root must be an ordinary directory");
            if(!GetIdInformation(handle,FileIdInfo,out var identity,(uint)Marshal.SizeOf<IdInformation>()))throw NativeFailure();
            if(identity.Low==0&&identity.High==0)throw new IOException("Source directory native identity is unavailable");
            byte[] identifier=new byte[16];
            BitConverter.TryWriteBytes(identifier.AsSpan(0,8),identity.Low);
            BitConverter.TryWriteBytes(identifier.AsSpan(8,8),identity.High);
            return new(root,identity.VolumeSerialNumber,Convert.ToHexString(identifier));
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
        {
            // Do not expose user paths through native/preflight exception messages.
            throw new IOException("Source directory identity could not be verified");
        }
    }

    internal static bool Matches(string root,StoredSourceRootBinding binding)
    {
        if(binding is null||binding.DirectoryFileId is not string id||id.Length!=32||id.Any(c=>!char.IsAsciiHexDigit(c))||id.All(c=>c=='0'))return false;
        try
        {
            string normalized=Normalize(root),stored=Normalize(binding.RootPath);
            if(!string.Equals(normalized,stored,StringComparison.OrdinalIgnoreCase))return false;
            var current=Capture(normalized);
            return current.VolumeSerialNumber==binding.VolumeSerialNumber&&string.Equals(current.DirectoryFileId,id,StringComparison.OrdinalIgnoreCase);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException){return false;}
    }

    private static string Normalize(string root)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Native source root identity requires Windows");
        if(string.IsNullOrEmpty(root)||!Path.IsPathFullyQualified(root)||root.Length<3||!char.IsAsciiLetter(root[0])||root[1]!=':'||root[2] is not ('\\' or '/'))
            throw new IOException("An absolute ordinary local source root is required");
        return Path.TrimEndingDirectorySeparator(LocalFilePath.Resolve(root));
    }

    private static IOException NativeFailure()=>new("Native source directory metadata is unavailable (Windows error "+Marshal.GetLastPInvokeError()+")");
    private const uint FileReadAttributes=0x80,ShareReadWriteDelete=0x7,OpenExisting=3,OpenReparsePoint=0x00200000,BackupSemantics=0x02000000;
    private const int FileAttributeTagInfo=9,FileIdInfo=18;
    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInformation{public uint Attributes;public uint ReparseTag;}
    // FILE_ID_INFO = ULONGLONG + FILE_ID_128 (16 native bytes), total 24 bytes.
    // https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info
    [StructLayout(LayoutKind.Sequential)]
    private struct IdInformation{public ulong VolumeSerialNumber;public ulong Low;public ulong High;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",EntryPoint="GetFileInformationByHandleEx",ExactSpelling=true,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAttributeTagInformation(SafeFileHandle handle,int informationClass,out AttributeTagInformation information,uint size);
    [DllImport("kernel32.dll",EntryPoint="GetFileInformationByHandleEx",ExactSpelling=true,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIdInformation(SafeFileHandle handle,int informationClass,out IdInformation information,uint size);
}
