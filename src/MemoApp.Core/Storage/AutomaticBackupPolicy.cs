using MemoApp.Core.Transfer;
namespace MemoApp.Core.Storage;
public static class AutomaticBackupPolicy
{
    public static string NextDestination(string directory,Guid vaultId,int capacity)
    {
        if(vaultId==Guid.Empty||capacity is <1 or >100)throw new ArgumentException("Backup limits");directory=LocalFilePath.Resolve(directory);LocalFilePath.CheckDataRoot(directory);if(!Directory.Exists(directory))throw new IOException("Select an existing backup directory");
        string prefix="memo-auto-"+vaultId.ToString("N")+"-";int count=0,enumerated=0;
        foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if(++enumerated>1024)throw new IOException("Backup directory enumeration limit");string name=Path.GetFileName(entry);
            if(name.StartsWith(prefix,StringComparison.Ordinal)&&name.EndsWith(".vault",StringComparison.Ordinal))
            {
                if((File.GetAttributes(entry)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)throw new IOException("Suspicious owned backup entry");
                if(++count>=capacity)throw new IOException("Backup capacity full; old files preserved");
            }
        }
        return Path.Combine(directory,prefix+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ",System.Globalization.CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N")+".vault");
    }
}
