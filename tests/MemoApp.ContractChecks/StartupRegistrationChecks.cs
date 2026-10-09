using MemoApp.Core.Lifecycle;
internal static class StartupRegistrationChecks
{
    internal static void Run()
    {
        VaultChecks.Require(StartupOptions.Parse([])==new StartupOptions(false,false)&&StartupOptions.Parse(["--tray-start","--portable"])==new StartupOptions(true,true)&&StartupOptions.Parse(["--portable","--tray-start"]).DataArguments.SequenceEqual(["--portable"]),"Explicit tray start cannot alter existing portable/default data selection");
        foreach(var args in new[]{new[]{"--tray-start","--tray-start"},new[]{"--portable","--portable"},new[]{"--install","--tray-start"},new[]{"--TRAY-START"},new[]{""},new[]{"--portable","--tray-start","--other"}})VaultChecks.ExpectFailure(()=>StartupOptions.Parse(args),"Unknown/repeated/installer startup arguments reject");
        string dir=Path.Combine(Path.GetTempPath(),"memo-startup-contract-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            string exe=Path.Combine(dir,"MemoApp.Windows.exe");File.WriteAllText(exe,"SYNTHETIC_NOT_EXECUTABLE");byte[] original=File.ReadAllBytes(exe);
            VaultChecks.Require(StartupCommand.Create(exe,false)=="\""+exe+"\" --tray-start"&&StartupCommand.Create(exe,true)=="\""+exe+"\" --portable --tray-start"&&File.ReadAllBytes(exe).SequenceEqual(original),"Exact quoted command/fixed arguments preserve source bytes");
            foreach(string path in new[]{"MemoApp.Windows.exe",exe+"\n","//server/app/MemoApp.Windows.exe","file:///app/MemoApp.Windows.exe",exe+":stream",Path.Combine(dir,"other.exe"),Path.Combine(dir,"\"MemoApp.Windows.exe"),Path.Combine(dir,new string('x',260),"MemoApp.Windows.exe")})VaultChecks.ExpectFailure(()=>StartupCommand.Create(path,false),"Relative/network/URI/stream/control/quote/host/long startup paths refused");
            string folder=Path.Combine(dir,"linked");Directory.CreateSymbolicLink(folder,dir);VaultChecks.ExpectFailure(()=>StartupCommand.Create(Path.Combine(folder,"MemoApp.Windows.exe"),false),"Linked startup executable ancestor refused");Directory.Delete(folder);
            int prefix=dir.Length+1,suffix="/MemoApp.Windows.exe".Length,tail="\"\" --tray-start".Length;int segment=260-prefix-suffix-tail;
            if(segment is >0 and <256)
            {
                string exactDir=Path.Combine(dir,new string('a',segment));Directory.CreateDirectory(exactDir);string exact=Path.Combine(exactDir,"MemoApp.Windows.exe");File.WriteAllText(exact,"SYNTHETIC");VaultChecks.Require(StartupCommand.Create(exact,false).Length==260,"Entire Run command accepts exact 260-character boundary");VaultChecks.ExpectFailure(()=>StartupCommand.Create(exact,true),"Fixed portable argument makes entire Run command too long before modification");
            }
        }
        finally{Directory.Delete(dir,true);}
        Console.WriteLine("PASS: strict startup options, exact quoted fixed command, actual regular path/source preservation and entire 260-character Run boundary (no registry writes)");
    }
}
