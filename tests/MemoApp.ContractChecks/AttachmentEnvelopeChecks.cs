using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using MemoApp.Core.Storage;
internal static class AttachmentEnvelopeChecks
{
    private delegate byte[] Seal(byte[] plaintext, byte[] vaultKey, byte[] recoveryKey, EnvelopeHeader header, Guid rootId, byte[] rootKey);
    private delegate IDisposable Open(byte[] bytes, byte[] recoveryKey);
    internal static void Run()
    {
        var type=typeof(EncryptedVault).Assembly.GetType("MemoApp.Core.Storage.AttachmentEnvelope");
        VaultChecks.Require(type is not null,"Strict envelope2 with owned temporary attachment root is missing");
        var encrypt=type!.GetMethod("Encrypt",BindingFlags.NonPublic|BindingFlags.Static)!.CreateDelegate<Seal>();
        var decrypt=type.GetMethod("Decrypt",BindingFlags.NonPublic|BindingFlags.Static)!.CreateDelegate<Open>();
        var vaultId=Guid.NewGuid();var rootId=Guid.NewGuid();var rootKey=RandomNumberGenerator.GetBytes(32);var vaultKey=RandomNumberGenerator.GetBytes(32);var secret=RandomNumberGenerator.GetBytes(32);
        var originalRoot=rootKey.ToArray();var originalVault=vaultKey.ToArray();var originalSecret=secret.ToArray();
        var original=Encoding.UTF8.GetBytes("SYNTHETIC_ATTACHMENT_ENVELOPE_ONLY original bytes가😀");
        var item=AttachmentObjectCodec.Encrypt(original,vaultId,rootId,rootKey,"合成.pdf","application/pdf");var now=DateTimeOffset.UtcNow;
        var snapshot=new VaultSnapshot(5,Guid.NewGuid(),[new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"synthetic","body"){AttachmentIds=[item.ObjectId]}]){AttachmentRootId=rootId,AttachmentObjects=[item]};
        byte[] plain=SnapshotSerialization.Bytes(snapshot);
        try
        {
            var header=new EnvelopeHeader(vaultId,Guid.NewGuid(),Guid.NewGuid(),1,3,plain.Length);
            var bytes=encrypt(plain,vaultKey,secret,header,rootId,rootKey);
            VaultChecks.Require(bytes.Length==plain.Length+308&&bytes.AsSpan(0,8).SequenceEqual("MEMOV002"u8)&&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8,4))==2&&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12,4))==1,"Strict84-byte envelope2 header and exact308-byte overhead");
            var independentlyRead=IndependentRead(bytes,secret);
            try{VaultChecks.Require(independentlyRead.RootId==rootId&&independentlyRead.RootKey.SequenceEqual(rootKey)&&independentlyRead.Payload.SequenceEqual(plain),"Independent BCL reader verifies rootUUID16BE||key32, offsets84/144/204/280 and purpose1/2/4/3 AAD");}
            finally{CryptographicOperations.ZeroMemory(independentlyRead.RootKey);CryptographicOperations.ZeroMemory(independentlyRead.Payload);}
            using(var opened=decrypt(bytes,secret))
            {
                var restored=(VaultSnapshot)opened.GetType().GetProperty("Snapshot",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(opened)!;
                var restoredHeader=(EnvelopeHeader)opened.GetType().GetProperty("Header",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(opened)!;
                VaultChecks.Require(JsonSerializer.Serialize(restored,VaultEnvelope.JsonOptions)==JsonSerializer.Serialize(snapshot,VaultEnvelope.JsonOptions)&&restoredHeader==header,"Authenticated schema5 immutable records and header roundtrip");
                var owner=(byte[])opened.GetType().GetField("rootKey",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(opened)!;
                VaultChecks.Require(owner.SequenceEqual(rootKey)&&!ReferenceEquals(owner,rootKey),"Temporary decoded root is an owned buffer");
                opened.Dispose();VaultChecks.Require(owner.All(b=>b==0),"Untransferred temporary root must be zeroed by dispose");
            }
            using(var opened=decrypt(bytes,secret))
            {
                var take=opened.GetType().GetMethod("TakeRootKey",BindingFlags.NonPublic|BindingFlags.Instance)!;
                var transferred=(byte[])take.Invoke(opened,null)!;
                try{opened.Dispose();VaultChecks.Require(transferred.SequenceEqual(rootKey)&&opened.GetType().GetField("rootKey",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(opened) is null,"Transfer is single ownership; old owner cannot zero new owner's buffer");}
                finally{CryptographicOperations.ZeroMemory(transferred);}
                bool rejected=false;try{take.Invoke(opened,null);}catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException){rejected=true;}VaultChecks.Require(rejected,"Double/disposed key transfer refused");
            }
            foreach(int offset in new[]{0,8,12,16,32,48,64,72,80,84,96,143,144,156,203,204,216,279,280,292,bytes.Length-1}){var altered=bytes.ToArray();altered[offset]^=1;Reject(altered,"Every envelope header/package/payload region must authenticate");}
            foreach(int size in new[]{0,83,84,203,279,307,bytes.Length-1})Reject(bytes[..size],"Strict truncated envelope2 rejected");
            Reject([..bytes,0],"No trailing envelope2 bytes");
            var oversize=bytes.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(oversize.AsSpan(80,4),uint.MaxValue);Reject(oversize,"Checked declared length before allocation");
            var wrong=RandomNumberGenerator.GetBytes(32);try{VaultChecks.ExpectFailure(()=>decrypt(bytes,wrong).Dispose(),"Wrong recovery key fails envelope2");}finally{CryptographicOperations.ZeroMemory(wrong);}
            VaultChecks.ExpectFailure(()=>encrypt(plain,vaultKey,secret,header with{WrapCount=2},rootId,rootKey),"Envelope2 minimum3 wraps");
            VaultChecks.ExpectFailure(()=>encrypt(plain,vaultKey,secret,header with{WrapCount=VaultEnvelope.MaxWraps+1},rootId,rootKey),"Envelope2 maximum wrap budget");
            VaultChecks.ExpectFailure(()=>encrypt(plain,vaultKey,secret,header with{Sequence=0},rootId,rootKey),"Envelope2 invalid sequence");
            VaultChecks.ExpectFailure(()=>encrypt(plain,vaultKey,secret,header with{VaultId=Guid.Empty},rootId,rootKey),"Envelope2 invalid header identity");
            VaultChecks.ExpectFailure(()=>encrypt(plain,vaultKey,secret,header,Guid.Empty,rootKey),"Envelope2 empty root identity");
            VaultChecks.ExpectFailure(()=>VaultEnvelope.ParseHeader(bytes),"Legacy envelope1 parser never silently accepts envelope2");
            Repack(snapshot with{AttachmentRootId=Guid.NewGuid(),AttachmentObjects=[] ,Notes=[]},"Root packet/snapshot identity mismatch");
            Repack(snapshot with{SchemaVersion=4,AttachmentRootId=Guid.Empty,AttachmentObjects=[],Notes=[]},"Envelope2 only carries schema5");
            Repack(snapshot with{AttachmentObjects=[item with{Chunks=item.Chunks.SetItem(0,Flip(item.Chunks[0]))}]},"Valid outer envelope still rejects unauthenticated internal object before open");
            Repack(snapshot with{AttachmentObjects=[item with{Sha256=new string('0',64)}]},"Valid outer envelope still checks complete original object hash");
            var missing=JsonSerializer.SerializeToNode(snapshot,VaultEnvelope.JsonOptions)!.AsObject();missing.Remove("attachmentRootId");RepackJson(missing.ToJsonString(),"Strict required schema5 root field");
            var unknown=JsonSerializer.SerializeToNode(snapshot,VaultEnvelope.JsonOptions)!.AsObject();unknown["unexpected"]=true;RepackJson(unknown.ToJsonString(),"Strict unknown outer snapshot field");
            RepackJson(Encoding.UTF8.GetString(plain).Replace("\"schemaVersion\":5","\"schemaVersion\":5,\"schemaVersion\":5",StringComparison.Ordinal),"Strict authenticated duplicate JSON properties");
            VaultChecks.Require(rootKey.SequenceEqual(originalRoot)&&vaultKey.SequenceEqual(originalVault)&&secret.SequenceEqual(originalSecret),"Borrowed encryption/recovery/root keys are unchanged");
            foreach(var marker in new[]{original,rootKey,vaultKey,secret,Encoding.UTF8.GetBytes(item.Name)})VaultChecks.Require(bytes.AsSpan().IndexOf(marker)<0,"No synthetic original bytes/name/root/vault/recovery key in envelope ciphertext");
            void Reject(byte[] candidate,string reason)=>VaultChecks.ExpectFailure(()=>decrypt(candidate,secret).Dispose(),reason);
            void Repack(VaultSnapshot candidate,string reason){var payload=SnapshotSerialization.Bytes(candidate);try{Reject(encrypt(payload,vaultKey,secret,header with{PayloadLength=payload.Length},rootId,rootKey),reason);}finally{CryptographicOperations.ZeroMemory(payload);}}
            void RepackJson(string json,string reason){var payload=Encoding.UTF8.GetBytes(json);try{Reject(encrypt(payload,vaultKey,secret,header with{PayloadLength=payload.Length},rootId,rootKey),reason);}finally{CryptographicOperations.ZeroMemory(payload);}}
            Console.WriteLine("PASS: strict envelope2, independent BCL layout/purpose/root reader, temporary-root transfer/dispose zeroing, package/header/EOF/schema/root/object tamper refusal (separate from durable root anchoring tests)");
        }
        finally{foreach(var key in new[]{rootKey,vaultKey,secret,originalRoot,originalVault,originalSecret,original,plain})CryptographicOperations.ZeroMemory(key);}
    }
    private static string Flip(string value){var bytes=Convert.FromBase64String(value);bytes[0]^=1;return Convert.ToBase64String(bytes);}
    private static (Guid RootId,byte[] RootKey,byte[] Payload) IndependentRead(byte[] bytes,byte[] secret)
    {
        byte[]? vault=null,data=null,packet=null,plain=null;
        try
        {
            vault=Package(84,32,secret,1);data=Package(144,32,vault,2);packet=Package(204,48,vault,4);plain=Package(280,bytes.Length-308,data,3);
            return(new Guid(packet.AsSpan(0,16),true),packet.AsSpan(16,32).ToArray(),plain);
        }
        catch{if(plain is not null)CryptographicOperations.ZeroMemory(plain);throw;}
        finally{foreach(var key in new[]{vault,data,packet})if(key is not null)CryptographicOperations.ZeroMemory(key);}
        byte[] Package(int offset,int length,byte[] key,byte purpose)
        {var output=new byte[length];try{using var cipher=new AesGcm(key,16);cipher.Decrypt(bytes.AsSpan(offset,12),bytes.AsSpan(offset+12,length),bytes.AsSpan(offset+12+length,16),output,[..bytes.AsSpan(0,84).ToArray(),purpose]);return output;}catch{CryptographicOperations.ZeroMemory(output);throw;}}
    }
}
