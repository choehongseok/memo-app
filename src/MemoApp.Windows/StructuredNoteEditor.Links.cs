using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using MemoApp.Core.Transfer;
namespace MemoApp.Windows;
public sealed partial class StructuredNoteEditor
{
    private void LinkClick(object sender,MouseButtonEventArgs e)
    {
        if((Keyboard.Modifiers&ModifierKeys.Control)==0)return;
        try{var position=RichInput.GetPositionFromPoint(e.GetPosition(RichInput),true);if(position is not null){e.Handled=true;OpenLinkWithConfirmation(position);}}catch{}
    }
    private void OpenSelectedLinkWithConfirmation(){try{OpenLinkWithConfirmation(RichInput.Selection.Start);}catch{}}
    private void OpenLinkWithConfirmation(TextPointer position)=>OpenLink(position,url=>MessageBox.Show(Window.GetWindow(this),$"다음 주소를 기본 브라우저로 열까요?\n{url}\n외부 프로그램과 사이트에 주소가 전달됩니다.","선택 링크 열기",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes,url=>{using var process=Process.Start(new ProcessStartInfo{FileName=url,UseShellExecute=true});});
    internal bool OpenSelectedLink(Func<string,bool> confirm,Action<string> launch){try{return OpenLink(RichInput.Selection.Start,confirm,launch);}catch{return false;}}
    private bool OpenLink(TextPointer position,Func<string,bool> confirm,Action<string> launch)
    {
        try
        {
            if(!Live()||!editable||composing||rebuilding||committing||refreshPending)return false;
            var target=note!;var owner=workspace!;var source=target.Document;long version=target.EditVersion,generation=projectionGeneration;var nativeDocument=RichInput.Document;var selectionStart=RichInput.Selection.Start;var selectionEnd=RichInput.Selection.End;
            bool Same()=>Live()&&editable&&!composing&&!rebuilding&&!committing&&!refreshPending&&ReferenceEquals(note,target)&&ReferenceEquals(workspace,owner)&&ReferenceEquals(target.Document,source)&&target.EditVersion==version&&projectionGeneration==generation&&ReferenceEquals(RichInput.Document,nativeDocument)&&RichInput.Selection.Start.CompareTo(selectionStart)==0&&RichInput.Selection.End.CompareTo(selectionEnd)==0;
            if(!Same()||position.Parent is not FrameworkContentElement element)return false;
            if(element.GetValue(LinkProperty) is not string link||!Same())return false;
            string url=UserLinkTarget.Parse(link);if(!Same()||!confirm(url)||!Same())return false;
            launch(url);return true;
        }
        catch{try{if(Live())notice?.Invoke("링크를 열지 못했습니다. 원본 주소와 메모를 보존했습니다.");}catch{}return false;}
    }
}
