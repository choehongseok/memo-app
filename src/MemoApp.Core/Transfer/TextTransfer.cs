using System.Security.Cryptography;
using System.Text;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Transfer;
public sealed record ImportedText(string Title, string Text, string SourceSha256);
public sealed class PreparedTextExport : IDisposable
{
    internal PreparedTextExport(byte[] bytes) => Bytes = bytes;
    internal byte[] Bytes { get; private set; }
    internal bool IsDisposed { get; private set; }
    public void Dispose() { CryptographicOperations.ZeroMemory(Bytes); Bytes = []; IsDisposed = true; }
}
public static class TextTransfer
{
    public static ImportedText Read(string source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); source = LocalFilePath.Resolve(source); LocalFilePath.CheckAncestors(source, true);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = input.Length;
        if (length > 1024 * 1024) throw new InvalidDataException("Text source size limit");
        var bytes = new byte[checked((int)length)];
        try
        {
            input.ReadExactly(bytes); if (input.ReadByte() != -1) throw new IOException("Text source changed while reading");
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }) || bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) throw new InvalidDataException("UTF32 not supported");
            string text;
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) text = new UnicodeEncoding(false, false, true).GetString(bytes.AsSpan(2));
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) text = new UnicodeEncoding(true, false, true).GetString(bytes.AsSpan(2));
            else text = new UTF8Encoding(false, true).GetString(bytes.AsSpan(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0));
            string title = Path.GetFileNameWithoutExtension(source);
            if (text.Length > 65536 || text.Contains('\0') || title.Length > 256) throw new InvalidDataException("Text content/title limit or binary input");
            cancellationToken.ThrowIfCancellationRequested();
            return new(title, Newlines(text), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static PreparedTextExport Capture(NoteDraft note, bool includeTitle = true)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (note.IsClosed || note.IsDeleted) throw new InvalidOperationException("Only active unlocked notes may be exported");
        string title = note.Title, text = note.Text;
        if (title.Length > 256 || text.Length > 65536 || title.Contains('\0') || text.Contains('\0')) throw new InvalidDataException("Text export limits");
        string payload = (includeTitle && title.Length != 0 ? Newlines(title) + "\n\n" : "") + Newlines(text);
        // Encoding happens before any destination is created. No replacement of malformed surrogates.
        return new(new UTF8Encoding(false, true).GetBytes(payload.Replace("\n", "\r\n", StringComparison.Ordinal)));
    }
    public static void Write(NoteDraft note, string destination, bool includeTitle = true)
    {
        using var prepared = Capture(note, includeTitle); WritePrepared(prepared, destination);
    }
    public static void WritePrepared(PreparedTextExport prepared, string destination, CancellationToken cancellationToken = default, IAtomicVaultFiles? files = null)
    {
        if (prepared.IsDisposed) throw new InvalidOperationException("Export payload is disposed");
        cancellationToken.ThrowIfCancellationRequested(); destination = LocalFilePath.Resolve(destination); LocalFilePath.CheckAncestors(destination, false);
        var outputFiles = files ?? new AtomicVaultFiles();
        cancellationToken.ThrowIfCancellationRequested();
        using var output = outputFiles.CreateNew(destination);
        cancellationToken.ThrowIfCancellationRequested(); // CreateNew itself can block while the session is concealed.
        output.Write(prepared.Bytes); outputFiles.FlushToDisk(output);
        // On failure a partial plaintext file may remain; never delete an unverified destination.
    }
    private static string Newlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
