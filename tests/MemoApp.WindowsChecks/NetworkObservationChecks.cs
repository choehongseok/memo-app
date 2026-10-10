using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Search;
using MemoApp.Windows;

internal static partial class Program
{
    private static readonly string[] NetworkPhases=["startup-idle","unlocked-edit-search-save","ocr-1","ocr-2","apply-search","lock-idle","done"];
    private static Task NetworkObservationRun()=>NetworkObservationRequireParent();
    private static async Task NetworkObservationRequireParent()=>Require(await NetworkObservationParentRun()==0,"Bounded independent network observation failed");
    private static async Task<int> NetworkObservationParentRun()
    {
        try
        {
            InstalledNetworkObservation.VerifySyntheticSockets();
            await NetworkPositiveProbe();await NetworkProtocolChecksAsync();
            string engine=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"ocr","tesseract.exe"));
            using var engineFile=new FileStream(engine,FileMode.Open,FileAccess.Read,FileShare.Read);
            using(var manifest=typeof(AttachmentPanel).Assembly.GetManifestResourceStream("MemoApp.OcrManifest")??throw new IOException("Observation fixed manifest missing"))
            {
                byte[] bytes=new byte[16385];int count=0,read;while(count<bytes.Length&&(read=manifest.Read(bytes,count,bytes.Length-count))>0)count+=read;
                if(count>16384)throw new IOException("Observation manifest bound");using var json=JsonDocument.Parse(bytes.AsMemory(0,count));
                var file=engineFile;long expected=json.RootElement.GetProperty("engineLength").GetInt64();
                if(expected is <1 or >8388608||file.Length!=expected||Convert.ToHexStringLower(SHA256.HashData(file))!=json.RootElement.GetProperty("engineSha256").GetString()!.ToLowerInvariant())throw new IOException("Observation expected engine identity failed");
            }
            string nonce=Guid.NewGuid().ToString("N");using var run=new NetworkChild("--network-observation-child",nonce);
            try
            {
            await run.Start();var identity=InstalledNetworkObservation.Identity.Capture(run.Process);var coverage=new NetworkCoverage();
            using var stop=new CancellationTokenSource();int phase=-1,discovered=0;Exception? observerFailure=null;
            var observer=Task.Run(async()=>
            {
                var engines=new Dictionary<InstalledNetworkObservation.Identity,Process>();
                try
                {
                    while(!stop.IsCancellationRequested)
                    {
                        run.Process.Refresh();if(run.Process.HasExited)break;
                        int active=Volatile.Read(ref phase);coverage.RecordHost(active,InstalledNetworkObservation.Snapshot(identity,run.Process));
                        foreach(int pid in NetworkChildren(identity.Pid))
                        {
                            Process? child=null;
                            try
                            {
                                child=Process.GetProcessById(pid);var bound=InstalledNetworkObservation.Identity.Capture(child);
                                if(bound.Creation<identity.Creation)throw new IOException("Observed child predates original host");
                                string path=Path.GetFullPath(child.MainModule?.FileName??throw new IOException("Observed engine image unavailable"));
                                if(!path.Equals(engine,StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected observed host descendant");
                                bound.RequireLive(child);
                                if(engines.Keys.Any(old=>old.Pid==bound.Pid&&old!=bound))throw new IOException("Observed engine PID reuse");
                                if(!engines.ContainsKey(bound)){if(engines.Count>=16)throw new IOException("Observed child count bound");engines.Add(bound,child);discovered++;child=null;}
                            }
                            catch(ArgumentException){/* exited between discovery and opening: no qualifying sample */}
                            catch(InvalidOperationException){/* exited during identity binding: no qualifying sample */}
                            finally{child?.Dispose();}
                        }
                        foreach(var item in engines)
                        {
                            item.Value.Refresh();if(item.Value.HasExited)continue;
                            try{coverage.RecordEngine(active,InstalledNetworkObservation.Snapshot(item.Key,item.Value));}
                            catch(IOException){item.Value.Refresh();if(!item.Value.HasExited)throw;/* lost sample, never zero evidence */}
                        }
                        await Task.Delay(InstalledNetworkObservation.IntervalMilliseconds,stop.Token);
                    }
                }
                catch(OperationCanceledException)when(stop.IsCancellationRequested){}
                catch(Exception e){observerFailure=e;}
                finally{foreach(var process in engines.Values)process.Dispose();}
            });
            try
            {
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(60));
                for(int index=0;index<NetworkPhases.Length;index++)
                {
                    string line=await run.Lines.Reader.ReadAsync(deadline.Token);
                    if(line!=$"OBS {nonce} {NetworkPhases[index]}")throw new IOException("Observation phase protocol mismatch");
                    if(index>0)coverage.RequireHost(index-1);
                    Volatile.Write(ref phase,index);
                    if(index<NetworkPhases.Length-1)
                    {
                        // One complete bound native snapshot before permission to execute this phase.
                        coverage.RecordHost(index,InstalledNetworkObservation.Snapshot(identity,run.Process));
                        await run.Command("GO",deadline.Token);
                    }
                    else await run.Command("EXIT",deadline.Token);
                    run.RequireStreamsHealthy();if(observerFailure is not null)throw new IOException("Observation native sampler failed",observerFailure);
                }
                await run.Wait(deadline.Token);if(run.Process.ExitCode!=0)throw new IOException("Observed synthetic WPF child failed");
                coverage.RequireComplete();
            }
            finally
            {
                stop.Cancel();await observer;await run.Drain();
            }
            if(observerFailure is not null)throw new IOException("Observation native sampler failed",observerFailure);
            if(run.Lines.Reader.TryRead(out _))throw new IOException("Observation unexpected additional phase");
            coverage.Report(discovered);
            Console.WriteLine("NETWORK_OBSERVATION_LIMIT test-host-production-wpf-and-native-ocr: sampled PID+creation TCP/UDP v4/v6 rows only; launch/discovery gaps, between-sample sockets, DNS/delegated processes, packets, other transports and future traffic are not covered; no firewall or full offline guarantee; installed production EXE coverage remains separate locked startup scenarios.");return 0;
            }finally{await run.Drain();}
        }
        catch(Exception e){Console.Error.WriteLine("FAIL: network observation "+e.GetType().Name);return 1;}
    }
    private static async Task NetworkObservationChildRun()
    {
        string nonce=NetworkNonce();string root=Path.Combine(Path.GetTempPath(),"memo-network-wpf-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;Window? panelWindow=null;AttachmentPanel? panel=null;SaveCoordinator? session=null;
        try
        {
            await NetworkPhase(nonce,0);Directory.CreateDirectory(root);main=new MainWindow(Path.Combine(root,"vault"));main.Show();await Idle();await Task.Delay(3000);
            await NetworkPhase(nonce,1);Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));Invoke(main,"NewNote_Click",main,new RoutedEventArgs());await Idle();
            session=Field<SaveCoordinator>(main,"session");var note=session.Workspace.Notes.Single();
            EditText(Control<TextBox>(main,"TitleEditor"),"합성 통신 관찰");EditText(Control<TextBox>(main,"BodyEditor"),"관찰 전용 합성 본문");await Idle();Require(note.Text=="관찰 전용 합성 본문","Observed actual native edit bound");
            Control<ComboBox>(main,"SearchFieldFilter").SelectedIndex=2;
            Control<TextBox>(main,"SearchInput").Text="SYNTHETIC_ABSENT_NETWORK_QUERY";await Idle();
            Require(Control<ListBox>(main,"NotesList").Items.Count==0,"Observed native body search rejects absent query");
            Control<TextBox>(main,"SearchInput").Text="관찰 전용";await Idle();
            Require(Control<ListBox>(main,"NotesList").Items.Count==1&&ReferenceEquals(Control<ListBox>(main,"NotesList").Items[0],note),"Observed actual WPF body search finds exact original note");
            Require(note.Text=="관찰 전용 합성 본문","Observed search preserves original body");Control<TextBox>(main,"SearchInput").Clear();await Idle();
            Field<DispatcherTimer>(main,"timer").Stop();await Field<Task>(main,"recentTask");Require(await session.SaveAsync(),"Observed encrypted UI save");
            Require(await session.PrepareAttachmentsAsync(),"Observed actual image root");byte[] png=Convert.FromBase64String(File.ReadAllText("tests/fixtures/ocr-synthetic-png.base64"));
            try{session.AttachBytes(note,png,"synthetic.png","image/png",note.EditVersion);}finally{CryptographicOperations.ZeroMemory(png);}
            Require(await session.SaveAsync(),"Observed encrypted image source save");panel=new AttachmentPanel(session,note,()=>true,_=>{},Guid.NewGuid());panelWindow=new Window{Content=panel,Width=800,Height=800};panelWindow.Show();await Idle();panel.FilesList.SelectedIndex=0;
            Require(await panel.ConfigureOcrModelsAsync(true,()=>root),"Observed explicit existing local public model installation");
            for(int recognition=0;recognition<2;recognition++)
            {
                await NetworkPhase(nonce,2+recognition);panel.FilesList.SelectedIndex=0;
                Require(await panel.RecognizeSelectedAsync()&&OcrComparable(panel.OcrResult.Text)==OcrComparable(OcrExpected),"Observed actual fixed Korean English OCR");
                if(recognition==0)panel.OcrCancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await NetworkPhase(nonce,4);Require(await panel.ApplyOcrResultAsync(),"Observed explicit encrypted OCR apply");
            Require(NoteSearch.Find(session.Workspace,new(){Query="ABC 123",Field=SearchField.Body}).Count()==1,"Observed actual OCR result body search");Require(await session.SaveAsync(),"Observed OCR encrypted save remains successful");
            await NetworkPhase(nonce,5);Require(await session.LockAsync()&&session.KeysReleased&&panel.OcrResult.Text==""&&Control<TextBox>(main,"BodyEditor").Text=="","Observed actual lock release and UI clear");await Task.Delay(3000);
            await NetworkPhase(nonce,6);
        }
        finally
        {
            Exception? cleanup=null;bool ownerDisposed=session is null;
            void Attempt(Action action){try{action();}catch(Exception e){cleanup??=e;}}
            Attempt(()=>panel?.Dispose());Attempt(()=>panelWindow?.Close());
            if(session is not null)
            {
                try{await session.LockAsync();Require(session.KeysReleased&&!session.IsBusy,"Observed child cleanup actual lock settles");}catch(Exception e){cleanup??=e;}
                if(main is not null&&session.IsLocked&&!session.IsBusy&&session.PendingKind=="none")
                    Attempt(()=>{Require((bool)Invoke(main,"ReleaseSettledSession")!,"Observed child settled owner release");ownerDisposed=true;});
                if(!ownerDisposed&&session.IsLocked&&!session.IsBusy)
                    Attempt(()=>{cleanup??=new IOException("Observed synthetic owner needed failure cleanup");session.Dispose();ownerDisposed=true;if(main is not null)SetField(main,"session",null!);});
            }
            if(main is not null&&ownerDisposed)Attempt(()=>{SetField(main,"fullExitRequested",true);main.Close();});
            CryptographicOperations.ZeroMemory(secret);
            if(ownerDisposed)Attempt(()=>{if(Directory.Exists(root))Directory.Delete(root,true);});
            if(cleanup is not null||!ownerDisposed)throw new IOException("Observed child cleanup incomplete",cleanup);
        }
    }
    private static string NetworkNonce()
    {var args=Environment.GetCommandLineArgs();string nonce=args[^1];if(nonce.Length!=32||nonce.Any(c=>!char.IsAsciiHexDigit(c)))throw new IOException("Observation nonce");return nonce;}
    private static async Task NetworkPhase(string nonce,int phase)
    {
        Console.WriteLine($"OBS {nonce} {NetworkPhases[phase]}");Console.Out.Flush();
        string command=await NetworkReadCommand(Console.OpenStandardInput());if(command!=(phase==6?"EXIT":"GO"))throw new IOException("Observation command order");
    }
    private static async Task<string> NetworkReadCommand(Stream stream)
    {
        byte[] bytes=new byte[8];int count=0;using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while(count<bytes.Length){int read=await stream.ReadAsync(bytes.AsMemory(count,1),timeout.Token);if(read==0)throw new IOException("Observation command EOF");if(bytes[count]==10)return Encoding.ASCII.GetString(bytes,0,count).TrimEnd('\r');if(bytes[count] is <32 or >126)throw new IOException("Observation command byte");count++;}throw new IOException("Observation command bound");
    }
    private static int NetworkObservationChildEntry()
    {
        int result=1;var application=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        application.Startup+=async(_,_)=>{try{await NetworkObservationChildRun();result=0;}catch(Exception e){Console.Error.WriteLine("Observed child failure "+e.GetType().Name);}finally{application.Shutdown();}};
        application.Run();return result;
    }
    private sealed class NetworkChild(string mode,string nonce):IDisposable
    {
        internal Process Process{get;private set;}=null!;
        internal readonly Channel<string> Lines=Channel.CreateBounded<string>(new BoundedChannelOptions(16){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
        private readonly byte[] outChunk=new byte[256],outLine=new byte[128],errChunk=new byte[256],errLine=new byte[128];
        private readonly CancellationTokenSource readerCancellation=new();
        private Task? output,error;private bool drained,terminalExit,readerDisposed;
        internal Task Start()
        {
            string executable=Environment.ProcessPath??throw new IOException("Observation host path missing");
            var start=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Environment.CurrentDirectory};
            if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add(mode);start.ArgumentList.Add(nonce);
            Process=System.Diagnostics.Process.Start(start)??throw new IOException("Observation test child missing");
            output=ReadBoundedLines(Process.StandardOutput.BaseStream,Lines.Writer,outChunk,outLine,readerCancellation.Token);error=ReadBoundedLines(Process.StandardError.BaseStream,null,errChunk,errLine,readerCancellation.Token);return Task.CompletedTask;
        }
        internal void RequireStreamsHealthy(){if(output?.IsFaulted==true||error?.IsFaulted==true)throw new IOException("Observation stream reader faulted");}
        internal async Task Command(string command,CancellationToken token)
        {byte[] bytes=Encoding.ASCII.GetBytes(command+"\n");await Process.StandardInput.BaseStream.WriteAsync(bytes,token);await Process.StandardInput.BaseStream.FlushAsync(token);}
        internal async Task Wait(CancellationToken token)=>await Process.WaitForExitAsync(token);
        internal async Task Drain()
        {
            if(drained||Process is null)return;
            Exception? failed=null;
            void Attempt(Action action){try{action();}catch(Exception e){failed??=e;}}
            Attempt(()=>{Process.Refresh();if(!Process.HasExited)Process.Kill(true);});
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try{await Process.WaitForExitAsync(deadline.Token);terminalExit=true;}catch(Exception e){failed??=e;}
            output??=ReadBoundedLines(Process.StandardOutput.BaseStream,Lines.Writer,outChunk,outLine,readerCancellation.Token);
            error??=ReadBoundedLines(Process.StandardError.BaseStream,null,errChunk,errLine,readerCancellation.Token);
            try{await Task.WhenAll(output,error).WaitAsync(deadline.Token);}catch(Exception e){failed??=e;}
            if(failed is not null)
            {
                // The deadline is a failure, never evidence of settlement. Cancel the real reads
                // and close each owned pipe independently, then observe both actual terminal tasks.
                Attempt(()=>readerCancellation.Cancel());
                Attempt(()=>Process.StandardInput.Close());Attempt(()=>Process.StandardOutput.Close());Attempt(()=>Process.StandardError.Close());
                Attempt(()=>{Process.Refresh();if(!Process.HasExited)Process.Kill(true);});
                if(!terminalExit)
                {try{await Process.WaitForExitAsync();terminalExit=true;}catch(Exception e){failed??=e;}}
                try{await Task.WhenAll(output,error);}catch(Exception e){failed??=e;}
            }
            if(output.IsCompleted&&error.IsCompleted&&!readerDisposed){readerCancellation.Dispose();readerDisposed=true;}
            if(!terminalExit||!output.IsCompleted||!error.IsCompleted)throw new IOException("Observation actual child cleanup remains unsettled",failed);
            if(failed is not null)throw new IOException("Observation actual child exit/stream drain failed",failed);
            drained=true;
        }
        public void Dispose()
        {
            if(Process is null){if(!readerDisposed){readerCancellation.Dispose();readerDisposed=true;}return;}
            if(!terminalExit||output?.IsCompleted!=true||error?.IsCompleted!=true)throw new IOException("Observation child disposed before actual exit/stream terminal drain");
            if(!readerDisposed){readerCancellation.Dispose();readerDisposed=true;}Process.Dispose();
        }
    }
    private static async Task ReadBoundedLines(Stream input,ChannelWriter<string>? destination,byte[]? chunk=null,byte[]? line=null,CancellationToken token=default)
    {
        chunk??=new byte[256];line??=new byte[128];int total=0,count=0,lines=0;
        try
        {
            while(true)
            {
                int read=await input.ReadAsync(chunk,token);if(read==0)break;
                if(read>8192-total)throw new IOException("Observation output byte budget");total+=read;
                for(int index=0;index<read;index++)
                {
                    byte value=chunk[index];if(value==10)
                    {
                        if(++lines>16)throw new IOException("Observation output line count budget");
                        // Bound enforced on bytes before constructing any managed string.
                        if(destination is not null&&!destination.TryWrite(Encoding.ASCII.GetString(line,0,count).TrimEnd('\r')))throw new IOException("Observation output phase count budget");count=0;
                    }
                    else{if(((value<32||value>126)&&value!=13)||count==line.Length)throw new IOException("Observation output line/ASCII budget");line[count++]=value;}
                }
            }
            if(count!=0)throw new IOException("Observation partial output line");destination?.TryComplete();
        }
        catch(Exception e){destination?.TryComplete(e);throw;}
        finally{CryptographicOperations.ZeroMemory(chunk);CryptographicOperations.ZeroMemory(line);}
    }
    private sealed class NetworkCoverage
    {
        private readonly object gate=new();private readonly Stopwatch elapsed=Stopwatch.StartNew();
        private readonly int[] hosts=new int[NetworkPhases.Length];private readonly long[] first=new long[NetworkPhases.Length],last=new long[NetworkPhases.Length],maxGap=new long[NetworkPhases.Length];
        private int engines;private long engineFirst,engineLast,engineGap;
        internal void RecordHost(int phase,InstalledNetworkObservation.Counts counts)
        {
            if(!counts.Empty)throw new IOException("Observed host TCP/UDP owned rows");if(phase<0)return;
            lock(gate){long now=elapsed.ElapsedMilliseconds;if(hosts[phase]++==0)first[phase]=now;else maxGap[phase]=Math.Max(maxGap[phase],now-last[phase]);last[phase]=now;}
        }
        internal void RecordEngine(int phase,InstalledNetworkObservation.Counts counts)
        {
            if(!counts.Empty)throw new IOException("Observed engine TCP/UDP owned rows");
            if(phase is not(2 or 3))throw new IOException("Observed engine outside declared recognition phase");
            lock(gate){long now=elapsed.ElapsedMilliseconds;if(engines++==0)engineFirst=now;else engineGap=Math.Max(engineGap,now-engineLast);engineLast=now;}
        }
        internal void RequireHost(int phase){lock(gate)if(hosts[phase]==0)throw new IOException("Required observed host phase has no active sample");}
        internal void RequireComplete(){for(int phase=0;phase<6;phase++)RequireHost(phase);lock(gate)if(engines==0)throw new IOException("Required actual engine active coverage missing");}
        internal void Report(int discovered)
        {
            lock(gate)
            {
                for(int phase=0;phase<6;phase++)Console.WriteLine($"NETWORK_PHASE evidence=test-host-production-wpf-and-native-ocr phase={NetworkPhases[phase]} samples={hosts[phase]} firstMs={first[phase]} lastMs={last[phase]} maximumGapMs={maxGap[phase]} requestedIntervalMs=50 tcp4=0 tcp6=0 udp4=0 udp6=0");
                Console.WriteLine($"NETWORK_ENGINE evidence=actual-fixed-tesseract-child aggregateActiveSamples={engines} discoveredIdentityCount={discovered} aggregateFirstMs={engineFirst} aggregateLastMs={engineLast} aggregateMaximumSampleGapMs={engineGap} elapsedMs={elapsed.ElapsedMilliseconds} tcp4=0 tcp6=0 udp4=0 udp6=0");
            }
        }
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct NetworkProcessEntry
    {
        internal uint Size,Usage,Pid;internal UIntPtr Heap;internal uint Module,Threads,Parent;internal int Priority;internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]internal string Exe;
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]private static extern bool Process32FirstW(IntPtr snapshot,ref NetworkProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]private static extern bool Process32NextW(IntPtr snapshot,ref NetworkProcessEntry entry);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool CloseHandle(IntPtr handle);
    private static int[] NetworkChildren(int parent)
    {
        IntPtr snapshot=CreateToolhelp32Snapshot(2,0);if(snapshot==(IntPtr)(-1))throw new IOException("Observation process snapshot failed");
        try
        {
            var row=new NetworkProcessEntry{Size=(uint)Marshal.SizeOf<NetworkProcessEntry>()};if(!Process32FirstW(snapshot,ref row))throw new IOException("Observation first process row failed");
            var children=new List<int>();int count=0;
            do{if(++count>4096)throw new IOException("Observation total process rows bound");if(row.Parent==(uint)parent){if(children.Count==16)throw new IOException("Observation descendant rows bound");children.Add(checked((int)row.Pid));}}while(Process32NextW(snapshot,ref row));
            if(Marshal.GetLastWin32Error()!=18)throw new IOException("Observation process rows incomplete");return children.ToArray();
        }
        finally{if(!CloseHandle(snapshot))throw new IOException("Observation snapshot close failed");}
    }
    private static async Task NetworkPositiveChildRun()
    {
        var sockets=new List<Socket>();
        try
        {
            foreach(var family in Socket.OSSupportsIPv6?new[]{AddressFamily.InterNetwork,AddressFamily.InterNetworkV6}:new[]{AddressFamily.InterNetwork})
            {
                foreach(var protocol in new[]{ProtocolType.Tcp,ProtocolType.Udp})
                {
                    var socket=new Socket(family,protocol==ProtocolType.Tcp?SocketType.Stream:SocketType.Dgram,protocol);sockets.Add(socket);
                    if(family==AddressFamily.InterNetworkV6)socket.DualMode=false;socket.Bind(new IPEndPoint(family==AddressFamily.InterNetwork?IPAddress.Loopback:IPAddress.IPv6Loopback,0));if(protocol==ProtocolType.Tcp)socket.Listen(1);
                }
            }
            Console.WriteLine("OBS "+NetworkNonce()+" positive");Console.Out.Flush();if(await NetworkReadCommand(Console.OpenStandardInput())!="EXIT")throw new IOException("Positive child command");
        }
        finally{foreach(var socket in sockets)socket.Dispose();}
    }
    private static async Task NetworkPositiveProbe()
    {
        string nonce=Guid.NewGuid().ToString("N");using var child=new NetworkChild("--network-positive-child",nonce);
        try
        {
        await child.Start();try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));if(await child.Lines.Reader.ReadAsync(timeout.Token)!=$"OBS {nonce} positive")throw new IOException("Positive child readiness");
            var identity=InstalledNetworkObservation.Identity.Capture(child.Process);var counts=InstalledNetworkObservation.Snapshot(identity,child.Process);
            Require(counts.Tcp4>0&&counts.Udp4>0&&(!Socket.OSSupportsIPv6||counts.Tcp6>0&&counts.Udp6>0),"Native separate-child PID observation positive fixture");
            bool wrongPid=false;try{InstalledNetworkObservation.Snapshot(identity with{Pid=identity.Pid+1},child.Process);}catch(IOException){wrongPid=true;}Require(wrongPid,"Wrong PID cannot borrow bound process identity");
            bool rejected=false;try{InstalledNetworkObservation.Snapshot(identity with{Creation=identity.Creation+1},child.Process);}catch(IOException){rejected=true;}Require(rejected,"Wrong creation cannot contribute even positive PID rows");
            await child.Command("EXIT",timeout.Token);await child.Wait(timeout.Token);Require(child.Process.ExitCode==0,"Positive fixture normal child exit");
            rejected=false;try{InstalledNetworkObservation.Snapshot(identity,child.Process);}catch(IOException){rejected=true;}Require(rejected,"Killed/exited identity cannot contribute zero snapshot");
            Console.WriteLine("NETWORK_CROSS_PROCESS_POSITIVE pidCreationBound=True tcp4Seen=True udp4Seen=True ipv6Supported="+Socket.OSSupportsIPv6);
        }
        finally{await child.Drain();}
        }finally{await child.Drain();}
        using var killed=new NetworkChild("--network-positive-child",Guid.NewGuid().ToString("N"));
        try
        {
            await killed.Start();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));await killed.Lines.Reader.ReadAsync(deadline.Token);
            var identity=InstalledNetworkObservation.Identity.Capture(killed.Process);killed.Process.Kill(true);await killed.Wait(deadline.Token);
            bool refused=false;try{InstalledNetworkObservation.Snapshot(identity,killed.Process);}catch(IOException){refused=true;}Require(refused,"Actually killed identity cannot yield a zero sample");
        }finally{await killed.Drain();}
    }
    private static async Task NetworkProtocolChecksAsync()
    {
        bool rejected=false;try{InstalledNetworkObservation.SnapshotForChecks(1,static(IntPtr _,ref uint size)=>5);}catch(IOException){rejected=true;}Require(rejected,"Required native table failure cannot become zero observation");
        int queries=0;rejected=false;try{InstalledNetworkObservation.SnapshotForChecks(1,(IntPtr _,ref uint size)=>{queries++;size=4;return 122;});}catch(IOException){rejected=true;}Require(rejected&&queries==4,"Bounded unstable native tables fail after three attempts");
        var missing=new NetworkCoverage();for(int phase=0;phase<6;phase++)missing.RecordHost(phase,default);rejected=false;try{missing.RequireComplete();}catch(IOException){rejected=true;}Require(rejected,"Missing actual OCR active coverage must fail");
        for(int count=0;count<2;count++)
        {
            var channel=Channel.CreateBounded<string>(16);byte[] bytes=count==0?Enumerable.Repeat((byte)'A',129).ToArray():Enumerable.Repeat((byte)'\n',8193).ToArray();
            rejected=false;try{await ReadBoundedLines(new MemoryStream(bytes,false),count==0?channel.Writer:null);}catch(IOException){rejected=true;}Require(rejected,"Output line/transcript bounds before strings");
        }
        using(var cancel=new CancellationTokenSource())
        using(var pending=new PendingNetworkReadStream())
        {
            byte[] firstChunk=new byte[256],firstLine=new byte[128],secondChunk=new byte[256],secondLine=new byte[128];
            Task failed=ReadBoundedLines(new MemoryStream(Enumerable.Repeat((byte)'A',129).ToArray(),false),null,firstChunk,firstLine,cancel.Token);
            Task waiting=ReadBoundedLines(pending,null,secondChunk,secondLine,cancel.Token);
            Require(failed.IsFaulted&&pending.Started&&!waiting.IsCompleted,"One reader fault does not claim other real reader settled");
            cancel.Cancel();bool actualFailure=false;try{await Task.WhenAll(failed,waiting);}catch(IOException){actualFailure=true;}
            Require(actualFailure&&failed.IsCompleted&&waiting.IsCompleted&&pending.Terminal&&new[]{firstChunk,firstLine,secondChunk,secondLine}.All(a=>a.All(b=>b==0)),"Cancellation is passed to actual reader and both terminal tasks/owned buffers are drained even first faults");
        }
        Console.WriteLine("NETWORK_FAILURE_CONTRACTS nativeTableFailure=True missingCoverage=True boundedOutput=True actualReaderCancelAndDrain=True");
    }
    // Controlled inert stream for the otherwise OS-dependent pending-read teardown branch.
    private sealed class PendingNetworkReadStream:Stream
    {
        internal bool Started,Terminal;
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {Started=true;try{await Task.Delay(Timeout.Infinite,cancellationToken);return 0;}finally{Terminal=true;}}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }

}
