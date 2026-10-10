using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task PngGrayRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-gray-png-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();Window? window=null;
        try
        {
            foreach(var (color,metadata) in new[]{(0,false),(4,false),(4,true)})
            {
                byte[] png=GrayPreviewPng(color);if(metadata)png=WithScalarPngMetadata(png);byte[] original=(byte[])png.Clone();
                byte[] expected=color==0?[37,37,37,255,201,201,201,255]:[37,37,37,0,201,201,201,255];
                string vaultRoot=Path.Combine(root,$"source-{color}-{metadata}"),reopenRoot=Path.Combine(root,$"copy-{color}-{metadata}");Guid attachment;
                using(var active=new SaveCoordinator(EncryptedVault.Create(vaultRoot,secret,secret),TimeProvider.System))
                {
                    var note=active.Workspace.CreateNote();note.Text="synthetic grayscale clipboard";
                    using var panel=new AttachmentPanel(active,note,()=>true,_=>{});window=new Window{Content=panel,Width=600,Height=600};window.Show();await Idle();
                    Require(await panel.ImportClipboardPngAsync(()=>new DataObject("PNG",png,false))&&png.SequenceEqual(original)&&note.AttachmentIds.Length==1&&panel.PreviewImage.Source is null,"Actual gray/gray-alpha clipboard encrypts exact original and waits for explicit display");attachment=note.AttachmentIds.Single();
                    panel.FilesList.SelectedIndex=0;Require(await panel.PreviewSelectedAsync(),"Explicit encrypted gray/gray-alpha attachment decodes through shared bounded WPF path");
                    var bitmap=panel.PreviewImage.Source as BitmapSource??throw new Exception("Gray PNG bitmap missing");byte[] pixels=new byte[8];bitmap.CopyPixels(pixels,8,0);
                    Require(bitmap.IsFrozen&&bitmap.Format==PixelFormats.Bgra32&&bitmap.PixelWidth==2&&bitmap.PixelHeight==1&&bitmap.DpiX==96&&bitmap.DpiY==96&&pixels.SequenceEqual(expected),"Actual frozen WPF gray BGRA preserves exact gray values, transparent alpha and fixed DPI including combined scalar metadata");
                    await active.LockAsync();Require(panel.PreviewImage.Source is null&&active.KeysReleased,"Actual gray preview lock clears image and releases keys");window.Close();window=null;
                }
                Directory.CreateDirectory(reopenRoot);
                foreach(string sourceFile in Directory.EnumerateFiles(vaultRoot,"*",SearchOption.AllDirectories))
                {
                    if(Path.GetFileName(sourceFile)=="writer.lock")continue;
                    string copy=Path.Combine(reopenRoot,Path.GetRelativePath(vaultRoot,sourceFile));Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(sourceFile,copy);
                }
                using var reopened=new SaveCoordinator(EncryptedVault.Open(reopenRoot,secret),TimeProvider.System);var restored=reopened.Workspace.Notes.Single();
                byte[] restoredBytes=(byte[])typeof(SaveCoordinator).GetMethod("ReadAttachmentBytes",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reopened,[restored,attachment,restored.EditVersion])!;
                try{Require(restoredBytes.SequenceEqual(original),"Independent encrypted snapshot/object reopen preserves actual grayscale PNG and metadata bytes exactly");}finally{CryptographicOperations.ZeroMemory(restoredBytes);}
                Require(png.SequenceEqual(original),"Gray clipboard and display leave borrowed synthetic source unchanged");
            }
        }
        finally{window?.Close();CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
    private static byte[] GrayPreviewPng(int color)
    {
        byte[] header=new byte[13];BinaryPrimitives.WriteUInt32BigEndian(header,2);BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4),1);header[8]=8;header[9]=checked((byte)color);
        byte[] scanline=color switch{0=>[0,37,201],4=>[0,37,0,201,255],_=>throw new ArgumentOutOfRangeException(nameof(color))};
        using var compressed=new MemoryStream();using(var zlib=new ZLibStream(compressed,CompressionLevel.SmallestSize,true))zlib.Write(scanline);
        using var output=new MemoryStream();output.Write([137,80,78,71,13,10,26,10]);Chunk("IHDR",header);Chunk("IDAT",compressed.ToArray());Chunk("IEND",[]);return output.ToArray();
        void Chunk(string name,byte[] payload)
        {
            byte[] chunk=new byte[payload.Length+12];BinaryPrimitives.WriteUInt32BigEndian(chunk,(uint)payload.Length);System.Text.Encoding.ASCII.GetBytes(name).CopyTo(chunk,4);payload.CopyTo(chunk,8);
            uint crc=uint.MaxValue;foreach(byte value in chunk.AsSpan(4,payload.Length+4)){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320U:crc>>1;}
            BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(payload.Length+8),crc^uint.MaxValue);output.Write(chunk);
        }
    }
}
