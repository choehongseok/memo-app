namespace MemoApp.Core.Storage;
public interface IAtomicVaultFiles
{
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);
    void Replace(string temporary, string current, string previous);
    void Move(string temporary, string current);
}
public sealed class AtomicVaultFiles : IAtomicVaultFiles
{
    public Stream CreateNew(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }
    public void FlushToDisk(Stream stream) => ((FileStream)stream).Flush(true);
    public void Replace(string temporary, string current, string previous) => File.Replace(temporary, current, previous);
    public void Move(string temporary, string current) => File.Move(temporary, current);
}
