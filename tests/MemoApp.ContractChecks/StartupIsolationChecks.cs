using MemoApp.Core.Lifecycle;
internal static class StartupIsolationChecks
{
    internal static void Run()
    {
        string program=Path.Combine(Path.GetTempPath(),"memo-isolation-program-"+Guid.NewGuid().ToString("N"));
        string local=Path.Combine(Path.GetTempPath(),"memo-isolation-forbidden-"+Guid.NewGuid().ToString("N"));
        var test=StartupOptions.Parse(["--synthetic-test"]);
        Require(test.SyntheticTest&&!test.Portable&&!test.TrayStart,"explicit synthetic option must not enable portable or tray start");
        Require(test.DataArguments.SequenceEqual(["--synthetic-test"]),"synthetic selection must survive data argument forwarding");
        Require(StartupPaths.Resolve(test.DataArguments,program,local)==Path.Combine(program,"SyntheticTest","Data"),"synthetic vault must stay under executable test directory");
        Require(StartupPaths.ResolveIdentityRoot(test,program,local)==Path.Combine(program,"SyntheticTest","UiIdentity"),"synthetic UI identity must stay separate from user identity");
        Require(!Directory.Exists(program)&&!Directory.Exists(local),"path preflight must not create or inspect user identity files");
        Require(StartupPaths.ResolveIdentityRoot(StartupOptions.Parse([]),program,local)==Path.Combine(local,"MemoApp"),"ordinary identity selection must remain unchanged");
        Require(StartupPaths.ResolveIdentityRoot(StartupOptions.Parse(["--portable"]),program,local)==Path.Combine(local,"MemoApp"),"ordinary portable identity selection must remain unchanged");
        foreach(var invalid in new[]{new[]{"--synthetic-test","--portable"},new[]{"--portable","--synthetic-test"},new[]{"--synthetic-test","--tray-start"},new[]{"--synthetic-test","--install"},new[]{"--synthetic-test","--synthetic-test"}})
            Reject(()=>StartupOptions.Parse(invalid),"synthetic mode must reject combinations before startup side effects");
        Reject(()=>StartupPaths.Resolve(["--synthetic-test"],"relative-program",local),"synthetic mode must refuse an ambiguous executable directory");
        Directory.CreateDirectory(program);
        string occupied=Path.Combine(program,"SyntheticTest");
        File.WriteAllText(occupied,"SYNTHETIC_NOT_A_DIRECTORY");
        try
        {
            Reject(()=>StartupPaths.Resolve(test.DataArguments,program,local),"non-directory test root must be rejected before vault access");
            Reject(()=>StartupPaths.ResolveIdentityRoot(test,program,local),"non-directory test root must be rejected before identity access");
        }
        finally{File.Delete(occupied);Directory.Delete(program);}
        Console.WriteLine("PASS: explicit synthetic startup roots, unchanged ordinary paths, argument refusal and no path creation");
    }
    private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    private static void Reject(Action action,string reason){try{action();}catch(ArgumentException){return;}catch(IOException){return;}throw new InvalidOperationException(reason);}
}
