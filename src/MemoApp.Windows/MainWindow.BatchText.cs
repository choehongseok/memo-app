using System.IO;
using System.Windows;
using MemoApp.Core.Transfer;
using MemoApp.Core.Storage;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private bool batchTextBusy;
    private async void BatchTxtExport_Click(object sender,RoutedEventArgs e)
    {
        await ExportSelectedTextAsync(()=>MessageBox.Show(this,"선택 메모의 제목·본문을 각각 암호화되지 않은 TXT 파일로 복사합니다. 서식과 첨부 원본은 포함하지 않습니다. 앱 잠금 후에도 저장된 평문이나 실패한 부분 파일은 남습니다. 새 평문 파일을 만들까요?","선택 메모 평문 내보내기",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes,
            ()=>{var dialog=new OpenFolderDialog{Title="새 TXT 파일을 만들 기존 로컬 폴더 선택",Multiselect=false};return dialog.ShowDialog(this)==true?dialog.FolderName:null;});
    }
    internal async Task<bool> ExportSelectedTextAsync(Func<bool> confirm,Func<string?> choose,IAtomicVaultFiles? files=null)
    {
        Dispatcher.VerifyAccess();if(batchTextBusy||CaptureBatch(false) is not { } request)return false;
        var versions=request.Notes.Select(n=>n.EditVersion).ToArray();var token=fileOperations.Token;
        bool Allowed()=>CurrentBatch(request)&&request.Notes.Where((n,i)=>n.EditVersion!=versions[i]).Any()==false&&NotesList.SelectedItems.Count==request.Notes.Length&&request.Notes.All(n=>NotesList.SelectedItems.Contains(n));
        batchTextBusy=true;
        try
        {
            UpdateSelectedActions();
            if(!Allowed()||!confirm()||!Allowed())return false;
            string? directory=choose();if(directory is null||!Allowed())return false;
            string relative=Path.GetRelativePath(Path.GetFullPath(root),Path.GetFullPath(directory));
            if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Plaintext directory inside active vault refused");
            using var prepared=BatchTextTransfer.Capture(request.Notes);
            if(!Allowed()||token.IsCancellationRequested)return false;
            await WriteBatchTextAsync(prepared,directory,token,files);
            if(!SameFileSession(request.Session,request.Epoch))return false;
            Notice.Text=$"{request.Notes.Length}개 메모의 제목·본문을 새 TXT로 복사했습니다. 평문 파일은 앱 잠금으로 보호되지 않습니다.";return true;
        }
        catch(OperationCanceledException){return false;}
        catch{if(SameFileSession(request.Session,request.Epoch))Notice.Text="일괄 TXT 내보내기 실패 — 기존 파일은 덮어쓰지 않았습니다. 완료된 파일이나 실패한 부분 평문 파일이 선택 폴더에 남을 수 있으니 확인하세요.";return false;}
        finally{batchTextBusy=false;if(!windowClosed)try{UpdateSelectedActions();}catch{}}
    }
    // Worker has prepared bytes, destination, token and a file adapter (null in production); no UI/key owner.
    private static Task WriteBatchTextAsync(PreparedTextBatch prepared,string directory,CancellationToken token,IAtomicVaultFiles? files)=>Task.Run(()=>BatchTextTransfer.WritePrepared(prepared,directory,token,files),token);
}
