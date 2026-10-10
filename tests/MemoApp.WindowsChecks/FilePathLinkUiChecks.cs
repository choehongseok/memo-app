using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task FilePathLinkRun()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-wpf-path-links-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);byte[] secret=EncryptedVault.GenerateRecoverySecret();Guid profile=Guid.NewGuid();Window? host=null;
        try
        {
            string root=Path.Combine(dir,"vault"),path=Path.Combine(dir,"합성 원본.txt");byte[] bytes=[0,255,13,10,123];File.WriteAllBytes(path,bytes);
            using var active=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=active.Workspace.CreateNote();note.Text="unchanged source";Require(await active.SaveAsync(),"Path link baseline");string notice="";Action? onNotice=null;using var panel=new AttachmentPanel(active,note,()=>true,m=>{notice=m;onNotice?.Invoke();},profile);host=new Window{Content=panel};host.Show();
            Require(panel.ConnectPathButton.IsEnabled,"Explicit path-only connection control");
            var descriptor=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(UIElement.IsEnabledProperty,typeof(Button));bool once=true;EventHandler disabling=(_,_)=>{if(once&&!panel.ConnectPathButton.IsEnabled){once=false;throw new IOException("Synthetic path native disable failure");}};descriptor.AddValueChanged(panel.ConnectPathButton,disabling);try{Require(!await panel.ConnectFilePathAsync(()=>throw new Exception("No picker after native failure"),_=>true)&&!Field<bool>(panel,"busy")&&note.Metadata.FilePathLinks.Length==0,"Native admission failure releases slot without metadata");}finally{descriptor.RemoveValueChanged(panel.ConnectPathButton,disabling);}
            Require(!await panel.ConnectFilePathAsync(()=>null,_=>throw new Exception("No confirm after cancel")),"Picker cancellation");
            Require(!await panel.ConnectFilePathAsync(()=>{note.Title="newer";return path;},_=>throw new Exception("No stale confirm"))&&note.Metadata.FilePathLinks.Length==0,"Picker source edit discarded");Require(await active.SaveAsync(),"Picker source saved");
            Require(!await panel.ConnectFilePathAsync(()=>path,_=>false)&&note.Metadata.FilePathLinks.Length==0,"Confirmation cancellation has no root/link publication");
            Require(!await panel.ConnectFilePathAsync(()=>path,_=>{active.Workspace.AcceptPrepared(active.Workspace.Capture());return true;})&&note.Metadata.FilePathLinks.Length==0,"Same-version source acceptance revokes pending path result");
            Require(await panel.ConnectFilePathAsync(()=>path,_=>true)&&note.Metadata.FilePathLinks.Length==1&&panel.PathLinksList.Items.Count==1&&note.Text=="unchanged source"&&File.ReadAllBytes(path).SequenceEqual(bytes)&&notice.Contains("암호화하지"),"Actual unanchored own root acceptance then metadata7 save with external raw source invariant");
            panel.PathLinksList.SelectedIndex=0;int launches=0;
            Require(!panel.OpenSelectedPath(_=>false,_=>launches++)&&launches==0,"Open default cancellation");
            Require(!panel.OpenSelectedPath(_=>{panel.PathLinksList.SelectedIndex=-1;panel.PathLinksList.SelectedIndex=0;return true;},_=>launches++),"Transient selection change never launches");
            onNotice=()=>active.Workspace.AcceptPrepared(active.Workspace.Capture());Require(!panel.OpenSelectedPath(_=>true,_=>launches++)&&launches==0,"Same-version native notice callback revokes launch");onNotice=null;Invoke(panel,"PreviewStateChanged");panel.PathLinksList.SelectedIndex=0;
            Require(panel.OpenSelectedPath(_=>true,p=>{Require(p==path,"Exact no-argument native path passed to launch");launches++;})&&launches==1&&File.ReadAllBytes(path).SequenceEqual(bytes),"Explicit selected original launch initiation preserves bytes");
            object oldRow=panel.PathLinksList.Items[0];bool attempted=false,nestedResult=true;System.ComponentModel.PropertyChangedEventHandler clearReentry=(_,_)=>{if(attempted)return;attempted=true;nestedResult=panel.OpenSelectedPath(_=>false,_=>throw new Exception("No launch while projection builds"));Invoke(panel,"RefreshPathLinks");};((System.ComponentModel.INotifyPropertyChanged)oldRow).PropertyChanged+=clearReentry;
            Invoke(panel,"RefreshPathLinks");((System.ComponentModel.INotifyPropertyChanged)oldRow).PropertyChanged-=clearReentry;Require(attempted&&!nestedResult&&(string)oldRow.GetType().GetProperty("Label")!.GetValue(oldRow)! =="","DTO clear native reentry cannot open or replace projection ownership");panel.PathLinksList.SelectedIndex=0;
            using(var foreign=new AttachmentPanel(active,note,()=>true,_=>{},Guid.NewGuid())){foreign.PathLinksList.SelectedIndex=0;Require(!foreign.OpenPathButton.IsEnabled&&!foreign.OpenSelectedPath(_=>throw new Exception("Foreign profile cannot even confirm"),_=>throw new Exception("Foreign cannot launch")),"Foreign path disabled/refused before filesystem/modal");}
            File.Delete(path);Require(!panel.OpenSelectedPath(_=>throw new Exception("Broken path cannot confirm"),_=>launches++)&&launches==1,"Broken original refused without launch");File.WriteAllBytes(path,bytes);panel.PathLinksList.SelectedIndex=0;object detachedLabel=panel.PathLinksList.Items[0];Require(panel.DetachSelectedPath()&&note.Metadata.FilePathLinks.Length==0&&File.ReadAllBytes(path).SequenceEqual(bytes)&&active.Workspace.Capture().History.Any(h=>h.Metadata.FilePathLinks.Length==1),"Detach external file remains, encrypted path history retained");Require((string)detachedLabel.GetType().GetProperty("Label")!.GetValue(detachedLabel)! =="","Retained detached UI row independently cleared");Require(await active.SaveAsync(),"Detach save");
            Task<bool>? locking=null;Require(!await panel.ConnectFilePathAsync(()=>path,_=>{locking=active.LockAsync();return true;}),"Confirmation lock discards result before publication");await locking!;Require(panel.IsDisposed&&panel.PathLinksList.Items.Count==0&&active.KeysReleased,"Conceal clears path labels and keys");
        }
        finally{host?.Close();CryptographicOperations.ZeroMemory(secret);Directory.Delete(dir,true);}
        await FilePathProductionRun();
    }
    private static async Task FilePathProductionRun()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-wpf-path-production-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            string root=Path.Combine(dir,"vault"),path=Path.Combine(dir,"합성.txt");File.WriteAllBytes(path,[0,1,255]);main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<System.Windows.Threading.DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");Require(await active.SaveAsync(),"Production recent baseline");await Idle();Invoke(main,"OpenSticky",note);await Idle();var sticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id];var mainPanel=(AttachmentPanel)Control<ContentControl>(main,"AttachmentHost").Content;var stickyPanel=(AttachmentPanel)Control<ContentControl>(sticky,"AttachmentHost").Content;Require(mainPanel.ConnectPathButton.IsEnabled&&stickyPanel.ConnectPathButton.IsEnabled,"Real management/sticky have active profile identity");Require(await mainPanel.ConnectFilePathAsync(()=>path,_=>true),"Management encrypted path save");await Idle();Require(mainPanel.PathLinksList.Items.Count==1&&stickyPanel.PathLinksList.Items.Count==1&&note.Metadata.FilePathLinks.Single().UiDeviceId==Field<Guid>(main,"uiDeviceId"),"Shared production displays exact original local profile");
            var other=active.Workspace.CreateNote();Invoke(main,"RefreshNotes",other);await Idle();Require(mainPanel.IsDisposed&&mainPanel.PathLinksList.Items.Count==0,"Selection change disposes old private path labels");stickyPanel.PathLinksList.SelectedIndex=0;Require(stickyPanel.DetachSelectedPath()&&File.Exists(path),"Sticky detach preserves external original");Require(await active.SaveAsync(),"Sticky path detach saved");await active.LockAsync();await Idle();Require(stickyPanel.IsDisposed&&stickyPanel.PathLinksList.Items.Count==0,"Both real hosts conceal private paths");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(dir,true);}
    }
}
