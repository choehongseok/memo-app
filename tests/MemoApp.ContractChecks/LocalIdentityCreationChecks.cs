using System.Diagnostics;
using System.Security.Cryptography;
using System.Reflection;
using MemoApp.Core.Lifecycle;
internal static class LocalIdentityCreationChecks
{
    internal static bool TryWorker(string[] args)
    {
        if(args.Length!=2||args[0] is not ("--ui-identity-create-worker" or "--ui-identity-create-no-flock" or "--ui-identity-hold-worker"))return false;
        if(args[0]=="--ui-identity-create-no-flock")AppContext.SetSwitch("System.IO.DisableFileLocking",true);
        if(args[0]=="--ui-identity-hold-worker"){Directory.CreateDirectory(args[1]);using var held=Acquire(args[1],5000);Console.WriteLine("READY");Console.Out.Flush();Console.ReadLine();return true;}
        Console.WriteLine("READY");Console.Out.Flush();if(Console.ReadLine()!="GO")throw new IOException("Synthetic identity worker release missing");
        Console.WriteLine(LocalUiIdentity.GetOrCreate(args[1]).ToString("D"));return true;
    }
    internal static async Task Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"memo-identity-create-"+Guid.NewGuid().ToString("N"));
        try
        {
            for(int round=0;round<10;round++)
            {
                var path=Path.Combine(root,"threads-"+round);using var start=new ManualResetEventSlim();using var ready=new CountdownEvent(16);
                var tasks=Enumerable.Range(0,16).Select(_=>Task.Factory.StartNew(()=>{ready.Signal();VaultChecks.Require(start.Wait(10000),"Synthetic identity release");return LocalUiIdentity.GetOrCreate(path);},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default)).ToArray();
                Guid[] results;try{VaultChecks.Require(ready.Wait(5000),"All synthetic creators reached start barrier");}finally{start.Set();}results=await Task.WhenAll(tasks);
                VaultChecks.Require(results.Distinct().Count()==1&&LocalUiIdentity.GetOrCreate(path)==results[0],"Barrier-synchronized creators must share one exact persisted UI identity");
            }
            var processPath=Path.Combine(root,"processes");var workers=Enumerable.Range(0,8).Select(i=>Start(processPath,i%2==0?"--ui-identity-create-no-flock":"--ui-identity-create-worker")).ToArray();
            try
            {
                foreach(var process in workers)VaultChecks.Require(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="READY","Independent identity process ready");
                foreach(var process in workers){await process.StandardInput.WriteLineAsync("GO");await process.StandardInput.FlushAsync();}
                var results=await Task.WhenAll(workers.Select(async process=>{string? text=await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));VaultChecks.Require(process.ExitCode==0&&Guid.TryParse(text,out _),"Independent creator result without child error output");return Guid.Parse(text!);}));
                VaultChecks.Require(results.Distinct().Count()==1&&LocalUiIdentity.GetOrCreate(processPath)==results[0],"Real concurrent processes converge to one byte-preserved winner");
            }
            finally{foreach(var process in workers){if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}process.Dispose();}}
            var heldPath=Path.Combine(root,"held");using(var holder=Start(heldPath,"--ui-identity-hold-worker")){try{VaultChecks.Require(await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="READY","Independent explicit range-lock owner ready");VaultChecks.ExpectFailure(()=>Acquire(heldPath,100).Dispose(),"Held explicit range lock times out");await Task.Run(()=>VaultChecks.ExpectFailure(()=>LocalUiIdentity.GetOrCreate(heldPath),"Public GetOrCreate times out under live explicit lock"));VaultChecks.Require(!File.Exists(Path.Combine(heldPath,"ui-device.id"))&&!Directory.EnumerateFiles(heldPath,"pending-ui-*.id").Any(),"Contention fails before ID/temp creation");}finally{if(!holder.HasExited){holder.Kill(true);await holder.WaitForExitAsync();}}}VaultChecks.Require(File.Exists(Path.Combine(heldPath,"ui-device.create.lock"))&&LocalUiIdentity.GetOrCreate(heldPath)!=Guid.Empty,"Killed process releases explicit lock while persistent lock file remains present");
            string existing=Path.Combine(root,"existing");_=LocalUiIdentity.GetOrCreate(existing);string idFile=Path.Combine(existing,"ui-device.id"),lockFile=Path.Combine(existing,"ui-device.create.lock");var original=File.ReadAllBytes(idFile);if(File.Exists(lockFile))File.Delete(lockFile);_=LocalUiIdentity.GetOrCreate(existing);VaultChecks.Require(File.ReadAllBytes(idFile).SequenceEqual(original)&&!File.Exists(lockFile),"Existing valid ID is byte-preserved without creating a lock");
            foreach(var data in new byte[][]{[1,2,3],new byte[32]}){var path=Path.Combine(root,"corrupt-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);File.WriteAllBytes(Path.Combine(path,"ui-device.id"),data);VaultChecks.ExpectFailure(()=>LocalUiIdentity.GetOrCreate(path),"Corrupt identity never regenerated");VaultChecks.Require(File.ReadAllBytes(Path.Combine(path,"ui-device.id")).SequenceEqual(data)&&!File.Exists(Path.Combine(path,"ui-device.create.lock")),"Corruption refusal does not create lock or rewrite ID");}
            for(int length=0;length<32;length++){string path=Path.Combine(root,"truncated-"+length);Directory.CreateDirectory(path);var data=original[..length];File.WriteAllBytes(Path.Combine(path,"ui-device.id"),data);VaultChecks.ExpectFailure(()=>LocalUiIdentity.GetOrCreate(path),"Every identity-byte truncation remains refused");VaultChecks.Require(File.ReadAllBytes(Path.Combine(path,"ui-device.id")).SequenceEqual(data)&&!File.Exists(Path.Combine(path,"ui-device.create.lock")),"Truncated ID remains byte-preserved without lock");}
            {string path=Path.Combine(root,"empty-guid");Directory.CreateDirectory(path);var data=(byte[])original.Clone();Array.Clear(data,8,16);SHA256.HashData(data.AsSpan(0,24)).AsSpan(0,8).CopyTo(data.AsSpan(24));File.WriteAllBytes(Path.Combine(path,"ui-device.id"),data);VaultChecks.ExpectFailure(()=>LocalUiIdentity.GetOrCreate(path),"Checksummed empty UUID refused");VaultChecks.Require(File.ReadAllBytes(Path.Combine(path,"ui-device.id")).SequenceEqual(data),"Empty UUID original preserved");}
            foreach(bool directory in new[]{false,true}){string path=Path.Combine(root,"bad-lock-"+directory);Directory.CreateDirectory(path);var lockPath=Path.Combine(path,"ui-device.create.lock");if(directory)Directory.CreateDirectory(lockPath);else File.WriteAllBytes(lockPath,[1]);VaultChecks.ExpectFailure(()=>LocalUiIdentity.GetOrCreate(path),"Directory/nonempty creation lock refused");VaultChecks.Require(!File.Exists(Path.Combine(path,"ui-device.id"))&&!Directory.EnumerateFiles(path,"pending-ui-*.id").Any(),"Invalid lock fails before ID/temp creation");}
            if(!OperatingSystem.IsWindows())
            {
                string path=Path.Combine(root,"linked-lock");Directory.CreateDirectory(path);string target=Path.Combine(root,"empty-target");File.WriteAllBytes(target,[]);File.CreateSymbolicLink(Path.Combine(path,"ui-device.create.lock"),target);VaultChecks.ExpectFailure(()=>LocalUiIdentity.GetOrCreate(path),"Linked creation lock refused");VaultChecks.Require(!File.Exists(Path.Combine(path,"ui-device.id")),"Linked lock cannot create ID");
                path=Path.Combine(root,"fifo-lock");Directory.CreateDirectory(path);string fifo=Path.Combine(path,"ui-device.create.lock");using(var make=Process.Start(new ProcessStartInfo("/usr/bin/mkfifo"){UseShellExecute=false,ArgumentList={fifo}})!){await make.WaitForExitAsync();VaultChecks.Require(make.ExitCode==0,"Synthetic FIFO fixture");}VaultChecks.ExpectFailure(()=>LocalUiIdentity.GetOrCreate(path),"FIFO lock refused without blocking open");VaultChecks.Require(!File.Exists(Path.Combine(path,"ui-device.id")),"FIFO cannot create ID");
            }
            var lockType=typeof(LocalUiIdentity).Assembly.GetType("MemoApp.Core.Lifecycle.UiIdentityCreationLock")!;var contention=lockType.GetMethod("Contention",BindingFlags.NonPublic|BindingFlags.Static)!;foreach(int code in new[]{0,5,11,13,unchecked((int)0x80070020),unchecked((int)0x80070021)}){bool expected=OperatingSystem.IsLinux()?code==11:OperatingSystem.IsWindows()&&code==unchecked((int)0x80070021);VaultChecks.Require((bool)contention.Invoke(null,[new IOException("Synthetic code",code)])! ==expected,"Only exact explicit range-lock contention is retryable");}
            Console.WriteLine("PASS: barrier-synchronized local UI identity creators, real multi-process convergence, unchanged existing/corrupt bytes and strict creation-lock refusals");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static FileStream Acquire(string root,int timeout)
    {
        var method=typeof(LocalUiIdentity).Assembly.GetType("MemoApp.Core.Lifecycle.UiIdentityCreationLock")!.GetMethod("Acquire",BindingFlags.NonPublic|BindingFlags.Static)!;
        return method.CreateDelegate<Func<string,int,FileStream>>()(root,timeout);
    }
    private static Process Start(string root,string mode)
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        if(Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet")start.ArgumentList.Add(typeof(LocalIdentityCreationChecks).Assembly.Location);
        if(mode=="--ui-identity-create-no-flock")start.Environment["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"]="true";start.ArgumentList.Add(mode);start.ArgumentList.Add(root);return Process.Start(start)!;
    }
}
