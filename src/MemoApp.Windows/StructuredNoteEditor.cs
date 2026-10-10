using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
namespace MemoApp.Windows;
// Only our bounded canonical model is loaded. Never parse user XAML/RTF or URI resources.
public sealed partial class StructuredNoteEditor:UserControl,IDisposable
{
    private EditingWorkspace? workspace;
    private NoteDraft? note;
    private Func<bool>? current;
    private Action<string>? notice;
    private StyledDocument? projected;
    private StyledDocument? projectionSource;
    private long projectedContentVersion;
    private bool rebuilding,committing,disposed,editable,refreshPending,composing;
    private long projectionGeneration;
    private long compositionToken;
    private long caretSelectionGeneration;
    internal long CaretSelectionGeneration=>caretSelectionGeneration;
    private void CaretSelectionChanged(object sender,RoutedEventArgs e)=>caretSelectionGeneration++;
    public string? CleanupErrorCode{get;private set;}
    private readonly TextBlock state=new(){TextWrapping=TextWrapping.Wrap,Margin=new(4)};
    private readonly TextBox linkInput=new(){Width=190,MaxLength=2048,ToolTip="http/https 링크를 원문에 보존 (자동 실행 없음)"};
    private readonly WrapPanel toolbar=new(){Margin=new(0,0,0,4)};
    private static readonly DependencyProperty LinkProperty=DependencyProperty.RegisterAttached("CanonicalLink",typeof(string),typeof(StructuredNoteEditor),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.Inherits));
    private static readonly DependencyProperty ChecklistPrefixProperty=DependencyProperty.RegisterAttached("CanonicalChecklistPrefix",typeof(bool),typeof(StructuredNoteEditor),new(false));
    private static readonly DependencyProperty ChecklistProperty=DependencyProperty.RegisterAttached("CanonicalChecklist",typeof(bool),typeof(StructuredNoteEditor),new(false));
    private static readonly HashSet<string> SafeFonts=Fonts.SystemFontFamilies.Select(f=>f.Source).Where(SafeFontName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private sealed class NativeRichTextBox:RichTextBox
    {
        internal int EventDepth{get;private set;}
        internal Action? EventFinished;
        internal Action? MutationStarting;
        internal long MutationGeneration{get;private set;}
        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            using var phase=RichImageTextPhase.Enter(Dispatcher);MutationGeneration++;EventDepth++;try{MutationStarting?.Invoke();base.OnTextChanged(e);}finally{EventDepth--;if(EventDepth==0)EventFinished?.Invoke();}
        }
        protected override void OnSelectionChanged(RoutedEventArgs e)
        {
            using var phase=RichImageTextPhase.Enter(Dispatcher);MutationGeneration++;MutationStarting?.Invoke();base.OnSelectionChanged(e);
        }
    }
    private readonly NativeRichTextBox native=new(){AllowDrop=false,IsUndoEnabled=true,UndoLimit=100,AcceptsTab=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new(8)};
    private readonly DispatcherTimer transactionRetry=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(100)};
    private bool waitingTransaction;
    public RichTextBox RichInput=>native;
    public bool IsDisposed=>disposed;
    public StructuredNoteEditor(EditingWorkspace workspace,NoteDraft note,Func<bool> current,Action<string> notice)
        :this(workspace,note,current,notice,null){}
    private StructuredNoteEditor(EditingWorkspace workspace,NoteDraft note,Func<bool> current,Action<string> notice,SaveCoordinator? imageSession)
    {
        this.workspace=workspace;this.note=note;this.current=current;this.notice=notice;native.MutationStarting=ImageTextNativeMutation;
        native.EventFinished=()=>{if(!disposed&&(refreshPending||waitingTransaction))QueueRefresh();};
        transactionRetry.Tick+=(_,_)=>{transactionRetry.Stop();if(!disposed&&waitingTransaction){waitingTransaction=false;if(Live())ScheduleRefresh();else ClearSensitive();}};
        var root=new DockPanel();DockPanel.SetDock(toolbar,Dock.Top);root.Children.Add(toolbar);DockPanel.SetDock(state,Dock.Top);root.Children.Add(state);root.Children.Add(RichInput);Content=root;
        Button("굵게",ApplyBold);var sizes=new ComboBox{Width=60,ItemsSource=new double[]{8,10,12,14,16,18,22,28,36,48,72,96},SelectedItem=14d};sizes.SelectionChanged+=(_,_)=>{if(!rebuilding&&sizes.SelectedItem is double size)ApplyFontSize(size);};toolbar.Children.Add(sizes);
        var fonts=new ComboBox{Width=140,ItemsSource=SafeFonts.Order(StringComparer.OrdinalIgnoreCase).Take(500).ToArray(),SelectedItem="Segoe UI"};fonts.SelectionChanged+=(_,_)=>{if(!rebuilding&&fonts.SelectedItem is string font)ApplyFontFamily(font);};toolbar.Children.Add(fonts);
        Button("밑줄",ApplyUnderline);Button("취소선",ApplyStrike);
        var foreground=new ComboBox{Width=65,ItemsSource=new[]{"#000000","#FFFFFF","#CC0000","#0044CC","#006600","#663399"},ToolTip="글자색"};foreground.SelectionChanged+=(_,_)=>{if(foreground.SelectedItem is string hex)ApplyForeground(hex);};toolbar.Children.Add(foreground);
        var highlight=new ComboBox{Width=65,ItemsSource=new[]{"#FFFF00","#CCFFCC","#FFCCDD","#CCCCFF","#FFFFFF"},ToolTip="형광펜"};highlight.SelectionChanged+=(_,_)=>{if(highlight.SelectedItem is string hex)ApplyHighlight(hex);};toolbar.Children.Add(highlight);
        Button("• 목록",()=>ToggleList(false));Button("1. 목록",()=>ToggleList(true));Button("체크목록",InsertChecklist);Button("체크",ToggleChecked);Button("2×2 표",()=>InsertTable(2,2));toolbar.Children.Add(linkInput);Button("링크 표시 추가",()=>ApplyLink(linkInput.Text));Button("선택 링크 열기",OpenSelectedLinkWithConfirmation);RichInput.PreviewMouseLeftButtonUp+=LinkClick;
        RichInput.TextChanged+=Changed;RichInput.SelectionChanged+=CaretSelectionChanged;DataObject.AddPastingHandler(RichInput,Pasting);
        RichInput.AddHandler(TextCompositionManager.PreviewTextInputStartEvent,new TextCompositionEventHandler(CompositionStart),true);
        RichInput.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent,new TextCompositionEventHandler(CompositionUpdate),true);
        RichInput.AddHandler(TextCompositionManager.PreviewTextInputEvent,new TextCompositionEventHandler(CompositionComplete),true);
        RichInput.PreviewDragOver+=RejectDrop;RichInput.PreviewDrop+=RejectDrop;
        CommandManager.AddPreviewExecutedHandler(RichInput,PreviewCommand);
        note.PropertyChanged+=DraftChanged;try{InitializeImageTextEditing(imageSession);PublishProjection();}catch{ClearSensitive();throw;}
    }
    private void Button(string label,Action action){var button=new Button{Content=label,Padding=new(6,3,6,3),Margin=new(0,0,4,0),Focusable=false};button.Click+=(_,_)=>action();toolbar.Children.Add(button);}
    private static bool SafeFontName(string name)=>name.Length is >0 and <=128&&RichDocumentCodec.IsWellFormedUnicode(name)&&name.All(c=>char.IsLetterOrDigit(c)||c is ' ' or '-' or '_' or '.');
    private bool Live()
    {
        var target=note;return !disposed&&target is {IsClosed:false,IsDeleted:false,Mode:"rich"}&&current?.Invoke()==true&&!disposed&&ReferenceEquals(note,target)&&target is {IsClosed:false,IsDeleted:false,Mode:"rich"};
    }
    private void DraftChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(disposed)return;if(!Live()){ClearSensitive();return;}if(ImageTextDraftChanged())return;
        if(!committing && e.PropertyName is nameof(NoteDraft.Document) or nameof(NoteDraft.Mode) && (projected!=note!.Document||projectedContentVersion!=note.ContentVersion))Rebuild();
    }
    private void Rebuild()
    {
        if(!Live()){ClearSensitive();return;}refreshPending=true;ScheduleRefresh();
    }
    private void PublishProjection()
    {
        using var phase=RichImageTextPhase.Enter(Dispatcher);
        if(!Live()){ClearSensitive();return;}
        if(rebuilding||native.EventDepth!=0){refreshPending=true;return;}
        var target=note!;var owner=workspace!;var source=target.Document!;string text=target.Text;long version=target.EditVersion,generation=++projectionGeneration;
        bool Same()=>Live()&&generation==projectionGeneration&&ReferenceEquals(note,target)&&ReferenceEquals(workspace,owner)&&ReferenceEquals(target.Document,source)&&target.EditVersion==version;
        rebuilding=true;refreshPending=false;
        try
        {
            // Build and verify a detached document before its single native publish.
            var document=new FlowDocument{FontFamily=new FontFamily("Segoe UI"),FontSize=14,PagePadding=new(0),TextAlignment=TextAlignment.Left,Foreground=RichInput.Foreground};
            var info=RichDocumentCodec.Inspect(source);bool ready=false;string limitation=info.Limitation;
            if(info.Supported&&source.SchemaVersion==1)
            {
                using var json=JsonDocument.Parse(source.SourceJson,new(){MaxDepth=16});
                try
                {
                    foreach(var block in json.RootElement.GetProperty("nodes").EnumerateArray())document.Blocks.Add(RenderBlock(block));
                    ready=Equivalent(source,CaptureNativeDocument(document));
                    if(!ready){document.Blocks.Clear();limitation="이 PC에서 정확히 표현하지 못하는 문서 — 원문을 보존하며 읽기 전용입니다.";}
                }
                catch(InvalidDataException){document.Blocks.Clear();limitation="이 PC의 미지원 글꼴/서식 — 전체 원문을 보존하며 읽기 전용입니다.";}
            }
            if(source.SchemaVersion==2)
            {
                limitation="이미지 문서는 읽기 전용입니다. 이미지 표시·블록 제거는 문서 보기에서 명시적으로 실행하세요.";
                if(info.Supported&&imageEditSession is not null)
                    try{ready=BuildImageTextProjection(source,document);if(!ready){RetireImageTextProjection();document.Blocks.Clear();}}
                    catch(Exception error)when(error is InvalidDataException or InvalidOperationException){RetireImageTextProjection();document.Blocks.Clear();ready=false;}
            }
            if(!ready)document.Blocks.Add(new Paragraph(new Run(text)));
            if(!Same()){if(!Live())ClearSensitive();else refreshPending=true;return;}
            var previous=projected;long previousContentVersion=projectedContentVersion;var oldNative=RichInput.Document;projected=source;projectedContentVersion=target.ContentVersion;editable=false;composing=false;
            try{RichInput.Document=document;waitingTransaction=false;transactionRetry.Stop();}
            catch(InvalidOperationException)
            {
                // A handler can conceal, change source, or throw after attachment. Only an unchanged old
                // native document proves a pre-detach BeginChange rejection; never resurrect revoked state.
                if(!Live()){ClearSensitive();return;}
                if(!Same()){refreshPending=true;return;}
                if(!ReferenceEquals(RichInput.Document,oldNative)){projected=null;refreshPending=true;return;}
                projected=previous;projectedContentVersion=previousContentVersion;waitingTransaction=true;RichInput.IsReadOnly=true;transactionRetry.Start();return;
            }
            editable=ready;
            // A native setter raises external handlers. Never reattach this local document after that boundary.
            if(!Same()){if(!Live())ClearSensitive();else refreshPending=true;return;}
            projectionSource=source;
            RichInput.IsReadOnly=!ready;if(!Same()){if(!Live())ClearSensitive();else refreshPending=true;return;}
            toolbar.IsEnabled=ready;if(!Same()){if(!Live())ClearSensitive();else refreshPending=true;return;}
            state.Text=ready?"서식 원문을 암호 저장합니다. 외부 링크·이미지는 자동 실행하지 않습니다.":limitation;
            if(Same()){RichInput.IsUndoEnabled=ready;if(!Same()){if(!Live())ClearSensitive();else refreshPending=true;}}else if(!Live())ClearSensitive();else refreshPending=true;
        }
        finally
        {
            rebuilding=false;
            if(refreshPending&&!disposed)ScheduleRefresh();
        }
    }
    private void ScheduleRefresh()
    {
        if(disposed)return;editable=false;RichInput.IsReadOnly=true;QueueRefresh();
    }
    private void QueueRefresh()
    {
        if(disposed)return;long scheduled=projectionGeneration;
        // EventDepth guards the full native routed event even through nested dispatcher/OLE pumps.
        Dispatcher.BeginInvoke(new Action(()=>{if(scheduled==projectionGeneration&&!disposed){refreshPending=false;if(Live())PublishProjection();else ClearSensitive();}}));
    }
    private static SolidColorBrush ColorBrush(string hex)
    {
        byte Part(int offset)=>byte.Parse(hex.AsSpan(offset,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        return new(hex.Length==9?Color.FromArgb(Part(1),Part(3),Part(5),Part(7)):Color.FromRgb(Part(1),Part(3),Part(5)));
    }
    private static void RenderRuns(Paragraph paragraph,JsonElement owner)
    {
        foreach(var item in owner.GetProperty("runs").EnumerateArray())
        {
            var run=new Run(item.GetProperty("text").GetString()!);
            if(item.TryGetProperty("bold",out var bold)&&bold.GetBoolean())run.FontWeight=FontWeights.Bold;
            var decorations=new TextDecorationCollection();if(item.TryGetProperty("underline",out var underline)&&underline.GetBoolean())decorations.Add(TextDecorations.Underline);if(item.TryGetProperty("strike",out var strike)&&strike.GetBoolean())decorations.Add(TextDecorations.Strikethrough);if(decorations.Count!=0)run.TextDecorations=decorations;
            if(item.TryGetProperty("fontSize",out var size))run.FontSize=size.GetDouble();
            if(item.TryGetProperty("fontFamily",out var font)){string name=font.GetString()!;if(!SafeFonts.Contains(name))throw new InvalidDataException("Font is not installed/allowlisted");run.FontFamily=new FontFamily(name);}
            if(item.TryGetProperty("foreground",out var foreground))run.Foreground=ColorBrush(foreground.GetString()!);if(item.TryGetProperty("background",out var background))run.Background=ColorBrush(background.GetString()!);
            if(item.TryGetProperty("link",out var link))run.SetValue(LinkProperty,link.GetString());paragraph.Inlines.Add(run);
        }
    }
    private static Block RenderBlock(JsonElement node)
    {
        switch(node.GetProperty("type").GetString())
        {
            case "paragraph":var paragraph=new Paragraph{Margin=new(0,0,0,4),TextAlignment=TextAlignment.Left,TextIndent=0};RenderRuns(paragraph,node);return paragraph;
            case "list":case "checklist":
                bool checklist=node.GetProperty("type").GetString()=="checklist";var list=new List{MarkerStyle=checklist?TextMarkerStyle.None:node.GetProperty("ordered").GetBoolean()?TextMarkerStyle.Decimal:TextMarkerStyle.Disc,Margin=new(16,0,0,4)};list.SetValue(ChecklistProperty,checklist);
                foreach(var item in node.GetProperty("items").EnumerateArray()){var line=new Paragraph{Margin=new(0),TextAlignment=TextAlignment.Left,TextIndent=0};if(checklist){var prefix=new Run(item.GetProperty("checked").GetBoolean()?"[x] ":"[ ] ");prefix.SetValue(ChecklistPrefixProperty,true);line.Inlines.Add(prefix);}RenderRuns(line,item);list.ListItems.Add(new ListItem(line));}return list;
            case "table":
                var table=new Table{CellSpacing=0,Margin=new(0,0,0,4)};var group=new TableRowGroup();table.RowGroups.Add(group);
                foreach(var row in node.GetProperty("rows").EnumerateArray()){var native=new TableRow();group.Rows.Add(native);foreach(var cell in row.EnumerateArray()){var line=new Paragraph{Margin=new(0),TextAlignment=TextAlignment.Left,TextIndent=0};RenderRuns(line,cell);native.Cells.Add(new TableCell(line){BorderBrush=Brushes.Gray,BorderThickness=new(1),Padding=new(4)});}}return table;
            default:throw new InvalidDataException("Unsupported rich node");
        }
    }
    private static object? Explicit(TextElement element,DependencyProperty property)
    {
        for(DependencyObject? next=element;next is TextElement text;next=LogicalTreeHelper.GetParent(text))
        {object value=text.ReadLocalValue(property);if(value!=DependencyProperty.UnsetValue)return value;}
        return null;
    }
    private static bool Decoration(TextElement element,TextDecorationLocation location)
    {
        for(DependencyObject? next=element;next is Inline inline;next=LogicalTreeHelper.GetParent(inline))if(inline.TextDecorations?.Any(d=>d.Location==location)==true)return true;
        return false;
    }
    private static JsonObject SerializeRun(Inline inline,string text)
    {
        if(text.Length>RichDocumentCodec.MaxText||!RichDocumentCodec.IsWellFormedUnicode(text))throw new InvalidDataException("Malformed native Unicode");
        if(inline.FontStyle!=FontStyles.Normal||inline.FontStretch!=FontStretches.Normal||inline.BaselineAlignment!=BaselineAlignment.Baseline||inline.FontWeight!=FontWeights.Normal&&inline.FontWeight!=FontWeights.Bold)throw new InvalidDataException("Unsupported native style");
        for(DependencyObject? next=inline;next is Inline ancestor;next=LogicalTreeHelper.GetParent(ancestor))
            if(ancestor.TextDecorations?.Any(d=>d.Location is not (TextDecorationLocation.Underline or TextDecorationLocation.Strikethrough))==true)throw new InvalidDataException("Unsupported native decoration");
        var result=new JsonObject{["text"]=text};if(inline.FontWeight==FontWeights.Bold)result["bold"]=true;if(Decoration(inline,TextDecorationLocation.Underline))result["underline"]=true;if(Decoration(inline,TextDecorationLocation.Strikethrough))result["strike"]=true;
        if(Explicit(inline,TextElement.FontSizeProperty) is double size)result["fontSize"]=size;
        if(Explicit(inline,TextElement.FontFamilyProperty) is FontFamily font){if(!SafeFonts.Contains(font.Source))throw new InvalidDataException("Unsafe/uninstalled font");result["fontFamily"]=font.Source;}
        foreach(var pair in new[]{("foreground",TextElement.ForegroundProperty),("background",TextElement.BackgroundProperty)})
            if(Explicit(inline,pair.Item2) is Brush brush){if(brush is not SolidColorBrush solid)throw new InvalidDataException("Unsupported native brush");result[pair.Item1]=solid.Color.ToString(CultureInfo.InvariantCulture);}
        if(inline.GetValue(LinkProperty) is string link)result["link"]=link;return result;
    }
    private static JsonArray ReadRuns(Paragraph paragraph,ref int count,Run? scaffold=null)
    {
        if(paragraph.TextAlignment!=TextAlignment.Left||paragraph.TextIndent!=0)throw new InvalidDataException($"Unsupported paragraph layout (alignment={paragraph.TextAlignment}, indent={paragraph.TextIndent})");
        var result=new JsonArray();var stack=new Stack<(Inline Element,int Depth)>();foreach(var child in paragraph.Inlines.Reverse())stack.Push((child,0));
        while(stack.TryPop(out var entry))
        {
            if(++count>8192||entry.Depth>32)throw new InvalidDataException("Native inline budget");
            switch(entry.Element)
            {
                case Hyperlink:throw new InvalidDataException("Active native link refused");
                case Run run:if(!ReferenceEquals(run,scaffold))result.Add(SerializeRun(run,run.Text));break;
                case LineBreak line:result.Add(SerializeRun(line,"\n"));break;
                case Span span:foreach(var child in span.Inlines.Reverse())stack.Push((child,entry.Depth+1));break;
                default:throw new InvalidDataException("Native embedded content refused");
            }
        }
        return result;
    }
    private static Paragraph OnlyParagraph(BlockCollection blocks)=>blocks.Count==1&&blocks.FirstBlock is Paragraph paragraph?paragraph:throw new InvalidDataException("Native nested/multi-paragraph item is outside supported model");
    // Compare semantics before allowing edits. Formatting defaults stay distinct from view defaults.
    private static bool Equivalent(StyledDocument left,StyledDocument right)
    {
        JsonNode Normalize(StyledDocument source)
        {
            var root=JsonNode.Parse(source.SourceJson)!;
            void Visit(JsonNode? node)
            {
                if(node is JsonObject value)
                {
                    if(value["runs"] is JsonArray runs)
                    {
                        var merged=new JsonArray();JsonObject? previous=null;
                        foreach(var raw in runs)
                        {
                            var run=(JsonObject)raw!.DeepClone();foreach(var flag in new[]{"bold","underline","strike"})if(run[flag]?.GetValue<bool>()==false)run.Remove(flag);
                            foreach(var color in new[]{"foreground","background"})if(run[color] is JsonValue hex){string text=hex.GetValue<string>().ToUpperInvariant();if(text.Length==7)text="#FF"+text[1..];run[color]=text;}
                            string textRun=run["text"]!.GetValue<string>();var style=(JsonObject)run.DeepClone();style.Remove("text");var oldStyle=previous is null?null:(JsonObject)previous.DeepClone();oldStyle?.Remove("text");
                            if(previous is not null&&JsonNode.DeepEquals(style,oldStyle))previous["text"]=previous["text"]!.GetValue<string>()+textRun;
                            else{merged.Add(run);previous=run;}
                        }
                        value["runs"]=merged;
                    }
                    foreach(var child in value.ToArray())Visit(child.Value);
                }
                else if(node is JsonArray array)foreach(var child in array)Visit(child);
            }
            Visit(root);return root;
        }
        return JsonNode.DeepEquals(Normalize(left),Normalize(right));
    }
    public bool TryGetCollapsedBlockBoundary(out int boundary)
    {
        boundary=0;
        if(disposed||!editable||rebuilding||committing||composing||refreshPending||native.EventDepth!=0||note?.Document is not {SchemaVersion:1} source||!ReferenceEquals(projected,source)||!RichInput.Selection.IsEmpty)return false;
        var target=note!;long version=target.EditVersion,selection=caretSelectionGeneration;var start=RichInput.Selection.Start;var end=RichInput.Selection.End;
        bool Unchanged()=>!disposed&&editable&&!composing&&ReferenceEquals(note,target)&&ReferenceEquals(target.Document,source)&&ReferenceEquals(projected,source)&&target.EditVersion==version&&selection==caretSelectionGeneration&&RichInput.Selection.IsEmpty&&RichInput.Selection.Start.CompareTo(start)==0&&RichInput.Selection.End.CompareTo(end)==0;
        if(!Live()||!Unchanged())return false;
        DependencyObject? cursor=start.Paragraph;
        while(cursor is FrameworkContentElement element&&element.Parent is not FlowDocument)cursor=element.Parent;
        if(cursor is not Block block||!ReferenceEquals(block.Parent,RichInput.Document))return false;
        int index=0;foreach(var candidate in RichInput.Document.Blocks){if(ReferenceEquals(candidate,block)){boundary=index+1;return Live()&&Unchanged();}index++;}
        return false;
    }
    private StyledDocument CaptureDocument()
    {
        if(note?.Document?.SchemaVersion==2&&imageEditSession is not null)return CaptureImageTextDocument(RichInput.Document);
        if(note?.Document?.SchemaVersion!=1)throw new InvalidDataException("Image documents cannot be captured as native v1 text");
        return CaptureNativeDocument(RichInput.Document);
    }
    private static StyledDocument CaptureNativeDocument(FlowDocument document)
    {
        var nodes=new JsonArray();int count=0;
        foreach(var block in document.Blocks)
        {
            if(++count>1024)throw new InvalidDataException("Native block budget");
            nodes.Add(CaptureNativeBlock(block,ref count));
        }
        return new(1,new JsonObject{["nodes"]=nodes}.ToJsonString());
    }
    private static JsonObject CaptureNativeBlock(Block block,ref int count)
    {
            switch(block)
            {
                case Paragraph paragraph:return new JsonObject{["type"]="paragraph",["runs"]=ReadRuns(paragraph,ref count)};
                case List list:
                    bool checklist=(bool)list.GetValue(ChecklistProperty);if(list.StartIndex!=1||checklist&&list.MarkerStyle!=TextMarkerStyle.None||!checklist&&list.MarkerStyle is not (TextMarkerStyle.Disc or TextMarkerStyle.Decimal))throw new InvalidDataException("Unsupported native list marker");
                    var items=new JsonArray();foreach(var item in list.ListItems)
                    {
                        var paragraph=OnlyParagraph(item.Blocks);Run? scaffold=null;
                        if(checklist){scaffold=paragraph.Inlines.FirstInline as Run;if(scaffold is null||!(bool)scaffold.GetValue(ChecklistPrefixProperty)||scaffold.Text is not ("[ ] " or "[x] "))throw new InvalidDataException("Checklist scaffold missing/changed");}
                        var runs=ReadRuns(paragraph,ref count,scaffold);var entry=new JsonObject{["runs"]=runs};if(scaffold is not null)entry["checked"]=scaffold.Text=="[x] ";
                        items.Add(entry);
                    }
                    var node=new JsonObject{["type"]=checklist?"checklist":"list",["items"]=items};if(!checklist)node["ordered"]=list.MarkerStyle==TextMarkerStyle.Decimal;return node;
                case Table table:
                    var rows=new JsonArray();foreach(var group in table.RowGroups)foreach(var row in group.Rows){var cells=new JsonArray();foreach(var cell in row.Cells){if(cell.ColumnSpan!=1||cell.RowSpan!=1)throw new InvalidDataException("Merged cells outside supported model");cells.Add(new JsonObject{["runs"]=ReadRuns(OnlyParagraph(cell.Blocks),ref count)});}rows.Add(cells);}return new JsonObject{["type"]="table",["rows"]=rows};
                default:throw new InvalidDataException("Unsupported native block");
            }
    }
    private void Changed(object sender,TextChangedEventArgs e)
    {
        if(composing||checklistEnterActive)return;CommitNative(e.UndoAction is UndoAction.Undo or UndoAction.Redo);
    }
    private void CompositionStart(object sender,TextCompositionEventArgs e){if(imageEditContext is not null&&!ImageTextCommandAllowed()){e.Handled=true;return;}if(Live()&&editable&&!rebuilding){compositionToken++;composing=true;}}
    private void CompositionUpdate(object sender,TextCompositionEventArgs e){if(!composing)CompositionStart(sender,e);}
    private void CompositionComplete(object sender,TextCompositionEventArgs e)
    {
        if(!composing)return;long generation=projectionGeneration,token=compositionToken;var target=note;var source=target?.Document;long? version=target?.ContentVersion;
        Dispatcher.BeginInvoke(new Action(()=>
        {
            if(disposed||generation!=projectionGeneration||token!=compositionToken||!composing)return;
            composing=false;
            if(Live()&&ReferenceEquals(note,target)&&ReferenceEquals(target!.Document,source)&&target.ContentVersion==version)CommitNative();
            else if(Live())Rebuild();else ClearSensitive();
        }));
    }
    private void CommitNative(bool restoreProjectionSource=false)
    {
        if(rebuilding||committing)return;
        using var phase=RichImageTextPhase.Enter(Dispatcher);
        try{BeginImageTextNativeGuard();if(!Live()||!editable)return;var captured=CaptureDocument();
            // Native Undo restores the model, but capture has its own JSON field order/run
            // normalization. Returning to the projection's original model must restore
            // its owned source bytes, including styled empty runs and JSON field order.
            if(restoreProjectionSource&&projectionSource is { } original&&Equivalent(original,captured))captured=original;
            long expected=note!.ContentVersion+(note.Document==captured?0:1);committing=true;if(imageEditSession is not null&&note.Document?.SchemaVersion==2){CommitImageTextDocument(captured);return;}workspace!.SetRichDocument(note,captured);if(Live()){if(note!.Document==captured&&note.ContentVersion==expected){projected=captured;projectedContentVersion=expected;}else Rebuild();}}
        catch{if(Live()){Rebuild();notice?.Invoke("지원하지 않는 서식/내용 또는 한도입니다. 편집을 적용하지 않고 기존 문서와 이력을 보존했습니다.");}}
        finally{committing=false;EndImageTextNativeGuard();}
    }
    private void Format(DependencyProperty property,object value)
    {if(!Live()||!editable||!ImageTextCommandAllowed())return;try{var allowed=CaptureImageTextCommandGuard();if(allowed?.Invoke()==false)return;RichInput.Selection.ApplyPropertyValue(property,value);if(Live())RichInput.Focus();}catch{if(Live())Rebuild();}}
    public void ApplyBold()=>Format(TextElement.FontWeightProperty,RichInput.Selection.GetPropertyValue(TextElement.FontWeightProperty).Equals(FontWeights.Bold)?FontWeights.Normal:FontWeights.Bold);
    public void ApplyFontSize(double size){if(!double.IsFinite(size)||size is <8 or >96)return;Format(TextElement.FontSizeProperty,size);}
    public void ApplyFontFamily(string name)
    {if(SafeFontName(name)&&SafeFonts.Contains(name))Format(TextElement.FontFamilyProperty,new FontFamily(name));}
    private void ToggleDecoration(TextDecorationLocation location)
    {
        if(!Live()||!editable)return;object value=RichInput.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        if(value==DependencyProperty.UnsetValue){notice?.Invoke("서식이 다른 영역은 한 영역씩 선택하세요. 기존 서식은 보존합니다.");return;}
        var decorations=value is TextDecorationCollection old?old.Clone():new TextDecorationCollection();bool remove=decorations.Any(d=>d.Location==location);
        foreach(var decoration in decorations.Where(d=>d.Location==location).ToArray())decorations.Remove(decoration);
        if(!remove)decorations.Add(location==TextDecorationLocation.Underline?TextDecorations.Underline:TextDecorations.Strikethrough);Format(Inline.TextDecorationsProperty,decorations);
    }
    public void ApplyUnderline()=>ToggleDecoration(TextDecorationLocation.Underline);
    public void ApplyStrike()=>ToggleDecoration(TextDecorationLocation.Strikethrough);
    private void ApplyColor(string hex,DependencyProperty property)
    {if(hex.Length is not (7 or 9)||hex[0]!='#'||hex.Skip(1).Any(c=>!char.IsAsciiHexDigit(c)))return;var brush=ColorBrush(hex);brush.Freeze();Format(property,brush);}
    public void ApplyForeground(string hex)=>ApplyColor(hex,TextElement.ForegroundProperty);
    public void ApplyHighlight(string hex)=>ApplyColor(hex,TextElement.BackgroundProperty);
    public void ToggleList(bool ordered)
    {if(!Live()||!editable||!ImageTextCommandAllowed())return;try{(ordered?EditingCommands.ToggleNumbering:EditingCommands.ToggleBullets).Execute(null,RichInput);}catch{if(Live())Rebuild();}}
    private void NativeChange(Action edit)
    {
        if(!Live()||!editable||!ImageTextCommandAllowed())return;var allowed=CaptureImageTextCommandGuard();RichInput.BeginChange();try{if(allowed?.Invoke()!=false)edit();}catch{if(Live())Rebuild();}finally{RichInput.EndChange();}if(Live())RichInput.Focus();
    }
    public void InsertChecklist()
    {
        if(!Live()||!editable||RichInput.Selection.Start.Paragraph is not Paragraph paragraph||paragraph.Parent is not FlowDocument document||!ReferenceEquals(document,RichInput.Document))return;
        NativeChange(()=>
        {
            var list=new List{MarkerStyle=TextMarkerStyle.None,Margin=new(16,0,0,4)};list.SetValue(ChecklistProperty,true);document.Blocks.InsertBefore(paragraph,list);document.Blocks.Remove(paragraph);
            var prefix=new Run("[ ] ");prefix.SetValue(ChecklistPrefixProperty,true);if(paragraph.Inlines.FirstInline is Inline first)paragraph.Inlines.InsertBefore(first,prefix);else paragraph.Inlines.Add(prefix);
            list.ListItems.Add(new ListItem(paragraph));RichInput.CaretPosition=paragraph.ContentStart;
        });
    }
    public void ToggleChecked()
    {
        if(!Live()||!editable)return;var paragraph=RichInput.CaretPosition.Paragraph;
        if(paragraph?.Parent is not ListItem item||item.Parent is not List list||!(bool)list.GetValue(ChecklistProperty)||paragraph.Inlines.FirstInline is not Run prefix||!(bool)prefix.GetValue(ChecklistPrefixProperty)||prefix.Text is not ("[ ] " or "[x] "))return;
        NativeChange(()=>prefix.Text=prefix.Text=="[ ] "?"[x] ":"[ ] ");
    }
    public void InsertTable(int rows,int columns)
    {
        if(!Live()||!editable||rows is <1 or >32||columns is <1 or >16)return;
        NativeChange(()=>
        {
            var table=new Table{CellSpacing=0};var group=new TableRowGroup();table.RowGroups.Add(group);
            for(int row=0;row<rows;row++){var nativeRow=new TableRow();group.Rows.Add(nativeRow);for(int column=0;column<columns;column++)nativeRow.Cells.Add(new TableCell(new Paragraph(new Run("")){Margin=new(0),TextAlignment=TextAlignment.Left,TextIndent=0}){BorderBrush=Brushes.Gray,BorderThickness=new(1),Padding=new(4)});}
            RichInput.Document.Blocks.Add(table);RichInput.CaretPosition=group.Rows[0].Cells[0].ContentStart;
        });
    }
    public void ApplyLink(string url)
    {
        if(!Live()||!editable||url.Length>2048||url.Any(char.IsControl)||!Uri.TryCreate(url,UriKind.Absolute,out var parsed)||parsed.Scheme is not ("http" or "https")||parsed.UserInfo.Length!=0||RichInput.Selection.IsEmpty||RichInput.Selection.Start.Paragraph!=RichInput.Selection.End.Paragraph)return;
        NativeChange(()=>{var span=new Span(RichInput.Selection.Start,RichInput.Selection.End);span.SetValue(LinkProperty,url);});
    }
    public void ApplyPreferences(UiPreferences preferences)
    {
        if(disposed)return;rebuilding=true;try{RichInput.Background=preferences.DarkMode?new SolidColorBrush(Color.FromRgb(42,47,57)):Brushes.White;RichInput.Foreground=RichInput.CaretBrush=preferences.DarkMode?Brushes.White:Brushes.Black;RichInput.Document.Foreground=RichInput.Foreground;RichInput.LayoutTransform=new ScaleTransform(preferences.FontSize/14,preferences.FontSize/14);}finally{rebuilding=false;}
    }
    public void PasteData(IDataObject data)
    {
        if(!Live()||!editable||!ImageTextCommandAllowed())return;
        var target=note!;var owner=workspace!;var source=target.Document;long version=target.EditVersion;var native=RichInput.Document;var start=RichInput.Selection.Start;var end=RichInput.Selection.End;var imageAllowed=CaptureImageTextCommandGuard();
        bool Same()=>Live()&&ReferenceEquals(note,target)&&ReferenceEquals(workspace,owner)&&ReferenceEquals(target.Document,source)&&target.EditVersion==version&&ReferenceEquals(RichInput.Document,native)&&RichInput.Selection.Start.CompareTo(start)==0&&RichInput.Selection.End.CompareTo(end)==0&&imageAllowed?.Invoke()!=false;
        try
        {
            bool present=data.GetDataPresent(DataFormats.UnicodeText,false);if(!Same()||!present)return;
            object value=data.GetData(DataFormats.UnicodeText,false);if(!Same()||!ImageTextCommandAllowed()||value is not string text||text.Length>RichDocumentCodec.MaxText||!RichDocumentCodec.IsWellFormedUnicode(text))return;
            string normalized=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');if(!Same())return;RichInput.Selection.Text=normalized;
        }
        catch{if(Live()){Rebuild();notice?.Invoke("붙여넣기 실패 — 기존 문서를 보존했습니다.");}}
    }
    private void Pasting(object sender,DataObjectPastingEventArgs e){e.CancelCommand();if(!e.IsDragDrop)PasteData(e.DataObject);}
    private static void RejectDrop(object sender,DragEventArgs e){e.Effects=DragDropEffects.None;e.Handled=true;}
    private void PreviewCommand(object sender,ExecutedRoutedEventArgs e)
    {
        if(imageEditContext is not null&&e.Command!=ApplicationCommands.Undo&&e.Command!=ApplicationCommands.Redo&&e.Command!=ApplicationCommands.Copy&&!ImageDeletionCommandAllowed(e.Command)){e.Handled=true;return;}
        if(e.Command==EditingCommands.EnterParagraphBreak&&!checklistEnterActive&&RichInput.CaretPosition.Paragraph?.Parent is ListItem item&&item.Parent is List list&&(bool)list.GetValue(ChecklistProperty))
        {e.Handled=true;EnterChecklistItem(item,list);return;}
        if(e.Command is RoutedCommand command && command.Name is "ToggleItalic" or "AlignCenter" or "AlignRight" or "AlignJustify" or "IncreaseIndentation" or "DecreaseIndentation" or "ToggleSubscript" or "ToggleSuperscript")e.Handled=true;
    }
    public void ClearSensitive()
    {
        if(disposed)return;using var phase=RichImageTextPhase.Enter(Dispatcher);var ownedNativeDocuments=CaptureOwnedNativeDocumentsForCleanup();disposed=true;projectionGeneration++;compositionToken++;refreshPending=false;waitingTransaction=composing=false;transactionRetry.Stop();native.EventFinished=null;native.MutationStarting=null;EndImageTextNativeGuard();editable=false;rebuilding=true;
        var oldNote=note;ClearImageTextEditing();projected=projectionSource=null;projectedContentVersion=0;note=null;workspace=null;current=null;notice=null;if(oldNote is not null)oldNote.PropertyChanged-=DraftChanged;
        // Drop all ownership before invoking native text operations, which can raise arbitrary handlers.
        foreach(Action detach in new Action[]{()=>RichInput.TextChanged-=Changed,()=>RichInput.SelectionChanged-=CaretSelectionChanged,()=>DataObject.RemovePastingHandler(RichInput,Pasting),
            ()=>RichInput.RemoveHandler(TextCompositionManager.PreviewTextInputStartEvent,new TextCompositionEventHandler(CompositionStart)),
            ()=>RichInput.RemoveHandler(TextCompositionManager.PreviewTextInputUpdateEvent,new TextCompositionEventHandler(CompositionUpdate)),
            ()=>RichInput.RemoveHandler(TextCompositionManager.PreviewTextInputEvent,new TextCompositionEventHandler(CompositionComplete)),
            ()=>RichInput.PreviewMouseLeftButtonUp-=LinkClick,()=>RichInput.PreviewDragOver-=RejectDrop,()=>RichInput.PreviewDrop-=RejectDrop,()=>CommandManager.RemovePreviewExecutedHandler(RichInput,PreviewCommand)})
            try{detach();}catch(Exception){CleanupErrorCode="RICH_VIEW_CLEANUP_FAILURE";}
        foreach(Action cleanup in new Action[]{()=>Visibility=Visibility.Collapsed,()=>RichInput.IsUndoEnabled=false,()=>RichInput.IsReadOnly=true,()=>RichInput.DataContext=null,()=>state.Text="",()=>toolbar.IsEnabled=false,()=>linkInput.IsUndoEnabled=false,linkInput.Clear})
        {
            try{cleanup();}catch(Exception){CleanupErrorCode="RICH_VIEW_CLEANUP_FAILURE";}
        }
        // A replacement can detach the original graph before concealment. Redact
        // every exact owned graph independently so later reattachment stays empty.
        foreach(var document in ownedNativeDocuments)
            try{document.Blocks.Clear();}catch(Exception){CleanupErrorCode="RICH_VIEW_CLEANUP_FAILURE";}
        try{RichInput.Document.Blocks.Clear();}catch(Exception){CleanupErrorCode="RICH_VIEW_CLEANUP_FAILURE";}
        rebuilding=false;
    }
    public void Dispose()=>ClearSensitive();
}
