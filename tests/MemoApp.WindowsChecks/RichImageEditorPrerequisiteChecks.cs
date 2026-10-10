using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

internal static partial class Program
{
    // Standalone native prerequisite, not a product editor or a canonical source grant.
    private static async Task RichImageEditorPrerequisiteRun()
    {
        foreach(int[] boundaries in new[]{new[]{0},new[]{1},new[]{3},new[]{0,2,3}})
            await RichImageEditorPrerequisiteCase(boundaries);
    }

    private static async Task RichImageEditorPrerequisiteCase(int[] boundaries)
    {
        const string label="[이미지: synthetic.png]";
        var document=new FlowDocument();var placeholders=new List<NativeImagePrerequisiteToken>();var text=new List<Paragraph>();
        object projection=new();Guid repeatedImage=Guid.Parse("bcb555ca-98f4-4501-8504-edf2a353e1ac");
        for(int boundary=0;boundary<=3;boundary++)
        {
            if(boundaries.Contains(boundary))
            {
                var run=new Run(label){FontFamily=new FontFamily("Segoe UI"),FontSize=14,Foreground=Brushes.Black};var paragraph=new Paragraph(run);
                document.Blocks.Add(paragraph);placeholders.Add(new(projection,paragraph,run,repeatedImage,label,run.FontFamily.Source,run.FontSize,run.Foreground));
            }
            if(boundary<3){var paragraph=new Paragraph(new Run(boundary==1?label:"ordinary text "+boundary));text.Add(paragraph);document.Blocks.Add(paragraph);}
        }
        var box=new RichTextBox{Document=document,IsUndoEnabled=true,UndoLimit=100,AcceptsTab=true,AllowDrop=false};
        var window=new Window{Title="Synthetic v2 editor prerequisite",Content=box,Width=600,Height=380};
        string phase="show";int starts=0,updates=0,completed=0;bool? startHandled=null,updateHandled=null,completeHandled=null;TextComposition? activeComposition=null;Exception? primaryFailure=null;
        TextCompositionEventHandler start=(_,e)=>{if(ReferenceEquals(e.TextComposition,activeComposition))starts++;};TextCompositionEventHandler update=(_,e)=>{if(ReferenceEquals(e.TextComposition,activeComposition))updates++;};TextCompositionEventHandler finish=(_,e)=>{if(ReferenceEquals(e.TextComposition,activeComposition))completed++;};
        box.AddHandler(TextCompositionManager.PreviewTextInputStartEvent,start,true);
        box.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent,update,true);
        box.AddHandler(TextCompositionManager.PreviewTextInputEvent,finish,true);
        bool Valid()=>ValidateNativeImagePrerequisites(document,projection,placeholders);
        void Exact(string action)=>Require(ReferenceEquals(box.Document,document)&&Valid(),"H01 v2 native prerequisite FAIL: "+action+" preserves exact image Paragraph/Run identity, full membership, relative order and label; boundaries="+string.Join(',',boundaries));
        string Contents()=>new TextRange(document.ContentStart,document.ContentEnd).Text;
        void UndoRedo(string action,string before,string after)
        {
            Require(box.CanUndo,"Native edit produced real Undo: "+action);ApplicationCommands.Undo.Execute(null,box);Exact(action+" Undo");Require(Contents()==before,"Native Undo restores exact full text: "+action);
            Require(box.CanRedo,"Native Undo produced real Redo: "+action);ApplicationCommands.Redo.Execute(null,box);Exact(action+" Redo");Require(Contents()==after,"Native Redo restores exact full text: "+action);
        }
        try
        {
            window.Show();window.Activate();box.Focus();Keyboard.Focus(box);await Idle();
            Require(window.IsVisible&&box.IsVisible&&box.IsKeyboardFocused,"Prerequisite uses actual visible focused STA RichTextBox");Exact("initial projection");
            Require(!placeholders.Any(p=>ReferenceEquals(p.Paragraph,text[1]))&&new TextRange(text[1].ContentStart,text[1].ContentEnd).Text.TrimEnd('\r','\n')==label,"Ordinary equal label is text, not registered image authority");
            box.IsUndoEnabled=false;box.IsUndoEnabled=true;phase="native text composition typing";
            box.Selection.Select(text[0].ContentEnd,text[0].ContentEnd);string before=Contents();
            activeComposition=new TextComposition(InputManager.Current,box,"한글 e\u0301 😀",TextCompositionAutoComplete.Off);
            // WPF v10.0.0 UnsafeStart/Update/CompleteComposition return ProcessInput's
            // event-handled flag. An unhandled start can still set Started and dispatch.
            startHandled=TextCompositionManager.StartComposition(activeComposition);Exact("composition start");
            Require(starts==1&&updates==0&&completed==0&&Contents()==before,"Uncompleted exact native composition has a real start boundary without prematurely inserting text");
            updateHandled=TextCompositionManager.UpdateComposition(activeComposition);Exact("composition update");Require(starts==1&&updates==1&&completed==0&&Contents()==before,"Actual same-composition native update observed before completion without premature text insertion");
            completeHandled=TextCompositionManager.CompleteComposition(activeComposition);activeComposition=null;await Idle();Exact("composition completion");
            string typed=Contents();Require(starts==1&&updates==1&&completed==1&&typed.Contains("한글 e\u0301 😀",StringComparison.Ordinal)&&typed!=before,"Native RichTextBox actually consumes the exact completed Unicode TextInput; event-only simulation is insufficient");
            UndoRedo("composition text",before,typed);

            phase="native selection replacement";box.IsUndoEnabled=false;box.IsUndoEnabled=true;
            box.Selection.Select(text[2].ContentStart,text[2].ContentEnd);before=Contents();box.BeginChange();try{box.Selection.Text="replacement plain text";}finally{box.EndChange();}await Idle();Exact("text-only replacement");string replaced=Contents();Require(replaced.Contains("replacement plain text",StringComparison.Ordinal)&&replaced!=before,"Actual native text selection replacement occurred");UndoRedo("text replacement",before,replaced);

            phase="native text formatting";box.IsUndoEnabled=false;box.IsUndoEnabled=true;
            box.Selection.Select(text[2].ContentStart,text[2].ContentEnd);before=Contents();box.BeginChange();try{box.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,FontWeights.Bold);}finally{box.EndChange();}await Idle();Exact("text-only formatting");Require(box.Selection.GetPropertyValue(TextElement.FontWeightProperty).Equals(FontWeights.Bold)&&Contents()==before,"Actual selection formatting applies bold without changing full text");
            Require(box.CanUndo,"Native format produces Undo");ApplicationCommands.Undo.Execute(null,box);Exact("format Undo");Require(box.Selection.GetPropertyValue(TextElement.FontWeightProperty).Equals(FontWeights.Normal),"Native Undo actually removes selected bold formatting");ApplicationCommands.Redo.Execute(null,box);Exact("format Redo");Require(box.Selection.GetPropertyValue(TextElement.FontWeightProperty).Equals(FontWeights.Bold),"Native Redo actually restores selected bold formatting");

