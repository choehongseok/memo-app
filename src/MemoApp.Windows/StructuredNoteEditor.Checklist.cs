using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
namespace MemoApp.Windows;
public sealed partial class StructuredNoteEditor
{
    private bool checklistEnterActive;
    private void EnterChecklistItem(ListItem originalItem,List list)
    {
        bool began=false;
        try
        {
            if(!Live()||!editable||composing||rebuilding||committing||refreshPending||!RichInput.Selection.IsEmpty)return;
            var target=note!;var source=target.Document;long version=target.EditVersion,generation=projectionGeneration;var document=RichInput.Document;
            bool Same()=>Live()&&editable&&!composing&&!rebuilding&&!committing&&!refreshPending&&ReferenceEquals(note,target)&&ReferenceEquals(target.Document,source)&&target.EditVersion==version&&projectionGeneration==generation&&ReferenceEquals(RichInput.Document,document);
            var original=OnlyParagraph(originalItem.Blocks);
            if(!Same()||original.Inlines.FirstInline is not Run prefix||!(bool)prefix.GetValue(ChecklistPrefixProperty)||prefix.Text is not("[ ] " or "[x] ")||RichInput.CaretPosition.CompareTo(prefix.ContentEnd)<0)return;
            RichInput.BeginChange();began=true;checklistEnterActive=true;
            if(!Same())return;
            // Keep the native split and its Undo unit, then add only our own new unchecked scaffold.
            EditingCommands.EnterParagraphBreak.Execute(null,RichInput);
            if(!Same())return;
            var paragraph=RichInput.CaretPosition.Paragraph;
            if(paragraph is null||ReferenceEquals(paragraph,original))throw new InvalidDataException("Checklist native split missing");
            if(ReferenceEquals(paragraph.Parent,originalItem)&&originalItem.Blocks.Count==2)
            {
                originalItem.Blocks.Remove(paragraph);var created=new ListItem(paragraph);list.ListItems.InsertAfter(originalItem,created);
            }
            if(paragraph.Parent is not ListItem item||item.Parent!=list||item.Blocks.Count!=1||!Same())throw new InvalidDataException("Checklist native split ownership changed");
            var scaffold=new Run("[ ] ");scaffold.SetValue(ChecklistPrefixProperty,true);
            if(paragraph.Inlines.FirstInline is Inline first)paragraph.Inlines.InsertBefore(first,scaffold);else paragraph.Inlines.Add(scaffold);
            if(!Same())return;
            paragraph.Margin=new Thickness(0);paragraph.TextAlignment=TextAlignment.Left;paragraph.TextIndent=0;
            if(Same())RichInput.CaretPosition=scaffold.ContentEnd;
        }
        catch{try{if(Live())Rebuild();else ClearSensitive();}catch{}}
        finally
        {
            try{if(began)RichInput.EndChange();}catch{try{if(Live())Rebuild();else ClearSensitive();}catch{}}
            checklistEnterActive=false;
        }
    }
}
