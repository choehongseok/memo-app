using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

// Test-only snapshots, not packet capture or a firewall. Addresses are never decoded or logged.
internal static class InstalledNetworkObservation
{
    internal const int IntervalMilliseconds = 50;
    private const uint AfInet = 2, AfInet6 = 23, TcpOwnerPidAll = 5, UdpOwnerPid = 1;
    private const uint InsufficientBuffer = 122;
    private static readonly byte[] ClearBlock=new byte[1024];
    private const int MaximumTableBytes = 1024 * 1024, MaximumAttempts = 3;

    // DWORD/UCHAR layouts from Microsoft SDK documentation. Explicit offsets avoid
    // accidentally interpreting addresses, padding, or IPv6 rows as IPv4 rows.
    // https://learn.microsoft.com/windows/win32/api/tcpmib/ns-tcpmib-mib_tcprow_owner_pid
    // https://learn.microsoft.com/windows/win32/api/tcpmib/ns-tcpmib-mib_tcp6row_owner_pid
    // https://learn.microsoft.com/windows/win32/api/udpmib/ns-udpmib-mib_udprow_owner_pid
    // https://learn.microsoft.com/windows/win32/api/udpmib/ns-udpmib-mib_udp6row_owner_pid
    private readonly record struct Table(bool Tcp, uint Family, int RowBytes, int PidOffset, int PortOffset, int StateOffset);
    private static readonly Table[] Tables = [new(true, AfInet, 24, 20, 8, 0), new(true, AfInet6, 56, 52, 20, 48), new(false, AfInet, 12, 8, 4, -1), new(false, AfInet6, 28, 24, 20, -1)];
    internal readonly record struct Counts(int Tcp4, int Tcp6, int Udp4, int Udp6)
    {
        internal bool Empty => Tcp4 == 0 && Tcp6 == 0 && Udp4 == 0 && Udp6 == 0;
        public override string ToString() => $"tcp4={Tcp4} tcp6={Tcp6} udp4={Udp4} udp6={Udp6}";
    }

    // Positive fixture first: a decoder that always returns zero must fail here,
    // before any zero-row claim about the actual installed child can be made.
    internal static void VerifySyntheticSockets()
    {
        VerifyFamily(AddressFamily.InterNetwork, IPAddress.Loopback, Tables[0], Tables[2]);
        if (Socket.OSSupportsIPv6)
            VerifyFamily(AddressFamily.InterNetworkV6, IPAddress.IPv6Loopback, Tables[1], Tables[3]);
        else Console.WriteLine("NETWORK_OBSERVER_SELF_CHECK ipv6=unsupported; IPv6 child tables still required");
    }

    private static void VerifyFamily(AddressFamily family, IPAddress loopback, Table tcpTable, Table udpTable)
    {
        using var tcp = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
        using var udp = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        if (family == AddressFamily.InterNetworkV6) { tcp.DualMode = false; udp.DualMode = false; }
        tcp.Bind(new IPEndPoint(loopback, 0)); tcp.Listen(1);
        udp.Bind(new IPEndPoint(loopback, 0));
        int tcpPort = ((IPEndPoint)tcp.LocalEndPoint!).Port, udpPort = ((IPEndPoint)udp.LocalEndPoint!).Port;
        uint pid = checked((uint)Environment.ProcessId);
        bool tcpSeen = false, udpSeen = false;
        var timer = Stopwatch.StartNew();
        do
        {
            ReadRows(tcpTable, pid, tcpPort, out tcpSeen);
            ReadRows(udpTable, pid, udpPort, out udpSeen);
            if (tcpSeen && udpSeen) break;
            Thread.Sleep(IntervalMilliseconds);
        } while (timer.Elapsed < TimeSpan.FromSeconds(2));
        if (!tcpSeen || !udpSeen) throw new IOException($"Network observer synthetic fixture missing: family={family} tcpListenerSeen={tcpSeen} udpBoundSeen={udpSeen}");
        Console.WriteLine($"NETWORK_OBSERVER_SELF_CHECK family={family} tcpListenerSeen={tcpSeen} udpBoundSeen={udpSeen}");
    }

    internal static Counts Snapshot(uint pid) => new(ReadRows(Tables[0], pid, null, out _), ReadRows(Tables[1], pid, null, out _), ReadRows(Tables[2], pid, null, out _), ReadRows(Tables[3], pid, null, out _));

