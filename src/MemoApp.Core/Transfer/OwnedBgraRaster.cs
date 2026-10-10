using System.Security.Cryptography;
namespace MemoApp.Core.Transfer;

public delegate void BgraPixelReader(ReadOnlySpan<byte> pixels);

// Detached pixels only; no source/session/publication authority and no callback retention.
public sealed class OwnedBgraRaster : IDisposable
{
    private readonly object gate = new();
    private byte[]? pixels;
    private bool running, finished, revoked;
    internal OwnedBgraRaster(int width, int height, byte[] ownedPixels)
    { Width = width; Height = height; Stride = checked(width * 4); pixels = ownedPixels; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public bool ConsumePixels(BgraPixelReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        byte[] borrowed;
        lock (gate)
        {
            if (running || finished || revoked) throw new InvalidOperationException("Preview raster access ended or was already consumed");
            running = true; borrowed = pixels!;
        }
        bool success = false;
        try { reader(borrowed); }
        finally
        {
            lock (gate)
            {
                success = !revoked; running = false; finished = true;
                CryptographicOperations.ZeroMemory(borrowed); pixels = null;
            }
        }
        return success;
    }
    public void Dispose()
    {
        lock (gate)
        {
            revoked = true;
            if (!running && !finished)
            {
                finished = true;
                if (pixels is not null) CryptographicOperations.ZeroMemory(pixels);
                pixels = null;
            }
        }
    }
}
