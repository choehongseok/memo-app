using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task RichImageDocumentRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-inline-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();Window? window=null;
        try
        {
            using var session=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=session.Workspace.CreateNote();
            session.Workspace.ConvertMode(note,"rich",true);
            session.Workspace.SetRichDocument(note,new(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"before\",\"bold\":true}]},{\"type\":\"checklist\",\"items\":[{\"checked\":true,\"runs\":[{\"text\":\"checked\"}]}]},{\"type\":\"table\",\"rows\":[[{\"runs\":[{\"text\":\"cell\"}]}]]},{\"type\":\"paragraph\",\"runs\":[{\"text\":\"after\"}]}]}"));
            Require(await session.PrepareAttachmentsAsync(),"H01 authenticated root");Guid id=session.AttachBytes(note,PreviewPng,"synthetic.png","image/png",note.EditVersion);
            Require(await session.InsertInlineImageAsync(note,id,1,note.EditVersion),"H01 first insertion anchored");Require(await session.SaveAsync(),"H01 fixture saved");
            var canonical=note.Document!;
            using(var native=new StructuredNoteEditor(session.Workspace,note,()=>true,_=>{}))
            {
                Require(native.RichInput.IsReadOnly&&!native.RichInput.IsUndoEnabled,"H01 v2 native rich host explicitly refuses editing and Undo");native.ApplyBold();native.RichInput.AppendText("SYNTHETIC_NATIVE_DROP");
                Require(ReferenceEquals(note.Document,canonical),"H01 native programmatic text and formatting cannot replace canonical v2");bool refused=false;
                try{Invoke(native,"CaptureDocument");}catch(TargetInvocationException error)when(error.InnerException is InvalidDataException){refused=true;}
                Require(refused,"H01 native capture cannot serialize image document as v1");
            }
            using var backend=new PausedImageBackend(true);using var view=new RichImageDocumentView(session,note,()=>true,_=>{},backend);using var panel=new AttachmentPanel(session,note,()=>true,_=>{});
            var host=new StackPanel();host.Children.Add(view);host.Children.Add(panel);window=new Window{Content=host,Width=600,Height=800};window.Show();await Idle();
            Require(backend.Decodes==0&&view.ImageForBlock(1).Source is null,"H01 opening and layout never decode automatically");
            Require(view.BlocksHost.Children.Count==5&&view.BlocksHost.Children[0] is TextBlock first&&first.Inlines.FirstInline is System.Windows.Documents.Run {Text:"before",FontWeight:var weight}&&weight==FontWeights.Bold&&view.BlocksHost.Children[2] is StackPanel&&view.BlocksHost.Children[3] is Grid,"H01 ordered styled paragraph/image/checklist/table blocks are readonly controls");
            panel.FilesList.SelectedIndex=0;Task<bool> pending=view.DisplayImageAsync(1);Require(backend.Started.Wait(10000),"H01 real decode reached barrier");Require(!await panel.PreviewSelectedAsync(),"H01 document shares attachment preview admission");
            note.Title="changed during decode";Require(!await panel.PreviewSelectedAsync(),"H01 stale decode still holds global slot until settlement");backend.Release.Set();Require(!await pending&&view.ImageForBlock(1).Source is null&&backend.Output!.All(b=>b==0),"H01 stale document raster discarded and zeroed");
            Require(await session.SaveAsync(),"H01 stale edit saved");Require(await view.DisplayImageAsync(1)&&view.ImageForBlock(1).Source is BitmapSource,"H01 explicit click displays current canonical block");
            Require(await panel.PreviewSelectedAsync()&&view.ImageForBlock(1).Source is null,"H01 attachment display replaces document display globally");Require(await view.DisplayImageAsync(1)&&panel.PreviewImage.Source is null,"H01 document display replaces attachment display globally");
            session.Workspace.AcceptPrepared(session.Workspace.Capture());Require(view.ImageForBlock(1).Source is null,"H01 unchanged source acceptance epoch clears pixels");
            string models=root+"-public-models";var deferred=new DeferredOcrBackend();Directory.CreateDirectory(models);
            try
            {
                Require(await panel.ConfigureOcrModelsAsync(true,()=>models),"H01 shared OCR local public model fixture");SetField(panel,"ocrBackend",deferred);
                Require(!await panel.RecognizeSelectedAsync()&&deferred.Started.Task.IsCompleted,"H01 actual OCR UI reaches synthetic unsettled operation");
                Require(!await view.DisplayImageAsync(1),"H01 document display refuses while OCR cleanup retains shared admission");deferred.Settled.TrySetResult();await Idle();
            }
            finally{deferred.Settled.TrySetResult();await Idle();Directory.Delete(models,true);}
            bool capacity=false;for(int retry=0;retry<100&&!capacity;retry++){capacity=await view.DisplayImageAsync(1);if(!capacity)await Task.Delay(10);}
            Require(capacity,"H01 capacity returns after OCR settlement before removal");Require(view.RemoveImage(1)&&note.Document!.SchemaVersion==2&&RichDocumentCodec.Images(note.Document).IsEmpty&&note.AttachmentIds.Contains(id),"H01 removal keeps v2 and authenticated attachment separately");await Idle();Require(backend.Decodes==4,"H01 removal and repaint do not decode");
            Require(await session.SaveAsync(),"H01 removal saved");await session.LockAsync();Require(view.IsDisposed&&panel.IsDisposed,"H01 conceal disposes both hosts");await session.WhenAttachmentReadsIdle;
            var captures=typeof(RichImageDocumentView).GetNestedTypes(BindingFlags.NonPublic).Where(t=>t.GetMethods(BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance).Any(m=>m.Name.Contains("<DecodeAsync>"))).ToArray();
            Require(captures.Length==1&&captures[0].GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).All(f=>f.FieldType==typeof(ImagePreviewBackend)||f.FieldType==typeof(AttachmentReadLease)||f.FieldType==typeof(CancellationToken)),"H01 compiled decode worker owns only backend/lease/token");
        }
        finally{window?.Close();CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static async Task RichImagePublicationRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-inline-publication-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();Window? window=null;
        try
        {
            using var session=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=session.Workspace.CreateNote();session.Workspace.ConvertMode(note,"rich",true);Require(await session.PrepareAttachmentsAsync(),"H01 setter root");Guid id=session.AttachBytes(note,PreviewPng,"setter.png","image/png",note.EditVersion);Require(await session.InsertInlineImageAsync(note,id,0,note.EditVersion),"H01 setter insertion");Require(await session.SaveAsync(),"H01 setter save");
            foreach(string boundary in new[]{"bitmap","source","visibility","throw-source"})
            {
                bool fired=false;Action revoke=()=>{if(fired)return;fired=true;session.Workspace.AcceptPrepared(session.Workspace.Capture());};
                using var view=new RichImageDocumentView(session,note,()=>true,_=>{},boundary=="bitmap"?new ReentrantImageBackend(revoke):new ImagePreviewBackend());
                window=new Window{Content=view,Width=500,Height=500};window.Show();await Idle();var image=view.ImageForBlock(0);
                var property=boundary=="visibility"?UIElement.VisibilityProperty:Image.SourceProperty;var descriptor=DependencyPropertyDescriptor.FromProperty(property,typeof(Image));
                EventHandler handler=(_,_)=>{if(boundary=="source"&&image.Source is not null)revoke();if(boundary=="visibility"&&image.Visibility==Visibility.Visible)revoke();if(boundary=="throw-source"&&image.Source is not null&&!fired){fired=true;throw new InvalidOperationException("SYNTHETIC_H01_SOURCE_SETTER");}};
                if(boundary!="bitmap")descriptor.AddValueChanged(image,handler);
                try{Require(!await view.DisplayImageAsync(0)&&fired&&image.Source is null&&image.Visibility==Visibility.Collapsed,$"H01 {boundary} native publication rejects revoked/throwing setters and clears pixels");}
                finally{if(boundary!="bitmap")descriptor.RemoveValueChanged(image,handler);window.Close();window=null;}
            }
        }
        finally{window?.Close();CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
