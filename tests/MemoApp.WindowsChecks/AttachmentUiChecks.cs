using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;

internal static partial class Program
{
    private static Type AttachmentPanelType => typeof(MainWindow).Assembly.GetType("MemoApp.Windows.AttachmentPanel") ?? throw new Exception("Bounded encrypted attachment panel is missing");
    private static FrameworkElement CreateAttachmentPanel(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice) =>
        (FrameworkElement)Activator.CreateInstance(AttachmentPanelType,[session,note,current,notice])!;
    private static Task<bool> ImportAttachment(object panel,Func<string?> pick)=>(Task<bool>)panel.GetType().GetMethod("ImportFileAsync")!.Invoke(panel,[pick])!;
    private static ListBox AttachmentList(object panel)=>(ListBox)panel.GetType().GetProperty("FilesList")!.GetValue(panel)!;
    private static bool PanelDisposed(object panel)=>(bool)panel.GetType().GetProperty("IsDisposed")!.GetValue(panel)!;
    private static async Task AttachmentPanelRun()
    {
        _=AttachmentPanelType;
        var root=Path.Combine(Path.GetTempPath(),"memo-wpf-attachment-panel-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var secret=EncryptedVault.GenerateRecoverySecret();var bytes=RandomNumberGenerator.GetBytes(70001);var path=Path.Combine(root,"합성 e\u0301😀.PDF");File.WriteAllBytes(path,bytes);
        try
        {
            using var session=new SaveCoordinator(EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret),TimeProvider.System);
            var note=session.Workspace.CreateNote();note.Text="opaque body stays unchanged";Require(await session.SaveAsync(),"direct panel legacy baseline save");Require(session.Workspace.Capture().AttachmentRootId==Guid.Empty,"baseline has no attachment root");bool live=true;string status="";
            var panel=CreateAttachmentPanel(session,note,()=>live,message=>status=message);var window=new Window{Content=panel,Width=500,Height=400};window.Show();await Idle();
            try
            {
                string original=JsonSerializer.Serialize(session.Workspace.Capture());Require(!await ImportAttachment(panel,()=>null)&&JsonSerializer.Serialize(session.Workspace.Capture())==original,"cancel picker does not initialize root or mutate a note");
                Require(!await ImportAttachment(panel,()=>{note.Title="changed during modal picker";return path;})&&note.AttachmentIds.Length==0,"picker callback version change rejects stale file without publishing attachment");
                Task<bool>? repeated=null;
                Require(await ImportAttachment(panel,()=>{repeated=ImportAttachment(panel,()=>throw new Exception("busy panel must not launch another picker"));return path;})&&repeated is {IsCompletedSuccessfully:true}&&!repeated.Result,"opaque original picker commits once and repeated click cannot start a second picker");await Idle();var list=AttachmentList(panel);Require(list.Items.Count==1&&list.Items[0].ToString()!.Contains("합성"),"safe file labels expose original name while no content is parsed");
                Guid id=note.AttachmentIds.Single();var stored=session.Workspace.Capture().AttachmentObjects.Single();Require(stored.Name==Path.GetFileName(path)&&stored.Length==bytes.Length&&stored.Sha256==Convert.ToHexStringLower(SHA256.HashData(bytes))&&File.ReadAllBytes(path).SequenceEqual(bytes)&&note.Text=="opaque body stays unchanged","panel keeps exact original bytes/name/hash and note text");
                list.SelectedIndex=0;Require((bool)panel.GetType().GetMethod("DetachSelected")!.Invoke(panel,null)!&&note.AttachmentIds.Length==0,"explicit detach removes only active reference");await Idle();Require(list.Items.Count==0&&session.Workspace.Capture().AttachmentObjects.Any(x=>x.ObjectId==id)&&session.Workspace.HistoryFor(note).Any(x=>x.AttachmentIds.Contains(id)),"detached original object remains in immutable history/full backup");
                Require(await ImportAttachment(panel,()=>path),"reimport uses a new immutable object");Require(note.AttachmentIds.Single()!=id,"reimport cannot reuse prior object identity");list.SelectedIndex=0;bool clearCallback=false;
                list.SelectionChanged+=(_,_)=>{if(PanelDisposed(panel)&&!clearCallback){clearCallback=true;Require(ImportAttachment(panel,()=>throw new Exception("conceal reentry may not invoke picker")).GetAwaiter().GetResult()==false,"conceal clears authority before native callbacks");throw new Exception("synthetic native selection-clear failure");}};
                await session.LockAsync();await Idle();Require(clearCallback&&PanelDisposed(panel)&&list.Items.Count==0&&list.SelectedItem is null&&session.KeysReleased,"lock revokes panel and independently clears names/selection despite a native callback exception before key release");
                Require(panel.Visibility==Visibility.Collapsed&&((UserControl)panel).Content is null,"revoked attachment panel independently hides and detaches its native rendered content");
                Require(!await ImportAttachment(panel,()=>throw new Exception("disposed picker must not be invoked")),"disposed panel refuses another modal file request");
            }
            finally{live=false;((IDisposable)panel).Dispose();window.Close();if(!session.IsLocked)await session.LockAsync();}
            session.Dispose(); // Lock releases keys but deliberately retains the process writer lease until disposal.
            using var reopened=new SaveCoordinator(EncryptedVault.Open(Path.Combine(root,"vault"),secret),TimeProvider.System);var restored=reopened.Workspace.Notes.Single();Require(restored.AttachmentIds.Length==1,"actual panel save/lock/restart retains reference");var plain=(byte[])typeof(SaveCoordinator).GetMethod("ReadAttachmentBytes",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(reopened,[restored,restored.AttachmentIds.Single(),restored.EditVersion])!;try{Require(plain.SequenceEqual(bytes),"actual panel encrypted restart restores every original byte");}finally{CryptographicOperations.ZeroMemory(plain);}await reopened.LockAsync();
        }
        finally{CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(bytes);Directory.Delete(root,true);}
    }
    private static async Task AttachmentProductionRun()
    {
        var root=Path.Combine(Path.GetTempPath(),"memo-wpf-attachment-main-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var path=Path.Combine(root,"synthetic-original.pdf");File.WriteAllBytes(path,[0,255,13,10,123]);var secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;string phase="create management";
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var session=Field<SaveCoordinator>(main,"session");var note=session.Workspace.CreateNote();Invoke(main,"RefreshNotes",note);await Idle();var host=Control<ContentControl>(main,"AttachmentHost");Require(host.Content?.GetType()==AttachmentPanelType,"management editor wires bounded attachment panel");var mainPanel=host.Content!;
            phase="open sticky";Invoke(main,"OpenSticky",note);await Idle();var sticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id];var stickyHost=Control<ContentControl>(sticky,"AttachmentHost");Require(stickyHost.Content?.GetType()==AttachmentPanelType,"sticky editor has its own same-note attachment panel");var stickyPanel=stickyHost.Content!;
            phase="management import";Require(await ImportAttachment(mainPanel,()=>path),"management attachment import commits");await Idle();Require(AttachmentList(mainPanel).Items.Count==1&&AttachmentList(stickyPanel).Items.Count==1,"management and sticky expose shared immutable attachment reference");
            phase="actual image hosts";var pngPath=Path.Combine(root,"literal-image.png");File.WriteAllBytes(pngPath,PreviewPng);await ProductionImageDrop(mainPanel,session,note,pngPath);await Idle();
            await ProductionImageDrop(stickyPanel,session,note,pngPath);await Idle();Require(File.ReadAllBytes(pngPath).SequenceEqual(PreviewPng),"Both production routed drops preserve source bytes");
            AttachmentList(mainPanel).SelectedIndex=1;AttachmentList(stickyPanel).SelectedIndex=1;Require(await PreviewSelected(mainPanel)&&PreviewImage(mainPanel).Source is not null,"Actual management editor displays authenticated selected PNG");Require(await PreviewSelected(stickyPanel)&&PreviewImage(mainPanel).Source is null&&PreviewImage(stickyPanel).Source is not null,"Actual sticky shares one published image across production hosts");
            phase="native minimize";var layout=session.Workspace.GetUiDevice(Field<Guid>(main,"uiDeviceId")).Windows.Single(w=>w.NoteId==note.Id);sticky.WindowState=WindowState.Minimized;await Idle();Require(sticky.WindowState==WindowState.Minimized&&!note.IsClosed&&session.Workspace.GetUiDevice(Field<Guid>(main,"uiDeviceId")).Windows.Single(w=>w.NoteId==note.Id)==layout,"Native minimize retains shared draft and normal-window layout");sticky.WindowState=WindowState.Normal;await Idle();Require(!PanelDisposed(stickyPanel)&&sticky.IsVisible,"Native restore keeps active image host");
            phase="detach preview";Require((bool)mainPanel.GetType().GetMethod("DetachSelected")!.Invoke(mainPanel,null)!&&PreviewImage(stickyPanel).Source is null,"Actual shared detach clears displayed sticky image immediately");Require(await session.SaveAsync(),"Preview detach saved with original object history retained");
            phase="change note";var other=session.Workspace.CreateNote();Invoke(main,"RefreshNotes",other);await Idle();Require(PanelDisposed(mainPanel)&&AttachmentList(mainPanel).Items.Count==0,"selection change disposes old panel and clears old labels");Require(!await ImportAttachment(mainPanel,()=>throw new Exception("old selection may not launch picker")),"old-note detached panel cannot import");
            phase="multiple selection/reselect";var list=Control<ListBox>(main,"NotesList");list.SelectAll();await Idle();Require(host.Content is null,"multiple selection has no single-note attachment authority");Invoke(main,"RefreshNotes",note);await Idle();Require(list.SelectedItems.Count==1&&ReferenceEquals(list.SelectedItem,note)&&host.Content?.GetType()==AttachmentPanelType,"reselected single note recreates live attachment authority after multiple selection");var current=host.Content!;bool started=false;
            phase="modal lock";Require(!await ImportAttachment(current,()=>{started=true;_=session.LockAsync();return path;})&&started,"lock inside modal callback refuses all late attachment publication");await session.LockAsync();await Idle();Require(host.Content is null&&stickyHost.Content is null&&PanelDisposed(stickyPanel)&&AttachmentList(stickyPanel).Items.Count==0,"production conceal removes both panels and all attachment labels");
            Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
        }
        catch(Exception error){throw new Exception("attachment production phase="+phase+"; "+error.GetBaseException().Message+"; "+error.GetBaseException().StackTrace,error);}
        finally{if(main is not null){var session=Field<SaveCoordinator?>(main,"session");if(session is not null&&!session.IsLocked)try{await session.LockAsync();}catch{}SetField(main,"confirmedExit",true);main.Close();session?.Dispose();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}
