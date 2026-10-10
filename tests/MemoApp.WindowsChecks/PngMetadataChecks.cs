using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static byte[] WithScalarPngMetadata(byte[] source)
    {
        using var output = new MemoryStream(); output.Write(source.AsSpan(0,33));
        foreach (var (name,data) in new (string,byte[])[]{("sRGB",[0]),("gAMA",[0,0,177,143]),("pHYs",[0,0,14,195,0,0,14,195,1])})
        {
            byte[] chunk=new byte[data.Length+12]; BinaryPrimitives.WriteUInt32BigEndian(chunk,(uint)data.Length); System.Text.Encoding.ASCII.GetBytes(name).CopyTo(chunk,4); data.CopyTo(chunk,8);
            uint crc=uint.MaxValue; foreach(byte b in chunk.AsSpan(4,data.Length+4)){crc^=b;for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320U:crc>>1;}
            BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(data.Length+8),crc^uint.MaxValue); output.Write(chunk);
        }
        output.Write(source.AsSpan(33)); return output.ToArray();
    }
    private static async Task PngMetadataRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-scalar-png-"+Guid.NewGuid().ToString("N")); byte[] secret=EncryptedVault.GenerateRecoverySecret(); Window? window=null;
        byte[] png=WithScalarPngMetadata(PreviewPng),original=(byte[])png.Clone();
        try
        {
            using(var active=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System))
            {
                var note=active.Workspace.CreateNote(); note.Text="synthetic metadata clipboard";
                using var panel=new AttachmentPanel(active,note,()=>true,_=>{}); window=new Window{Content=panel,Width=600,Height=600};window.Show();await Idle();
                Require(await panel.ImportClipboardPngAsync(()=>new DataObject("PNG",png,false))&&png.SequenceEqual(original)&&panel.PreviewImage.Source is null,"Scalar metadata clipboard preserves original encrypted bytes and requires explicit preview");
                panel.FilesList.SelectedIndex=0;Require(await panel.PreviewSelectedAsync(),"Scalar metadata encrypted attachment reaches actual bounded WPF preview");
                var bitmap=panel.PreviewImage.Source as BitmapSource??throw new Exception("Metadata PNG bitmap missing");byte[] pixels=new byte[8];bitmap.CopyPixels(pixels,8,0);Require(bitmap.IsFrozen&&bitmap.PixelWidth==2&&bitmap.PixelHeight==1&&bitmap.DpiX==96&&pixels.SequenceEqual(new byte[]{0,0,255,255,255,0,0,128}),"Scalar metadata neither changes WPF pixels/alpha nor DPI");
                await active.LockAsync();Require(panel.PreviewImage.Source is null&&active.KeysReleased,"Metadata preview cleared by actual key-releasing lock");window.Close();window=null;
            }
            using var reopened=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System);var restored=reopened.Workspace.Notes.Single();byte[] bytes=(byte[])typeof(SaveCoordinator).GetMethod("ReadAttachmentBytes",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reopened,[restored,restored.AttachmentIds.Single(),restored.EditVersion])!;
            try{Require(bytes.SequenceEqual(original),"Encrypted restart preserves exact PNG scalar metadata as opaque original");}finally{CryptographicOperations.ZeroMemory(bytes);}
        }
        finally{window?.Close();CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}
