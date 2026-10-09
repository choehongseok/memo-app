using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task ClipboardPngRun()
    {
        string root=Path.Combine(System.IO.Path.GetTempPath(),"memo-wpf-clipboard-png-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using var active=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=active.Workspace.CreateNote();note.Text="clipboard body";Require(await active.SaveAsync(),"Clipboard baseline");bool live=true;using var panel=new AttachmentPanel(active,note,()=>live,_=>{});var method=typeof(AttachmentPanel).GetMethod("ImportClipboardPngAsync")??throw new Exception("Bounded explicit clipboard PNG input is missing");
            Task<bool> Paste(Func<IDataObject?> get)=>(Task<bool>)method.Invoke(panel,[get])!;string before=JsonSerializer.Serialize(active.Workspace.Capture());
            Require(!await Paste(()=>new DataObject(DataFormats.UnicodeText,"text",false))&&JsonSerializer.Serialize(active.Workspace.Capture())==before,"No text/bitmap automatic conversion or canonical mutation");
            Require(!await Paste(()=>{note.Title="newer";return new DataObject("PNG",PreviewPng,false);})&&note.AttachmentIds.Length==0,"GetDataObject callback edit revokes authority");Require(await active.SaveAsync(),"Getter edit baseline");
            byte[] borrowed=(byte[])PreviewPng.Clone();Require(await Paste(()=>new DataObject("PNG",borrowed,false))&&borrowed.SequenceEqual(PreviewPng)&&note.AttachmentIds.Length==1&&panel.PreviewImage.Source is null,"Exact borrowed PNG attaches and encrypts without mutation or auto decode");
            Guid id=note.AttachmentIds.Single();Task? locking=null;Require(!await Paste(()=>{locking=active.LockAsync();return new DataObject("PNG",borrowed,false);}),"Getter lock discards late clipboard data");await locking!;active.Dispose();
            using var reopened=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System);var restored=reopened.Workspace.Notes.Single();byte[] read=(byte[])typeof(SaveCoordinator).GetMethod("ReadAttachmentBytes",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reopened,[restored,id,restored.EditVersion])!;try{Require(read.SequenceEqual(PreviewPng),"Clipboard encrypted restart preserves exact PNG");}finally{CryptographicOperations.ZeroMemory(read);}
        }
        finally{CryptographicOperations.ZeroMemory(secret);System.IO.Directory.Delete(root,true);}
    }
}
