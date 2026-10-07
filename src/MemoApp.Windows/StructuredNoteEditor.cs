using System.Windows.Controls;
using System.Windows;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
namespace MemoApp.Windows;
public sealed class StructuredNoteEditor:UserControl,IDisposable
{
    public RichTextBox RichInput{get;}=new(){AllowDrop=false,IsUndoEnabled=true};
    public StructuredNoteEditor(EditingWorkspace workspace,NoteDraft note,Func<bool> current,Action<string> notice)=>throw new NotImplementedException();
    public void ApplyBold()=>throw new NotImplementedException();
    public void ApplyFontSize(double size)=>throw new NotImplementedException();
    public void ApplyPreferences(UiPreferences preferences)=>throw new NotImplementedException();
    public void PasteData(IDataObject data)=>throw new NotImplementedException();
    public void ClearSensitive()=>throw new NotImplementedException();
    public void Dispose()=>throw new NotImplementedException();
}
