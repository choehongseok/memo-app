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
        backupPreviewBusy=true;BackupPreviewButton.IsEnabled=false;
        try
        {
            ClearBackupPreviews();if(!Current())return false;string? path=choose();if(path is null||!Current())return false;
            cipher=await ReadBackupCipherAsync(path,token);if(!Current())return false;
            // Authentication is bounded and synchronous here; no key or session is passed to the file worker.
            var data=active.PreviewEncryptedBackup(cipher);CryptographicOperations.ZeroMemory(cipher);cipher=null;if(!Current())return false;
            viewer=new(Current);viewer.Owner=this;if(!Current()){viewer.Dispose();return false;}backupPreviews.Add(viewer);RegisterPreviewClose(backupPreviews,viewer);
            if(!viewer.Publish(data)||!Current()){viewer.Dispose();backupPreviews.Remove(viewer);return false;}Notice.Text="암호 백업을 읽기 전용으로 미리봅니다. 현재 자료와 원본 파일은 바꾸지 않았습니다.";if(!Current()){viewer.Dispose();return false;}return true;
        }
        catch(OperationCanceledException){viewer?.Dispose();return false;}
        catch{viewer?.Dispose();if(Current())Notice.Text="백업 미리보기 실패 — 같은 저장소의 인증된 로컬 암호 파일과 읽기 권한을 확인하세요. 원본과 현재 자료는 보존했습니다.";return false;}
        finally{if(cipher is not null)CryptographicOperations.ZeroMemory(cipher);backupPreviewBusy=false;if(!windowClosed&&!concealing)BackupPreviewButton.IsEnabled=session is{IsLocked:false};}
    }
    private static void RegisterPreviewClose(HashSet<BackupPreviewWindow> viewers,BackupPreviewWindow viewer)
    {
        EventHandler? closed=null;closed=(_,_)=>{viewers.Remove(viewer);viewer.Closed-=closed;};viewer.Closed+=closed;
    }
    private static Task<byte[]> ReadBackupCipherAsync(string path,CancellationToken token)=>Task.Run(()=>BackupFileReader.Read(path,token),token);
}
