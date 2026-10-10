using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;

namespace MemoApp.Windows;
public sealed partial class StructuredNoteEditor
{
    private SaveCoordinator? imageEditSession;
    private RichImageTextEditContext? imageEditContext;
    private FlowDocument? imageNativeDocument;
    private readonly Dictionary<Paragraph,ImageTextPlaceholder> imagePlaceholders=[];
    private readonly List<ImageTextPlaceholder> imagePlaceholderOrder=[];
    private bool imageWasVisible;
    private bool imageSessionV2;
    private RichImageTextEditContext? imageGuardContext;
    private FlowDocument? imageGuardDocument;
    private long imageGuardMutation;
    private sealed record ImageTextPlaceholder(Paragraph Paragraph,Run Run,RichImageTextBlock Original);

    internal StructuredNoteEditor(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice)
        :this(RequireImageTextSession(session,note),note,current,notice,session){}
    private static EditingWorkspace RequireImageTextSession(SaveCoordinator session,NoteDraft note)
    {
        ArgumentNullException.ThrowIfNull(session);ArgumentNullException.ThrowIfNull(note);
        if(note.Mode!="rich"||note.Document?.SchemaVersion!=2)throw new InvalidOperationException("Session image text editor requires original v2 source");
        return session.Workspace;
    }
    private void BeginImageTextNativeGuard()
    {
        if(!imageSessionV2||imageEditContext is null)return;
        imageGuardContext=imageEditContext;imageGuardDocument=imageNativeDocument;imageGuardMutation=native.MutationGeneration;
        if(!ImageTextNativeGuardCurrent()){ClearSensitive();throw new InvalidDataException("Installed native image graph changed");}
    }
    private bool ImageTextNativeGuardCurrent()=>imageGuardContext is null||!disposed&&imageGuardDocument is not null&&ReferenceEquals(imageGuardDocument,imageNativeDocument)&&ReferenceEquals(imageGuardDocument,RichInput.Document)&&imageGuardMutation==native.MutationGeneration;
    private void EndImageTextNativeGuard(){imageGuardContext=null;imageGuardDocument=null;imageGuardMutation=0;}
    private void ImageTextNativeMutation()
    {
        if(imageGuardContext is not{ } original||ImageTextNativeGuardCurrent())return;
        // The fixed native event bridge revokes before any owner/Core publication callback
        // can continue. A later swap back cannot renew the original context.
        var active=imageEditSession;
        try{active?.RetireRichImageTextEditContext(original);}finally{ClearSensitive();}
    }
    private FlowDocument[] CaptureOwnedNativeDocumentsForCleanup()
    {
        // Keep exact native graphs in cleanup locals before dropping all authority and
        // registries. These are WPF projections, never the canonical source document.
        var graphs=new HashSet<FlowDocument>(ReferenceEqualityComparer.Instance);
        if(imageNativeDocument is{ } projection)graphs.Add(projection);
        if(imageGuardDocument is{ } captured)graphs.Add(captured);
        graphs.Add(RichInput.Document);return graphs.ToArray();
    }
    private void InitializeImageTextEditing(SaveCoordinator? session)
    {
        if(session is null)return;
        Dispatcher.VerifyAccess();session.RegisterRichImageTextEditOwner(Dispatcher.CheckAccess);imageEditSession=session;imageSessionV2=note?.Document?.SchemaVersion==2;
        session.Conceal+=ClearSensitive;session.Changed+=ImageTextSessionChanged;
        workspace!.AttachmentReadInvalidating+=ImageTextInvalidating;IsVisibleChanged+=ImageTextVisibilityChanged;
    }
    internal bool HasCurrentRichImageTextProjection
    {
        get
        {
            Dispatcher.VerifyAccess();var context=imageEditContext;var document=imageNativeDocument;
            bool Pure()=>!disposed&&editable&&!RichInput.IsReadOnly&&context is not null&&ReferenceEquals(context,imageEditContext)&&document is not null&&ReferenceEquals(document,imageNativeDocument)&&ReferenceEquals(document,RichInput.Document);
            return Pure()&&ImageTextCurrent()&&Pure();
        }
    }
    private bool ImageTextCurrent()
    {
        var active=imageEditSession;var context=imageEditContext;
        return ImageTextNativeGuardCurrent()&&active is not null&&context is not null&&active.IsRichImageTextEditContextCurrent(context)&&Live()&&ImageTextNativeGuardCurrent()&&ReferenceEquals(active,imageEditSession)&&ReferenceEquals(context,imageEditContext)&&active.IsRichImageTextEditContextCurrent(context);
    }
    private bool ExactOwnImageTextTransition()
    {
        var active=imageEditSession;var context=imageEditContext;var source=note;
        bool Pure()=>ImageTextNativeGuardCurrent()&&active is not null&&context is not null&&source?.Document is{ } document&&ReferenceEquals(active,imageEditSession)&&ReferenceEquals(context,imageEditContext)&&ReferenceEquals(source,note)&&active.IsRichImageTextEditOwnTransition(context,document,source.EditVersion,source.ContentVersion,active.AttachmentPreviewEpoch);
        return Pure()&&Live()&&Pure();
    }
    private void ImageTextInvalidating(NoteDraft? source)
    {
        if(!imageSessionV2||disposed)return;
        if(!ExactOwnImageTextTransition())ClearSensitive();
    }
    private void ImageTextSessionChanged()
    {
        if(imageSessionV2&&!disposed&&!ImageTextCurrent()&&!ExactOwnImageTextTransition())ClearSensitive();
    }
    private void ImageTextVisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        if(IsVisible)imageWasVisible=true;
        else if(imageWasVisible&&imageSessionV2)ClearSensitive();
    }
    private bool ImageTextDraftChanged()
    {
        if(!imageSessionV2)return false;
        if(ExactOwnImageTextTransition())return true;
        if(imageEditContext is null||!ImageTextCurrent())ClearSensitive();
        return disposed;
    }
    private void RetireImageTextProjection()
    {
        var context=imageEditContext;imageEditContext=null;imageNativeDocument=null;imagePlaceholders.Clear();imagePlaceholderOrder.Clear();
        if(context is not null&&imageEditSession is{ } active)active.RetireRichImageTextEditContext(context);
    }
    private bool BuildImageTextProjection(StyledDocument source,FlowDocument document)
    {
        if(imageEditSession is not{ } active||note is not{ } target)return false;
        RetireImageTextProjection();
        var context=active.CaptureRichImageTextEditContext(target,target.EditVersion,active.AttachmentPreviewEpoch);
        imageEditContext=context;imageNativeDocument=document;
        try
        {
            if(!ReferenceEquals(context.OriginalDocument,source)||!ImageTextCurrent())throw new InvalidDataException("Original image text source changed");
            using var json=JsonDocument.Parse(source.SourceJson,new(){MaxDepth=16});int index=0;
            foreach(var block in json.RootElement.GetProperty("nodes").EnumerateArray())
            {
                if(block.GetProperty("type").GetString()=="image")
                {
                    var original=context.Images.Single(i=>i.BlockIndex==index);
                    var run=new Run("[이미지: "+original.Alt+"]");var paragraph=new Paragraph(run){Margin=new(0,4,0,8),TextAlignment=TextAlignment.Left,TextIndent=0};
                    var token=new ImageTextPlaceholder(paragraph,run,original);imagePlaceholders.Add(paragraph,token);imagePlaceholderOrder.Add(token);document.Blocks.Add(paragraph);
                }
                else document.Blocks.Add(RenderBlock(block));index++;
            }
            return ImageTextCurrent()&&Equivalent(source,CaptureImageTextDocument(document,detached:true));
        }
        catch{RetireImageTextProjection();throw;}
    }
    private StyledDocument CaptureImageTextDocument(FlowDocument document,bool detached=false)
    {
        if(!ReferenceEquals(imageNativeDocument,document)||!detached&&(!ReferenceEquals(document,RichInput.Document)||imageGuardContext is null)||!ImageTextCurrent())throw new InvalidDataException("Image native projection authority ended");
        var context=imageEditContext!;var active=imageEditSession;long generation=projectionGeneration,mutation=native.MutationGeneration;
        bool Pure()=>ReferenceEquals(context,imageEditContext)&&ReferenceEquals(active,imageEditSession)&&ReferenceEquals(document,imageNativeDocument)&&generation==projectionGeneration&&(detached||ReferenceEquals(document,RichInput.Document)&&mutation==native.MutationGeneration&&ImageTextNativeGuardCurrent());
        bool Same()=>Pure()&&ImageTextCurrent()&&Pure();
        if(!Same())throw new InvalidDataException("Original native image capture changed");
        var expected=context.Images;int imageIndex=0,count=0;var nodes=new List<string>();
        foreach(var block in document.Blocks)
        {
            if(++count>1024)throw new InvalidDataException("Native block budget");
            if(block is Paragraph paragraph&&imagePlaceholders.TryGetValue(paragraph,out var token))
            {
                if(imageIndex>=expected.Length||imageIndex>=imagePlaceholderOrder.Count||!ReferenceEquals(token,imagePlaceholderOrder[imageIndex])||!ReferenceEquals(paragraph.Parent,document)||paragraph.Inlines.Count!=1||!ReferenceEquals(paragraph.Inlines.FirstInline,token.Run)||token.Run.Text!="[이미지: "+token.Original.Alt+"]"||paragraph.TextAlignment!=TextAlignment.Left||paragraph.TextIndent!=0||token.Run.GetValue(LinkProperty) is not null
                    ||token.Original.AttachmentId!=expected[imageIndex].AttachmentId||token.Original.Alt!=expected[imageIndex].Alt||!JsonNode.DeepEquals(JsonNode.Parse(token.Original.RawNode),JsonNode.Parse(expected[imageIndex].RawNode)))throw new InvalidDataException("Image placeholder missing, changed or reordered");
                // ReadRuns validates native style/content, but it never supplies image authority.
                var runs=ReadRuns(paragraph,ref count);if(runs.Count!=1||!JsonNode.DeepEquals(runs[0],new JsonObject{["text"]=token.Run.Text}))throw new InvalidDataException("Image placeholder style changed");
                nodes.Add(token.Original.RawNode);imageIndex++;
            }
            else nodes.Add(CaptureNativeBlock(block,ref count).ToJsonString());
        }
        if(imageIndex!=expected.Length||imagePlaceholders.Count!=expected.Length||imagePlaceholderOrder.Count!=expected.Length||!Same())throw new InvalidDataException("Complete original image membership required");
        var result=new StyledDocument(2,"{\"nodes\":["+string.Join(',',nodes)+"]}");if(!RichDocumentCodec.Inspect(result).Supported)throw new InvalidDataException("Complete known v2 required");return result;
    }
    private bool ImageSelectionTouchesPlaceholder()
    {
        if(imageEditContext is null)return false;
        var selection=RichInput.Selection;
        return imagePlaceholders.Keys.Any(p=>selection.Start.CompareTo(p.ElementEnd)<0&&selection.End.CompareTo(p.ElementStart)>0||selection.IsEmpty&&ReferenceEquals(selection.Start.Paragraph,p));
    }
    private bool ImageTextCommandAllowed()=>imageEditContext is null||ImageTextCurrent()&&!ImageSelectionTouchesPlaceholder();
    private Func<bool>? CaptureImageTextCommandGuard()
    {
        if(imageEditContext is not{ } context)return null;
        var source=note;var original=source?.Document;long version=source?.EditVersion??-1,generation=projectionGeneration,selectionGeneration=caretSelectionGeneration;
        var document=RichInput.Document;var start=RichInput.Selection.Start;var end=RichInput.Selection.End;
        bool Pure()=>!disposed&&ReferenceEquals(context,imageEditContext)&&ReferenceEquals(source,note)&&ReferenceEquals(original,source?.Document)&&source?.EditVersion==version&&generation==projectionGeneration&&selectionGeneration==caretSelectionGeneration&&ReferenceEquals(document,RichInput.Document)&&RichInput.Selection.Start.CompareTo(start)==0&&RichInput.Selection.End.CompareTo(end)==0;
        return ()=>Pure()&&ImageTextCommandAllowed()&&Pure();
    }
    private bool ImageDeletionCommandAllowed(ICommand command)
    {
        if(!ImageTextCommandAllowed())return false;
        if(imageEditContext is null||!RichInput.Selection.IsEmpty)return true;
        var paragraph=RichInput.Selection.Start.Paragraph;if(paragraph is null||paragraph.Parent is not FlowDocument)return true;
        bool backwards=command==EditingCommands.Backspace||command==EditingCommands.DeletePreviousWord;
        bool forwards=command==EditingCommands.Delete||command==EditingCommands.DeleteNextWord;
        if(backwards&&new TextRange(paragraph.ContentStart,RichInput.Selection.Start).Text.Length==0&&paragraph.PreviousBlock is Paragraph previous&&imagePlaceholders.ContainsKey(previous))return false;
        if(forwards&&new TextRange(RichInput.Selection.Start,paragraph.ContentEnd).Text.Length==0&&paragraph.NextBlock is Paragraph next&&imagePlaceholders.ContainsKey(next))return false;
        return true;
    }
    private bool CommitImageTextDocument(StyledDocument captured)
    {
        if(imageEditSession is not{ } active||imageEditContext is not{ } context||!ImageTextCurrent()||!ReferenceEquals(active,imageEditSession)||!ReferenceEquals(context,imageEditContext))throw new InvalidDataException("Image text commit authority ended");
        var receipt=active.ApplyRichImageTextEdit(context,captured);
        if(receipt.Outcome==RichImageTextEditOutcome.NotApplied)throw new InvalidDataException("Image text edit refused");
        var continuation=receipt.Continuation;
        try
        {
            if(!receipt.NotificationSucceeded||continuation is null||!Live()||disposed||!ReferenceEquals(active,imageEditSession)||!ReferenceEquals(context,imageEditContext)||!active.IsRichImageTextEditContextCurrent(continuation))
            {if(continuation is not null)active.RetireRichImageTextEditContext(continuation);ClearSensitive();return true;}
            imageEditContext=continuation;projected=note!.Document;projectedContentVersion=note.ContentVersion;
            if(!ImageTextCurrent())ClearSensitive();
        }
        catch
        {
            // The receipt already states that publication occurred (or was a no-op).
            // A hostile native/host observer cannot turn AppliedDirty into "not applied".
            try{if(continuation is not null)active.RetireRichImageTextEditContext(continuation);}catch{CleanupErrorCode="RICH_IMAGE_CONTEXT_RETIRE_FAILURE";}
            ClearSensitive();
        }
        return true;
    }
    private void ClearImageTextEditing()
    {
        var active=imageEditSession;var owner=workspace;
        try{RetireImageTextProjection();}catch{CleanupErrorCode="RICH_IMAGE_CONTEXT_RETIRE_FAILURE";}finally{imageEditSession=null;}
        foreach(Action cleanup in new Action[]{()=>{if(active is not null)active.Conceal-=ClearSensitive;},()=>{if(active is not null)active.Changed-=ImageTextSessionChanged;},()=>{if(owner is not null)owner.AttachmentReadInvalidating-=ImageTextInvalidating;},()=>IsVisibleChanged-=ImageTextVisibilityChanged})
            try{cleanup();}catch{CleanupErrorCode="RICH_IMAGE_CONTEXT_RETIRE_FAILURE";}
    }
}
