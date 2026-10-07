using System.Windows.Controls;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
namespace MemoApp.Windows;
public sealed class MarkdownNotePreview:UserControl,IDisposable
{
    public MarkdownNotePreview(NoteDraft note,Func<bool> current,Func<string,MarkdownPreview>? parse=null){throw new NotImplementedException("Readonly Markdown preview red-first");}
    public Task WhenIdle=>Task.CompletedTask;
    public void Dispose(){}
}
