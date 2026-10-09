using System.IO;
using System.Windows;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private bool officeExportBusy;
    private async void ExcelExport_Click(object sender,RoutedEventArgs e)=>await ChooseOfficeExportAsync(OfficeTextFormat.Spreadsheet);
    private async void WordExport_Click(object sender,RoutedEventArgs e)=>await ChooseOfficeExportAsync(OfficeTextFormat.Word);
    private Task<bool> ChooseOfficeExportAsync(OfficeTextFormat format)=>ExportSelectedOfficeAsync(format,
        ()=>MessageBox.Show(this,"선택 메모의 제목과 검증된 본문을 암호화되지 않은 Office 파일로 복사합니다. 문서 원본 서식·첨부는 포함하지 않습니다. Excel의 긴 본문은 세 열로 나눕니다. 앱 잠금 후에도 평문·실패한 부분 파일은 남습니다. 평문 파일을 만들까요?","선택 메모 평문 내보내기",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes,
        ()=>{bool sheet=format==OfficeTextFormat.Spreadsheet;var dialog=new SaveFileDialog{Filter=sheet?"Excel 제목·본문|*.xlsx":"Word 제목·본문|*.docx",DefaultExt=sheet?".xlsx":".docx",FileName=sheet?"selected-notes.xlsx":"selected-notes.docx",OverwritePrompt=true};return dialog.ShowDialog(this)==true?dialog.FileName:null;});
    internal async Task<bool> ExportSelectedOfficeAsync(OfficeTextFormat format,Func<bool> confirm,Func<string?> choose,IAtomicVaultFiles? files=null)
    {
        Dispatcher.VerifyAccess();if(officeExportBusy||!Enum.IsDefined(format)||CaptureBatch(false) is not{ } request)return false;
        long sourceEpoch=request.Session.AttachmentPreviewEpoch;var versions=request.Notes.Select(n=>n.EditVersion).ToArray();var token=fileOperations.Token;
        bool Current()=>CurrentBatch(request)&&sourceEpoch==request.Session.AttachmentPreviewEpoch&&request.Notes.Where((n,i)=>n.EditVersion!=versions[i]).Any()==false&&NotesList.SelectedItems.Count==request.Notes.Length&&request.Notes.All(n=>NotesList.SelectedItems.Contains(n));
        officeExportBusy=true;
        try
        {
            UpdateSelectedActions();if(!Current()||!confirm()||!Current())return false;string? destination=choose();if(destination is null||!Current())return false;
            if(!string.Equals(Path.GetExtension(destination),format==OfficeTextFormat.Spreadsheet?".xlsx":".docx",StringComparison.OrdinalIgnoreCase))throw new IOException("Office destination extension mismatch");
            string relative=Path.GetRelativePath(Path.GetFullPath(root),Path.GetFullPath(destination));if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Plaintext Office file inside active vault refused");
            using var prepared=OfficeTextTransfer.Capture(request.Notes,format);if(!Current()||token.IsCancellationRequested)return false;await WriteOfficeExportAsync(prepared,destination,token,files);
            if(!SameFileSession(request.Session,request.Epoch))return false;Notice.Text=$"{request.Notes.Length}개 메모의 제목·본문을 평문 Office 파일로 복사했습니다. 서식 원문·첨부 보존에는 전체 암호 이전 파일을 사용하세요.";return true;
        }
        catch(OperationCanceledException){return false;}
        catch{if(SameFileSession(request.Session,request.Epoch))Notice.Text="Office 내보내기 실패 — 원본과 기존 파일은 보존했습니다. 지원 문서·XML 제어문자·한도·새 이름·외부 로컬 폴더를 확인하세요. 실패한 부분 평문 파일이 남을 수 있습니다.";return false;}
        finally{officeExportBusy=false;if(!windowClosed)try{UpdateSelectedActions();}catch{}}
    }
    private static Task WriteOfficeExportAsync(PreparedTextExport prepared,string destination,CancellationToken token,IAtomicVaultFiles? files)=>Task.Run(()=>TextTransfer.WritePrepared(prepared,destination,token,files),token);
}
