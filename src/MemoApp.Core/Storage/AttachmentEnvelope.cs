using System.Security.Cryptography;
using System.Text.Json;
namespace MemoApp.Core.Storage;

// Temporary decrypt owner. A successfully opened vault takes the root once; every other path disposes it.
internal sealed class DecodedAttachmentEnvelope : IDisposable
{
    private byte[]? rootKey;
    internal EnvelopeHeader Header { get; }
    internal VaultSnapshot Snapshot { get; }
    internal DecodedAttachmentEnvelope(EnvelopeHeader header, VaultSnapshot snapshot, byte[]? rootKey)
    { Header=header; Snapshot=snapshot; this.rootKey=rootKey; }
    internal byte[] TakeRootKey()
    { return Interlocked.Exchange(ref rootKey,null)??throw new InvalidOperationException("Attachment root ownership ended"); }
    public void Dispose()
    { var owned=Interlocked.Exchange(ref rootKey,null);if(owned is not null)CryptographicOperations.ZeroMemory(owned); }
}

// Separate strict v2 layout; the vault owner alone keeps a transferred root beyond synchronous decoding.
internal static class AttachmentEnvelope
{
    internal const int Overhead=308;
    private static readonly byte[] Magic="MEMOV002"u8.ToArray();
    internal static byte[] Encrypt(byte[] plaintext,byte[] vaultKey,byte[] recoveryKey,EnvelopeHeader header,Guid rootId,byte[] rootKey)
    {
        if(vaultKey.Length!=32||recoveryKey.Length!=32||rootKey.Length!=32||rootId==Guid.Empty||header.PayloadLength!=plaintext.Length)throw new InvalidDataException("Attachment envelope key/root/payload");
        var aad=WriteHeader(header);
        byte[]? dataKey=null;var packet=new byte[48];
        try
        {
            dataKey=RandomNumberGenerator.GetBytes(32);
            rootId.TryWriteBytes(packet.AsSpan(0,16),true,out _);rootKey.CopyTo(packet,16);
            using var output=new MemoryStream();output.Write(aad);
            Seal(output,recoveryKey,vaultKey,[..aad,1]);Seal(output,vaultKey,dataKey,[..aad,2]);Seal(output,vaultKey,packet,[..aad,4]);Seal(output,dataKey,plaintext,[..aad,3]);
            return output.ToArray();
        }
        finally{if(dataKey is not null)CryptographicOperations.ZeroMemory(dataKey);CryptographicOperations.ZeroMemory(packet);}
    }
    internal static DecodedAttachmentEnvelope Decrypt(byte[] bytes,byte[] recoveryKey)
    {
        if(recoveryKey.Length!=32)throw new ArgumentException("Recovery secret must be32 bytes");
        var header=ParseHeader(bytes);
        byte[]? oldVaultKey=null,dataKey=null,packet=null,plaintext=null,ownedRoot=null;
        try
        {
            var aad=bytes[..VaultEnvelope.HeaderSize];
            oldVaultKey=Open(bytes,84,32,recoveryKey,[..aad,1]);dataKey=Open(bytes,144,32,oldVaultKey,[..aad,2]);packet=Open(bytes,204,48,oldVaultKey,[..aad,4]);
            var rootId=new Guid(packet.AsSpan(0,16),true);if(rootId==Guid.Empty)throw new InvalidDataException("Empty attachment root");
            plaintext=Open(bytes,280,header.PayloadLength,dataKey,[..aad,3]);
            var snapshot=VaultEnvelope.ReadSnapshot(plaintext);
            if(snapshot.SchemaVersion is not(5 or 6 or 7 or 8)||snapshot.AttachmentRootId!=rootId)throw new InvalidDataException("Envelope2/schema5-8/root pairing");
            foreach(var item in snapshot.AttachmentObjects)
            {
                var original=AttachmentObjectCodec.Decrypt(item,header.VaultId,rootId,packet.AsSpan(16,32));
                try{ /* Authentication/padding/hash were checked before this owned temporary was returned. */ }
                finally{CryptographicOperations.ZeroMemory(original);}
            }
            ownedRoot=packet.AsSpan(16,32).ToArray();
            var result=new DecodedAttachmentEnvelope(header,snapshot,ownedRoot);ownedRoot=null;return result;
        }
        catch(JsonException){throw new InvalidDataException("Invalid attachment snapshot JSON");}
        finally
        {
            if(oldVaultKey is not null)CryptographicOperations.ZeroMemory(oldVaultKey);if(dataKey is not null)CryptographicOperations.ZeroMemory(dataKey);
            if(packet is not null)CryptographicOperations.ZeroMemory(packet);if(plaintext is not null)CryptographicOperations.ZeroMemory(plaintext);if(ownedRoot is not null)CryptographicOperations.ZeroMemory(ownedRoot);
        }
    }
    private static byte[] Open(byte[] bytes,int offset,int length,byte[] key,byte[] aad)
    {
        var output=new byte[length];
        try{using var cipher=new AesGcm(key,16);cipher.Decrypt(bytes.AsSpan(offset,12),bytes.AsSpan(offset+12,length),bytes.AsSpan(offset+12+length,16),output,aad);return output;}
        catch{CryptographicOperations.ZeroMemory(output);throw;}
    }
    private static void Seal(Stream output,byte[] key,byte[] plaintext,byte[] aad)
    {
        var nonce=RandomNumberGenerator.GetBytes(12);var bytes=new byte[plaintext.Length];var tag=new byte[16];
        using var cipher=new AesGcm(key,16);cipher.Encrypt(nonce,plaintext,bytes,tag,aad);output.Write(nonce);output.Write(bytes);output.Write(tag);
    }
    private static EnvelopeHeader ParseHeader(byte[] bytes)
    {
        if(bytes.Length<=Overhead||bytes.Length>VaultEnvelope.MaxFile)throw new InvalidDataException("Attachment envelope size");
        using var reader=new BinaryReader(new MemoryStream(bytes,false));
        if(!reader.ReadBytes(8).SequenceEqual(Magic)||reader.ReadUInt32()!=2||reader.ReadUInt32()!=1)throw new InvalidDataException("Unsupported attachment envelope");
        var vault=new Guid(reader.ReadBytes(16),true);var epoch=new Guid(reader.ReadBytes(16),true);var snapshot=new Guid(reader.ReadBytes(16),true);
        ulong sequence=reader.ReadUInt64(),wraps=reader.ReadUInt64();uint size=reader.ReadUInt32();
        if(size>VaultEnvelope.MaxFile-Overhead||checked((long)Overhead+size)!=bytes.Length)throw new InvalidDataException("Truncated/trailing attachment envelope");
        var header=new EnvelopeHeader(vault,epoch,snapshot,sequence,wraps,checked((int)size));Fields(header);return header;
    }
    private static void Fields(EnvelopeHeader header)
    {
        if(header.VaultId==Guid.Empty||header.Epoch==Guid.Empty||header.SnapshotId==Guid.Empty||header.Sequence==0||header.WrapCount is <3 or >VaultEnvelope.MaxWraps||header.PayloadLength<=0||header.PayloadLength>VaultEnvelope.MaxFile-Overhead)throw new InvalidDataException("Attachment envelope fields/budget");
    }
    private static byte[] WriteHeader(EnvelopeHeader header)
    {
        Fields(header);using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);
        writer.Write(Magic);writer.Write(2U);writer.Write(1U);writer.Write(header.VaultId.ToByteArray(true));writer.Write(header.Epoch.ToByteArray(true));writer.Write(header.SnapshotId.ToByteArray(true));
        writer.Write(header.Sequence);writer.Write(header.WrapCount);writer.Write(checked((uint)header.PayloadLength));return bytes.ToArray();
    }
}
