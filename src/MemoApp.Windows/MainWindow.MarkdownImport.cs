using System.Windows;
using MemoApp.Core.Transfer;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private bool markdownImportBusy;
    private async void MarkdownImport_Click(object sender,RoutedEventArgs e)=>await ImportMarkdownAsync(
        ()=>{var dialog=new OpenFileDialog{Filter="Markdown 원문 (*.md)|*.md",CheckFileExists=true,Multiselect=false};return dialog.ShowDialog(this)==true?dialog.FileName:null;},
        ()=>MessageBox.Show(this,"선택한 UTF8 .md 파일 한 개를 새 Markdown 메모로 추가합니다. Obsidian의 내부 링크·첨부·플러그인·설정·그래프 관계를 이전하지 않으며 확장 문법은 원문으로 보존합니다. HTML·이미지·외부 링크는 자동 실행하지 않습니다. 기존 메모와 원본 파일은 보존합니다. 추가할까요?","Markdown 원문 가져오기",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes);
    internal async Task<bool> ImportMarkdownAsync(Func<string?> choose,Func<bool> confirm)
    {
        Dispatcher.VerifyAccess();if(markdownImportBusy||session is not{IsLocked:false} active||windowClosed||concealing)return false;
        long epoch=uiEpoch,sourceEpoch=active.AttachmentPreviewEpoch,selectionGeneration=searchPreviewGeneration;Guid? folder=(FolderFilter.SelectedItem as FolderChoice)?.Id;var token=fileOperations.Token;bool applied=false;HashSet<Guid>? beforeImport=null;
        bool Live()=>!windowClosed&&!concealing&&SameFileSession(active,epoch)&&!token.IsCancellationRequested;
        bool Current()=>Live()&&sourceEpoch==active.AttachmentPreviewEpoch&&selectionGeneration==searchPreviewGeneration&&(FolderFilter.SelectedItem as FolderChoice)?.Id==folder&&(folder is null||active.Workspace.Folders.Any(f=>f.FolderId==folder));
        markdownImportBusy=true;
        try
        {
            MarkdownImportButton.IsEnabled=false;if(!Current())return false;string? path=choose();if(path is null||!Current())return false;
            var imported=await ReadMarkdownFileAsync(path,token);if(!Current()||!confirm()||!Current())return false;
            beforeImport=active.Workspace.Notes.Select(n=>n.Id).ToHashSet();var added=active.Workspace.ImportMarkdown(imported.Title,imported.Text,folder);applied=true;if(!Live())return false;
            bool saved=await active.SaveAsync();if(!Live())return false;RefreshNotes(added);if(!Live())return false;
            Notice.Text=saved?"Markdown 원문을 새 메모로 추가하고 암호 저장했습니다. 원본 파일과 기존 메모는 보존했습니다. 확장 문법·첨부 관계의 지원 범위는 원문 가져오기 안내를 따릅니다.":"Markdown 원문을 추가했지만 암호 저장에 실패했습니다. 새 메모와 기존 수정 전체는 편집 상태에 유지됩니다. 저장 상태를 확인하세요.";return saved&&Live();
        }
        catch(OperationCanceledException){return false;}
        catch
        {
            if(Live())try{Notice.Text=(applied||beforeImport is not null&&active.Workspace.Notes.Any(n=>!beforeImport.Contains(n.Id)))?"Markdown 메모 추가 이후 처리 실패 — 추가 원문과 기존 수정의 편집·암호 저장 상태를 확인하세요.":"Markdown 가져오기 실패 — UTF8 .md·로컬 일반 파일·본문/전체 자료 한도를 확인하세요. 원본과 기존 메모는 보존했습니다.";}catch{}return false;
        }
        finally{markdownImportBusy=false;if(!windowClosed&&!concealing)try{MarkdownImportButton.IsEnabled=session is{IsLocked:false};}catch{}}
    }
    private static Task<ImportedText> ReadMarkdownFileAsync(string path,CancellationToken token)=>Task.Run(()=>MarkdownFileTransfer.Read(path,token),token);
}
