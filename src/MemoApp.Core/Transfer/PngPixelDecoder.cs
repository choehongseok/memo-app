using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
namespace MemoApp.Core.Transfer;

// Inactive Core primitive. Later UI must admit globally before decrypting and recheck publication authority.
internal static class PngPixelDecoder
{
    internal static OwnedBgraRaster Decode(ReadOnlySpan<byte> source, CancellationToken cancellation = default, Action<byte[]>? allocations = null)
    {
        byte[]? compressed = null, rowA = null, rowB = null, pixels = null;
        Span<byte> single = stackalloc byte[1];
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var header = PngPreviewProfile.Inspect(source);
            Allocate(ref compressed, header.DataBytes, allocations, cancellation);
            int position = 8, copied = 0;
            while (position < source.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(source[position..]));
                if (source.Slice(position + 4, 4).SequenceEqual("IDAT"u8))
                { source.Slice(position + 8, length).CopyTo(compressed!.AsSpan(copied)); copied = checked(copied + length); }
                position = checked(position + length + 12);
            }
            if (copied != header.DataBytes || compressed!.Length < 6) throw Refused();
            int cmf = compressed[0], flg = compressed[1];
            if ((cmf & 15) != 8 || (cmf >> 4) > 7 || ((cmf << 8) + flg) % 31 != 0 || (flg & 32) != 0) throw Refused();
            int rowBytes = checked(header.Width * header.Channels);
            Allocate(ref rowA, rowBytes, allocations, cancellation);
            Allocate(ref rowB, rowBytes, allocations, cancellation);
            Allocate(ref pixels, checked(header.PreviewPixels * 4), allocations, cancellation);
            cancellation.ThrowIfCancellationRequested();
            uint adlerA = 1, adlerB = 0;
            using (var input = new ExactZlibInput(compressed, cancellation))
            {
                using (var inflater = new ZLibStream(input, CompressionMode.Decompress, true))
                {
                    byte[] current = rowA!, previous = rowB!;
                    int outputY = 0;
                    for (int y = 0; y < header.Height; y++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        inflater.ReadExactly(single);
                        int filter = single[0];
                        if (filter > 4) throw Refused();
                        inflater.ReadExactly(current);
                        AddAdler(single, ref adlerA, ref adlerB);
                        AddAdler(current, ref adlerA, ref adlerB); // Checksum covers filtered bytes before reconstruction.
                        Reconstruct(current, previous, header.Channels, filter);
                        if (outputY < header.PreviewHeight && y == (long)outputY * header.Height / header.PreviewHeight)
                        {
                            for (int x = 0; x < header.PreviewWidth; x++)
                            {
                                int from = checked((int)((long)x * header.Width / header.PreviewWidth) * header.Channels);
                                int to = checked((outputY * header.PreviewWidth + x) * 4);
                                if (header.Channels <= 2)
                                {
                                    pixels![to] = current[from]; pixels[to + 1] = current[from]; pixels[to + 2] = current[from];
                                    pixels[to + 3] = header.Channels == 2 ? current[from + 1] : (byte)255;
                                }
                                else
                                {
                                    pixels![to] = current[from + 2]; pixels[to + 1] = current[from + 1]; pixels[to + 2] = current[from];
                                    pixels[to + 3] = header.Channels == 4 ? current[from + 3] : (byte)255;
                                }
                            }
                            outputY++;
                        }
                        (current, previous) = (previous, current);
                    }
                    if (outputY != header.PreviewHeight || inflater.Read(single) != 0 || input.Position != compressed.Length) throw Refused();
                    uint stored = BinaryPrimitives.ReadUInt32BigEndian(compressed.AsSpan(compressed.Length - 4));
                    if ((adlerB << 16 | adlerA) != stored) throw Refused();
                }
            } // Teardown must succeed before any raster ownership is transferred.
            cancellation.ThrowIfCancellationRequested();
            var result = new OwnedBgraRaster(header.PreviewWidth, header.PreviewHeight, pixels!);
            pixels = null; return result;
        }
        catch (InvalidDataException) { throw Refused(); }
        catch (EndOfStreamException) { throw Refused(); }
        finally
        {
            single.Clear();
            if (compressed is not null) CryptographicOperations.ZeroMemory(compressed);
            if (rowA is not null) CryptographicOperations.ZeroMemory(rowA);
            if (rowB is not null) CryptographicOperations.ZeroMemory(rowB);
            if (pixels is not null) CryptographicOperations.ZeroMemory(pixels);
        }
    }
    private static void Allocate(ref byte[]? owned, int count, Action<byte[]>? observer, CancellationToken cancellation)
    { cancellation.ThrowIfCancellationRequested(); owned = new byte[count]; observer?.Invoke(owned); }
    private static InvalidDataException Refused() => new("Unsupported PNG preview pixels");
    private static void AddAdler(ReadOnlySpan<byte> bytes, ref uint a, ref uint b)
    { foreach (byte value in bytes) { a = (a + value) % 65521; b = (b + a) % 65521; } }
    private static void Reconstruct(Span<byte> row, ReadOnlySpan<byte> previous, int channels, int filter)
    {
        for (int i = 0; i < row.Length; i++)
        {
            int a = i >= channels ? row[i - channels] : 0, b = previous[i], c = i >= channels ? previous[i - channels] : 0;
            int prediction = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => throw Refused() };
            row[i] = unchecked((byte)(row[i] + prediction));
        }
    }
    private static int Paeth(int a, int b, int c)
    { int p = a + b - c, da = Math.Abs(p - a), db = Math.Abs(p - b), dc = Math.Abs(p - c); return da <= db && da <= dc ? a : db <= dc ? b : c; }
    private sealed class ExactZlibInput(byte[] source, CancellationToken cancellation) : Stream
    {
        private int position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        { ValidateBufferArguments(buffer, offset, count); return Read(buffer.AsSpan(offset, count)); }
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty) return 0;
            cancellation.ThrowIfCancellationRequested();
            if (position >= source.Length) throw Refused();
            buffer[0] = source[position++]; return 1;
        }
        public override int ReadByte() { Span<byte> one = stackalloc byte[1]; if (Read(one) != 1) throw Refused(); return one[0]; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
