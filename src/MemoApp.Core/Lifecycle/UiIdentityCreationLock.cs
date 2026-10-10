using System.Diagnostics;
using MemoApp.Core.Transfer;
namespace MemoApp.Core.Lifecycle;

// Caller holds LocalUiIdentity's global monitor through disposal. The persistent empty lock is never deleted.
internal static class UiIdentityCreationLock
{
    internal static FileStream Acquire(string root, int timeoutMilliseconds = 5000)
    {
        if (timeoutMilliseconds is < 0 or > 5000) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) throw new IOException("UI identity creation locking is unsupported");
        string path = LocalFilePath.Resolve(Path.Combine(root, "ui-device.create.lock"));
        LocalFilePath.CheckDataRoot(root);
        var stream = LocalRegularFile.OpenCreationLock(path);
        try
        {
            if (!stream.CanSeek || stream.Length != 0) throw new InvalidDataException("UI identity creation lock must be empty and regular");
            var wait = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    if (OperatingSystem.IsWindows()) stream.Lock(0, 1);
                    else if (OperatingSystem.IsLinux()) stream.Lock(0, 1);
                    else throw new IOException("UI identity creation locking is unsupported");
                    break;
                }
                catch (IOException error) when (Contention(error))
                {
                    if (wait.ElapsedMilliseconds >= timeoutMilliseconds) throw new IOException("UI identity creation lock contention timeout");
                    Thread.Sleep(10);
                }
            }
            if (stream.Length != 0) throw new InvalidDataException("UI identity creation lock changed");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
    private static bool Contention(IOException error) => OperatingSystem.IsLinux() ? error.HResult == 11 :
        OperatingSystem.IsWindows() && error.HResult == unchecked((int)0x80070021);
}
