using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using Microsoft.Win32;
namespace MemoApp.Windows;
public sealed partial class AttachmentPanel
{
    public Button OpenButton{get;}=new(){Content="선택 첨부 외부 앱으로 열기",Padding=new(6,3,6,3),Margin=new(6,0,0,0),IsEnabled=false};
    private async void OpenClicked(object sender,RoutedEventArgs e)
    {
        await OpenSelectedAsync(()=>
        {
            var owner=Window.GetWindow(this);if(owner is null)return false;
            string name=Current()&&FilesList.SelectedItem is Entry entry?session!.Workspace.DescribeAttachments(note!).Single(a=>a.Id==entry.Id).Name:"";
            return MessageBox.Show(owner,$"선택 첨부: {name}\n\n원본을 복호화한 평문 사본을 지정한 새 파일에 저장하고 외부 앱으로 엽니다. 이 사본과 외부 앱의 캐시는 메모앱 잠금 이후에도 남습니다. 외부 프로그램은 파일의 명령·매크로를 실행하거나 네트워크에 접근할 수 있습니다. 취소·실패·잠금 중 만들어진 사본도 자동 삭제하지 않습니다. 계속할까요?","첨부 외부 열기",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
        },()=>
        {
            var owner=Window.GetWindow(this);if(owner is null)return null;
            string name=Current()&&FilesList.SelectedItem is Entry entry?session!.Workspace.DescribeAttachments(note!).Single(a=>a.Id==entry.Id).Name:"attachment.bin";
            var dialog=new SaveFileDialog{Filter="평문 첨부 사본|*.*",FileName=name,OverwritePrompt=true,CheckPathExists=true,AddExtension=false};return dialog.ShowDialog(owner)==true?dialog.FileName:null;
        },path=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true,Verb="open"}));
    }
    private void AttachmentDoubleClick(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject origin&&ItemsControl.ContainerFromElement(FilesList,origin) is ListBoxItem{IsSelected:true}){e.Handled=true;OpenClicked(sender,e);}
    }
    public async Task<bool> OpenSelectedAsync(Func<bool> confirm,Func<string?> choose,Action<string> launch,IAtomicVaultFiles? files=null)
    {
        Dispatcher.VerifyAccess();if(!Current()||busy||FilesList.SelectedItem is not Entry selected)return false;
        var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch,generation=previewGeneration;var token=cancellation.Token;AttachmentReadLease? lease=null;bool copied=false;string? path=null;busy=true;
        bool Allowed()=>Same(active,source,version)&&!token.IsCancellationRequested&&previewGeneration==generation&&FilesList.SelectedItem is Entry item&&item.Id==selected.Id&&active.IsAttachmentPreviewCurrent(source,selected.Id,version,epoch);
        try
        {
            OpenButton.IsEnabled=false;if(!Allowed()||!confirm()||!Allowed())return false;path=choose();if(path is null||!Allowed())return false;
            string protectedRoot=active.AttachmentExportRoot(source,selected.Id,version,epoch);path=AttachmentFileTransfer.ResolveDestination(path,protectedRoot);if(!Allowed())return false;
            lease=active.CreateAttachmentReadLease(source,selected.Id,version);if(!Allowed())return false;
            copied=await WriteAttachmentCopyAsync(lease,path,protectedRoot,token,files);lease.Dispose();lease=null;if(!copied||!Allowed())return false;
            path=AttachmentFileTransfer.ValidateLaunchFile(path,protectedRoot);if(!Allowed())return false;
            Report("평문 첨부 사본을 저장했습니다. 외부 앱을 열며 이 사본과 외부 앱 캐시는 앱 잠금 후에도 남습니다.");if(!Allowed())return false;
            launch(path);return Allowed(); // Launch initiation cannot be retracted by a later lock.
        }
        catch(OperationCanceledException){return false;}
        catch{try{Report(copied?"외부 열기 실패 — 저장한 평문 첨부 사본은 남습니다. 원본 암호 첨부는 보존했습니다.":"첨부 열기 실패 — 만들기 시작한 평문 사본/일부 파일은 남을 수 있습니다. 기존 파일과 원본 암호 첨부는 보존했습니다.");}catch{}return false;}
        finally{lease?.Dispose();busy=false;if(!IsDisposed)try{Refresh();}catch{}}
    }
    private static Task<bool> WriteAttachmentCopyAsync(AttachmentReadLease lease,string path,string protectedRoot,CancellationToken token,IAtomicVaultFiles? files)=>Task.Run(()=>AttachmentFileTransfer.Write(lease,path,protectedRoot,token,files),token);
}
