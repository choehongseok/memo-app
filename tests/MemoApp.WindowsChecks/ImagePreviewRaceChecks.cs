using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;
internal static partial class Program
{
    private sealed class PausedImageBackend(bool after):ImagePreviewBackend,IDisposable
    {
        internal readonly ManualResetEventSlim Started=new(),Release=new();
        internal OwnedBgraRaster? Raster;
        internal byte[]? Output;
        internal int Decodes;
        internal override OwnedBgraRaster Decode(AttachmentReadLease lease,CancellationToken token)
        {
            Interlocked.Increment(ref Decodes);
            if(after){Raster=base.Decode(lease,token);Output=(byte[])typeof(OwnedBgraRaster).GetField("pixels",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Raster)!;}
            Started.Set();if(!Release.Wait(15000)){Raster?.Dispose();throw new IOException("Synthetic decoder barrier timeout");}
            return after?Raster!:base.Decode(lease,token);
        }
        public void Dispose(){Release.Set();Started.Dispose();Release.Dispose();}
    }
    private sealed class ReentrantImageBackend(Action action):ImagePreviewBackend
    {
        internal override BitmapSource Create(OwnedBgraRaster raster){var bitmap=base.Create(raster);action();return bitmap;}
    }
    private static readonly byte[] PreviewPng=Convert.FromHexString("89504e470d0a1a0a0000000d4948445200000002000000010806000000f4227f8a0000000e49444154789c63f8cfc000420d000f7a037e77e97f970000000049454e44ae426082");
    private sealed class PreviewFaultFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal bool Fail;internal readonly ManualResetEventSlim Entered=new(),Release=new();
        public Stream CreateNew(string path)=>actual.CreateNew(path);
        public void FlushToDisk(Stream stream){if(Fail){Entered.Set();if(!Release.Wait(15000))throw new IOException("Synthetic flush timeout");throw new IOException("Synthetic preview flush failure");}actual.FlushToDisk(stream);}
        public void Move(string source,string target)=>actual.Move(source,target);
        public void Replace(string source,string target,string previous)=>actual.Replace(source,target,previous);
    }
    private static async Task ImagePreviewFaultRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-png-fault-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();var files=new PreviewFaultFiles();
        try
        {
            using var session=new SaveCoordinator(EncryptedVault.Create(root,secret,secret,files),TimeProvider.System);var note=session.Workspace.CreateNote();Require(await session.PrepareAttachmentsAsync(),"Fault preview root");session.AttachBytes(note,PreviewPng,"synthetic.png","image/png",note.EditVersion);Require(await session.SaveAsync(),"Fault baseline");
            using var panel=new AttachmentPanel(session,note,()=>true,_=>{});files.Fail=true;note.Title="saved preparation before preview";Task<bool> saving=session.SaveAsync();Require(files.Entered.Wait(10000),"Real flush paused after immutable source preparation");panel.FilesList.SelectedIndex=0;
            Require(await panel.PreviewSelectedAsync()&&panel.PreviewImage.Source is not null,"Preview publication from current source while ciphertext write pending");long version=note.EditVersion;
            files.Release.Set();Require(!await saving&&note.EditVersion==version&&panel.PreviewImage.Source is null,"Real unchanged-version commit fault immediately clears displayed image");Require(!await panel.PreviewSelectedAsync(),"Faulted coordinator refuses decryption and publication");await session.LockAsync();
        }
        finally{files.Release.Set();files.Entered.Dispose();files.Release.Dispose();CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static async Task ImagePreviewRacesRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-png-race-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            var captures=typeof(AttachmentPanel).GetNestedTypes(BindingFlags.NonPublic).Where(t=>t.GetMethods(BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance).Any(m=>m.Name.Contains("<DecodeAsync>"))).ToArray();
            Require(captures.Length==1&&captures[0].GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).All(f=>f.FieldType==typeof(ImagePreviewBackend)||f.FieldType==typeof(AttachmentReadLease)||f.FieldType==typeof(CancellationToken)),"Compiled worker capture graph contains only backend/lease/token, no owner or UI authority");
            using var session=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=session.Workspace.CreateNote();Require(await session.PrepareAttachmentsAsync(),"race root");Guid id=session.AttachBytes(note,PreviewPng,"fixture.png","image/png",note.EditVersion);Require(await session.SaveAsync(),"race saved");
            foreach(bool after in new[]{false,true})
            {
                using var backend=new PausedImageBackend(after);using var panel=new AttachmentPanel(session,note,()=>true,_=>{},backend);using var second=new AttachmentPanel(session,note,()=>true,_=>{});panel.FilesList.SelectedIndex=second.FilesList.SelectedIndex=0;
                Task<bool> work=panel.PreviewSelectedAsync();Require(backend.Started.Wait(10000),"Real worker reaches deterministic before/after decode barrier");
                Require(!await second.PreviewSelectedAsync()&&backend.Decodes==1,"Second host denied while worker holds application slot");
                session.Workspace.AcceptPrepared(session.Workspace.Capture());Require(panel.PreviewImage.Source is null,"Unchanged-version source acceptance clears display immediately");
                Require(!await second.PreviewSelectedAsync(),"Revocation cannot release global slot while old worker holds lease/raster");backend.Release.Set();Require(!await work&&panel.PreviewImage.Source is null,"Stale paused worker never publishes after AcceptPrepared");
                if(after)Require(backend.Output!.All(b=>b==0),"Actual owned detached output zeroed on stale worker cleanup");
                Require(await second.PreviewSelectedAsync(),"Capacity returned exactly after worker and posted cleanup");
            }
            using(var panel=new AttachmentPanel(session,note,()=>true,_=>{},new ReentrantImageBackend(()=>session.Workspace.AcceptPrepared(session.Workspace.Capture()))))
            {panel.FilesList.SelectedIndex=0;Require(!await panel.PreviewSelectedAsync()&&panel.PreviewImage.Source is null,"Reentrant source invalidation during native bitmap creation cannot publish");}
            using(var panel=new AttachmentPanel(session,note,()=>true,_=>{}))
            {
                panel.FilesList.SelectedIndex=0;var descriptor=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Image.SourceProperty,typeof(Image));bool fired=false;
                EventHandler handler=(_,_)=>{if(panel.PreviewImage.Source is not null&&!fired){fired=true;session.Workspace.AcceptPrepared(session.Workspace.Capture());}};descriptor.AddValueChanged(panel.PreviewImage,handler);
                try{Require(!await panel.PreviewSelectedAsync()&&fired&&panel.PreviewImage.Source is null&&panel.PreviewImage.Visibility==Visibility.Collapsed,"Source assignment callback invalidates before visibility publication");}finally{descriptor.RemoveValueChanged(panel.PreviewImage,handler);}
            }
            using(var panel=new AttachmentPanel(session,note,()=>true,_=>{}))
            {
                panel.FilesList.SelectedIndex=0;Require(await panel.PreviewSelectedAsync(),"Pre-mutation reentry fixture displayed");bool saw=false;
                var descriptor=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Image.SourceProperty,typeof(Image));
                EventHandler handler=(_,_)=>{if(panel.PreviewImage.Source is null&&!saw){saw=true;Require(note.Title=="post mutation","Native source-clear callback runs only after title mutation");using var lease=session.CreateAttachmentReadLease(note,id,note.EditVersion);}};
                descriptor.AddValueChanged(panel.PreviewImage,handler);try{note.Title="post mutation";Require(saw,"Normal public note notification clears stale image");}finally{descriptor.RemoveValueChanged(panel.PreviewImage,handler);}Require(await session.SaveAsync(),"reentry fixture saved");
            }
            using(var backend=new PausedImageBackend(true))using(var panel=new AttachmentPanel(session,note,()=>true,_=>{},backend))using(var other=new AttachmentPanel(session,note,()=>true,_=>{}))
            {
                panel.FilesList.SelectedIndex=other.FilesList.SelectedIndex=0;using var posted=new ManualResetEventSlim();
                System.Windows.Threading.DispatcherHookEventHandler handler=(_,e)=>{if(e.Operation.Priority==System.Windows.Threading.DispatcherPriority.Background)posted.Set();};
                panel.Dispatcher.Hooks.OperationPosted+=handler;
                try
                {
                    Task<bool> pending=panel.PreviewSelectedAsync();Require(backend.Started.Wait(10000),"Blocked UI fixture has complete raster");
                    using(panel.Dispatcher.DisableProcessing())
                    {
                        backend.Release.Set();Require(posted.Wait(10000),"Completed raster queued on blocked Dispatcher");
                        Require(!pending.IsCompleted&&panel.PreviewImage.Source is null&&!other.PreviewSelectedAsync().GetAwaiter().GetResult(),"Blocked UI holds global admission and cannot publish or decrypt next request");
                        session.Workspace.AcceptPrepared(session.Workspace.Capture());
                    }
                    Require(!await pending&&backend.Output!.All(b=>b==0),"Queued stale UI publication zeroes actual detached output");
                }
                finally{panel.Dispatcher.Hooks.OperationPosted-=handler;}
            }
            using(var backend=new PausedImageBackend(true))using(var panel=new AttachmentPanel(session,note,()=>true,_=>{},backend))
            {
                panel.FilesList.SelectedIndex=0;Task<bool> work=panel.PreviewSelectedAsync();Require(backend.Started.Wait(10000),"Lock fixture owns actual detached raster");
                await session.LockAsync();Require(panel.PreviewImage.Source is null&&session.KeysReleased,"Conceal and key release do not wait on paused raster");
                session.Dispose();using var replacement=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System);var replacementNote=replacement.Workspace.Notes.Single();using var next=new AttachmentPanel(replacement,replacementNote,()=>true,_=>{});next.FilesList.SelectedIndex=0;
                Require(!await next.PreviewSelectedAsync(),"New coordinator cannot decrypt while canceled old worker is still running");backend.Release.Set();Require(!await work&&backend.Output!.All(b=>b==0),"Old-session raster zeroed and publication rejected");Require(await next.PreviewSelectedAsync(),"Fresh session works once old slot cleanup completes");await replacement.LockAsync();
            }
            _=id;
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
