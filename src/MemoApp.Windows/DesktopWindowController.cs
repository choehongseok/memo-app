using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MemoApp.Core.Storage;
using MemoApp.Core.Lifecycle;
namespace MemoApp.Windows;

// Native pixel work areas + HWND DPI. User zoom is unrelated to OS DPI conversion.
public sealed class DesktopWindowController : IDisposable
{
    private readonly Window window;
    private readonly Func<bool> current;
    private readonly Action<StoredWindowLayout> publish;
    private StoredWindowLayout state;
    private HwndSource? source;
    private int suppression;
    private bool closed,disposed;
    private readonly DependencyPropertyDescriptor topmost=DependencyPropertyDescriptor.FromProperty(Window.TopmostProperty,typeof(Window));
    private readonly DependencyPropertyDescriptor opacity=DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty,typeof(Window));
    public DesktopWindowController(Window window,string kind,Guid? noteId,StoredWindowLayout? saved,Func<bool> current,Action<StoredWindowLayout> publish)
    {
        this.window=window;this.current=current;this.publish=publish;
        state=saved??DefaultState(window,kind,noteId);
        window.SourceInitialized+=SourceInitialized;window.Loaded+=Loaded;window.LocationChanged+=Changed;window.SizeChanged+=SizeChanged;
        window.DpiChanged+=DpiChanged;window.Closed+=Closed;
        topmost.AddValueChanged(window,Changed);opacity.AddValueChanged(window,Changed);
    }
    public StoredWindowLayout State=>state;
    internal static StoredWindowLayout DefaultState(Window window,string kind,Guid? noteId)=>new(kind,noteId,"DEFAULT",0.1,0.1,Math.Clamp(window.Width,200,2000),Math.Clamp(window.Height,100,1600),96);
    private IntPtr Handle=>new WindowInteropHelper(window).Handle;
    private bool CanTrack=>!disposed&&!closed&&window.IsLoaded&&suppression==0&&current()&&window.WindowState==WindowState.Normal;
    private void SourceInitialized(object? sender,EventArgs e)
    {
        source=HwndSource.FromHwnd(Handle);source?.AddHook(Hook);
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(state.PositionLocked&&suppression==0&&current())
        {
            if(message==0x112 && (((long)wParam&0xFFF0) is 0xF000 or 0xF010 or 0xF030)){handled=true;return IntPtr.Zero;}
            if(message==0x84)
            {
                long hit=(long)DefWindowProc(hwnd,message,wParam,lParam);
                if(hit==2||hit is >=10 and <=17){handled=true;return (IntPtr)1;}
            }
        }
        return IntPtr.Zero;
    }
    private async void Loaded(object sender,RoutedEventArgs e)
    {
        try{await RestoreAsync();if(CanTrack)Capture();}catch{ /* The owner reports persistence errors; placement never exposes note content. */ }
    }
    public async Task RestoreAsync()
    {
        if(closed||disposed||!current()||Handle==IntPtr.Zero)return;
        suppression++;
        try
        {
            var monitor=Monitors().FirstOrDefault(m=>m.Device==state.Monitor)??Monitor(Handle);
            GetWindowRect(Handle,out var oldRect);
            var provisional=DesktopLayoutGeometry.Provisional(Work(monitor),oldRect.Width,oldRect.Height);
            if(!SetWindowPos(Handle,IntPtr.Zero,provisional.X,provisional.Y,provisional.Width,provisional.Height,0x14))throw new InvalidOperationException("Native placement failed"); // fit wholly inside target before asking this HWND's DPI
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Loaded);
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);
            if(closed||disposed||!current()||Handle==IntPtr.Zero)return;
            var stillPresent=Monitors().FirstOrDefault(m=>m.Device==monitor.Device);
            if(stillPresent is null)
            {
                monitor=Monitor(Handle);provisional=DesktopLayoutGeometry.Provisional(Work(monitor),oldRect.Width,oldRect.Height);
                if(!SetWindowPos(Handle,IntPtr.Zero,provisional.X,provisional.Y,provisional.Width,provisional.Height,0x14))throw new InvalidOperationException("Fallback placement failed");
                await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);
                if(closed||disposed||!current()||Handle==IntPtr.Zero)return;
                monitor=Monitors().FirstOrDefault(m=>m.Device==monitor.Device)??Monitor(Handle);
            }
            else monitor=stillPresent;
            uint dpi=GetDpiForWindow(Handle);if(dpi==0)dpi=96;
            var restored=DesktopLayoutGeometry.Restore(Work(monitor),state,dpi);
            if(!SetWindowPos(Handle,IntPtr.Zero,restored.X,restored.Y,restored.Width,restored.Height,0x14))throw new InvalidOperationException("Native size/position failed");
            window.Topmost=state.Topmost;window.Opacity=state.Opacity;window.ResizeMode=state.PositionLocked?ResizeMode.CanMinimize:ResizeMode.CanResize;
        }
        finally{suppression--;}
    }
    public void SetFolded(bool value)
    {
        if(!current()||closed||value==state.Folded)return;
        if(CanTrack)Capture();state=state with{Folded=value};suppression++;
        try{window.Height=value?160:state.Height;}finally{suppression--;}
        if(CanTrack)Capture();
    }
    public void SetPositionLocked(bool value)
    {
        if(!current()||closed||value==state.PositionLocked)return;
        state=state with{PositionLocked=value};window.ResizeMode=value?ResizeMode.CanMinimize:ResizeMode.CanResize;if(CanTrack)Capture();
    }
    public void Arrange(int ordinal)
    {
        if(!current()||closed||Handle==IntPtr.Zero)return;
        var monitor=Monitor(Handle);uint dpi=GetDpiForWindow(Handle);if(dpi==0)dpi=96;
        var sized=DesktopLayoutGeometry.Restore(Work(monitor),state with{X=0,Y=0},dpi);int width=sized.Width,height=sized.Height;
        int columns=Math.Max(1,monitor.Work.Width/Math.Max(1,width+12)),column=ordinal%columns,row=ordinal/columns;
        suppression++;try{if(!SetWindowPos(Handle,IntPtr.Zero,Math.Min(monitor.Work.Left+column*(width+12),monitor.Work.Right-width),Math.Min(monitor.Work.Top+row*(height+12),monitor.Work.Bottom-height),width,height,0x14))throw new InvalidOperationException("Native arrange failed");}finally{suppression--;}
        if(CanTrack)Capture();
    }
    private void Changed(object? sender,EventArgs e){if(CanTrack)Capture();}
    private void SizeChanged(object sender,SizeChangedEventArgs e){if(CanTrack)Capture();}
    private void DpiChanged(object sender,DpiChangedEventArgs e)
    {
        window.Dispatcher.BeginInvoke(new Action(()=>{if(CanTrack)Capture();}),DispatcherPriority.Loaded);
    }
    private void Capture()
    {
        if(!GetWindowRect(Handle,out var rect))return;var monitor=Monitor(Handle);uint dpi=GetDpiForWindow(Handle);if(dpi==0)dpi=96;
        double width=Math.Clamp(rect.Width*96d/dpi,200,2000),height=state.Folded?state.Height:Math.Clamp(rect.Height*96d/dpi,100,1600);
        state=state with{Monitor=monitor.Device,X=Math.Clamp((rect.Left-monitor.Work.Left)/(double)monitor.Work.Width,0,1),Y=Math.Clamp((rect.Top-monitor.Work.Top)/(double)monitor.Work.Height,0,1),Width=width,Height=height,Dpi=dpi,Open=true,Topmost=window.Topmost,Opacity=Math.Clamp(window.Opacity,0.3,1)};
        publish(state);
    }
    private void Closed(object? sender,EventArgs e)
    {
        if(!closed&&current())publish(state with{Open=false});closed=true;Dispose();
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;source?.RemoveHook(Hook);source=null;
        window.SourceInitialized-=SourceInitialized;window.Loaded-=Loaded;window.LocationChanged-=Changed;window.SizeChanged-=SizeChanged;window.DpiChanged-=DpiChanged;window.Closed-=Closed;
        topmost.RemoveValueChanged(window,Changed);opacity.RemoveValueChanged(window,Changed);
    }
    private sealed record MonitorArea(string Device,Rect Work);
    private static PixelWorkArea Work(MonitorArea monitor)=>new(monitor.Work.Left,monitor.Work.Top,monitor.Work.Width,monitor.Work.Height);
    private static MonitorArea Monitor(IntPtr windowHandle)=>Area(MonitorFromWindow(windowHandle,2));
    private static MonitorArea Area(IntPtr handle)
    {
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>(),Device=""};
        if(!GetMonitorInfo(handle,ref info)||info.Work.Width<1||info.Work.Height<1)throw new InvalidOperationException("Monitor work area unavailable");return new(info.Device,info.Work);
    }
    private static List<MonitorArea> Monitors()
    {
        var result=new List<MonitorArea>();EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr h,IntPtr d,ref Rect r,IntPtr p)=>{result.Add(Area(h));return true;},IntPtr.Zero);return result;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;public readonly int Width=>Right-Left;public readonly int Height=>Bottom-Top;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct MonitorInfo{public int Size;public Rect Monitor,Work;public int Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    private delegate bool MonitorEnum(IntPtr monitor,IntPtr dc,ref Rect rect,IntPtr data);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr window,int flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetMonitorInfoW")]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorEnum callback,IntPtr data);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,int flags);
    [DllImport("user32.dll")]private static extern IntPtr DefWindowProc(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
}
