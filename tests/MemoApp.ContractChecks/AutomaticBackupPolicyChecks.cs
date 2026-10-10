using MemoApp.Core.Storage;
internal static class AutomaticBackupPolicyChecks
{
    internal static void Run()
    {
        var type=typeof(EncryptedVault).Assembly.GetType("MemoApp.Core.Storage.AutomaticBackupPolicy")??throw new Exception("Bounded automatic encrypted-backup policy is missing");
        string root=Path.Combine(Path.GetTempPath(),"memo-backup-policy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var id=Guid.NewGuid();var method=type.GetMethod("NextDestination")!;string path=(string)method.Invoke(null,[root,id,2])!;VaultChecks.Require(Path.GetDirectoryName(path)==root&&Path.GetFileName(path).StartsWith("memo-auto-"+id.ToString("N")+"-",StringComparison.Ordinal),"Fixed per-vault filename");File.WriteAllBytes(path,[]);string next=(string)method.Invoke(null,[root,id,2])!;File.WriteAllBytes(next,[1]);
            try{method.Invoke(null,[root,id,2]);throw new Exception("Full backup capacity accepted");}catch(System.Reflection.TargetInvocationException e)when(e.InnerException is IOException){}
            VaultChecks.Require(Directory.GetFiles(root).Length==2,"Capacity refusal retains both complete and failed/empty files");
        }
        finally{Directory.Delete(root,true);}
    }
}
