using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace MemoApp.Windows;
internal interface ITrayIcon : IDisposable
{
    bool Registered { get; }
    bool Focus();
}
// Only the generic application tooltip/icon crosses into Explorer. No note metadata/balloons.
internal sealed class NativeTrayIcon : ITrayIcon
{
    private HwndSource? source;
    private Action? open,menu,lost;
    private readonly uint taskbarCreated;
    private bool disposed;
    public bool Registered { get; private set; }
    private const int Callback=0x8000+73;
    internal NativeTrayIcon(Action open,Action menu,Action lost)
    {
        this.open=open;this.menu=menu;this.lost=lost;
        try
        {
            taskbarCreated=RegisterWindowMessageW("TaskbarCreated");
            if(taskbarCreated==0)throw new InvalidOperationException("Tray restart notification unavailable");
            source=new HwndSource(new HwndSourceParameters("MemoAppTray"){Width=1,Height=1,PositionX=-32000,PositionY=-32000,WindowStyle=0,ExtendedWindowStyle=0x80});
            source.AddHook(Hook);Registered=Add();
            if(!Registered)throw new InvalidOperationException("Tray registration refused");
        }
        catch{Dispose();throw;}
    }
    private bool Add()
    {
        if(disposed||source is null)return false;
        var data=Data();return data.hIcon!=IntPtr.Zero&&Shell_NotifyIconW(0,ref data);
    }
    private NotifyIconData Data()=>new(){cbSize=(uint)Marshal.SizeOf<NotifyIconData>(),hWnd=source?.Handle??IntPtr.Zero,uID=1,uFlags=7,uCallbackMessage=Callback,hIcon=LoadIconW(IntPtr.Zero,(IntPtr)32512),szTip="메모앱",szInfo="",szInfoTitle=""};
    public bool Focus()=>!disposed&&Registered&&source is not null&&SetForegroundWindow(source.Handle);
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(disposed)return IntPtr.Zero;
        try
        {
            if((uint)message==taskbarCreated){Registered=false;Registered=Add();if(!Registered)lost?.Invoke();}
            else if(message==Callback&&Registered&&(long)wParam==1)
            {
                int action=unchecked((int)(long)lParam);
                if(action==0x203){handled=true;open?.Invoke();}
                else if(action is 0x205 or 0x7B){handled=true;menu?.Invoke();}
            }
        }
        catch{Registered=false;try{lost?.Invoke();}catch{}}
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;Registered=false;open=menu=lost=null;
        var owned=source;source=null;if(owned is null)return;
        try{owned.RemoveHook(Hook);}catch{}
        try{var data=Data();data.hWnd=owned.Handle;Shell_NotifyIconW(2,ref data);}catch{}
        try{owned.Dispose();}catch{}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;public IntPtr hWnd;public uint uID,uFlags,uCallbackMessage;public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string szTip;
        public uint dwState,dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string szInfoTitle;
        public uint dwInfoFlags;public Guid guidItem;public IntPtr hBalloonIcon;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool Shell_NotifyIconW(uint message,ref NotifyIconData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern IntPtr LoadIconW(IntPtr instance,IntPtr name);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetForegroundWindow(IntPtr hwnd);
}
