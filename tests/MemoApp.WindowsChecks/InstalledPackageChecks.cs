using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
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
            TrialInstaller.CreateShortcut(installed,Path.Combine(root,"MemoApp Synthetic Trial.lnk"));
            var start=new ProcessStartInfo(Path.Combine(installed,"MemoApp.Windows.exe")){UseShellExecute=false,WorkingDirectory=installed};start.ArgumentList.Add("--portable");
            process=Process.Start(start)??throw new IOException("Installed process missing");
            var timer=Stopwatch.StartNew();while(timer.Elapsed<TimeSpan.FromSeconds(30)){process.Refresh();if(process.HasExited||process.MainWindowHandle!=IntPtr.Zero)break;Thread.Sleep(50);}
            Require(!process.HasExited&&process.MainWindowHandle!=IntPtr.Zero,"Installed self-contained EXE opens actual Windows main window without external runtime");
            Require(process.CloseMainWindow()&&process.WaitForExit(15000)&&process.ExitCode==0,"Installed locked production app closes normally");
            Require(!Directory.EnumerateFiles(installed,"*.vault",SearchOption.AllDirectories).Any(),"Launching untouched trial does not create a vault or recovery secret");
            Console.WriteLine("PASS: actual self-contained package installed/hash-checked/native shortcut/production EXE window/clean exit; synthetic temporary path only; not user-PC trust/ACL acceptance");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("FAIL: actual installed package "+error);return 1;}
        finally
        {
            if(process is not null){if(!process.HasExited){process.Kill(true);process.WaitForExit(15000);}process.Dispose();}
            Directory.Delete(root,true);
        }
    }
}
