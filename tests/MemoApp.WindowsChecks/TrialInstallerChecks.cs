using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task TrialInstallerRun()
    {
        var type=typeof(MainWindow).Assembly.GetType("MemoApp.Windows.TrialInstaller");Require(type is not null,"Per-user trial installer is missing");
        var install=type!.GetMethod("Install",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<Action<string,string>>();
        string root=Path.Combine(Path.GetTempPath(),"memo-installer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string source=Path.Combine(root,"한글 source"),target=Path.Combine(root,"한글 target");Directory.CreateDirectory(source);
        string[] paths=["MemoApp.Windows.exe","MemoApp.Windows.dll","MemoApp.Windows.deps.json","MemoApp.Windows.runtimeconfig.json","Markdig.dll","licenses/Markdig1.4.0.txt","Start-Portable.cmd","Install-User.cmd"];
        foreach(string path in paths){string full=Path.Combine(source,path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);File.WriteAllText(full,"synthetic fixture "+path);}
        void Manifest()=>File.WriteAllText(Path.Combine(source,"installation-manifest.json"),JsonSerializer.Serialize(new{schemaVersion=1,sourceCommit=new string('a',40),files=paths.Select(path=>new{path,size=new FileInfo(Path.Combine(source,path)).Length,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(source,path))))}).ToArray()}));Manifest();
        string phase="install";
        try
        {
            install(source,target);phase="create shortcut";
            var shortcut=type.GetMethod("CreateShortcut",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<Action<string,string>>();string linkPath=Path.Combine(root,"MemoApp Synthetic Trial.lnk");shortcut(target,linkPath);
            phase="read shortcut";object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;dynamic native=shell;object link=native.CreateShortcut(linkPath);dynamic entry=link;
            try{Require((string)entry.TargetPath==Path.Combine(target,"MemoApp.Windows.exe")&&(string)entry.Arguments==""&&(string)entry.WorkingDirectory==target,"Actual Windows shortcut has only fixed executable target, no arguments and installed working directory");}finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
            phase="occupied shortcut";byte[] shortcutBytes=File.ReadAllBytes(linkPath);bool occupied=false;try{shortcut(target,linkPath);}catch{occupied=true;}Require(occupied&&File.ReadAllBytes(linkPath).SequenceEqual(shortcutBytes),"Existing shortcut is preserved");
            phase="exact copied files";Require(paths.All(path=>File.ReadAllBytes(Path.Combine(target,path)).SequenceEqual(File.ReadAllBytes(Path.Combine(source,path)))),"Per-user installation preserves every exact declared source file and Korean paths");
            phase="occupied target";bool failed=false;try{install(source,target);}catch{failed=true;}Require(failed&&File.ReadAllText(Path.Combine(target,"MemoApp.Windows.exe"))=="synthetic fixture MemoApp.Windows.exe","Occupied target is refused without overwrite");
            phase="extra vault";File.WriteAllText(Path.Combine(source,"private.vault"),"synthetic data that may not enter package");failed=false;try{install(source,Path.Combine(root,"unexpected"));}catch{failed=true;}Require(failed&&!Directory.Exists(Path.Combine(root,"unexpected")),"Unexpected vault/source data never installs");File.Delete(Path.Combine(source,"private.vault"));
            phase="same length tamper";string original=File.ReadAllText(Path.Combine(source,paths[0]));File.WriteAllText(Path.Combine(source,paths[0]),new string('z',original.Length));failed=false;try{install(source,Path.Combine(root,"tampered"));}catch{failed=true;}Require(failed&&!Directory.Exists(Path.Combine(root,"tampered"))&&!Directory.EnumerateDirectories(root,".memo-install-*").Any(),"Same-length payload mutation refuses target and leaves no stage");File.WriteAllText(Path.Combine(source,paths[0]),original);
            phase="malformed manifests";string good=File.ReadAllText(Path.Combine(source,"installation-manifest.json"));foreach(string bad in new[]{good.Replace(paths[0],"../escape.exe"),good.Replace(paths[0],"CON.exe"),good.Replace(paths[0],"MemoApp.Windows.exe:ads"),good.Replace(paths[0],"MemoApp.Windows.exe."),good.Replace("\"schemaVersion\":1","\"schemaVersion\":1,\"unknown\":true"),new string(' ',262145)})
            {File.WriteAllText(Path.Combine(source,"installation-manifest.json"),bad);failed=false;try{install(source,Path.Combine(root,"bad"));}catch{failed=true;}Require(failed&&!Directory.Exists(Path.Combine(root,"bad")),"Strict manifest path/fields/size preflight refuses unchanged destination");}File.WriteAllText(Path.Combine(source,"installation-manifest.json"),good);
        }
        catch(Exception error){throw new Exception("installer phase="+phase+"; "+error.GetBaseException().GetType().Name+": "+error.GetBaseException().Message+"; "+error.GetBaseException().StackTrace,error);}
        finally{Directory.Delete(root,true);}
        await Task.CompletedTask;
    }
}
