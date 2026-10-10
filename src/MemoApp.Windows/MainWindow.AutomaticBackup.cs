using System.IO;
using System.Windows;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private sealed record AutomaticBackupConfiguration(SaveCoordinator Session,long Epoch,string Directory,Guid VaultId,int Capacity,bool AfterSave,bool Daily,bool Exit,StoredAutomaticBackupPolicy? Persistent=null,long Revision=0,CancellationToken Authority=default);
    private AutomaticBackupConfiguration? automaticBackup;
    private DateOnly? automaticBackupAttemptDay;
    private bool automaticBackupBusy;
    private long automaticBackupPolicyRevision;
    private CancellationTokenSource? automaticBackupCancellation;
    private CancellationToken NewAutomaticBackupAuthority(){automaticBackupCancellation=new();return automaticBackupCancellation.Token;}
    private void ClearAutomaticBackupViews()
    {
        automaticBackupCancellation?.Cancel();automaticBackupCancellation?.Dispose();automaticBackupCancellation=null;automaticBackupPolicyRevision++;automaticBackup=null;automaticBackupAttemptDay=null;
        foreach(Action clear in new Action[]{()=>AutoBackupDirectory.Text="",()=>AutoBackupMode.SelectedIndex=0,()=>AutoBackupExit.IsChecked=false,()=>AutoBackupPersist.IsChecked=false,()=>AutoBackupCapacity.Text="20"})try{clear();}catch{}
    }
    private async void AutoBackupChoose_Click(object sender,RoutedEventArgs e)
    {
        if(session is not{IsLocked:false} active||concealing)return;long epoch=uiEpoch;
        var dialog=new OpenFolderDialog{Title="암호 자동 백업 폴더",Multiselect=false};
        if(dialog.ShowDialog(this)!=true||!LiveSearch(active,epoch))return;
        if(!int.TryParse(AutoBackupCapacity.Text,out int capacity)){Notice.Text="보관 한도는 1~100개입니다.";return;}
        bool afterSave=AutoBackupMode.SelectedIndex==1,daily=AutoBackupMode.SelectedIndex==2,exit=AutoBackupExit.IsChecked==true;
        bool persist=AutoBackupPersist.IsChecked==true;
        if(!persist&&active.Workspace.GetUiDevice(uiDeviceId).AutomaticBackupPolicy is not null&&!await DisableAutomaticBackupsAsync())return;
        if(!LiveSearch(active,epoch))return;
        if(persist?await ConfigurePersistentAutomaticBackupsAsync(dialog.FolderName,capacity,afterSave,daily,exit):ConfigureAutomaticBackups(dialog.FolderName,capacity,afterSave,daily,exit))Notice.Text="암호 자동 백업을 설정했습니다. 한도가 차면 기존 파일을 보존하고 새 백업을 멈춥니다.";
    }
    private async void AutoBackupDisable_Click(object sender,RoutedEventArgs e)=>await DisableAutomaticBackupsAsync();
    internal bool ConfigureAutomaticBackups(string directory,int capacity,bool afterSave,bool daily,bool exit)
    {
        ClearAutomaticBackupViews();if(concealing||automaticBackupBusy||session is not{IsLocked:false} active){Notice.Text="현재 자동 실행은 껐습니다. 설정 작업이 끝난 뒤 다시 선택하세요. 마지막 저장 설정은 재실행 때 남을 수 있습니다.";return false;}long epoch=uiEpoch;
        try
        {
            directory=Path.GetFullPath(directory);string relative=Path.GetRelativePath(root,directory);
            if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Active vault backup directory refused");
            Guid id=active.VaultIdentity;_ = AutomaticBackupPolicy.NextDestination(directory,id,capacity);
            if(!LiveSearch(active,epoch))return false;var config=new AutomaticBackupConfiguration(active,epoch,directory,id,capacity,afterSave,daily,exit,Authority:NewAutomaticBackupAuthority());automaticBackup=config;automaticBackupAttemptDay=null;ShowAutomaticBackupConfiguration(config);
            if(!LiveSearch(active,epoch)||!ReferenceEquals(automaticBackup,config)){ClearAutomaticBackupViews();return false;}return true;
        }
        catch{if(LiveSearch(active,epoch))Notice.Text="자동 백업 설정 실패 — 외부 로컬 폴더·개수 한도·기존 백업 개수를 확인하세요.";return false;}
    }
    private string ValidateAutomaticBackupDirectory(string directory,Guid vaultId,int capacity)
    {
        directory=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));string relative=Path.GetRelativePath(root,directory);
        if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Active vault backup directory refused");
        _=AutomaticBackupPolicy.NextDestination(directory,vaultId,capacity);return directory;
    }
    private bool PolicyAuthority(SaveCoordinator active,long epoch,long revision)=>revision==automaticBackupPolicyRevision&&LiveSearch(active,epoch);
    internal async Task<bool> ConfigurePersistentAutomaticBackupsAsync(string directory,int capacity,bool afterSave,bool daily,bool exit)
    {
        ClearAutomaticBackupViews();long revision=automaticBackupPolicyRevision;
        if(concealing||automaticBackupBusy||session is not{IsLocked:false} active){Notice.Text="현재 자동 실행은 껐습니다. 설정 작업이 끝난 뒤 다시 선택하세요. 마지막 저장 설정은 재실행 때 남을 수 있습니다.";return false;}
        long epoch=uiEpoch;automaticBackup=null;automaticBackupAttemptDay=null;automaticBackupBusy=true;
        try
        {
            directory=ValidateAutomaticBackupDirectory(directory,active.VaultIdentity,capacity);
            var binding=AutomaticBackupRootBinding.Capture(root);
            if(!await active.PrepareAttachmentsAsync()){if(PolicyAuthority(active,epoch,revision))Notice.Text="자동 백업 설정 저장 실패 — 자동 실행은 꺼져 있습니다. 저장 상태를 확인하세요.";return false;}
            if(!PolicyAuthority(active,epoch,revision))return false;
            var policy=new StoredAutomaticBackupPolicy(directory,active.VaultIdentity,binding,capacity,afterSave,daily,exit);
            active.Workspace.SetAutomaticBackupPolicy(uiDeviceId,policy);
            if(!await active.SaveAsync()){if(PolicyAuthority(active,epoch,revision))Notice.Text="자동 백업 설정 저장 실패 — 자동 실행은 꺼져 있습니다. 저장 상태를 확인하세요.";return false;}
            if(!PolicyAuthority(active,epoch,revision))return false;
            if(!AutomaticBackupRootBinding.Matches(root,binding)){Notice.Text="암호 설정은 저장했지만 원본 폴더를 다시 확인하지 못해 자동 실행은 껐습니다. 백업 폴더를 다시 선택하세요.";return false;}
            automaticBackup=new(active,epoch,directory,active.VaultIdentity,capacity,afterSave,daily,exit,policy,revision,NewAutomaticBackupAuthority());
            ShowAutomaticBackupConfiguration(automaticBackup);return true;
        }
        catch{if(PolicyAuthority(active,epoch,revision))Notice.Text="자동 백업 설정을 저장하지 못했습니다. 자동 실행은 꺼져 있습니다. 폴더·저장 상태를 확인하고 다시 선택하세요.";return false;}
        finally{automaticBackupBusy=false;}
    }
    internal async Task<bool> DisableAutomaticBackupsAsync()
    {
        ClearAutomaticBackupViews();long revision=automaticBackupPolicyRevision;
        if(session is not{IsLocked:false} active||concealing)return false;long epoch=uiEpoch;
        try
        {
            active.Workspace.SetAutomaticBackupPolicy(uiDeviceId,null);
            bool saved=await active.SaveAsync();
            if(PolicyAuthority(active,epoch,revision))Notice.Text=saved?"자동 백업을 끄고 재실행 복원 설정을 저장했습니다.":"자동 실행은 껐지만 해제 설정 저장에 실패했습니다. 재실행 전에 저장 상태를 확인하세요.";
            return saved&&PolicyAuthority(active,epoch,revision);
        }
        catch{if(PolicyAuthority(active,epoch,revision))Notice.Text="자동 실행은 껐지만 해제 설정 저장에 실패했습니다. 재실행 전에 저장 상태를 확인하세요.";return false;}
    }
    private void ShowAutomaticBackupConfiguration(AutomaticBackupConfiguration config)
    {
        AutoBackupDirectory.Text=config.Directory;AutoBackupCapacity.Text=config.Capacity.ToString();AutoBackupMode.SelectedIndex=config.AfterSave?1:config.Daily?2:0;AutoBackupExit.IsChecked=config.Exit;AutoBackupPersist.IsChecked=config.Persistent is not null;
    }
    private void RestoreAutomaticBackupConfiguration()
    {
        ClearAutomaticBackupViews();if(session is not{IsLocked:false} active)return;long epoch=uiEpoch;
        var policy=active.Workspace.GetUiDevice(uiDeviceId).AutomaticBackupPolicy;if(policy is null)return;
        try
        {
            if(policy.VaultIdentity!=active.VaultIdentity||!AutomaticBackupRootBinding.Matches(root,policy.SourceRootBinding))throw new IOException();
            string directory=ValidateAutomaticBackupDirectory(policy.Directory,policy.VaultIdentity,policy.Capacity);
            if(!LiveSearch(active,epoch))return;
            automaticBackup=new(active,epoch,directory,policy.VaultIdentity,policy.Capacity,policy.AfterSave,policy.Daily,policy.OnExit,policy,automaticBackupPolicyRevision,NewAutomaticBackupAuthority());
            automaticBackupAttemptDay=policy.LastAttemptDay;ShowAutomaticBackupConfiguration(automaticBackup);
        }
        catch{if(LiveSearch(active,epoch))Notice.Text="저장된 자동 백업 조건을 확인하지 못해 자동 실행은 꺼져 있습니다. 이 기기에서 백업 폴더를 다시 선택하세요.";}
    }
    private bool AutomaticBackupAuthority(AutomaticBackupConfiguration config)=>!config.Authority.IsCancellationRequested&&LiveSearch(config.Session,config.Epoch)&&ReferenceEquals(automaticBackup,config)&&(config.Persistent is null||config.Revision==automaticBackupPolicyRevision&&config.Session.Workspace.GetUiDevice(uiDeviceId).AutomaticBackupPolicy==config.Persistent);
    internal Task<bool> RunAutomaticBackupAsync(bool afterSave,IAtomicVaultFiles? files=null)=>RunAutomaticBackupAtAsync(afterSave,DateOnly.FromDateTime(DateTime.UtcNow),files);
    internal async Task<bool> RunAutomaticBackupAtAsync(bool afterSave,DateOnly day,IAtomicVaultFiles? files=null)
    {
        if(automaticBackupBusy||automaticBackup is not{ } config||!AutomaticBackupAuthority(config))return false;
        bool saveTrigger=afterSave&&config.AfterSave,dailyTrigger=config.Daily&&(automaticBackupAttemptDay is null||automaticBackupAttemptDay<day);
        if(!saveTrigger&&!dailyTrigger)return false;
        automaticBackupBusy=true;automaticBackupAttemptDay=day;
        bool Current()=>AutomaticBackupAuthority(config);
        try
        {
            if(dailyTrigger&&config.Persistent is{} policy)
            {
                if(!Current())return false;
                var reserved=policy with{LastAttemptDay=day};config.Session.Workspace.SetAutomaticBackupPolicy(uiDeviceId,reserved);
                if(!PolicyAuthority(config.Session,config.Epoch,config.Revision)||!ReferenceEquals(automaticBackup,config)||config.Session.Workspace.GetUiDevice(uiDeviceId).AutomaticBackupPolicy!=reserved)return false;
                config=config with{Persistent=reserved};automaticBackup=config;
                if(!await config.Session.SaveAsync()){if(Current())Notice.Text="UTC 하루 백업 예약을 저장하지 못해 백업을 만들지 않았습니다. 저장 상태를 확인하세요.";return false;}
                if(!Current())return false;
            }
            string destination=AutomaticBackupPolicy.NextDestination(config.Directory,config.VaultId,config.Capacity);
            if(!Current())return false;
            bool copied=await config.Session.BackupAsync(destination,new CapacityCheckedBackupFiles(config.Directory,config.VaultId,config.Capacity,files,config.Authority));
            if(!Current())return false;
            Notice.Text=copied?"새 암호 자동 백업을 저장했습니다.":"자동 백업 실패 — 원본과 기존 백업을 보존했습니다. 폴더와 저장 상태를 확인하세요.";return copied;
        }
        catch{if(Current())Notice.Text="자동 백업을 만들지 않았습니다. 보관 한도·폴더·기존 파일을 확인하세요. 기존 백업은 삭제하지 않았습니다.";return false;}
        finally{automaticBackupBusy=false;}
    }
    internal async Task<bool> LockForCloseAsync(SaveCoordinator active)
    {
        var config=automaticBackup;
        if(config is null||!config.Exit||!ReferenceEquals(config.Session,active)||!AutomaticBackupAuthority(config)){await active.LockAsync();return true;}
        string destination;
        try{destination=AutomaticBackupPolicy.NextDestination(config.Directory,config.VaultId,config.Capacity);}
        catch{await active.LockAsync();Notice.Text="종료 백업 실패 — 기존 백업을 보존했습니다. 보관 한도와 폴더를 확인한 뒤 다시 종료하세요.";return false;}
        bool success=await active.LockWithBackupAsync(destination,new CapacityCheckedBackupFiles(config.Directory,config.VaultId,config.Capacity));
        if(!success)Notice.Text="종료 백업을 확인하지 못해 종료를 보류했습니다. 원본과 기존 파일을 보존했습니다.";return success;
    }
    private sealed class CapacityCheckedBackupFiles(string directory,Guid vaultId,int capacity,IAtomicVaultFiles? files=null,CancellationToken authority=default):IAtomicVaultFiles
    {
        private readonly IAtomicVaultFiles actual=files??new AtomicVaultFiles();
        public Stream CreateNew(string path){authority.ThrowIfCancellationRequested();_ = AutomaticBackupPolicy.NextDestination(directory,vaultId,capacity);var output=actual.CreateNew(path);try{authority.ThrowIfCancellationRequested();return output;}catch{output.Dispose();throw;}}
        public void FlushToDisk(Stream stream){authority.ThrowIfCancellationRequested();actual.FlushToDisk(stream);}public void Move(string temporary,string current)=>throw new NotSupportedException();public void Replace(string temporary,string current,string previous)=>throw new NotSupportedException();
    }

}