    internal delegate uint NativeQuery(IntPtr buffer,ref uint size);
    internal static Counts SnapshotForChecks(uint pid,NativeQuery query)=>new(ReadRows(Tables[0],pid,null,out _,query),ReadRows(Tables[1],pid,null,out _,query),ReadRows(Tables[2],pid,null,out _,query),ReadRows(Tables[3],pid,null,out _,query));
    private static int ReadRows(Table table, uint pid, int? syntheticPort, out bool fixtureSeen,NativeQuery? testQuery=null)
    {
        fixtureSeen = false;
        uint size = 0;
        uint error = testQuery is null?Query(table,IntPtr.Zero,ref size):testQuery(IntPtr.Zero,ref size);
        if (error != InsufficientBuffer) throw NativeFailure(table, error);
        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            if (size < sizeof(uint) || size > MaximumTableBytes) throw new IOException("Network observer table exceeds bounded size or lacks header");
            int allocated = checked((int)size);
            IntPtr buffer = Marshal.AllocHGlobal(allocated);
            try
            {
                error = testQuery is null?Query(table,buffer,ref size):testQuery(buffer,ref size);
                if (error == InsufficientBuffer) continue;
                if (error != 0) throw NativeFailure(table, error);
                if (size < sizeof(uint) || size > allocated) throw new IOException("Network observer native table returned invalid byte length");
                uint entries = unchecked((uint)Marshal.ReadInt32(buffer));
                if (entries > (size - sizeof(uint)) / table.RowBytes) throw new IOException("Network observer native table row count exceeds returned buffer");
                int owned = 0;
                for (int row = 0; row < entries; row++)
                {
                    int offset = checked(sizeof(uint) + row * table.RowBytes);
                    if (unchecked((uint)Marshal.ReadInt32(buffer, offset + table.PidOffset)) != pid) continue;
                    owned++;
                    if (syntheticPort is int port)
                    {
                        uint nativePort = unchecked((uint)Marshal.ReadInt32(buffer, offset + table.PortOffset));
                        int hostPort = (int)(((nativePort & 255) << 8) | ((nativePort >> 8) & 255));
                        if (hostPort == port && (!table.Tcp || Marshal.ReadInt32(buffer, offset + table.StateOffset) == 2)) fixtureSeen = true;
                    }
                }
                return owned;
            }
            finally { for(int clear=0;clear<allocated;clear+=ClearBlock.Length)Marshal.Copy(ClearBlock,0,IntPtr.Add(buffer,clear),Math.Min(ClearBlock.Length,allocated-clear)); Marshal.FreeHGlobal(buffer); }
        }
        throw new IOException("Network observer native table did not stabilize within three bounded attempts");
    }

    private static IOException NativeFailure(Table table, uint error) => new($"Network observer API failed: protocol={(table.Tcp ? "TCP" : "UDP")} family={table.Family} nativeError={error}");
    private static uint Query(Table table, IntPtr buffer, ref uint size) => table.Tcp
        ? GetExtendedTcpTable(buffer, ref size, false, table.Family, TcpOwnerPidAll, 0)
        : GetExtendedUdpTable(buffer, ref size, false, table.Family, UdpOwnerPid, 0);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);

    internal readonly record struct Identity(int Pid,long Creation)
    {
        internal static Identity Capture(Process process)
        { process.Refresh();if(process.HasExited)throw new IOException("Observation process already exited");return new(process.Id,process.StartTime.ToUniversalTime().ToFileTimeUtc()); }
        internal void RequireLive(Process process)
        { if(Capture(process)!=this)throw new IOException("Observation process identity changed"); }
    }
    internal static Counts Snapshot(Identity identity,Process process)
    {identity.RequireLive(process);var counts=Snapshot(checked((uint)identity.Pid));identity.RequireLive(process);return counts;}

    internal sealed class ChildMonitor(Process child, string scenario)
    {
        private readonly Identity identity=Identity.Capture(child);
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private int samples;
        private long previousSample, maximumGap;
        internal void Sample()
        {
            child.Refresh();
            if (child.HasExited) throw new IOException("Installed network observation child exited before sample");
            Counts counts = Snapshot(identity,child);
            child.Refresh();
            if (child.HasExited) throw new IOException("Installed network observation child exited during sample");
            long now = elapsed.ElapsedMilliseconds;
            if (samples > 0) maximumGap = Math.Max(maximumGap, now - previousSample);
            previousSample = now; samples++;
            if (!counts.Empty) throw new IOException($"Installed child network rows observed: scenario={scenario} sample={samples} {counts}");
        }
        internal bool WaitForInputIdle(int timeoutMilliseconds)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                Sample();
                if (child.WaitForInputIdle(0)) return true;
                Thread.Sleep(IntervalMilliseconds);
            } while (timer.ElapsedMilliseconds < timeoutMilliseconds);
            return false;
        }
        internal void ObserveIdle()
        {
            var timer = Stopwatch.StartNew();
            do { Sample(); Thread.Sleep(IntervalMilliseconds); } while (timer.Elapsed < TimeSpan.FromSeconds(3));
            Sample();
            Console.WriteLine($"INSTALLED_CHILD_NETWORK scenario={scenario} samples={samples} elapsedMs={elapsed.ElapsedMilliseconds} idleMs={timer.ElapsedMilliseconds} requestedIntervalMs={IntervalMilliseconds} maximumSampleGapMs={maximumGap} tcp4=0 tcp6=0 udp4=0 udp6=0");
        }
    }
}
