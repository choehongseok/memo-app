using System.Reflection;
using System.Security.Cryptography;
using MemoApp.Core.Transfer;
internal static class ClipboardPngChecks
{
    internal static void Run()
    {
        var type=typeof(AttachmentSource).Assembly.GetType("MemoApp.Core.Transfer.ClipboardPngSource")??throw new Exception("Bounded clipboard PNG source is missing");var method=type.GetMethod("Capture")??throw new Exception("Clipboard PNG capture is missing");
        byte[] vector=[0x78,0x01,0x01,0x05,0x00,0xfa,0xff,0,1,2,3,4,0,0x19,0,0x0b];byte[] original=(byte[])typeof(PngDecoderChecks).GetMethod("Png",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[1,1,4,new byte[][]{vector}])!;
        using(var captured=(IDisposable)method.Invoke(null,[original])!){byte[] owned=(byte[])type.GetField("bytes",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(captured)!;VaultChecks.Require(!ReferenceEquals(owned,original)&&owned.SequenceEqual(original),"Owned clipboard copy is exact and separate");captured.Dispose();VaultChecks.Require(owned.All(b=>b==0)&&original.Any(b=>b!=0),"Only owned copy zeroed");}
        foreach(byte[] bad in new[]{new byte[]{1},new byte[4194305],original[..^1]}){try{method.Invoke(null,[bad]);throw new Exception("Malformed clipboard PNG accepted");}catch(TargetInvocationException e)when(e.InnerException is InvalidDataException){}}
        Console.WriteLine("PASS: strict clipboard PNG owned-copy bounds and zero without modifying borrowed bytes");
    }
}
