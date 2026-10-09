using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Runtime.InteropServices;
using MemoApp.Windows;
internal static partial class Program
{
    private static int InstalledPackageRun(string package)
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-package-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        Process? process=null;
        try
        {
            string installed=Path.Combine(root,"한글 설치 폴더");TrialInstaller.Install(Path.GetFullPath(package),installed);
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(package,"installation-manifest.json")));
            foreach(var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
            {
                string file=Path.Combine(installed,entry.GetProperty("path").GetString()!);
                using var input=File.OpenRead(file);Require(input.Length==entry.GetProperty("size").GetInt64()&&Convert.ToHexStringLower(SHA256.HashData(input))==entry.GetProperty("sha256").GetString(),"Installed actual publish file length/hash matches manifest");
            }
            Require(!File.Exists(Path.Combine(installed,"createdump.exe")),"Optional runtime dump utility is excluded from application trial");
            TrialInstaller.CreateShortcut(installed,Path.Combine(root,"MemoApp Synthetic Trial.lnk"));
            var start=new ProcessStartInfo(Path.Combine(installed,"MemoApp.Windows.exe")){UseShellExecute=false,WorkingDirectory=installed};start.ArgumentList.Add("--portable");
            process=Process.Start(start)??throw new IOException("Installed process missing");
            var timer=Stopwatch.StartNew();while(timer.Elapsed<TimeSpan.FromSeconds(30)){process.Refresh();if(process.HasExited||process.MainWindowHandle!=IntPtr.Zero)break;Thread.Sleep(50);}
            Require(!process.HasExited&&process.MainWindowHandle!=IntPtr.Zero&&process.MainWindowTitle=="메모앱 — 합성 자료용 시험판","Installed EXE opens actual production main window rather than an error dialog");
            Require(process.Modules.Cast<ProcessModule>().Any(module=>module.ModuleName.Equals("coreclr.dll",StringComparison.OrdinalIgnoreCase)&&Path.GetFullPath(module.FileName).Equals(Path.Combine(installed,"coreclr.dll"),StringComparison.OrdinalIgnoreCase)),"Installed process actually loads its packaged runtime rather than runner SDK runtime");
            Require(process.CloseMainWindow()&&process.WaitForExit(15000)&&process.ExitCode==0,"Installed locked production app closes normally");
            process.Dispose();process=null;
            var trayStart=new ProcessStartInfo(Path.Combine(installed,"MemoApp.Windows.exe")){UseShellExecute=false,WorkingDirectory=installed};trayStart.ArgumentList.Add("--portable");trayStart.ArgumentList.Add("--tray-start");process=Process.Start(trayStart)??throw new IOException("Installed tray-start process missing");
            IntPtr lockedHandle=IntPtr.Zero,parkingHandle=IntPtr.Zero;timer.Restart();while(timer.Elapsed<TimeSpan.FromSeconds(30)){process.Refresh();if(process.HasExited)break;lockedHandle=InstalledWindow(process.Id);parkingHandle=InstalledWindow(process.Id,true);if(lockedHandle!=IntPtr.Zero&&parkingHandle!=IntPtr.Zero)break;Thread.Sleep(50);}
            Require(!process.HasExited&&lockedHandle!=IntPtr.Zero&&parkingHandle!=IntPtr.Zero,"Installed EXE accepts explicit tray startup and has an actual WPF application message window");
            // Send OS-ending messages to this synthetic child only. No system/session shutdown.
            Require(InstalledSend(parkingHandle,0x11,IntPtr.Zero,(IntPtr)unchecked((int)0x80000000),2,2000,out var queryResult)!=IntPtr.Zero&&queryResult==(IntPtr)1,"Installed child's WPF application receives and accepts real query-end-session boundary");
            Require(process.WaitForExit(15000)&&process.ExitCode==0,"Actual production SessionEnding shuts down the locked resident child normally");
            Require(!Directory.EnumerateFiles(installed,"*.vault",SearchOption.AllDirectories).Any(),"Launching untouched trial does not create a vault or recovery secret");
            Console.WriteLine("PASS: actual self-contained package installed/hash-checked/native shortcut/production EXE/default and locked tray-start/session-ending child boundary/clean exit; temporary app/vault path, shared CI account non-secret UI ID; not actual login/user-PC trust/ACL acceptance");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("FAIL: actual installed package "+error);return 1;}
        finally
        {
            if(process is not null){if(!process.HasExited){process.Kill(true);process.WaitForExit(15000);}process.Dispose();}
            Directory.Delete(root,true);
        }
    }
    private delegate bool InstalledEnumCallback(IntPtr hwnd,IntPtr parameter);
    private static IntPtr InstalledWindow(int processId,bool parking=false)
    {
        IntPtr found=IntPtr.Zero;InstalledEnum((hwnd,_)=>{InstalledPid(hwnd,out uint pid);if(pid!=(uint)processId)return true;var title=new StringBuilder(256);InstalledTitle(hwnd,title,title.Capacity);var type=new StringBuilder(256);InstalledClass(hwnd,type,type.Capacity);if(parking?title.Length==0&&type.ToString().StartsWith("HwndWrapper[",StringComparison.Ordinal):title.ToString()=="메모앱 — 합성 자료용 시험판"){found=hwnd;return false;}return true;},IntPtr.Zero);return found;
    }
    [DllImport("user32.dll",EntryPoint="EnumWindows")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool InstalledEnum(InstalledEnumCallback callback,IntPtr parameter);
    [DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")]private static extern uint InstalledPid(IntPtr hwnd,out uint processId);
    [DllImport("user32.dll",EntryPoint="GetWindowTextW",CharSet=CharSet.Unicode)]private static extern int InstalledTitle(IntPtr hwnd,StringBuilder title,int count);
    [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW")]private static extern IntPtr InstalledSend(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll",EntryPoint="GetClassNameW",CharSet=CharSet.Unicode)]private static extern int InstalledClass(IntPtr hwnd,StringBuilder name,int count);
}
