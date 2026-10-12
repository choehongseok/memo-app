using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using Microsoft.Win32;

namespace MemoApp.Windows;

public partial class MainWindow
{
    private async Task<bool> ExportSelectedPdfWithModeAsync(Func<PdfExportMode?> chooseMode,Func<bool> confirmText,Func<bool> confirmVisual,Func<string?> choose,IAtomicVaultFiles? files=null)
    {
        Dispatcher.VerifyAccess();if(windowClosed||concealing||pdfExportBusy||CaptureBatch(false) is not{ } request)return false;
        using var authority=new PdfOperationAuthority(this,request);pdfExportBusy=true;bool textOwnsAdmission=false;
        try
        {
            UpdateSelectedActions();if(!authority.Current())return false;PdfExportMode? mode=chooseMode();if(mode is null||!authority.Current())return false;
            if(mode==PdfExportMode.Visual)return await ExportSelectedVisualPdfCoreAsync(request,authority,confirmVisual,choose,files);
            if(mode!=PdfExportMode.Text)return false;
            // The existing Text route synchronously acquires the slot before its first native setter.
            // Its finalizer owns release: a native enable callback may already start the next operation.
            textOwnsAdmission=true;pdfExportBusy=false;
            bool result=await ExportSelectedPdfAsync(confirmText,choose,files);
            return result&&authority.Current();
        }
        catch(OperationCanceledException){return false;}
        catch{return false;}
        finally{if(!textOwnsAdmission){pdfExportBusy=false;if(!windowClosed)try{UpdateSelectedActions();}catch{}}}
    }

    internal async Task<bool> ExportSelectedVisualPdfAsync(Func<bool> confirm,Func<string?> choose,IAtomicVaultFiles? files=null)
    {
        Dispatcher.VerifyAccess();if(windowClosed||concealing||pdfExportBusy||CaptureBatch(false) is not{ } request)return false;
        using var authority=new PdfOperationAuthority(this,request);pdfExportBusy=true;
        try{return await ExportSelectedVisualPdfCoreAsync(request,authority,confirm,choose,files);}
        finally{pdfExportBusy=false;if(!windowClosed)try{UpdateSelectedActions();}catch{}}
    }

    private async Task<bool> ExportSelectedVisualPdfCoreAsync(BatchRequest request,PdfOperationAuthority authority,Func<bool> confirm,Func<string?> choose,IAtomicVaultFiles? files)
    {
        try
        {
            UpdateSelectedActions();if(!authority.Current()||!confirm()||!authority.Current())return false;
            string? destination=choose();if(destination is null||!authority.Current())return false;
            if(!Path.GetExtension(destination).Equals(".pdf",StringComparison.OrdinalIgnoreCase))throw new IOException("PDF extension mismatch");
            string relative=Path.GetRelativePath(Path.GetFullPath(root),Path.GetFullPath(destination));if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Plaintext PDF inside active vault refused");
            PreparedTextExport prepared;
            if(request.Notes.All(note=>note.Mode=="plain"))
            {
                var sources=PdfVisualRenderer.CapturePlainSources(request.Notes,authority.Token);if(!authority.Current())return false;
                prepared=await PdfVisualRenderer.RenderPlainAsync(sources,authority.Current,authority.Token);
            }
            else
            {
                // Complete selection preflight and exact immutable raw/style capture under the original authority.
                var sources=RichParagraphPdfCapture.Capture(request.Notes,authority.Token);if(!authority.Current())return false;
                prepared=await PdfVisualRenderer.RenderParagraphsAsync(sources,authority.Current,authority.Token);
            }
            using(prepared)
            {
                if(!authority.Current())return false;
                // Await actual file settlement; cancellation alone cannot free the slot or dispose its bytes.
                await WriteVisualPdfExportAsync(prepared,destination,authority.Token,files);
                if(!authority.Current())return false;Notice.Text="표시용 PDF를 저장했습니다. 이 PDF에는 검색·복사용 텍스트가 없습니다.";return authority.Current();
            }
        }
        catch(OperationCanceledException){return false;}
        catch
        {
            if(authority.Current())try{Notice.Text="표시용 PDF 내보내기 실패 — 원본과 기존 파일은 보존했습니다. plain 또는 지원 rich v1 문단 서식·지원 문자/글꼴·쪽/파일 한도·새 이름·외부 로컬 폴더를 확인하세요. 미지원 메모가 있으면 선택 전체를 거절합니다. 실패한 부분 평문 파일이 남을 수 있습니다.";}catch{}
            return false;
        }
    }

    private static Task WriteVisualPdfExportAsync(PreparedTextExport prepared,string destination,CancellationToken token,IAtomicVaultFiles? files)=>Task.Run(()=>TextTransfer.WritePrepared(prepared,destination,token,files),token);

