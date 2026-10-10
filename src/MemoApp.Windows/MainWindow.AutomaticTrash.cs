using System.IO;
using System.Windows;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private sealed record AutomaticTrashConfiguration(SaveCoordinator Session,long Epoch,string Directory,int Days);
    private AutomaticTrashConfiguration? automaticTrash;
    private DateOnly? automaticTrashAttemptDay;
    private bool automaticTrashBusy;
    private void ClearAutomaticTrashViews()
    {
        automaticTrash=null;automaticTrashAttemptDay=null;
        foreach(Action clear in new Action[]{()=>AutoTrashDirectory.Text="",()=>AutoTrashDays.SelectedIndex=1})try{clear();}catch{}
    }
    private void AutoTrashDisable_Click(object sender,RoutedEventArgs e)=>ClearAutomaticTrashViews();
    private void AutoTrashChoose_Click(object sender,RoutedEventArgs e)
    {
        if(concealing||session is not{IsLocked:false} active)return;long epoch=uiEpoch;
        var dialog=new OpenFolderDialog{Title="휴지통 정리 전 암호 백업 폴더",Multiselect=false};
        if(dialog.ShowDialog(this)!=true||!LiveSearch(active,epoch))return;
        int days=AutoTrashDays.SelectedIndex switch{0=>7,1=>30,2=>90,_=>0};
        ConfigureAutomaticTrash(dialog.FolderName,days,()=>MessageBox.Show(this,$"현재 해제 세션에서 {days}일보다 오래된 휴지통 메모를 자동 정리할까요?\n먼저 선택 폴더에 암호 백업을 저장한 뒤 현재 본문과 내용 이력을 제거합니다. 백업·이전 파일·첨부 객체와 삭제 관계는 남습니다. 완전 삭제가 아닙니다.\n백업은 100개까지 보관하며 가득 차면 정리를 멈춥니다. 잠금·재실행하면 설정이 꺼집니다.","암호 백업 후 휴지통 자동 정리",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes);
    }
    internal bool ConfigureAutomaticTrash(string directory,int days,Func<bool> confirm)
    {
        if(concealing||automaticTrashBusy||session is not{IsLocked:false} active||days is not(7 or 30 or 90))return false;long epoch=uiEpoch;
        try
        {
            directory=Path.GetFullPath(directory);string relative=Path.GetRelativePath(root,directory);
            if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Vault destination refused");
            _=AutomaticBackupPolicy.NextDestination(directory,active.VaultIdentity,100);
            if(!LiveSearch(active,epoch)||!confirm()||!LiveSearch(active,epoch))return false;
            var config=new AutomaticTrashConfiguration(active,epoch,directory,days);automaticTrash=config;automaticTrashAttemptDay=null;AutoTrashDirectory.Text=directory;
            if(!LiveSearch(active,epoch)||!ReferenceEquals(automaticTrash,config)){ClearAutomaticTrashViews();return false;}
            TrashNotice("이 해제 세션에서 암호 백업 후 오래된 휴지통을 하루 한 번 정리합니다.");return true;
        }
        catch{if(LiveSearch(active,epoch))ClearAutomaticTrashViews();TrashNotice("자동 정리를 설정하지 않았습니다. 외부 로컬 백업 폴더와 보관 한도를 확인하세요.");return false;}
    }
    private void TrashNotice(string text){try{Notice.Text=text;}catch{}}
    internal async Task<bool> RunAutomaticTrashAsync(DateTimeOffset now,IAtomicVaultFiles? backupFiles=null)
    {
        if(automaticTrashBusy||automaticTrash is not{ } config||!LiveSearch(config.Session,config.Epoch)||config.Session.IsBusy)return false;
        var day=DateOnly.FromDateTime(now.UtcDateTime);if(automaticTrashAttemptDay==day)return false;
        automaticTrashBusy=true;automaticTrashAttemptDay=day;bool applied=false;
        bool Current()=>LiveSearch(config.Session,config.Epoch)&&ReferenceEquals(automaticTrash,config);
        try
        {
            var active=config.Session;var cutoff=now.ToUniversalTime().AddDays(-config.Days);
            var ids=active.Workspace.EligibleTrash(cutoff);if(ids.Length==0)return false;
            string destination=AutomaticBackupPolicy.NextDestination(config.Directory,active.VaultIdentity,100);
            if(!Current()||!await active.PrepareAttachmentsAsync()||!Current()||!await active.SaveAsync()||!Current()||active.IsDirty)return false;
            ids=active.Workspace.EligibleTrash(cutoff);if(ids.Length==0)return false;
            var before=active.Workspace.Capture();long epoch=active.AttachmentPreviewEpoch;
            var versions=active.Workspace.Notes.ToDictionary(n=>n.Id,n=>n.EditVersion);
            bool copied=await active.BackupAsync(destination,new CapacityCheckedBackupFiles(config.Directory,active.VaultIdentity,100,backupFiles));
            if(!copied||!Current()||active.IsDirty||epoch!=active.AttachmentPreviewEpoch)return false;
            var after=active.Workspace.Capture();
            if(!ids.SequenceEqual(active.Workspace.EligibleTrash(cutoff))||after.Notes.Length!=before.Notes.Length||after.Notes.Any(n=>!before.Notes.Any(b=>b.NoteId==n.NoteId&&b.RevisionId==n.RevisionId))||active.Workspace.Notes.Any(n=>!versions.TryGetValue(n.Id,out long version)||version!=n.EditVersion))return false;
            try{active.Workspace.PurgeTrash(ids,cutoff);applied=true;}
            catch{applied=ids.All(id=>active.Workspace.Notes.All(n=>n.Id!=id));if(!applied)throw;}
            if(!Current())return false;
            bool saved=await active.SaveAsync();if(!Current())return false;
            if(!saved||active.IsDirty){ClearAutomaticTrashViews();TrashNotice("휴지통 정리가 적용됐지만 저장을 확인하지 못했습니다. 변경과 암호 백업을 보존했습니다. 자동 정리는 중단했습니다.");return false;}
            TrashNotice($"휴지통 {ids.Length}개 정리를 저장했습니다. 원문과 이력은 정리 전 암호 백업에 남습니다.");return true;
        }
        catch{if(Current()){ClearAutomaticTrashViews();TrashNotice(applied?"정리가 적용된 변경과 암호 백업을 보존했습니다. 자동 정리를 중단했습니다.":"휴지통을 정리하지 않았습니다. 백업·저장·삭제 관계 한도를 확인하세요. 자동 정리를 중단했습니다.");}return false;}
        finally{automaticTrashBusy=false;}
    }
}
