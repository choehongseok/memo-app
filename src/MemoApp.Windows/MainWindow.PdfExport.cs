using System.IO;
using System.Windows;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private bool pdfExportBusy;
    private async void PdfExport_Click(object sender,RoutedEventArgs e)=>await ExportSelectedPdfAsync(
        ()=>MessageBox.Show(this,"선택 메모의 제목·검증된 본문을 평문 PDF로 복사합니다. 고정 글꼴의 한글·일부 한자·영문 등을 지원하며 미지원 글자·이모지·복잡한 문자 조합은 거절합니다. 서식·첨부는 포함하지 않고 Markdown은 원문 문자로 표시합니다. 100개/256쪽/16 MiB 한도이며 탭·줄은 읽기용 배치로 바뀝니다. 원본 보존에는 전체 암호 이전을 사용하세요. PDF와 실패한 부분 파일은 앱 잠금 후에도 남습니다. 평문 파일을 만들까요?","선택 메모 PDF 내보내기",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes,
        ()=>{var picker=new SaveFileDialog{Filter="제목·본문 평문 PDF|*.pdf",DefaultExt=".pdf",FileName="selected-notes.pdf",OverwritePrompt=true};return picker.ShowDialog(this)==true?picker.FileName:null;});
    internal async Task<bool> ExportSelectedPdfAsync(Func<bool> confirm,Func<string?> choose,IAtomicVaultFiles? files=null)
    {
        Dispatcher.VerifyAccess();if(pdfExportBusy||CaptureBatch(false) is not{ } request)return false;
        long sourceEpoch=request.Session.AttachmentPreviewEpoch;var versions=request.Notes.Select(n=>n.EditVersion).ToArray();var token=fileOperations.Token;
        bool Current()=>CurrentBatch(request)&&sourceEpoch==request.Session.AttachmentPreviewEpoch&&request.Notes.Where((n,i)=>n.EditVersion!=versions[i]).Any()==false&&NotesList.SelectedItems.Count==request.Notes.Length&&request.Notes.All(n=>NotesList.SelectedItems.Contains(n));
        pdfExportBusy=true;
        try
        {
            UpdateSelectedActions();if(!Current()||!confirm()||!Current())return false;string? destination=choose();if(destination is null||!Current())return false;
            if(!Path.GetExtension(destination).Equals(".pdf",StringComparison.OrdinalIgnoreCase))throw new IOException("PDF extension mismatch");
            string relative=Path.GetRelativePath(Path.GetFullPath(root),Path.GetFullPath(destination));if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Plaintext PDF inside active vault refused");
            using var prepared=PdfTextTransfer.Capture(request.Notes,token);if(!Current()||token.IsCancellationRequested)return false;await WritePdfExportAsync(prepared,destination,token,files);
            if(!Current())return false;Notice.Text=$"{request.Notes.Length}개 메모의 제목·본문을 평문 PDF로 복사했습니다. 서식 원문·첨부는 전체 암호 이전으로 보존하세요.";return Current();
        }
        catch(OperationCanceledException){return false;}
        catch{if(SameFileSession(request.Session,request.Epoch))try{Notice.Text="PDF 내보내기 실패 — 원본과 기존 파일은 보존했습니다. 미지원 글자·문자 조합·쪽/파일 한도·새 이름·외부 로컬 폴더를 확인하세요. 실패한 부분 평문 파일이 남을 수 있습니다.";}catch{}return false;}
        finally{pdfExportBusy=false;if(!windowClosed)try{UpdateSelectedActions();}catch{}}
    }
    private static Task WritePdfExportAsync(PreparedTextExport prepared,string destination,CancellationToken token,IAtomicVaultFiles? files)=>Task.Run(()=>TextTransfer.WritePrepared(prepared,destination,token,files),token);
}
