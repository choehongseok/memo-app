using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Windows;
using NativeList = System.Windows.Documents.List;

internal static partial class Program
{
    private static async Task ChecklistEnterCompletionRun()
    {
        // These are real native commands; canonical JSON is only the synthetic baseline and oracle.
        foreach(int emptyIndex in new[]{0,1,2})
        {
            var items=new JsonArray();
            for(int index=0;index<3;index++)items.Add(new JsonObject{["checked"]=index!=0,["runs"]=new JsonArray(new JsonObject{["text"]=index==emptyIndex?"":"[x] literal "+index,["bold"]=true,["foreground"]="#FF123456"})});
            var nodes=new JsonArray(new JsonObject{["type"]="paragraph",["runs"]=new JsonArray(new JsonObject{["text"]="before"})},new JsonObject{["type"]="checklist",["items"]=items.DeepClone()},new JsonObject{["type"]="paragraph",["runs"]=new JsonArray(new JsonObject{["text"]="after"})});
            var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();workspace.ConvertMode(note,"rich",true);workspace.SetRichDocument(note,new StyledDocument(1,new JsonObject{["nodes"]=nodes}.ToJsonString()));
            string notice="";using var view=new StructuredNoteEditor(workspace,note,()=>true,message=>notice=message);var window=new Window{Content=view};
            try
            {
                window.Show();await Idle();string baseline=note.Document!.SourceJson;
                var paragraph=(Paragraph)view.RichInput.Document.Blocks.OfType<NativeList>().Single().ListItems.ElementAt(emptyIndex).Blocks.FirstBlock;
                var end=paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);view.RichInput.Selection.Select(end,end);
                EditingCommands.EnterParagraphBreak.Execute(null,view.RichInput);await Idle();
                var actual=JsonNode.Parse(note.Document!.SourceJson)!["nodes"]!.AsArray();
                Require(actual.Count==4+(emptyIndex==1?1:0),"Empty checklist Enter exits as a standalone paragraph at first/middle/last item");
                Require(JsonNode.DeepEquals(actual[0],nodes[0])&&JsonNode.DeepEquals(actual[^1],nodes[^1]),"Empty-item exit preserves both sibling paragraphs");
                var retained=actual.Where(node=>node!["type"]!.GetValue<string>()=="checklist").SelectMany(node=>node!["items"]!.AsArray()).ToArray();
                var expected=items.Where((_,index)=>index!=emptyIndex).ToArray();
                Require(retained.Length==2&&retained.Zip(expected).All(pair=>JsonNode.DeepEquals(pair.First,pair.Second)),"Empty-item exit preserves other items, explicit styles, literal prefix body and checked state");
                var prefixProperty=(DependencyProperty)typeof(StructuredNoteEditor).GetField("ChecklistPrefixProperty",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)!;
                Require(view.RichInput.Document.Blocks.OfType<NativeList>().SelectMany(list=>list.ListItems).All(item=>((Paragraph)item.Blocks.FirstBlock).Inlines.FirstInline is Run prefix&&(bool)prefix.GetValue(prefixProperty)&&prefix.Text is "[ ] " or "[x] "),"Exit preserves each remaining native item's owned scaffold flag");
                Require(view.RichInput.CaretPosition.Paragraph?.Parent is FlowDocument&&new TextRange(view.RichInput.CaretPosition.Paragraph.ContentStart,view.RichInput.CaretPosition.Paragraph.ContentEnd).Text.TrimEnd('\r','\n')=="","Empty exit caret is outside checklist and contains no own prefix");
                string exited=note.Document.SourceJson;Require(view.RichInput.CanUndo,"Empty-item exit has native Undo");view.RichInput.Undo();await Idle();Require(note.Document!.SourceJson==baseline&&view.RichInput.CanRedo,"Undo empty-item exit restores exact canonical source; index="+emptyIndex+", redo="+view.RichInput.CanRedo+", notice="+notice+", expected="+baseline+", actual="+note.Document.SourceJson);view.RichInput.Redo();await Idle();Require(note.Document!.SourceJson==exited,"Redo empty-item exit restores exact canonical source; index="+emptyIndex);
            }
            finally{view.ClearSensitive();workspace.Clear();window.Close();}
        }
        {
            var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();workspace.ConvertMode(note,"rich",true);
            workspace.SetRichDocument(note,new StyledDocument(1,"{\"nodes\":[{\"type\":\"checklist\",\"items\":[{\"checked\":true,\"runs\":[{\"text\":\"leftDELETE[x] right\",\"bold\":true,\"foreground\":\"#FF123456\"}]}]}]}"));
            string notice="";using var view=new StructuredNoteEditor(workspace,note,()=>true,message=>notice=message);var window=new Window{Content=view};
            try
            {
                window.Show();await Idle();string baseline=note.Document!.SourceJson;var paragraph=(Paragraph)view.RichInput.Document.Blocks.OfType<NativeList>().Single().ListItems.FirstListItem.Blocks.FirstBlock;
                var body=paragraph.Inlines.OfType<Run>().Last();view.RichInput.Selection.Select(body.ContentStart.GetPositionAtOffset(4)!,body.ContentStart.GetPositionAtOffset(10)!);
                EditingCommands.EnterParagraphBreak.Execute(null,view.RichInput);await Idle();
                var items=JsonNode.Parse(note.Document!.SourceJson)!["nodes"]![0]!["items"]!.AsArray();
                Require(items.Count==2&&items[0]!["checked"]!.GetValue<bool>()&&!items[1]!["checked"]!.GetValue<bool>(),"Selected body Enter creates independent unchecked item and preserves original checked state; notice="+notice);
                for(int index=0;index<2;index++){var runs=items[index]!["runs"]!.AsArray();Require(string.Concat(runs.Select(run=>run!["text"]!.GetValue<string>()))==(index==0?"left":"[x] right"),"Selected body Enter deletes exactly the selection and preserves literal [x] body");Require(runs.Where(run=>run!["text"]!.GetValue<string>().Length>0).All(run=>run!["bold"]!.GetValue<bool>()&&run["foreground"]!.GetValue<string>()=="#FF123456"),"Selected body split preserves explicit bold/color styles");}
                string split=note.Document.SourceJson;view.RichInput.Undo();await Idle();Require(note.Document!.SourceJson==baseline&&view.RichInput.CanRedo,"Undo selection Enter restores exact body/styles/check state");view.RichInput.Redo();await Idle();Require(note.Document!.SourceJson==split,"Redo selection Enter restores exact canonical split");
                var nativeItems=view.RichInput.Document.Blocks.OfType<NativeList>().Single().ListItems.ToArray();var firstBody=((Paragraph)nativeItems[0].Blocks.FirstBlock).Inlines.OfType<Run>().Last();var secondBody=((Paragraph)nativeItems[1].Blocks.FirstBlock).Inlines.OfType<Run>().Last();view.RichInput.Selection.Select(firstBody.ContentStart,secondBody.ContentEnd);notice="";EditingCommands.EnterParagraphBreak.Execute(null,view.RichInput);await Idle();Require(note.Document.SourceJson==split&&notice.Length>0,"Enter selection across items is refused with a notice and preserves source");
                paragraph=(Paragraph)view.RichInput.Document.Blocks.OfType<NativeList>().Single().ListItems.FirstListItem.Blocks.FirstBlock;view.RichInput.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);string beforeRefusal=note.Document.SourceJson;notice="";EditingCommands.EnterParagraphBreak.Execute(null,view.RichInput);await Idle();Require(note.Document.SourceJson==beforeRefusal&&notice.Length>0,"Enter selection including owned prefix is refused with a notice and preserves source");
            }
            finally{view.ClearSensitive();workspace.Clear();window.Close();}
        }
    }
}
