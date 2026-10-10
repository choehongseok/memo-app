using MemoApp.Core.Documents;
namespace MemoApp.Core.Storage;
// Structural DTO only. Native directory identity and filesystem checks belong to the Windows boundary.
public sealed record StoredSourceRootBinding(string RootPath,ulong VolumeSerialNumber,string DirectoryFileId);
public sealed record StoredAutomaticBackupPolicy(string Directory,Guid VaultIdentity,StoredSourceRootBinding SourceRootBinding,int Capacity,
    bool AfterSave,bool Daily,bool OnExit,DateOnly? LastAttemptDay=null);
internal static class AutomaticBackupPolicyValidation
{
    internal static void Validate(StoredAutomaticBackupPolicy? policy,int schema)
    {
        if(policy is null)return;
        if(schema<9||policy.VaultIdentity==Guid.Empty||policy.Capacity is <1 or >100||policy.AfterSave&&policy.Daily||!(policy.AfterSave||policy.Daily||policy.OnExit)||policy.SourceRootBinding is null)throw new InvalidDataException("Invalid backup policy schema/limits/trigger");
        DirectoryPath(policy.Directory);DirectoryPath(policy.SourceRootBinding.RootPath);
        string id=policy.SourceRootBinding.DirectoryFileId;
        if(id is null||id.Length!=32||id.Any(c=>!char.IsAsciiHexDigit(c))||id.All(c=>c=='0'))throw new InvalidDataException("Invalid 128-bit source directory identity");
    }
    private static void DirectoryPath(string path)
    {
        if(path is null||path.Length is <1 or >1024||!RichDocumentCodec.IsWellFormedUnicode(path)||path.Any(char.IsControl)||path.Contains('"'))throw new InvalidDataException("Backup directory path bounds");
        bool windows=path.Length>=3&&char.IsAsciiLetter(path[0])&&path[1]==':'&&path[2]=='\\';
        string relative;
        if(windows){if(path[2..].Any(c=>c is ':' or '/'))throw new InvalidDataException("Nonordinary Windows directory path");relative=path[3..];}
        else{if(path[0]!='/'||path.StartsWith("//",StringComparison.Ordinal)||path.Any(c=>c is ':' or '\\'))throw new InvalidDataException("Nonordinary local directory path");relative=path[1..];}
        if(relative.Length==0)return;
        foreach(string segment in relative.Split(windows?'\\':'/'))
        {
            string stem=segment.Split('.')[0].ToUpperInvariant();
            if(segment.Length is <1 or >256||segment.Trim()!=segment||segment is "." or ".."||windows&&(segment.EndsWith('.')||segment.Any(c=>c is '<' or '>' or '|' or '?' or '*')||stem is "CON" or "PRN" or "AUX" or "NUL"||stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&"123456789¹²³".Contains(stem[3])))throw new InvalidDataException("Noncanonical/reserved directory path segment");
        }
    }
}
