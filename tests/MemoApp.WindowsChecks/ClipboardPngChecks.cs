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
    private sealed class ClipboardProvider(object value,Action present,Action read):IDataObject
    {
        public object GetData(string format,bool autoConvert){Require(format=="PNG"&&!autoConvert,"Exact PNG payload only");read();return value;}
        public bool GetDataPresent(string format,bool autoConvert){Require(format=="PNG"&&!autoConvert,"Exact PNG presence only");present();return true;}
        public object GetData(string format)=>throw new Exception("No automatic clipboard conversion");public object GetData(Type format)=>throw new Exception("No clipboard type conversion");public bool GetDataPresent(string format)=>throw new Exception("No automatic conversion");public bool GetDataPresent(Type format)=>false;public string[] GetFormats()=>throw new Exception("No format enumeration");public string[] GetFormats(bool autoConvert)=>GetFormats();
        public void SetData(object data)=>throw new NotSupportedException();public void SetData(string format,object data)=>throw new NotSupportedException();public void SetData(string format,object data,bool autoConvert)=>throw new NotSupportedException();public void SetData(Type format,object data)=>throw new NotSupportedException();

    }
    private static async Task ProductionClipboardPng(object host,SaveCoordinator active,NoteDraft note)
    {
        int before=note.AttachmentIds.Length;using var data=new MemoryStream(PreviewPng,false);Clipboard.SetDataObject(new DataObject("PNG",data,false),true);
        try
        {
            var button=(System.Windows.Controls.Button)host.GetType().GetField("pastePng",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)!;button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));var wait=System.Diagnostics.Stopwatch.StartNew();while(note.AttachmentIds.Length==before||active.IsBusy||active.IsDirty){Require(wait.Elapsed<TimeSpan.FromSeconds(20),"Native clipboard PNG button must settle");await Task.Delay(10);}Require(note.AttachmentIds.Length==before+1&&PreviewImage(host).Source is null,"Actual main/sticky clipboard button and OLE PNG adds one encrypted attachment without decoding");
        }
        finally{Clipboard.Clear();}
    }
    private static async Task ClipboardPngRun()
    {
        string root=Path.Combine(System.IO.Path.GetTempPath(),"memo-wpf-clipboard-png-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using var active=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=active.Workspace.CreateNote();note.Text="clipboard body";Require(await active.SaveAsync(),"Clipboard baseline");bool live=true;using var panel=new AttachmentPanel(active,note,()=>live,_=>{});var method=typeof(AttachmentPanel).GetMethod("ImportClipboardPngAsync")??throw new Exception("Bounded explicit clipboard PNG input is missing");
            Task<bool> Paste(Func<IDataObject?> get)=>(Task<bool>)method.Invoke(panel,[get])!;string before=JsonSerializer.Serialize(active.Workspace.Capture());
            Require(!await Paste(()=>new DataObject(DataFormats.UnicodeText,"text",false))&&JsonSerializer.Serialize(active.Workspace.Capture())==before,"No text/bitmap automatic conversion or canonical mutation");
            Require(!await Paste(()=>{note.Title="newer";return new DataObject("PNG",PreviewPng,false);})&&note.AttachmentIds.Length==0,"GetDataObject callback edit revokes authority");Require(await active.SaveAsync(),"Getter edit baseline");
            foreach(bool atPresence in new[]{true,false})
            {
                Action change=()=>note.Title+=" newer";Require(!await Paste(()=>new ClipboardProvider(PreviewPng,atPresence?change:()=>{},atPresence?()=>{}:change))&&note.AttachmentIds.Length==0,"Each OLE call rechecks source version");Require(await active.SaveAsync(),"OLE edit baseline");var accepted=active.Workspace.Capture();long version=note.EditVersion;change=()=>active.Workspace.AcceptPrepared(accepted);Require(!await Paste(()=>new ClipboardProvider(PreviewPng,atPresence?change:()=>{},atPresence?()=>{}:change))&&note.EditVersion==version&&note.AttachmentIds.Length==0,"Same-version accepted snapshot reentry revokes clipboard epoch");
            }
            var gate=typeof(AttachmentPanel).Assembly.GetType("MemoApp.Windows.ImagePreviewAdmission")!;Require((bool)gate.GetMethod("TryEnter",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[panel.Dispatcher])!,"Synthetic existing image job admitted");try{Require(!await Paste(()=>throw new Exception("No clipboard access while global image job exists")),"Global image admission precedes OLE");}finally{gate.GetMethod("Exit",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);}
            Task<bool>? reentrant=null;Require(!await Paste(()=>{reentrant=Paste(()=>throw new Exception("No reentrant clipboard query"));return null;})&&reentrant is{IsCompletedSuccessfully:true}&&!reentrant.Result,"Busy reentry rejected before getter");
            Require(!await Paste(()=>new ClipboardProvider(new byte[4194305],()=>{},()=>{}))&&note.AttachmentIds.Length==0,"Oversized clipboard refused before root preparation");
            byte[] borrowed=(byte[])PreviewPng.Clone();Require(await Paste(()=>new DataObject("PNG",borrowed,false))&&borrowed.SequenceEqual(PreviewPng)&&note.AttachmentIds.Length==1&&panel.PreviewImage.Source is null,"Exact borrowed PNG attaches and encrypts without mutation or auto decode");
            byte[]? observed=null;using(var disposed=new MemoryStream((byte[])PreviewPng.Clone(),false)){try{typeof(AttachmentPanel).GetMethod("CaptureClipboardStream",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[disposed,new Action<byte[]>(copy=>observed=copy),new Action(disposed.Dispose)]);throw new Exception("Restoration of disposed stream unexpectedly succeeded");}catch(TargetInvocationException error)when(error.InnerException is ObjectDisposedException){Require(observed is not null&&observed.All(b=>b==0),"Borrowed restore failure zeroes entire owned scratch before source construction");}}
            using(var memory=new MemoryStream((byte[])PreviewPng.Clone(),false)){memory.Position=3;Require(await Paste(()=>new ClipboardProvider(memory,()=>{},()=>{}))&&memory.Position==3&&memory.ToArray().SequenceEqual(PreviewPng),"Exact MemoryStream borrowed position/content preserved");}
            Guid id=note.AttachmentIds.First();Task? locking=null;Require(!await Paste(()=>{locking=active.LockAsync();return new DataObject("PNG",borrowed,false);}),"Getter lock discards late clipboard data");await locking!;active.Dispose();
            using var reopened=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System);var restored=reopened.Workspace.Notes.Single();byte[] read=(byte[])typeof(SaveCoordinator).GetMethod("ReadAttachmentBytes",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reopened,[restored,id,restored.EditVersion])!;try{Require(read.SequenceEqual(PreviewPng),"Clipboard encrypted restart preserves exact PNG");}finally{CryptographicOperations.ZeroMemory(read);}
        }
        finally{CryptographicOperations.ZeroMemory(secret);System.IO.Directory.Delete(root,true);}
    }
}
