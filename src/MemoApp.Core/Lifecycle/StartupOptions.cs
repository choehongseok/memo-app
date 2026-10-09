using MemoApp.Core.Transfer;
namespace MemoApp.Core.Lifecycle;
public sealed record StartupOptions(bool Portable,bool TrayStart)
{
    public string[] DataArguments=>Portable?["--portable"]:[];
    public static StartupOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);bool portable=false,tray=false;
        if(args.Length>2)throw new ArgumentException("Too many startup arguments");
        foreach(string option in args)
        {
            if(option=="--portable"&&!portable)portable=true;
            else if(option=="--tray-start"&&!tray)tray=true;
            else throw new ArgumentException("Unknown/repeated startup argument");
        }
        return new(portable,tray);
    }
}
public static class StartupCommand
{
    public static string Create(string executable,bool portable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        if(executable.Length>260||executable.Contains('"')||!Path.IsPathFullyQualified(executable))throw new IOException("Invalid startup executable path");
        string path=LocalFilePath.Resolve(executable);
        if(!Path.GetFileName(path).Equals("MemoApp.Windows.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Startup must target the actual app executable");
        string command="\""+path+"\""+(portable?" --portable":"")+" --tray-start";
        if(command.Length>260)throw new IOException("Run command exceeds Windows 260-character limit");
        LocalFilePath.CheckAncestors(path,true);using var source=LocalRegularFile.Open(path);
        return command;
    }
}
