using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Transfer;

public sealed class PreparedTextBatch : IDisposable
{
    internal PreparedTextBatch((string Name,PreparedTextExport Payload)[] entries)=>Entries=entries;
    internal (string Name,PreparedTextExport Payload)[] Entries{get;private set;}
    internal bool IsDisposed{get;private set;}
    public string[] FileNames=>Entries.Select(e=>e.Name).ToArray();
    public void Dispose(){if(IsDisposed)return;IsDisposed=true;foreach(var entry in Entries)entry.Payload.Dispose();Entries=[];}
}
public static class BatchTextTransfer
{
    private const int MaxBytes=16*1024*1024;
    public static PreparedTextBatch Capture(IEnumerable<NoteDraft> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);var notes=selected.Take(101).ToArray();
        if(notes.Length is <1 or >100||notes.Any(n=>n is null)||notes.Select(n=>n.Id).Distinct().Count()!=notes.Length)throw new InvalidOperationException("Batch selection limit/duplicates");
        var entries=new List<(string,PreparedTextExport)>();int length=0;
        try
        {
            foreach(var note in notes)
            {
                var payload=TextTransfer.Capture(note);entries.Add(($"memo-{note.Id:N}.txt",payload));
                if(payload.Bytes.Length>MaxBytes-length)throw new InvalidDataException("Batch plaintext byte limit");length+=payload.Bytes.Length;
            }
            return new(entries.ToArray());
        }
        catch{foreach(var entry in entries)entry.Item2.Dispose();throw;}
    }
    public static void WritePrepared(PreparedTextBatch prepared,string directory,CancellationToken cancellationToken=default,IAtomicVaultFiles? files=null)
    {
        ArgumentNullException.ThrowIfNull(prepared);if(prepared.IsDisposed)throw new InvalidOperationException("Batch is disposed");
        cancellationToken.ThrowIfCancellationRequested();directory=LocalFilePath.Resolve(directory);LocalFilePath.CheckDataRoot(directory);
        if(!Directory.Exists(directory))throw new DirectoryNotFoundException("Choose an existing local directory");
        var paths=prepared.Entries.Select(e=>Path.Combine(directory,e.Name)).ToArray();
        // Preflight the entire selection, then rely on CreateNew against races. Partial files remain on failure.
        foreach(string path in paths){cancellationToken.ThrowIfCancellationRequested();LocalFilePath.CheckAncestors(path,false);if(File.Exists(path)||Directory.Exists(path))throw new IOException("Batch destination already exists");}
        for(int index=0;index<paths.Length;index++){cancellationToken.ThrowIfCancellationRequested();TextTransfer.WritePrepared(prepared.Entries[index].Payload,paths[index],cancellationToken,files);}
    }
}
