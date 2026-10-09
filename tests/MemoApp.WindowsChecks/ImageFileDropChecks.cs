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
    private static Task<bool> DropImage(object panel,IDataObject data)=>(Task<bool>)(panel.GetType().GetMethod("ImportDroppedImageAsync")??throw new Exception("Protected image FileDrop input is missing")).Invoke(panel,[data])!;
    private sealed class DropProvider(object value,Action present,Action read):IDataObject
    {
        public int Reads{get;private set;}
        public object GetData(string format,bool autoConvert){Require(format==DataFormats.FileDrop&&!autoConvert,"Only exact non-converted FileDrop requested");Reads++;read();return value;}
        public bool GetDataPresent(string format,bool autoConvert){Require(format==DataFormats.FileDrop&&!autoConvert,"Only exact non-converted FileDrop presence requested");present();return true;}
        public object GetData(string format)=>throw new Exception("Automatic conversion forbidden");public object GetData(Type format)=>throw new Exception("Type conversion forbidden");
        public bool GetDataPresent(string format)=>throw new Exception("Automatic conversion forbidden");public bool GetDataPresent(Type format)=>false;
        public string[] GetFormats(bool autoConvert)=>throw new Exception("Format enumeration forbidden");public string[] GetFormats()=>GetFormats(false);
        public void SetData(string format,object data,bool autoConvert)=>throw new NotSupportedException();public void SetData(string format,object data)=>throw new NotSupportedException();public void SetData(Type format,object data)=>throw new NotSupportedException();public void SetData(object data)=>throw new NotSupportedException();
    }
    private static async Task ImageFileDropRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-drop-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();string path=Path.Combine(root,"합성😀.png");File.WriteAllBytes(path,PreviewPng);
        try
        {
            using var session=new SaveCoordinator(EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret),TimeProvider.System);var note=session.Workspace.CreateNote();note.Text="drop body unchanged";Require(await session.SaveAsync(),"Drop accepted baseline");bool live=true;using var panel=new AttachmentPanel(session,note,()=>live,_=>{});
            string Snapshot()=>JsonSerializer.Serialize(session.Workspace.Capture());string before=Snapshot();
            Require(!await DropImage(panel,new DropProvider(new[]{path,path},()=>{},()=>{}))&&Snapshot()==before,"Multiple files rejected without preparing attachment root");
            foreach(object value in new object[]{new[]{"https://example.invalid/image.png"},new[]{"\\\\server\\share\\image.png"},new[]{Path.Combine(root,"unsupported.txt")},new[]{""},new byte[]{1},Array.Empty<string>()})
                Require(!await DropImage(panel,new DropProvider(value,()=>{},()=>{}))&&Snapshot()==before,"Unsupported/nonlocal input has no canonical mutation");
            foreach(bool atPresence in new[]{true,false})
            {
                Action change=()=>note.Title=note.Title+" newer";var provider=new DropProvider(new[]{path},atPresence?change:()=>{},atPresence?()=>{}:change);
                Require(!await DropImage(panel,provider)&&note.AttachmentIds.Length==0&&(atPresence?provider.Reads==0:provider.Reads==1),"Edit during either OLE call rejects before stale source read");
                Require(await session.SaveAsync(),"Edit baseline saved");var original=session.Workspace.Capture();long version=note.EditVersion;change=()=>session.Workspace.AcceptPrepared(original);provider=new DropProvider(new[]{path},atPresence?change:()=>{},atPresence?()=>{}:change);
                Require(!await DropImage(panel,provider)&&note.EditVersion==version&&note.AttachmentIds.Length==0&&(atPresence?provider.Reads==0:provider.Reads==1),"Unchanged-version prepared replacement during either OLE call revokes old epoch");
                change=()=>live=false;provider=new DropProvider(new[]{path},atPresence?change:()=>{},atPresence?()=>{}:change);Require(!await DropImage(panel,provider)&&note.AttachmentIds.Length==0,"Selection authority loss rejects either OLE callback");live=true;
                provider=new DropProvider(new[]{path},atPresence?()=>throw new IOException("Synthetic provider fault"):()=>{},atPresence?()=>{}:()=>throw new IOException("Synthetic provider fault"));Require(!await DropImage(panel,provider)&&note.AttachmentIds.Length==0,"Throwing OLE provider fails closed");
            }
            Require(await DropImage(panel,new DropProvider(new[]{path},()=>{},()=>{})),"Single image drop uses existing encrypted import");Require(note.Text=="drop body unchanged"&&File.ReadAllBytes(path).SequenceEqual(PreviewPng)&&panel.PreviewImage.Source is null,"Original and body unchanged; drop never starts preview");Guid id=note.AttachmentIds.Single();Require(session.Workspace.Capture().AttachmentObjects.Single().Sha256==Convert.ToHexStringLower(SHA256.HashData(PreviewPng)),"Opaque dropped original hash preserved");
            Task? pending=null;var locking=new DropProvider(new[]{path},()=>{pending=session.LockAsync();},()=>throw new Exception("GetData after lock forbidden"));Require(!await DropImage(panel,locking)&&locking.Reads==0,"Lock during presence callback stops before GetData");await pending!;session.Dispose();
            using var reopened=new SaveCoordinator(EncryptedVault.Open(Path.Combine(root,"vault"),secret),TimeProvider.System);var restored=reopened.Workspace.Notes.Single();var bytes=(byte[])typeof(SaveCoordinator).GetMethod("ReadAttachmentBytes",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(reopened,[restored,id,restored.EditVersion])!;try{Require(bytes.SequenceEqual(PreviewPng),"Encrypted drop save/lock/restart restores exact original bytes");}finally{CryptographicOperations.ZeroMemory(bytes);}await reopened.LockAsync();
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}
