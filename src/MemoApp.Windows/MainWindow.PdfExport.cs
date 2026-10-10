using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private bool pdfExportBusy;
    private async void PdfExport_Click(object sender,RoutedEventArgs e)=>await ExportSelectedPdfWithModeAsync(ChoosePdfMode,ConfirmTextPdf,ConfirmVisualPdf,ChoosePdfDestination);
    internal async Task<bool> ExportSelectedPdfAsync(Func<bool> confirm,Func<string?> choose,IAtomicVaultFiles? files=null)
    {
        Dispatcher.VerifyAccess();if(windowClosed||concealing||pdfExportBusy||CaptureBatch(false) is not{ } request)return false;
        long sourceEpoch=request.Session.AttachmentPreviewEpoch;var versions=request.Notes.Select(n=>n.EditVersion).ToArray();
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(fileOperations.Token);var token=operation.Token;var workspace=request.Session.Workspace;
        bool Current()=>!token.IsCancellationRequested&&!windowClosed&&!concealing&&CurrentBatch(request)&&sourceEpoch==request.Session.AttachmentPreviewEpoch&&request.Notes.Where((n,i)=>n.EditVersion!=versions[i]).Any()==false&&NotesList.SelectedItems.Count==request.Notes.Length&&request.Notes.All(n=>NotesList.SelectedItems.Contains(n));
        void Revoke()=>operation.Cancel();
        // Cancellation is monotonic: restoring the original selection cannot restore this operation.
        void SelectionChanged(object sender,SelectionChangedEventArgs e)=>Revoke();
        void SourceChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(NoteDraft.EditVersion))Revoke();}
        void SourceInvalidating(NoteDraft? source)=>Revoke(); // Includes same-version AcceptPrepared, before publication.
        void SessionChanged(){if(!Current())Revoke();}
        void WindowClosed(object? sender,EventArgs e)=>Revoke();
        pdfExportBusy=true;
        try
        {
            NotesList.SelectionChanged+=SelectionChanged;foreach(var note in request.Notes)note.PropertyChanged+=SourceChanged;
            workspace.AttachmentReadInvalidating+=SourceInvalidating;request.Session.Changed+=SessionChanged;request.Session.Conceal+=Revoke;Closed+=WindowClosed;
            UpdateSelectedActions();if(!Current()||!confirm()||!Current())return false;string? destination=choose();if(destination is null||!Current())return false;
            if(!Path.GetExtension(destination).Equals(".pdf",StringComparison.OrdinalIgnoreCase))throw new IOException("PDF extension mismatch");
            string relative=Path.GetRelativePath(Path.GetFullPath(root),Path.GetFullPath(destination));if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Plaintext PDF inside active vault refused");
            using var prepared=PdfTextTransfer.Capture(request.Notes,token);if(!Current()||token.IsCancellationRequested)return false;await WritePdfExportAsync(prepared,destination,token,files);
            if(!Current())return false;Notice.Text=$"{request.Notes.Length}개 메모의 제목·본문을 평문 PDF로 복사했습니다. 서식 원문·첨부는 전체 암호 이전으로 보존하세요.";return Current();
        }
        catch(OperationCanceledException){return false;}
        catch{if(Current())try{Notice.Text="PDF 내보내기 실패 — 원본과 기존 파일은 보존했습니다. 미지원 글자·문자 조합·쪽/파일 한도·새 이름·외부 로컬 폴더를 확인하세요. 실패한 부분 평문 파일이 남을 수 있습니다.";}catch{}return false;}
        finally
        {
            NotesList.SelectionChanged-=SelectionChanged;foreach(var note in request.Notes)note.PropertyChanged-=SourceChanged;
            workspace.AttachmentReadInvalidating-=SourceInvalidating;request.Session.Changed-=SessionChanged;request.Session.Conceal-=Revoke;Closed-=WindowClosed;
            pdfExportBusy=false;if(!windowClosed)try{UpdateSelectedActions();}catch{}
        }
    }
    private static Task WritePdfExportAsync(PreparedTextExport prepared,string destination,CancellationToken token,IAtomicVaultFiles? files)=>Task.Run(()=>TextTransfer.WritePrepared(prepared,destination,token,files),token);
}
