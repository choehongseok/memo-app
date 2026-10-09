using System.IO;
using System.Windows;
using MemoApp.Core.Transfer;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private bool excelImportBusy;
    private async void ExcelImport_Click(object sender,RoutedEventArgs e)=>await ImportExcelAsync(
        ()=>{var dialog=new OpenFileDialog{Filter="기본 제목·본문 Excel|*.xlsx",CheckFileExists=true,Multiselect=false};return dialog.ShowDialog(this)==true?dialog.FileName:null;},
        count=>MessageBox.Show(this,$"첫 시트에서 {count}개 메모의 제목·본문을 일반 텍스트로 추가합니다. 제목/본문 2열 또는 메모앱 5열 템플릿만 지원합니다. 서식·첨부·다른 시트는 가져오지 않으며 원본은 보존합니다. 추가할까요?","Excel 제목·본문 가져오기",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes);
    internal async Task<bool> ImportExcelAsync(Func<string?> choose,Func<int,bool> confirm)
    {
        Dispatcher.VerifyAccess();if(excelImportBusy||session is not{IsLocked:false} active||windowClosed||concealing)return false;
        long epoch=uiEpoch,sourceEpoch=active.AttachmentPreviewEpoch;Guid? folder=(FolderFilter.SelectedItem as FolderChoice)?.Id;var token=fileOperations.Token;bool applied=false;HashSet<Guid>? beforeImport=null;
        bool Current()=>!windowClosed&&!concealing&&SameFileSession(active,epoch)&&sourceEpoch==active.AttachmentPreviewEpoch&&(FolderFilter.SelectedItem as FolderChoice)?.Id==folder&&(folder is null||active.Workspace.Folders.Any(f=>f.FolderId==folder));
        excelImportBusy=true;ExcelImportButton.IsEnabled=false;
        try
        {
            if(!Current())return false;string? path=choose();if(path is null||!Current())return false;if(!string.Equals(Path.GetExtension(path),".xlsx",StringComparison.OrdinalIgnoreCase))throw new IOException("Workbook extension mismatch");
            var imported=await ReadExcelAsync(path,token);if(!Current()||!confirm(imported.Notes.Length)||!Current()||token.IsCancellationRequested)return false;
            beforeImport=active.Workspace.Notes.Select(n=>n.Id).ToHashSet();var added=active.Workspace.ImportTexts(imported.Notes,folder);applied=true;if(!SameFileSession(active,epoch)||windowClosed||concealing)return false;
            bool saved=await active.SaveAsync();if(!SameFileSession(active,epoch)||windowClosed||concealing)return false;
            RefreshNotes(added.FirstOrDefault());if(!SameFileSession(active,epoch)||windowClosed||concealing)return false;
            Notice.Text=saved?$"{added.Length}개 일반 텍스트 메모를 추가하고 암호 저장했습니다. 원본·서식·첨부·다른 시트는 변경하지 않았습니다.":"모든 행을 추가했지만 암호 저장에 실패했습니다. 전체 추가 내용과 기존 수정은 편집 상태에 유지됩니다. 저장 상태를 확인하세요.";return saved;
        }
        catch(OperationCanceledException){return false;}
        catch{if(SameFileSession(active,epoch)&&!windowClosed&&!concealing)Notice.Text=(applied||beforeImport is not null&&active.Workspace.Notes.Any(n=>!beforeImport.Contains(n.Id)))?"Excel 행 추가 이후 처리 실패 — 추가 내용과 기존 수정의 편집 상태·암호 저장 상태를 확인하세요.":"Excel 가져오기 실패 — 기본 문자열 템플릿·수식 없는 첫 시트·파일/행 한도를 확인하세요. 원본과 기존 메모는 보존했습니다.";return false;}
        finally{excelImportBusy=false;if(!windowClosed&&!concealing)ExcelImportButton.IsEnabled=session is{IsLocked:false};}
    }
    private static Task<ImportedWorkbookText> ReadExcelAsync(string path,CancellationToken token)=>Task.Run(()=>ExcelTextTransfer.Read(path,token),token);
}
