using System.Reflection;
using System.Security.Cryptography;
using MemoApp.Core.Storage;
internal static class AttachmentSourceChecks
{
    private delegate ReadOnlySpan<byte> Content();
    internal static bool TryWorker(string[] args)
    {
        if(args.Length!=2||args[0]!="--attachment-special-source-probe")return false;
        try{using var source=MemoApp.Core.Transfer.AttachmentSource.Read(args[1]);Environment.ExitCode=1;}
        catch(IOException){Environment.ExitCode=0;}return true;
    }
    internal static void Run()
    {
        var type=typeof(EncryptedVault).Assembly.GetType("MemoApp.Core.Transfer.AttachmentSource");VaultChecks.Require(type is not null,"Bounded opaque attachment file reader with owned buffers is missing");
        var read=type!.GetMethod("Read",BindingFlags.Static|BindingFlags.Public)!.CreateDelegate<Func<string,CancellationToken,IDisposable>>();
        var captured=type.GetMethod("ReadCaptured",BindingFlags.Static|BindingFlags.NonPublic);
        VaultChecks.Require(captured is not null,"Deterministic bounded source-read ownership seam is missing");
        var readCaptured=captured!.CreateDelegate<Func<Stream,long,string,string,CancellationToken,IDisposable>>();
        using(var cancel=new CancellationTokenSource())using(var interrupted=new InterruptedStream(cancel,false))
        {
            bool canceled=false;try{readCaptured(interrupted,70001,"synthetic.bin","application/octet-stream",cancel.Token).Dispose();}catch(OperationCanceledException){canceled=true;}
            VaultChecks.Require(canceled&&interrupted.Owned is not null&&interrupted.Owned.All(b=>b==0),"Cancellation during read refuses return and zeroes exact allocated input array");
        }
        using(var broken=new InterruptedStream(null,true))
        {
            VaultChecks.ExpectFailure(()=>readCaptured(broken,70001,"synthetic.bin","application/octet-stream",CancellationToken.None).Dispose(),"Read failure is not an imported attachment");
            VaultChecks.Require(broken.Owned is not null&&broken.Owned.All(b=>b==0),"Read failure zeroes every allocated input byte before escaping");
        }
        var root=Path.Combine(Path.GetTempPath(),"memo-attachment-source-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            foreach(var (name,mime,length) in new[]{("합성😀e\u0301.PDF","application/pdf",70001),("word.DOCX","application/vnd.openxmlformats-officedocument.wordprocessingml.document",1234),("sheet.xlsx","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",2),("hangul.hwp","application/x-hwp",3),("raw.unknown","application/octet-stream",4194304),("empty.bin","application/octet-stream",0)})
            {
                var bytes=RandomNumberGenerator.GetBytes(length);string file=Path.Combine(root,name);File.WriteAllBytes(file,bytes);var input=read(file,CancellationToken.None);
                try
                {
                    string String(string field)=>(string)input.GetType().GetProperty(field)!.GetValue(input)!;
                    var owned=(byte[])input.GetType().GetField("bytes",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(input)!;
                    var content=input.GetType().GetProperty("Content")!.GetMethod!.CreateDelegate<Content>(input);
                    VaultChecks.Require(content().SequenceEqual(bytes),"Public borrowed span exposes exact owned input bytes");
                    VaultChecks.Require(owned.SequenceEqual(bytes)&&String("Name")==name&&String("Mime")==mime&&String("Sha256")==Convert.ToHexStringLower(SHA256.HashData(bytes)),"Opaque reader preserves every original byte and exact filename/hash while inferring only inert extension metadata");
                    VaultChecks.Require(File.ReadAllBytes(file).SequenceEqual(bytes),"Reader never rewrites original source");input.Dispose();VaultChecks.Require(owned.All(b=>b==0)&&String("Name")==""&&String("Mime")==""&&String("Sha256")=="","Dispose zeroes owned plaintext and drops descriptive metadata");
                    VaultChecks.ExpectFailure(()=>content(),"Disposed input cannot issue another plaintext borrow");
                }
                finally{input.Dispose();CryptographicOperations.ZeroMemory(bytes);}
            }
            var huge=Path.Combine(root,"huge.bin");using(var file=new FileStream(huge,FileMode.CreateNew))file.SetLength(4194305);VaultChecks.ExpectFailure(()=>read(huge,CancellationToken.None).Dispose(),"File size4MiB bound is checked before allocation");
            VaultChecks.ExpectFailure(()=>read(root,CancellationToken.None).Dispose(),"Directories are not opaque regular files");
            foreach(string path in new[]{"https://example.invalid/file.pdf","file:///private.pdf","\\\\server\\secret.pdf","//server/secret.pdf","bad\0file.pdf"})VaultChecks.ExpectFailure(()=>read(path,CancellationToken.None).Dispose(),"Network/device/URI/control source rejected before IO");
            using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool rejected=false;try{read(Path.Combine(root,"not-opened.bin"),cancel.Token).Dispose();}catch(OperationCanceledException){rejected=true;}VaultChecks.Require(rejected,"Canceled selection is rejected before touching a missing source");}
            if(!OperatingSystem.IsWindows())
            {
                var source=Path.Combine(root,"empty.bin");var link=Path.Combine(root,"linked.bin");File.CreateSymbolicLink(link,source);VaultChecks.ExpectFailure(()=>read(link,CancellationToken.None).Dispose(),"Linked leaf source rejected");
                var parent=Path.Combine(root,"linked-dir");Directory.CreateSymbolicLink(parent,root);VaultChecks.ExpectFailure(()=>read(Path.Combine(parent,"empty.bin"),CancellationToken.None).Dispose(),"Linked ancestor rejected before child lookup");
                if(OperatingSystem.IsLinux())
                {
                    var fifo=Path.Combine(root,"synthetic.fifo");using(var make=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/mkfifo"){UseShellExecute=false,ArgumentList={fifo}})!){make.WaitForExit();VaultChecks.Require(make.ExitCode==0,"Synthetic FIFO setup");}
                    Probe(fifo);Probe("/dev/null");
                    void Probe(string path)
                    {
                        var start=new System.Diagnostics.ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,ArgumentList={typeof(AttachmentSourceChecks).Assembly.Location,"--attachment-special-source-probe",path}};
                        using var worker=System.Diagnostics.Process.Start(start)!;bool finished=worker.WaitForExit(3000);if(!finished){worker.Kill(true);worker.WaitForExit();}
                        VaultChecks.Require(finished&&worker.ExitCode==0,"Special FIFO/device source must reject before a blocking open within the bounded probe");
                    }
                }
            }
            Console.WriteLine("PASS: bounded opaque local file bytes/name/MIME/hash preservation, source invariance, owned zero/dispose, empty/max/oversize/cancel/network/URI/link and Unix special-file refusal (no parsing, launching or exporting)");
        }
        finally{Directory.Delete(root,true);}
    }
    private sealed class InterruptedStream(CancellationTokenSource? cancel,bool fail):Stream
    {
        internal byte[]? Owned;
        public override int Read(byte[] buffer,int offset,int count)
        {
            Owned=buffer;Array.Fill(buffer,(byte)0xA1,offset,count);cancel?.Cancel();if(fail)throw new IOException("synthetic source read failure");return count;
        }
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>70001;public override long Position{get=>0;set=>throw new NotSupportedException();}
        public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
