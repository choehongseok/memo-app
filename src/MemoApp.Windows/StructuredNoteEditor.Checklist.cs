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
        bool began=false,prepared=false;Func<bool>? allowedUntilEnd=null;
        try
        {
            if(!Live()||!editable||composing||rebuilding||committing||refreshPending)return;
            var target=note!;var source=target.Document;long version=target.EditVersion,generation=projectionGeneration;var document=RichInput.Document;
            bool Same()=>Live()&&editable&&!composing&&!rebuilding&&!committing&&!refreshPending&&ReferenceEquals(note,target)&&ReferenceEquals(target.Document,source)&&target.EditVersion==version&&projectionGeneration==generation&&ReferenceEquals(RichInput.Document,document);
            allowedUntilEnd=Same;
            var original=OnlyParagraph(originalItem.Blocks);
            if(!Same())return;
            if(original.Inlines.FirstInline is not Run prefix||!(bool)prefix.GetValue(ChecklistPrefixProperty)||prefix.Text is not("[ ] " or "[x] ")||
                RichInput.Selection.Start.Paragraph!=original||RichInput.Selection.End.Paragraph!=original||RichInput.Selection.Start.CompareTo(prefix.ContentEnd)<0)
            {
                notice?.Invoke("체크박스 표시나 여러 항목을 포함한 선택에서는 Enter를 적용하지 않습니다. 한 항목의 본문을 선택하세요.");return;
            }
            int count=0;bool empty=ReadRuns(original,ref count,prefix).All(run=>run!["text"]!.GetValue<string>().Length==0);
            RichInput.BeginChange();began=true;checklistEnterActive=true;
            if(!Same())return;
            if(empty)
            {
                // Our visible scaffold makes WPF's item nonempty. Remove only that scaffold
                // and move its paragraph outside the list within this native Undo unit.
                if(list.Parent is not FlowDocument owner||!ReferenceEquals(owner,document))throw new InvalidDataException("Checklist exit ownership changed");
                var following=list.ListItems.SkipWhile(item=>!ReferenceEquals(item,originalItem)).Skip(1).ToArray();
                List? tail=null;
                if(following.Length>0)
                {
                    tail=new List{MarkerStyle=TextMarkerStyle.None,Margin=list.Margin};tail.SetValue(ChecklistProperty,true);
                    foreach(var sibling in following){list.ListItems.Remove(sibling);tail.ListItems.Add(sibling);}
                }
                original.Inlines.Remove(prefix);originalItem.Blocks.Remove(original);list.ListItems.Remove(originalItem);
                owner.Blocks.InsertAfter(list,original);if(tail is not null)owner.Blocks.InsertAfter(original,tail);
                if(list.ListItems.Count==0)owner.Blocks.Remove(list);
                if(Same()){RichInput.CaretPosition=original.ContentStart;prepared=true;}
                return;
            }
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
            if(Same()){RichInput.CaretPosition=scaffold.ContentEnd;prepared=true;}
        }
        catch{try{if(Live())Rebuild();else ClearSensitive();}catch{}}
        finally
        {
            try{if(began)RichInput.EndChange();}catch{try{if(Live())Rebuild();else ClearSensitive();}catch{}}
            checklistEnterActive=false;
            // EndChange publishes native selection/text callbacks. Commit only after all have returned.
            if(began)try{if(prepared&&allowedUntilEnd?.Invoke()==true)CommitNative();else if(Live())Rebuild();else ClearSensitive();}catch{try{if(Live())Rebuild();else ClearSensitive();}catch{}}
        }
    }
}
