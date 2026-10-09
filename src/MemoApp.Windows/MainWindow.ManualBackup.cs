using MemoApp.Core.Storage;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private bool manualBackupBusy;
    internal async Task<bool> ChooseManualBackupAsync(Func<string?> choose, IAtomicVaultFiles? files = null)
    {
        Dispatcher.VerifyAccess();
        if (manualBackupBusy || windowClosed || concealing || session is not { IsLocked: false } active) return false;
        long epoch = uiEpoch;
        bool Current() => LiveSearch(active, epoch);
        manualBackupBusy = true;
        try
        {
            string? destination = choose();
            if (destination is null || !Current()) return false;
            bool success = await active.BackupAsync(destination, files);
            if (!Current()) return false;
            Notice.Text = success ? "최신 암호 백업을 새 파일로 저장했습니다. 같은 복구 비밀이 필요합니다." : "백업 실패/상태 변경 — 기존 파일 덮어쓰기 없이 현재 자료를 보존했습니다. 새 이름·경로·쓰기 권한·저장 상태를 확인하세요.";
            return success && Current();
        }
        catch { try { if (Current()) Notice.Text = "백업 실패 — 원본과 기존 파일을 보존했습니다. 새 이름·외부 로컬 경로·저장 상태를 확인하세요."; } catch { } return false; }
        finally { manualBackupBusy = false; }
    }
}
