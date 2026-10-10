using System.Security.Cryptography;
using System.Windows;
using MemoApp.Core.Transfer;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private readonly HashSet<BackupPreviewWindow> backupPreviews=[];
    private bool backupPreviewBusy;
    private void ClearBackupPreviews(){foreach(var viewer in backupPreviews.ToArray())try{viewer.Dispose();}catch{}backupPreviews.Clear();}
    private async void BackupPreview_Click(object sender,RoutedEventArgs e)=>await ShowBackupPreviewAsync(()=>{var dialog=new OpenFileDialog{Filter="암호 메모 백업|*.vault",CheckFileExists=true,Multiselect=false};return dialog.ShowDialog(this)==true?dialog.FileName:null;});
    internal async Task<bool> ShowBackupPreviewAsync(Func<string?> choose)
    {
        Dispatcher.VerifyAccess();if(backupPreviewBusy||windowClosed||concealing||session is not{IsLocked:false} active)return false;long epoch=uiEpoch,sourceEpoch=active.AttachmentPreviewEpoch;var token=fileOperations.Token;byte[]? cipher=null;BackupPreviewWindow? viewer=null;
        bool Current()=>!windowClosed&&!concealing&&SameFileSession(active,epoch)&&active.AttachmentPreviewEpoch==sourceEpoch&&!token.IsCancellationRequested;
        backupPreviewBusy=true;
        try
        {
            BackupPreviewButton.IsEnabled=false;
            ClearBackupPreviews();if(!Current())return false;string? path=choose();if(path is null||!Current())return false;
            cipher=await ReadBackupCipherAsync(path,token);if(!Current())return false;
            // Authentication is bounded and synchronous here; no key or session is passed to the file worker.
            string sourceHash=Convert.ToHexStringLower(SHA256.HashData(cipher));var data=active.PreviewEncryptedBackup(cipher);CryptographicOperations.ZeroMemory(cipher);cipher=null;if(!Current())return false;
            viewer=new(Current);viewer.Owner=this;if(!Current()){viewer.Dispose();return false;}backupPreviews.Add(viewer);RegisterPreviewClose(backupPreviews,viewer);var sourceViewer=viewer;viewer.ConfigureRestore(ids=>RestoreSelectedBackupAsync(sourceViewer,path,sourceHash,ids,count=>MessageBox.Show(this,$"선택한 {count}개 백업 메모를 새 메모 ID의 복사로 추가합니다. 현재 메모를 덮어쓰지 않습니다. 원문 서식·첨부·관련 조직은 보존하고 삭제 표시는 해제합니다. 과거 이력/기기 설정은 원본 백업에 남으며 새 복사에 적용하지 않습니다. 추가할까요?","선택 메모 새 복사 복구",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes));
            if(!viewer.Publish(data)||!Current()){viewer.Dispose();backupPreviews.Remove(viewer);return false;}Notice.Text="암호 백업을 읽기 전용으로 미리봅니다. 현재 자료와 원본 파일은 바꾸지 않았습니다.";if(!Current()){viewer.Dispose();return false;}return true;
        }
        catch(OperationCanceledException){viewer?.Dispose();return false;}
        catch{viewer?.Dispose();if(Current())Notice.Text="백업 미리보기 실패 — 같은 저장소의 인증된 로컬 암호 파일과 읽기 권한을 확인하세요. 원본과 현재 자료는 보존했습니다.";return false;}
        finally{if(cipher is not null)CryptographicOperations.ZeroMemory(cipher);backupPreviewBusy=false;if(!windowClosed&&!concealing)try{BackupPreviewButton.IsEnabled=session is{IsLocked:false};}catch{}}
    }
    private static void RegisterPreviewClose(HashSet<BackupPreviewWindow> viewers,BackupPreviewWindow viewer)
    {
        EventHandler? closed=null;closed=(_,_)=>{viewers.Remove(viewer);viewer.Closed-=closed;};viewer.Closed+=closed;
    }
    private static Task<byte[]> ReadBackupCipherAsync(string path,CancellationToken token)=>Task.Run(()=>BackupFileReader.Read(path,token),token);
}
