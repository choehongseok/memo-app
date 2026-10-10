using MemoApp.Core.Lifecycle;
internal static class StartupChecks
{
    internal static void Run()
    {
        string program = Path.Combine(Path.GetTempPath(), "memo-startup-program"); string appData = Path.Combine(Path.GetTempPath(), "memo-startup-local");
        VaultChecks.Require(StartupPaths.Resolve([], program, appData) == Path.Combine(appData, "MemoApp", "SyntheticTrial"), "default must keep per-user data root");
        VaultChecks.Require(StartupPaths.Resolve(["--portable"], program, appData) == Path.Combine(program, "Data"), "portable must explicitly use exe-side Data");
        VaultChecks.ExpectFailure(() => StartupPaths.Resolve(["--unknown"], program, appData), "unknown switches must not silently select a different data root");
        VaultChecks.ExpectFailure(() => StartupPaths.Resolve(["--portable", "--portable"], program, appData), "ambiguous/repeated startup arguments reject");
        string temporary = Path.Combine(Path.GetTempPath(),"memo-startup-links-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            string link=Path.Combine(temporary,"linked-program"); Directory.CreateSymbolicLink(link,temporary);
            VaultChecks.ExpectFailure(()=>StartupPaths.Resolve(["--portable"],link,appData),"portable linked ancestor must reject before vault access"); Directory.Delete(link);
            string dataLink=Path.Combine(temporary,"Data"); Directory.CreateSymbolicLink(dataLink,temporary);
            VaultChecks.ExpectFailure(()=>StartupPaths.Resolve(["--portable"],temporary,appData),"portable linked Data root must reject"); Directory.Delete(dataLink);
        }
        finally {Directory.Delete(temporary,true);}
        Console.WriteLine("PASS: default per-user root, explicit portable exe-side Data and argument rejection");
    }
}
