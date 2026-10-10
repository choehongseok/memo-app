using System.IO;
using System.Windows;
using MemoApp.Core.Transfer;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private bool excelImportBusy;
    private async void ExcelImport_Click(object sender,RoutedEventArgs e)=>await ImportMappedExcelAsync(
        ()=>{var dialog=new OpenFileDialog{Filter="Excel 제목·본문|*.xlsx",CheckFileExists=true,Multiselect=false};return dialog.ShowDialog(this)==true?dialog.FileName:null;},
        ShowExcelSelection,
        count=>MessageBox.Show(this,$"선택한 시트·열에서 {count}개 메모의 제목·본문을 일반 텍스트로 추가합니다. 수식·서식·첨부는 가져오지 않으며 원본은 보존합니다. 추가할까요?","Excel 제목·본문 가져오기",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes);
    private WorkbookImportSelection? ShowExcelSelection(WorkbookImportCatalog catalog)
    {
        if(session is not{IsLocked:false} active||windowClosed||concealing)return null;long epoch=uiEpoch;
        var dialog=new ExcelImportSelectionDialog(catalog){Owner=this};
        void Revoke()=>dialog.Revoke();active.Conceal+=Revoke;
        try{return SameFileSession(active,epoch)&&!concealing&&dialog.ShowDialog()==true&&SameFileSession(active,epoch)&&!concealing?dialog.Selection:null;}
        finally{active.Conceal-=Revoke;dialog.Revoke();}
    }
    internal Task<bool> ImportMappedExcelAsync(Func<string?> choose,Func<WorkbookImportCatalog,WorkbookImportSelection?> select,Func<int,bool> confirm)
        =>ImportExcelCoreAsync(choose,confirm,select);
    internal async Task<bool> ImportExcelAsync(Func<string?> choose,Func<int,bool> confirm)
        =>await ImportExcelCoreAsync(choose,confirm,null);
    private async Task<bool> ImportExcelCoreAsync(Func<string?> choose,Func<int,bool> confirm,Func<WorkbookImportCatalog,WorkbookImportSelection?>? select)
    {
        Dispatcher.VerifyAccess();if(excelImportBusy||session is not{IsLocked:false} active||windowClosed||concealing)return false;
        long epoch=uiEpoch,sourceEpoch=active.AttachmentPreviewEpoch;Guid? folder=(FolderFilter.SelectedItem as FolderChoice)?.Id;var token=fileOperations.Token;bool applied=false;HashSet<Guid>? beforeImport=null;
        bool Current()=>!windowClosed&&!concealing&&SameFileSession(active,epoch)&&sourceEpoch==active.AttachmentPreviewEpoch&&(FolderFilter.SelectedItem as FolderChoice)?.Id==folder&&(folder is null||active.Workspace.Folders.Any(f=>f.FolderId==folder));
        excelImportBusy=true;
        try
        {
            ExcelImportButton.IsEnabled=false;
            if(!Current())return false;string? path=choose();if(path is null||!Current())return false;if(!string.Equals(Path.GetExtension(path),".xlsx",StringComparison.OrdinalIgnoreCase))throw new IOException("Workbook extension mismatch");
            ImportedWorkbookText imported;
            if(select is null)imported=await ReadExcelAsync(path,token);
            else
            {
                var catalog=await ReadExcelCatalogAsync(path,token);if(!Current()||token.IsCancellationRequested)return false;
                var selection=select(catalog);if(selection is null||!Current()||token.IsCancellationRequested)return false;
                if(!string.Equals(selection.ExpectedSourceSha256,catalog.SourceSha256,StringComparison.Ordinal))throw new InvalidDataException("Selection source mismatch");
                imported=await ReadSelectedExcelAsync(path,selection,token);
            }
            if(!Current()||!confirm(imported.Notes.Length)||!Current()||token.IsCancellationRequested)return false;
            beforeImport=active.Workspace.Notes.Select(n=>n.Id).ToHashSet();var added=active.Workspace.ImportTexts(imported.Notes,folder);applied=true;if(!SameFileSession(active,epoch)||windowClosed||concealing)return false;
            bool saved=await active.SaveAsync();if(!SameFileSession(active,epoch)||windowClosed||concealing)return false;
            RefreshNotes(added.FirstOrDefault());if(!SameFileSession(active,epoch)||windowClosed||concealing)return false;
            Notice.Text=saved?$"{added.Length}개 일반 텍스트 메모를 추가하고 암호 저장했습니다. 원본·서식·첨부·다른 시트는 변경하지 않았습니다.":"모든 행을 추가했지만 암호 저장에 실패했습니다. 전체 추가 내용과 기존 수정은 편집 상태에 유지됩니다. 저장 상태를 확인하세요.";return saved;
        }
        catch(OperationCanceledException){return false;}
        catch{if(SameFileSession(active,epoch)&&!windowClosed&&!concealing)Notice.Text=(applied||beforeImport is not null&&active.Workspace.Notes.Any(n=>!beforeImport.Contains(n.Id)))?"Excel 행 추가 이후 처리 실패 — 추가 내용과 기존 수정의 편집 상태·암호 저장 상태를 확인하세요.":"Excel 가져오기 실패 — 시트·열 선택, 수식 없는 시트, 원본 변경 여부와 파일/행 한도를 확인하세요. 원본과 기존 메모는 보존했습니다.";return false;}
        finally{excelImportBusy=false;if(!windowClosed&&!concealing)try{ExcelImportButton.IsEnabled=session is{IsLocked:false};}catch{}}
    }
    private static Task<WorkbookImportCatalog> ReadExcelCatalogAsync(string path,CancellationToken token)=>Task.Run(()=>ExcelTextTransfer.Inspect(path,token),token);
    private static Task<ImportedWorkbookText> ReadSelectedExcelAsync(string path,WorkbookImportSelection selection,CancellationToken token)=>Task.Run(()=>selection.UseKnownTemplate?ExcelTextTransfer.ReadTemplateVerified(path,selection.ExpectedSourceSha256,token):ExcelTextTransfer.ReadSelected(path,selection,token),token);
    private static Task<ImportedWorkbookText> ReadExcelAsync(string path,CancellationToken token)=>Task.Run(()=>ExcelTextTransfer.Read(path,token),token);
}
