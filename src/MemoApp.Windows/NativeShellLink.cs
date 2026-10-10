using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace MemoApp.Windows;

// IShellLinkW vtable, explicitly Unicode (never WSH's automation path conversion).
// https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishelllinkw
internal static class NativeShellLink
{
    [ComImport,Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkClass { }
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int capacity,IntPtr findData,uint flags);
        void GetIDList(out IntPtr list);void SetIDList(IntPtr list);
        void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string text);
        void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string path);
        void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder arguments,int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string arguments);
        void GetHotkey(out ushort hotkey);void SetHotkey(ushort hotkey);
        void GetShowCmd(out int command);void SetShowCmd(int command);
        void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int capacity,out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string path,int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,uint reserved);
        void Resolve(IntPtr window,uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)]string path);
    }
    internal static void Save(string target,string workingDirectory,string path)
    {
        object instance=new ShellLinkClass();
        try
        {
            var link=(IShellLinkW)instance;link.SetPath(target);link.SetArguments("");link.SetWorkingDirectory(workingDirectory);link.SetDescription("메모앱 합성 자료용 시험판");((IPersistFile)instance).Save(path,true);
        }
        finally{Marshal.FinalReleaseComObject(instance);}
    }
    internal static (string Target,string Arguments,string WorkingDirectory) Read(string path)
    {
        object instance=new ShellLinkClass();
        try
        {
            ((IPersistFile)instance).Load(path,0);var link=(IShellLinkW)instance;
            var target=new StringBuilder(32768);var arguments=new StringBuilder(32768);var working=new StringBuilder(32768);
            link.GetPath(target,target.Capacity,IntPtr.Zero,4);link.GetArguments(arguments,arguments.Capacity);link.GetWorkingDirectory(working,working.Capacity);
            return(target.ToString(),arguments.ToString(),working.ToString());
        }
        finally{Marshal.FinalReleaseComObject(instance);}
    }
}
