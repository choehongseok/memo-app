using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Windows;
internal static partial class Program
{
    private static int SyntheticInstalledPackageRun(string package)
    {
        string temp=Path.GetFullPath(Path.GetTempPath());
        string root=Path.Combine(temp,"memo-synthetic-package-"+Guid.NewGuid().ToString("N"));
        Process? process=null;
        try
        {
            Directory.CreateDirectory(root);
            string installed=Path.Combine(root,"한글 격리 시험판");
            TrialInstaller.Install(Path.GetFullPath(package),installed);
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(package,"installation-manifest.json")));
            foreach(var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
            {
                using var file=File.OpenRead(Path.Combine(installed,entry.GetProperty("path").GetString()!));
                Require(file.Length==entry.GetProperty("size").GetInt64()&&Convert.ToHexStringLower(SHA256.HashData(file))==entry.GetProperty("sha256").GetString(),"Synthetic installed payload matches exact manifest");
            }
            Require(File.Exists(Path.Combine(installed,"Start-SyntheticTest.cmd"))&&File.Exists(Path.Combine(installed,"SYNTHETIC-TEST.txt")),"Installed package preserves isolated launcher and guide");
            Require(!Directory.Exists(Path.Combine(installed,"SyntheticTest")),"Shipping inventory contains no prior test data");
            string? firstIdentityHash=null;
            for(int launch=0;launch<2;launch++)
            {
                var start=new ProcessStartInfo(Path.Combine(installed,"MemoApp.Windows.exe")){UseShellExecute=false,WorkingDirectory=installed};
                start.ArgumentList.Add("--synthetic-test");
                process=Process.Start(start)??throw new IOException("Synthetic installed process missing");
                var timer=Stopwatch.StartNew();while(timer.Elapsed<TimeSpan.FromSeconds(30))
                {process.Refresh();if(process.HasExited||process.MainWindowHandle!=IntPtr.Zero)break;Thread.Sleep(50);}
                Require(!process.HasExited&&process.MainWindowHandle!=IntPtr.Zero&&process.MainWindowTitle=="메모앱 — 합성 자료용 시험판","Self-contained production EXE opens locked main window");
                Require(process.Modules.Cast<ProcessModule>().Any(module=>module.ModuleName.Equals("coreclr.dll",StringComparison.OrdinalIgnoreCase)&&Path.GetFullPath(module.FileName).Equals(Path.Combine(installed,"coreclr.dll"),StringComparison.OrdinalIgnoreCase)),"Production EXE uses its packaged runtime");
                Require(process.WaitForInputIdle(10000),"Synthetic product settles startup");
                Require(process.CloseMainWindow()&&process.WaitForExit(15000)&&process.ExitCode==0,"Synthetic installed product exits normally");
                process.Dispose();process=null;
                string identity=Path.Combine(installed,"SyntheticTest","UiIdentity","ui-device.id");
                Require(File.Exists(identity),"Synthetic UI identity is inside installed test root");
                string identityHash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(identity)));
                Require(firstIdentityHash is null||firstIdentityHash==identityHash,"Synthetic restart retains the exact UI identity");firstIdentityHash=identityHash;
                Require(!Directory.Exists(Path.Combine(installed,"SyntheticTest","Data"))&&!Directory.EnumerateFiles(installed,"*.vault",SearchOption.AllDirectories).Any(),"Untouched locked launch creates no vault or recovery secret");
            }
            Console.WriteLine("PASS: self-contained OCR-inclusive trial hash-checked, isolated synthetic EXE startup/restart, packaged runtime and clean exit; no ordinary profile, shortcut or startup registration requested; physical input is not established");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine("FAIL: synthetic installed package "+error);return 1;}
        finally
        {
            if(process is not null){if(!process.HasExited){process.Kill(true);process.WaitForExit(15000);}process.Dispose();}
            if(Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(temp)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
