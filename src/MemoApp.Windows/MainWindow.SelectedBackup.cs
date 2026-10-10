using System.IO;
using System.Security.Cryptography;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private bool selectedBackupBusy;
    internal async Task<bool> RestoreSelectedBackupAsync(BackupPreviewWindow viewer,string source,string sourceHash,Guid[] selected,Func<int,bool> confirm)
    {
        Dispatcher.VerifyAccess();if(selectedBackupBusy||windowClosed||concealing||session is not{IsLocked:false} active||selected.Length is <1 or >100||selected.Distinct().Count()!=selected.Length)return false;
        long epoch=uiEpoch,sourceEpoch=active.AttachmentPreviewEpoch;var token=fileOperations.Token;byte[]? cipher=null;HashSet<Guid>? beforeImport=null;bool applied=false;
        bool Current()=>!windowClosed&&!concealing&&SameFileSession(active,epoch)&&active.AttachmentPreviewEpoch==sourceEpoch&&!token.IsCancellationRequested&&backupPreviews.Contains(viewer)&&!viewer.IsRevoked&&viewer.IsVisible&&selected.SequenceEqual(viewer.SelectedIds);
        selectedBackupBusy=true;
        try
        {
            if(!Current()||!confirm(selected.Length)||!Current())return false;cipher=await ReadBackupCipherAsync(source,token);if(!Current())return false;
            if(sourceHash!=Convert.ToHexStringLower(SHA256.HashData(cipher)))throw new IOException("Selected preview source changed");
            beforeImport=active.Workspace.Notes.Select(n=>n.Id).ToHashSet();var copies=active.ImportSelectedEncryptedBackup(cipher,selected,sourceEpoch);applied=true;CryptographicOperations.ZeroMemory(cipher);cipher=null;
            if(!SameFileSession(active,epoch)||windowClosed||concealing)return false;
            bool saved=await active.SaveAsync();if(!SameFileSession(active,epoch)||windowClosed||concealing)return false;
            RefreshFolders();if(!SameFileSession(active,epoch)||windowClosed||concealing)return false;RefreshNotes(copies.FirstOrDefault());if(!SameFileSession(active,epoch)||windowClosed||concealing)return false;
            Notice.Text=saved?$"선택한 {copies.Length}개 백업 메모를 새 복사로 추가하고 암호 저장했습니다. 현재 메모와 원본 백업을 덮어쓰지 않았습니다.":"모든 선택 메모를 새 복사로 추가했지만 암호 저장에 실패했습니다. 전체 추가 내용과 기존 dirty 수정을 유지합니다. 저장 상태를 확인하세요.";return saved&&SameFileSession(active,epoch)&&!windowClosed&&!concealing;
        }
        catch(OperationCanceledException){return false;}
        catch
        {
            if(SameFileSession(active,epoch)&&!windowClosed&&!concealing)Notice.Text=(applied||beforeImport is not null&&active.Workspace.Notes.Any(n=>!beforeImport.Contains(n.Id)))?"선택 메모 추가 이후 처리 실패 — 전체 추가 내용·기존 수정과 암호 저장 상태를 확인하세요.":"선택 복구를 적용하지 못했습니다. 미리보기 원본 교체·조직 충돌·현재 첨부 키·전체 한도를 확인하세요. 현재 자료와 원본 백업은 보존했습니다.";return false;
        }
        finally{if(cipher is not null)CryptographicOperations.ZeroMemory(cipher);selectedBackupBusy=false;}
    }
}
