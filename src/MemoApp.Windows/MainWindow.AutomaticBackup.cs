using System.IO;
using System.Windows;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private sealed record AutomaticBackupConfiguration(SaveCoordinator Session,long Epoch,string Directory,Guid VaultId,int Capacity,bool AfterSave,bool Daily,bool Exit);
    private AutomaticBackupConfiguration? automaticBackup;
    private DateOnly? automaticBackupAttemptDay;
    private bool automaticBackupBusy;
    private void ClearAutomaticBackupViews()
    {
        automaticBackup=null;automaticBackupAttemptDay=null;
        foreach(Action clear in new Action[]{()=>AutoBackupDirectory.Text="",()=>AutoBackupMode.SelectedIndex=0,()=>AutoBackupExit.IsChecked=false,()=>AutoBackupCapacity.Text="20"})try{clear();}catch{}
    }
    private void AutoBackupChoose_Click(object sender,RoutedEventArgs e)
    {
        if(session is not{IsLocked:false} active||concealing)return;long epoch=uiEpoch;
        var dialog=new OpenFolderDialog{Title="이 해제 세션에서 사용할 암호 백업 폴더",Multiselect=false};
        if(dialog.ShowDialog(this)!=true||!LiveSearch(active,epoch))return;
        if(!int.TryParse(AutoBackupCapacity.Text,out int capacity)){Notice.Text="보관 한도는 1~100개입니다.";return;}
        bool afterSave=AutoBackupMode.SelectedIndex==1,daily=AutoBackupMode.SelectedIndex==2,exit=AutoBackupExit.IsChecked==true;
        if(ConfigureAutomaticBackups(dialog.FolderName,capacity,afterSave,daily,exit))Notice.Text="이 해제 세션의 암호 자동 백업을 설정했습니다. 한도가 차면 기존 파일을 보존하고 새 백업을 멈춥니다.";
    }
    private void AutoBackupDisable_Click(object sender,RoutedEventArgs e)=>ClearAutomaticBackupViews();
    internal bool ConfigureAutomaticBackups(string directory,int capacity,bool afterSave,bool daily,bool exit)
    {
        if(concealing||automaticBackupBusy||session is not{IsLocked:false} active)return false;long epoch=uiEpoch;
        try
        {
            directory=Path.GetFullPath(directory);string relative=Path.GetRelativePath(root,directory);
            if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Active vault backup directory refused");
            Guid id=active.VaultIdentity;_ = AutomaticBackupPolicy.NextDestination(directory,id,capacity);
            if(!LiveSearch(active,epoch))return false;var config=new AutomaticBackupConfiguration(active,epoch,directory,id,capacity,afterSave,daily,exit);automaticBackup=config;automaticBackupAttemptDay=null;AutoBackupDirectory.Text=directory;
            if(!LiveSearch(active,epoch)||!ReferenceEquals(automaticBackup,config)){ClearAutomaticBackupViews();return false;}return true;
        }
        catch{if(LiveSearch(active,epoch))Notice.Text="자동 백업 설정 실패 — 외부 로컬 폴더·개수 한도·기존 백업 개수를 확인하세요.";return false;}
    }
    internal async Task<bool> RunAutomaticBackupAsync(bool afterSave,IAtomicVaultFiles? files=null)
    {
        if(automaticBackupBusy||automaticBackup is not{ } config||!LiveSearch(config.Session,config.Epoch))return false;
        DateOnly day=DateOnly.FromDateTime(DateTime.UtcNow);
        if(!(afterSave&&config.AfterSave)&&!(config.Daily&&automaticBackupAttemptDay!=day))return false;
        automaticBackupBusy=true;automaticBackupAttemptDay=day;
        try
        {
            string destination=AutomaticBackupPolicy.NextDestination(config.Directory,config.VaultId,config.Capacity);
            if(!LiveSearch(config.Session,config.Epoch)||!ReferenceEquals(automaticBackup,config))return false;
            bool copied=await config.Session.BackupAsync(destination,new CapacityCheckedBackupFiles(config.Directory,config.VaultId,config.Capacity,files));
            if(!LiveSearch(config.Session,config.Epoch)||!ReferenceEquals(automaticBackup,config))return false;
            Notice.Text=copied?"새 암호 자동 백업을 저장했습니다.":"자동 백업 실패 — 원본과 기존 백업을 보존했습니다. 폴더와 저장 상태를 확인하세요.";return copied;
        }
        catch{if(LiveSearch(config.Session,config.Epoch)&&ReferenceEquals(automaticBackup,config))Notice.Text="자동 백업을 만들지 않았습니다. 보관 한도·폴더·기존 파일을 확인하세요. 기존 백업은 삭제하지 않았습니다.";return false;}
        finally{automaticBackupBusy=false;}
    }
    internal async Task<bool> LockForCloseAsync(SaveCoordinator active)
    {
        var config=automaticBackup;
        if(config is null||!config.Exit||!ReferenceEquals(config.Session,active)||!LiveSearch(active,config.Epoch)){await active.LockAsync();return true;}
        string destination;
        try{destination=AutomaticBackupPolicy.NextDestination(config.Directory,config.VaultId,config.Capacity);}
        catch{await active.LockAsync();Notice.Text="종료 백업 실패 — 기존 백업을 보존했습니다. 보관 한도와 폴더를 확인한 뒤 다시 종료하세요.";return false;}
        bool success=await active.LockWithBackupAsync(destination,new CapacityCheckedBackupFiles(config.Directory,config.VaultId,config.Capacity));
        if(!success)Notice.Text="종료 백업을 확인하지 못해 종료를 보류했습니다. 원본과 기존 파일을 보존했습니다.";return success;
    }
    private sealed class CapacityCheckedBackupFiles(string directory,Guid vaultId,int capacity,IAtomicVaultFiles? files=null):IAtomicVaultFiles
    {
        private readonly IAtomicVaultFiles actual=files??new AtomicVaultFiles();
        public Stream CreateNew(string path){_ = AutomaticBackupPolicy.NextDestination(directory,vaultId,capacity);return actual.CreateNew(path);}
        public void FlushToDisk(Stream stream)=>actual.FlushToDisk(stream);public void Move(string temporary,string current)=>throw new NotSupportedException();public void Replace(string temporary,string current,string previous)=>throw new NotSupportedException();
    }

}