    private PdfExportMode? ChoosePdfMode()
    {
        using var dialog=new PdfExportModeChoiceWindow(()=>!windowClosed&&!concealing&&session is{IsLocked:false}){Owner=this};
        return dialog.ShowDialog()==true?dialog.SelectedMode:null;
    }
    private bool ConfirmTextPdf()=>MessageBox.Show(this,"선택 메모의 제목·검증된 본문을 평문 PDF로 복사합니다. 고정 글꼴의 한글·일부 한자·영문 등을 지원하며 미지원 글자·이모지·복잡한 문자 조합은 거절합니다. 서식·첨부는 포함하지 않고 Markdown은 원문 문자로 표시합니다. 100개/256쪽/16 MiB 한도이며 탭·줄은 읽기용 배치로 바뀝니다. 원본 보존에는 전체 암호 이전을 사용하세요. PDF와 실패한 부분 파일은 앱 잠금 후에도 남습니다. 평문 파일을 만들까요?","선택 메모 PDF 내보내기",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
    private bool ConfirmVisualPdf()=>MessageBox.Show(this,"선택한 plain 메모 또는 지원 rich v1 문단의 제목·본문을 표시용 평문 PDF로 복사합니다. 굵게·밑줄·취소선·고정 글꼴/크기·글자/배경색을 지원합니다. 목록·체크리스트·표·rich v2·Markdown·미지원 서식/문자/글꼴이 하나라도 있으면 선택 전체를 거절하며 첨부는 포함하지 않습니다. 페이지 이미지에는 텍스트 검색·복사용 텍스트가 없으며 외부 OCR로 읽힐 수 있습니다. A4 96 DPI 고정 출력으로 확대 품질·컬러 이모지 외형은 보장하지 않습니다. 줄·탭은 읽기용 배치로 바뀌며 100개/256쪽/16 MiB 한도입니다. PDF와 실패한 부분 파일은 앱 잠금 후에도 남습니다. 앱의 native/GC 메모리 사본을 완전히 지우는 기능은 아닙니다. 평문 파일을 만들까요?","선택 메모 표시용 PDF 내보내기",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
    private string? ChoosePdfDestination()
    {
        var picker=new SaveFileDialog{Filter="제목·본문 평문 PDF|*.pdf",DefaultExt=".pdf",FileName="selected-notes.pdf",OverwritePrompt=true};return picker.ShowDialog(this)==true?picker.FileName:null;
    }

    private sealed class PdfOperationAuthority:IDisposable
    {
        private readonly MainWindow owner;private readonly BatchRequest request;private readonly EditingWorkspace workspace;private readonly long sourceEpoch;private readonly long[] versions;private readonly CancellationTokenSource operation;
        internal PdfOperationAuthority(MainWindow owner,BatchRequest request)
        {
            this.owner=owner;this.request=request;workspace=request.Session.Workspace;sourceEpoch=request.Session.AttachmentPreviewEpoch;versions=request.Notes.Select(n=>n.EditVersion).ToArray();operation=CancellationTokenSource.CreateLinkedTokenSource(owner.fileOperations.Token);
            owner.NotesList.SelectionChanged+=SelectionChanged;foreach(var note in request.Notes)note.PropertyChanged+=SourceChanged;
            workspace.AttachmentReadInvalidating+=SourceInvalidating;request.Session.Changed+=SessionChanged;request.Session.Conceal+=Revoke;owner.Closed+=WindowClosed;
        }
        internal CancellationToken Token=>operation.Token;
        internal bool Current()
        {
            if(operation.IsCancellationRequested||owner.windowClosed||owner.concealing||!ReferenceEquals(owner.session,request.Session)||!ReferenceEquals(request.Session.Workspace,workspace)||!owner.CurrentBatch(request)||sourceEpoch!=request.Session.AttachmentPreviewEpoch||owner.NotesList.SelectedItems.Count!=request.Notes.Length)return false;
            for(int index=0;index<request.Notes.Length;index++)
            {
                var note=request.Notes[index];
                if(!ReferenceEquals(owner.NotesList.SelectedItems[index],note)||note.EditVersion!=versions[index]||!workspace.Notes.Any(current=>ReferenceEquals(current,note)))return false;
            }
            return true;
        }
        private void Revoke()=>operation.Cancel();
        private void SelectionChanged(object sender,SelectionChangedEventArgs e)=>Revoke();
        private void SourceChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(NoteDraft.EditVersion))Revoke();}
        private void SourceInvalidating(NoteDraft? source)=>Revoke();
        private void SessionChanged(){if(!Current())Revoke();}
        private void WindowClosed(object? sender,EventArgs e)=>Revoke();
        public void Dispose()
        {
            owner.NotesList.SelectionChanged-=SelectionChanged;foreach(var note in request.Notes)note.PropertyChanged-=SourceChanged;
            workspace.AttachmentReadInvalidating-=SourceInvalidating;request.Session.Changed-=SessionChanged;request.Session.Conceal-=Revoke;owner.Closed-=WindowClosed;operation.Dispose();
        }
    }
}
