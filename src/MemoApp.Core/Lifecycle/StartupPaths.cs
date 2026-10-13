using MemoApp.Core.Transfer;
namespace MemoApp.Core.Lifecycle;
public static class StartupPaths
{
    public static string Resolve(string[] args, string programDirectory, string localAppData)
    {
        ArgumentNullException.ThrowIfNull(args);
        string path;
        if (args.Length == 0) path = Path.Combine(localAppData, "MemoApp", "SyntheticTrial");
        else if (args.Length == 1 && args[0] == "--portable") path = Path.Combine(programDirectory, "Data");
        else if (args.Length == 1 && args[0] == "--synthetic-test") path = Path.Combine(SyntheticRoot(programDirectory), "Data");
        else throw new ArgumentException("Supported explicit startup arguments: --portable or --synthetic-test");
        path = LocalFilePath.Resolve(path); LocalFilePath.CheckDataRoot(path);
        return path;
    }
    public static string ResolveIdentityRoot(StartupOptions options,string programDirectory,string localAppData)
    {
        ArgumentNullException.ThrowIfNull(options);
        string path=options.SyntheticTest?Path.Combine(SyntheticRoot(programDirectory),"UiIdentity"):Path.Combine(localAppData,"MemoApp");
        path=LocalFilePath.Resolve(path);LocalFilePath.CheckDataRoot(path);return path;
    }
    private static string SyntheticRoot(string programDirectory)
    {
        if(!Path.IsPathFullyQualified(programDirectory))throw new ArgumentException("Synthetic test requires an absolute executable directory");
        string path=LocalFilePath.Resolve(Path.Combine(programDirectory,"SyntheticTest"));
        LocalFilePath.CheckDataRoot(path);return path;
    }
}
