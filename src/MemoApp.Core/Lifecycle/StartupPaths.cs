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
        else throw new ArgumentException("Supported startup arguments: --portable (explicit only)");
        path = LocalFilePath.Resolve(path); LocalFilePath.CheckDataRoot(path);
        return path;
    }
}