            phase="invalid native membership";box.IsUndoEnabled=false;
            var original=placeholders[0];var clone=new Paragraph(new Run(label)){Tag=original.ImageId};document.Blocks.InsertBefore(original.Paragraph,clone);document.Blocks.Remove(original.Paragraph);
            Require(!Valid(),"Cloned equal-label paragraph/Run and spoofed Tag never substitutes for registered exact image references");document.Blocks.InsertBefore(clone,original.Paragraph);document.Blocks.Remove(clone);Exact("restored original after clone refusal");
            Block? next=original.Paragraph.NextBlock;document.Blocks.Remove(original.Paragraph);Require(!Valid(),"Deleted registered image is a whole-candidate prerequisite refusal");if(next is not null)document.Blocks.InsertBefore(next,original.Paragraph);else document.Blocks.Add(original.Paragraph);Exact("restored original after deletion refusal");
            if(placeholders.Count>1)
            {
                var last=placeholders[^1];Block? after=last.Paragraph.NextBlock;document.Blocks.Remove(last.Paragraph);document.Blocks.InsertBefore(original.Paragraph,last.Paragraph);Require(!Valid(),"Reordered repeated image references fail despite equal IDs and labels");document.Blocks.Remove(last.Paragraph);if(after is not null)document.Blocks.InsertBefore(after,last.Paragraph);else document.Blocks.Add(last.Paragraph);Exact("restored original order");
            }
            Console.WriteLine("H01 v2 native prerequisite observed boundaries="+string.Join(',',boundaries)+" placeholders="+placeholders.Count+" compositionStart="+starts+" update="+updates+" completed="+completed+" handled="+startHandled+"/"+updateHandled+"/"+completeHandled+" exactIdentity=true (synthetic TextCompositionManager, not physical keyboard IME)");
        }
        catch(Exception error)
        {
            primaryFailure=error;
            Console.Error.WriteLine("H01 v2 native prerequisite FAIL phase="+phase+" boundaries="+string.Join(',',boundaries)+" identity="+Valid()+" start="+starts+" update="+updates+" completed="+completed+" handled="+startHandled+"/"+updateHandled+"/"+completeHandled+" type="+error.GetType().Name);
            throw;
        }
        finally
        {
            var cleanupFailures=new List<Exception>();
            foreach(Action cleanup in new Action[]
            {
                ()=>{if(activeComposition is not null)TextCompositionManager.CompleteComposition(activeComposition);},
                ()=>box.RemoveHandler(TextCompositionManager.PreviewTextInputStartEvent,start),
                ()=>box.RemoveHandler(TextCompositionManager.PreviewTextInputUpdateEvent,update),
                ()=>box.RemoveHandler(TextCompositionManager.PreviewTextInputEvent,finish),
                ()=>box.IsUndoEnabled=false,
                ()=>box.IsReadOnly=true,
                ()=>document.Blocks.Clear(),
                ()=>placeholders.Clear(),
                ()=>text.Clear(),
                ()=>window.Content=null,
                ()=>window.Close()
            })
                try{cleanup();}catch(Exception error){cleanupFailures.Add(error);}
            if(cleanupFailures.Count!=0)
            {
                Console.Error.WriteLine("H01 native prerequisite cleanup failures="+cleanupFailures.Count+" types="+string.Join(',',cleanupFailures.Select(e=>e.GetType().Name)));
                if(primaryFailure is not null)cleanupFailures.Insert(0,primaryFailure);
                throw new AggregateException("H01 native prerequisite cleanup failed; every action was attempted",cleanupFailures);
            }
        }
    }

    private sealed record NativeImagePrerequisiteToken(object Projection,Paragraph Paragraph,Run Run,Guid ImageId,string Label,string FontFamily,double FontSize,Brush Foreground);
    private static bool ValidateNativeImagePrerequisites(FlowDocument document,object projection,IReadOnlyList<NativeImagePrerequisiteToken> expected)
    {
        var found=new List<NativeImagePrerequisiteToken>();
        foreach(var block in document.Blocks)
        {
            var token=expected.SingleOrDefault(t=>ReferenceEquals(t.Paragraph,block));if(token is null)continue;
            if(!ReferenceEquals(token.Projection,projection)||!ReferenceEquals(token.Paragraph.Parent,document)||token.Paragraph.Inlines.Count!=1||!ReferenceEquals(token.Paragraph.Inlines.FirstInline,token.Run)||token.Run.Text!=token.Label
                ||token.Run.FontWeight!=FontWeights.Normal||token.Run.FontStyle!=FontStyles.Normal||token.Run.FontFamily.Source!=token.FontFamily||token.Run.FontSize!=token.FontSize||!ReferenceEquals(token.Run.Foreground,token.Foreground)||token.Run.Background is not null||token.Run.TextDecorations.Count!=0)return false;
            found.Add(token);
        }
        return found.Count==expected.Count&&found.Where((t,index)=>!ReferenceEquals(t,expected[index])).Any()==false;
    }
}
