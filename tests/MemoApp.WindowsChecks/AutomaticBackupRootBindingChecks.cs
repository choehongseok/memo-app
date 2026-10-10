using System.IO;
using System.Reflection;
using MemoApp.Core.Storage;
using MemoApp.Windows;

internal static partial class Program
{
    private static Task AutomaticBackupRootBindingRun()
    {
        var helper=typeof(MainWindow).Assembly.GetType("MemoApp.Windows.AutomaticBackupRootBinding");
        Require(helper is not null,"Native automatic backup source root binding helper is missing");
        var capture=helper!.GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic)!;
        var matches=helper.GetMethod("Matches",BindingFlags.Static|BindingFlags.NonPublic)!;
        Require(capture is not null&&matches is not null,"Native source root binding operations are missing");
        StoredSourceRootBinding Capture(string path)=>(StoredSourceRootBinding)capture!.Invoke(null,[path])!;
        bool Matches(string path,StoredSourceRootBinding binding)=>(bool)matches!.Invoke(null,[path,binding])!;
        void Refused(string path)
        {
            bool refused=false;
            try{Capture(path);}catch(TargetInvocationException e)when(e.InnerException is IOException or UnauthorizedAccessException or ArgumentException){refused=true;}
            Require(refused,"Invalid synthetic source root capture must be refused");
        }
        string sandbox=Path.Combine(Path.GetTempPath(),"memo-native-root-binding-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            string root=Path.Combine(sandbox,"source"),copy=Path.Combine(sandbox,"copied"),moved=Path.Combine(sandbox,"moved"),missing=Path.Combine(sandbox,"missing");
            Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"synthetic.txt"),"synthetic directory identity fixture");
            var first=Capture(root);
            Require(first.DirectoryFileId.Length==32&&first.DirectoryFileId.Any(c=>c!='0')&&first.DirectoryFileId.All(char.IsAsciiHexDigit),"Source binding contains a nonzero full 128-bit directory identifier");
            Require(Matches(root,first)&&Capture(root)==first,"Unchanged native source root retains identity");
            Require(Matches(root,first with{DirectoryFileId=first.DirectoryFileId.ToLowerInvariant()}),"Stored hexadecimal identifier casing does not change native identity");
            Require(Matches(root.ToUpperInvariant()+Path.DirectorySeparatorChar,first),"Ordinary Windows case and trailing separator normalize to the same source root");
            Directory.CreateDirectory(copy);File.Copy(Path.Combine(root,"synthetic.txt"),Path.Combine(copy,"synthetic.txt"));
            var copied=Capture(copy);
            Require(!Matches(copy,first)&&!Matches(copy,first with{RootPath=copy}),"Copied source tree does not inherit directory identity even when stored path is substituted");
            Require(first.VolumeSerialNumber==copied.VolumeSerialNumber&&first.DirectoryFileId!=copied.DirectoryFileId,"Different directories on one volume have different native identifiers");
            Require(!Matches(root,first with{VolumeSerialNumber=first.VolumeSerialNumber^1})&&!Matches(root,first with{DirectoryFileId=new string('F',32)}),"Volume and full file identifier are independently required");
            Directory.Move(root,moved);Directory.CreateDirectory(root);
            Require(!Matches(root,first)&&!Matches(moved,first),"Same-path replacement and renamed original do not match the original source binding");
            Require(Matches(moved,first with{RootPath=moved}),"Renamed original preserves native identity only with its current explicit path");
            Require(!Matches(missing,first),"Missing source root fails closed");Refused(missing);Refused(Path.Combine(copy,"synthetic.txt"));
            foreach(string path in new[]{"relative-source",@"C:relative-source",@"\\synthetic-host\share\source",@"\\?\C:\synthetic-source",@"\\.\C:\synthetic-source",root+":stream"})Refused(path);
            Require(!Matches(root,first with{DirectoryFileId="bad"}),"Malformed stored native identity fails closed");
            Require(!Matches(root,first with{DirectoryFileId=new string('0',32)})&&!Matches(root,first with{RootPath=@"\\synthetic-host\share\source"}),"Unavailable identity and network stored roots fail closed");
            string link=Path.Combine(sandbox,"linked");
            try{Directory.CreateSymbolicLink(link,copy);}
            catch(Exception e)when(e is UnauthorizedAccessException or IOException or PlatformNotSupportedException){Console.WriteLine("SKIP: synthetic directory symlink capture check (creation unavailable without extra privileges)");return Task.CompletedTask;}
            try
            {
                Refused(link);Require(!Matches(link,first),"Leaf reparse source root fails closed");
                string child=Path.Combine(copy,"child");Directory.CreateDirectory(child);Refused(Path.Combine(link,"child"));
            }
            finally{Directory.Delete(link);}
        }
        finally{Directory.Delete(sandbox,true);}
        return Task.CompletedTask;
    }
}
