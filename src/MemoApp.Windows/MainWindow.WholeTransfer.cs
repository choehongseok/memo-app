using System.Windows;
using MemoApp.Core.Storage;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private bool wholeTransferBusy;
    private async void WholeTransfer_Click(object sender,RoutedEventArgs e)
    {
        await ExportWholeVaultAsync(()=>MessageBox.Show(this,"메모·수정 이력·폴더·태그·첨부 원본·기기별 설정을 하나의 암호 파일로 복사합니다. 원본 자료는 보존합니다. 다른 PC에서 파일을 가져와 후보 검사·복구하려면 같은 복구 비밀이 필요합니다. 비밀은 이 파일에 평문으로 포함하지 않습니다.","전체 자료 이전",MessageBoxButton.OKCancel,MessageBoxImage.Information,MessageBoxResult.Cancel)==MessageBoxResult.OK,
            ()=>{var dialog=new SaveFileDialog{Filter="전체 자료 암호 이전|*.vault",FileName=$"memo-transfer-{DateTime.Now:yyyyMMdd-HHmmss}.vault",OverwritePrompt=true};return dialog.ShowDialog(this)==true?dialog.FileName:null;});
    }
    internal async Task<bool> ExportWholeVaultAsync(Func<bool> confirm,Func<string?> choose,IAtomicVaultFiles? files=null)
    {
        if(wholeTransferBusy||concealing||session is not{IsLocked:false} active)return false;long epoch=uiEpoch;wholeTransferBusy=true;
        bool Current()=>LiveSearch(active,epoch);
        try
        {
            if(!Current()||!confirm()||!Current())return false;string? destination=choose();if(destination is null||!Current())return false;
            bool success=await active.BackupAsync(destination,files);if(!Current())return false;
            Notice.Text=success?"전체 자료를 새 암호 이전 파일로 복사했습니다. 다른 PC의 잠금 화면에서 가져오기·후보 검사·복구를 진행하세요.":"전체 이전 실패 — 원본과 기존 파일을 보존했습니다. 새 이름·외부 로컬 폴더·저장 상태를 확인하세요.";return success;
        }
        catch{if(Current())Notice.Text="전체 이전 파일을 확인하지 못했습니다. 원본은 보존했습니다.";return false;}
        finally{wholeTransferBusy=false;}
    }
}
