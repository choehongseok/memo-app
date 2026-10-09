using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
namespace MemoApp.Windows;

// New, per-user, unsigned trial installation only. Never upgrades, opens a vault or launches payloads.
internal static class TrialInstaller
{
    private const string ManifestName="installation-manifest.json";
    private sealed record Entry(string Path,long Size,string Hash);
    private static readonly string[] required=["MemoApp.Windows.exe","MemoApp.Windows.dll","MemoApp.Windows.deps.json","MemoApp.Windows.runtimeconfig.json","Markdig.dll","licenses/Markdig1.4.0.txt","Start-Portable.cmd","Install-User.cmd"];
    internal static void Prompt()
    {
        string target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","MemoApp","SyntheticTrial");
        if(MessageBox.Show($"서명 없는 합성 자료용 시험판을 아래 사용자 폴더에 설치합니다. 관리자 권한·자동 실행·업데이트는 사용하지 않으며 기존 설치가 있으면 거절합니다. 메모 자료는 별도 폴더에 보존됩니다.\n\n{target}\n\n설치하시겠습니까?","메모앱 설치",MessageBoxButton.YesNo,MessageBoxImage.Information)!=MessageBoxResult.Yes)return;
        try
        {
            Install(AppContext.BaseDirectory,target);string shortcut="시작 메뉴 바로가기를 만들었습니다.";
            try{CreateShortcut(target,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),"Programs","MemoApp Synthetic Trial.lnk"));}
            catch(Exception error){shortcut="바로가기는 만들지 못했습니다. 설치 폴더의 MemoApp.Windows.exe로 실행하세요.\n"+error.Message;}
            MessageBox.Show($"설치했습니다. {shortcut}\n\n{target}\n\n삭제할 때는 앱을 완전히 종료한 뒤 이 실행 파일 폴더와 바로가지만 제거하세요. 별도 메모 자료 폴더는 보존하세요.","메모앱 설치");
        }
        catch(Exception error){MessageBox.Show("설치하지 못했습니다. 기존 설치와 메모 자료는 바꾸지 않았습니다. 패키지·경로·기존 설치·쓰기 권한을 확인하세요.\n\n"+error.Message,"메모앱 설치");}
    }
    internal class InstallFiles
    {
        internal virtual FileStream CreateDestination(string path)=>new(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        internal virtual void Publish(string source,string target)=>Directory.Move(source,target);
    }
    internal static void Install(string source,string target)=>InstallUsing(source,target,new InstallFiles());
    internal static void InstallUsing(string source,string target,InstallFiles files)
    {
        source=LocalPath(source);target=LocalPath(target);
        if(source.Equals(target,StringComparison.OrdinalIgnoreCase)||target.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(target)||File.Exists(target))throw Refused();
        var entries=ReadManifest(source);var names=entries.Select(x=>x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);names.Add(ManifestName);
        var sourceFiles=Inventory(source);if(!sourceFiles.SetEquals(names))throw Refused();
        foreach(var item in entries)VerifyFile(Path.Combine(source,item.Path),item);
        string parent=Path.GetDirectoryName(target)!;LocalPath(parent);Directory.CreateDirectory(parent);LocalPath(parent);
        string stage=Path.Combine(parent,".memo-install-"+Guid.NewGuid().ToString("N"));if(Directory.Exists(stage)||File.Exists(stage))throw Refused();

        try
        {
            Directory.CreateDirectory(stage);LocalPath(stage);
            foreach(var item in entries)
            {
                LocalPath(stage);string destination=Path.Combine(stage,item.Path);string folder=Path.GetDirectoryName(destination)!;
                if(!Directory.Exists(folder)){Directory.CreateDirectory(folder);}LocalPath(folder);
                using var input=File.Open(Path.Combine(source,item.Path),FileMode.Open,FileAccess.Read,FileShare.Read);if(input.Length!=item.Size)throw Refused();
                using var output=files.CreateDestination(destination);
                input.CopyTo(output,16384);output.Flush(true);if(output.Length!=item.Size)throw Refused();output.Position=0;
                if(Convert.ToHexStringLower(SHA256.HashData(output))!=item.Hash)throw Refused();
            }
            // Manifest is never a command or source of target/shortcut authority.
            LocalPath(stage);LocalPath(parent);if(Directory.Exists(target)||File.Exists(target))throw Refused();files.Publish(stage,target);
        }
        catch(Exception error)
        {
            // No path-based rollback: a replaced ordinary directory can pass a reparse preflight.
            // Leave only this attempt's new app stage rather than risk deleting replacement files.
            throw new IOException($"새 임시 설치 폴더를 보존했습니다: {stage}",error);
        }
    }
    private static Entry[] ReadManifest(string source)
    {
        string path=LocalPath(Path.Combine(source,ManifestName));using var input=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(input.Length is <1 or >262144)throw Refused();
        var bytes=new byte[(int)input.Length];input.ReadExactly(bytes);using var document=JsonDocument.Parse(bytes,new(){MaxDepth=8});var root=document.RootElement;Fields(root,["schemaVersion","sourceCommit","files"]);
        if(root.GetProperty("schemaVersion").GetInt32()!=1||!Regex.IsMatch(root.GetProperty("sourceCommit").GetString()??"","\\A[0-9a-f]{40}\\z"))throw Refused();
        var files=root.GetProperty("files");if(files.ValueKind!=JsonValueKind.Array||files.GetArrayLength() is <1 or >512)throw Refused();
        var result=new List<Entry>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var file in files.EnumerateArray())
        {
            Fields(file,["path","size","sha256"]);string name=file.GetProperty("path").GetString()??"",hash=file.GetProperty("sha256").GetString()??"";long size=file.GetProperty("size").GetInt64();
            if(name.Split('/').Length>2||name.Length is <1 or >180||!names.Add(name)||size is <0 or >67108864||!Regex.IsMatch(hash,"\\A[0-9a-f]{64}\\z"))throw Refused();
            foreach(string segment in name.Split('/'))
            {
                if(!Regex.IsMatch(segment,"\\A[A-Za-z0-9_-][A-Za-z0-9._-]*\\z")||segment.EndsWith('.'))throw Refused();
                string stem=segment.Split('.')[0].ToUpperInvariant();if(stem is "CON" or "PRN" or "AUX" or "NUL"||Regex.IsMatch(stem,"\\A(?:COM|LPT)[1-9]\\z"))throw Refused();
            }
            if(!(name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||required.Contains(name,StringComparer.Ordinal)||name is "README.txt" or "USER-TESTS.txt"))throw new IOException("Unexpected published app filename: "+name);
            total=checked(total+size);if(total>536870912)throw Refused();result.Add(new(name,size,hash));
        }
        if(required.Any(name=>!names.Contains(name)))throw Refused();return result.ToArray();
    }
    private static void Fields(JsonElement item,string[] expected)
    {if(item.ValueKind!=JsonValueKind.Object)throw Refused();var actual=item.EnumerateObject().Select(x=>x.Name).ToArray();if(actual.Length!=expected.Length||actual.Distinct(StringComparer.Ordinal).Count()!=actual.Length||!actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected))throw Refused();}
    private static HashSet<string> Inventory(string source)
    {
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var pending=new Stack<string>();pending.Push(source);int nodes=0;
        while(pending.TryPop(out string? folder))foreach(string path in Directory.EnumerateFileSystemEntries(folder))
        {
            if(++nodes>1024)throw Refused();LocalPath(path);var attributes=File.GetAttributes(path);
            if((attributes&FileAttributes.Directory)!=0){if(!Directory.EnumerateFileSystemEntries(path).Any())throw Refused();pending.Push(path);}
            else if(!result.Add(Path.GetRelativePath(source,path).Replace('\\','/')))throw Refused();
        }
        return result;
    }
    private static void VerifyFile(string path,Entry item)
    {LocalPath(path);using var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(file.Length!=item.Size||Convert.ToHexStringLower(SHA256.HashData(file))!=item.Hash)throw Refused();}
    private static string LocalPath(string path)
    {
        if(!Path.IsPathFullyQualified(path)||path.StartsWith("\\\\",StringComparison.Ordinal)||path.Any(char.IsControl))throw Refused();path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if(path.IndexOf(':')!=1||path.IndexOf(':',2)>=0||new DriveInfo(Path.GetPathRoot(path)!).DriveType==DriveType.Network)throw Refused();
        for(string? current=path;current is not null;current=Path.GetDirectoryName(current))
            if(Path.Exists(current)&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw Refused();
        return path; // Preflight, not handle-based containment against hostile concurrent path replacement.
    }
    internal static void CreateShortcut(string installed,string shortcut)
    {
        installed=LocalPath(installed);shortcut=LocalPath(shortcut);string parent=Path.GetDirectoryName(shortcut)!;if(!Directory.Exists(parent)||Path.Exists(shortcut))throw Refused();
        string temporary=Path.Combine(parent,"memo-link-"+Guid.NewGuid().ToString("N")+".lnk");string phase="save Unicode link";
        try
        {
            NativeShellLink.Save(Path.Combine(installed,"MemoApp.Windows.exe"),installed,temporary);
            phase="publish";LocalPath(temporary);File.Move(temporary,shortcut,false);
        }
        catch(Exception error){throw new IOException($"새 임시 바로가기를 보존했습니다 ({phase}): {temporary}",error);}

    }
    private static IOException Refused()=>new("Trial installation validation failed");
}
