using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using MemoApp.Core.Editing;
using MemoApp.Windows;
using NativeList = System.Windows.Documents.List;

internal static partial class Program
{
    // Exercise the actual native Enter command, not a fabricated canonical checklist.
    private static async Task ChecklistEditingRun()
    {
        const string beforeText="선행 합성 문단",firstText="첫 체크 항목 합성",afterText="후행 합성 문단";
        const string secondText="둘째 항목 한글 👩‍💻",thirdText="[x] 본문으로 남을 셋째 항목";
        var workspace=new EditingWorkspace(TimeProvider.System);
        var note=workspace.CreateNote();note.Text=$"{beforeText}\n{firstText}\n{afterText}";
        workspace.ConvertMode(note,"rich",true);
        bool live=true;string notice="";
        using var view=new StructuredNoteEditor(workspace,note,()=>live,message=>notice=message);
        var window=new Window{Content=view,Width=620,Height=420};
        try
        {
            window.Show();await Idle();
            Require(!view.RichInput.IsReadOnly,"Checklist fixture must be editable in real WPF");
            var originalParagraph=(Paragraph)view.RichInput.Document.Blocks.ElementAt(1);
            view.RichInput.Selection.Select(originalParagraph.ContentStart,originalParagraph.ContentEnd);
            view.ApplyBold();await Idle();view.ApplyForeground("#123456");await Idle();

            using var original=JsonDocument.Parse(note.Document!.SourceJson);
            var originalNodes=original.RootElement.GetProperty("nodes");
            var firstRuns=originalNodes[1].GetProperty("runs").Clone();
            var preceding=originalNodes[0].Clone();var following=originalNodes[2].Clone();

            Paragraph ItemParagraph(int index)
            {
                var list=view.RichInput.Document.Blocks.OfType<NativeList>().Single();
                return (Paragraph)list.ListItems.ElementAt(index).Blocks.FirstBlock;
            }
            void AtEnd(int index)
            {
                var position=ItemParagraph(index).ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
                view.RichInput.Selection.Select(position,position);view.RichInput.Focus();
            }
            void AssertItems(string stage,string[] bodies,bool[] checkedValues)
            {
                using var current=JsonDocument.Parse(note.Document!.SourceJson);
                var nodes=current.RootElement.GetProperty("nodes");
                Require(nodes.GetArrayLength()==3 && nodes[1].GetProperty("type").GetString()=="checklist",
                    stage+": checklist stays between the two original paragraphs");
                var items=nodes[1].GetProperty("items");
                Require(items.GetArrayLength()==bodies.Length,
                    stage+": native Enter must retain a new canonical checklist item; actual="+items.GetArrayLength()+", notice="+notice);
                Require(JsonNode.DeepEquals(JsonNode.Parse(preceding.GetRawText()),JsonNode.Parse(nodes[0].GetRawText())) &&
                        JsonNode.DeepEquals(JsonNode.Parse(following.GetRawText()),JsonNode.Parse(nodes[2].GetRawText())),
                    stage+": Enter/toggle/typing must preserve both original sibling paragraphs");
                Require(JsonNode.DeepEquals(JsonNode.Parse(firstRuns.GetRawText()),JsonNode.Parse(items[0].GetProperty("runs").GetRawText())),
                    stage+": original first-item text and explicit bold/color marks must survive intact");
                for(int index=0;index<bodies.Length;index++)
                {
                    string body=string.Concat(items[index].GetProperty("runs").EnumerateArray().Select(run=>run.GetProperty("text").GetString()));
                    Require(body==bodies[index] && items[index].GetProperty("checked").GetBoolean()==checkedValues[index],
                        stage+": canonical item "+index+" must keep exact body and independent checked state");
                }
                Require(note.Text==beforeText+"\n"+string.Join("\n",bodies.Select((body,index)=>(checkedValues[index]?"[x] ":"[ ] ")+body))+"\n"+afterText,
                    stage+": full canonical text projection must preserve original and newly typed content");
                Require(view.RichInput.Document.Blocks.OfType<NativeList>().Single().ListItems.Count==bodies.Length && !view.RichInput.IsReadOnly,
                    stage+": actual editable native list must match canonical item count");
            }

            originalParagraph=(Paragraph)view.RichInput.Document.Blocks.ElementAt(1);
            view.RichInput.CaretPosition=originalParagraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            view.InsertChecklist();await Idle();AssertItems("initial checklist",[firstText],[false]);

            AtEnd(0);
            Require(EditingCommands.EnterParagraphBreak.CanExecute(null,view.RichInput),"Native Enter must be enabled at first checklist item end");
            EditingCommands.EnterParagraphBreak.Execute(null,view.RichInput);await Idle();
            AssertItems("first native Enter",[firstText,""],[false,false]);
            AtEnd(1);view.RichInput.Selection.Text=secondText;await Idle();
            AssertItems("second item typing",[firstText,secondText],[false,false]);
            AtEnd(1);view.ToggleChecked();await Idle();
            AssertItems("second item checked",[firstText,secondText],[false,true]);
            string twoItems=note.Document!.SourceJson;

            AtEnd(1);EditingCommands.EnterParagraphBreak.Execute(null,view.RichInput);await Idle();
            AssertItems("second native Enter",[firstText,secondText,""],[false,true,false]);
            Require(view.RichInput.CanUndo,"Native checklist Enter must remain undoable");
            view.RichInput.Undo();await Idle();
            AssertItems("Undo third item Enter",[firstText,secondText],[false,true]);
            Require(note.Document!.SourceJson==twoItems && view.RichInput.CanRedo,"Undo Enter must restore exact canonical source and retain Redo");
            view.RichInput.Redo();await Idle();
            AssertItems("Redo third item Enter",[firstText,secondText,""],[false,true,false]);

            AtEnd(2);view.RichInput.Selection.Text=thirdText;await Idle();
            AssertItems("third item literal prefix typing",[firstText,secondText,thirdText],[false,true,false]);
            string beforeToggle=note.Document!.SourceJson;
            AtEnd(2);view.ToggleChecked();await Idle();
            AssertItems("third item checked",[firstText,secondText,thirdText],[false,true,true]);
            string afterToggle=note.Document!.SourceJson;
            Require(view.RichInput.CanUndo,"Native checked-state mutation must remain undoable");
            view.RichInput.Undo();await Idle();
            Require(note.Document!.SourceJson==beforeToggle && view.RichInput.CanRedo,"Undo checked-state change must restore complete original source and retain Redo");
            AssertItems("Undo third toggle",[firstText,secondText,thirdText],[false,true,false]);
            view.RichInput.Redo();await Idle();
            Require(note.Document!.SourceJson==afterToggle,"Redo checked-state change must restore exact complete source");
            AssertItems("Redo third toggle",[firstText,secondText,thirdText],[false,true,true]);

            AtEnd(0);view.ToggleChecked();await Idle();
            AssertItems("independent first-item toggle",[firstText,secondText,thirdText],[true,true,true]);
            live=false;view.ClearSensitive();workspace.Clear();await Idle();
            Require(view.RichInput.Document.Blocks.Count==0 && !view.RichInput.CanUndo && !view.RichInput.CanRedo && note.Document is null,
                "Checklist conceal must clear all native items/source/Undo/Redo without writing an empty canonical document");
        }
        finally
        {
            live=false;view.ClearSensitive();workspace.Clear();window.Close();
        }
    }
}
