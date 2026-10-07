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
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
namespace MemoApp.Windows;
// Only our bounded canonical model is loaded. Never parse user XAML/RTF or URI resources.
public sealed class StructuredNoteEditor:UserControl,IDisposable
{
    private EditingWorkspace? workspace;
    private NoteDraft? note;
    private Func<bool>? current;
    private Action<string>? notice;
    private StyledDocument? projected;
    private bool rebuilding,committing,disposed,editable,refreshPending;
    private long projectionGeneration;
    private readonly TextBlock state=new(){TextWrapping=TextWrapping.Wrap,Margin=new(4)};
    private readonly WrapPanel toolbar=new(){Margin=new(0,0,0,4)};
    private static readonly DependencyProperty LinkProperty=DependencyProperty.RegisterAttached("CanonicalLink",typeof(string),typeof(StructuredNoteEditor),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.Inherits));
    private static readonly DependencyProperty ChecklistPrefixProperty=DependencyProperty.RegisterAttached("CanonicalChecklistPrefix",typeof(bool),typeof(StructuredNoteEditor),new(false));
    private static readonly DependencyProperty ChecklistProperty=DependencyProperty.RegisterAttached("CanonicalChecklist",typeof(bool),typeof(StructuredNoteEditor),new(false));
    private static readonly HashSet<string> SafeFonts=Fonts.SystemFontFamilies.Select(f=>f.Source).Where(SafeFontName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public RichTextBox RichInput{get;}=new(){AllowDrop=false,IsUndoEnabled=true,UndoLimit=100,AcceptsTab=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new(8)};
    public StructuredNoteEditor(EditingWorkspace workspace,NoteDraft note,Func<bool> current,Action<string> notice)
    {
        this.workspace=workspace;this.note=note;this.current=current;this.notice=notice;
        var root=new DockPanel();DockPanel.SetDock(toolbar,Dock.Top);root.Children.Add(toolbar);DockPanel.SetDock(state,Dock.Top);root.Children.Add(state);root.Children.Add(RichInput);Content=root;
        Button("굵게",ApplyBold);var sizes=new ComboBox{Width=60,ItemsSource=new double[]{8,10,12,14,16,18,22,28,36,48,72,96},SelectedItem=14d};sizes.SelectionChanged+=(_,_)=>{if(!rebuilding&&sizes.SelectedItem is double size)ApplyFontSize(size);};toolbar.Children.Add(sizes);
        RichInput.TextChanged+=Changed;DataObject.AddPastingHandler(RichInput,Pasting);
        RichInput.PreviewDragOver+=RejectDrop;RichInput.PreviewDrop+=RejectDrop;
        CommandManager.AddPreviewExecutedHandler(RichInput,PreviewCommand);
        note.PropertyChanged+=DraftChanged;PublishProjection();
    }
    private void Button(string label,Action action){var button=new Button{Content=label,Padding=new(6,3,6,3),Margin=new(0,0,4,0),Focusable=false};button.Click+=(_,_)=>action();toolbar.Children.Add(button);}
    private static bool SafeFontName(string name)=>name.Length is >0 and <=128&&RichDocumentCodec.IsWellFormedUnicode(name)&&name.All(c=>char.IsLetterOrDigit(c)||c is ' ' or '-' or '_' or '.');
    private bool Live()
    {
        var target=note;return !disposed&&target is {IsClosed:false,IsDeleted:false,Mode:"rich"}&&current?.Invoke()==true&&!disposed&&ReferenceEquals(note,target)&&target is {IsClosed:false,IsDeleted:false,Mode:"rich"};
    }
    private void DraftChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(disposed)return;if(!Live()){ClearSensitive();return;}
        if(!committing && e.PropertyName is nameof(NoteDraft.Document) or nameof(NoteDraft.Mode) && projected!=note!.Document)Rebuild();
    }
    private void Rebuild()
    {
        if(!Live()){ClearSensitive();return;}refreshPending=true;ScheduleRefresh();
    }
    private void PublishProjection()
    {
        if(!Live()){ClearSensitive();return;}
        if(rebuilding){refreshPending=true;return;}
        var target=note!;var owner=workspace!;var source=target.Document!;string text=target.Text;long version=target.EditVersion,generation=++projectionGeneration;
        bool Same()=>Live()&&generation==projectionGeneration&&ReferenceEquals(note,target)&&ReferenceEquals(workspace,owner)&&ReferenceEquals(target.Document,source)&&target.EditVersion==version;
        rebuilding=true;refreshPending=false;
        try
        {
            // Build and verify a detached document before its single native publish.
            var document=new FlowDocument{FontFamily=new FontFamily("Segoe UI"),FontSize=14,PagePadding=new(0),Foreground=RichInput.Foreground};
            var info=RichDocumentCodec.Inspect(source);bool ready=false;string limitation=info.Limitation;
            if(info.Supported)
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
            if(!ready)document.Blocks.Add(new Paragraph(new Run(text)));
            if(!Same()){refreshPending=Live();return;}
            RichInput.IsUndoEnabled=false;if(!Same()){refreshPending=Live();return;}
            projected=source;editable=ready;RichInput.Document=document;
            // A native setter raises external handlers. Never reattach this local document after that boundary.
            if(!Same()){refreshPending=Live();return;}
            RichInput.IsReadOnly=!ready;toolbar.IsEnabled=ready;state.Text=ready?"서식 원문을 암호 저장합니다. 외부 링크·이미지는 자동 실행하지 않습니다.":limitation;
            if(Same())RichInput.IsUndoEnabled=ready;else refreshPending=Live();
        }
        finally
        {
            rebuilding=false;
            if(refreshPending&&!disposed)ScheduleRefresh();
        }
    }
    private void ScheduleRefresh()
    {
        if(disposed)return;long scheduled=projectionGeneration;editable=false;RichInput.IsReadOnly=true;
        // RichTextBox cannot replace Document while its TextChanged PendingUndoAction is active.
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
            case "paragraph":var paragraph=new Paragraph{Margin=new(0,0,0,4)};RenderRuns(paragraph,node);return paragraph;
            case "list":case "checklist":
                bool checklist=node.GetProperty("type").GetString()=="checklist";var list=new List{MarkerStyle=checklist?TextMarkerStyle.None:node.GetProperty("ordered").GetBoolean()?TextMarkerStyle.Decimal:TextMarkerStyle.Disc,Margin=new(16,0,0,4)};list.SetValue(ChecklistProperty,checklist);
                foreach(var item in node.GetProperty("items").EnumerateArray()){var line=new Paragraph{Margin=new(0)};if(checklist){var prefix=new Run(item.GetProperty("checked").GetBoolean()?"[x] ":"[ ] ");prefix.SetValue(ChecklistPrefixProperty,true);line.Inlines.Add(prefix);}RenderRuns(line,item);list.ListItems.Add(new ListItem(line));}return list;
            case "table":
                var table=new Table{CellSpacing=0,Margin=new(0,0,0,4)};var group=new TableRowGroup();table.RowGroups.Add(group);
                foreach(var row in node.GetProperty("rows").EnumerateArray()){var native=new TableRow();group.Rows.Add(native);foreach(var cell in row.EnumerateArray()){var line=new Paragraph{Margin=new(0)};RenderRuns(line,cell);native.Cells.Add(new TableCell(line){BorderBrush=Brushes.Gray,BorderThickness=new(1),Padding=new(4)});}}return table;
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
        if(inline.FontStyle!=FontStyles.Normal||inline.FontWeight!=FontWeights.Normal&&inline.FontWeight!=FontWeights.Bold)throw new InvalidDataException("Unsupported native style");
        var result=new JsonObject{["text"]=text};if(inline.FontWeight==FontWeights.Bold)result["bold"]=true;if(Decoration(inline,TextDecorationLocation.Underline))result["underline"]=true;if(Decoration(inline,TextDecorationLocation.Strikethrough))result["strike"]=true;
        if(Explicit(inline,TextElement.FontSizeProperty) is double size)result["fontSize"]=size;
        if(Explicit(inline,TextElement.FontFamilyProperty) is FontFamily font){if(!SafeFonts.Contains(font.Source))throw new InvalidDataException("Unsafe/uninstalled font");result["fontFamily"]=font.Source;}
        foreach(var pair in new[]{("foreground",TextElement.ForegroundProperty),("background",TextElement.BackgroundProperty)})
            if(Explicit(inline,pair.Item2) is Brush brush){if(brush is not SolidColorBrush solid)throw new InvalidDataException("Unsupported native brush");result[pair.Item1]=solid.Color.ToString(CultureInfo.InvariantCulture);}
        if(inline.GetValue(LinkProperty) is string link)result["link"]=link;return result;
    }
    private static JsonArray ReadRuns(Paragraph paragraph,ref int count,Run? scaffold=null)
    {
        if(paragraph.TextAlignment!=TextAlignment.Left||paragraph.TextIndent!=0)throw new InvalidDataException("Unsupported paragraph layout");
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
    private StyledDocument CaptureDocument()=>CaptureNativeDocument(RichInput.Document);
    private static StyledDocument CaptureNativeDocument(FlowDocument document)
    {
        var nodes=new JsonArray();int count=0;
        foreach(var block in document.Blocks)
        {
            if(++count>1024)throw new InvalidDataException("Native block budget");
            switch(block)
            {
                case Paragraph paragraph:nodes.Add(new JsonObject{["type"]="paragraph",["runs"]=ReadRuns(paragraph,ref count)});break;
                case List list:
                    bool checklist=(bool)list.GetValue(ChecklistProperty);if(list.StartIndex!=1||checklist&&list.MarkerStyle!=TextMarkerStyle.None||!checklist&&list.MarkerStyle is not (TextMarkerStyle.Disc or TextMarkerStyle.Decimal))throw new InvalidDataException("Unsupported native list marker");
                    var items=new JsonArray();foreach(var item in list.ListItems)
                    {
                        var paragraph=OnlyParagraph(item.Blocks);Run? scaffold=null;
                        if(checklist){scaffold=paragraph.Inlines.FirstInline as Run;if(scaffold is null||!(bool)scaffold.GetValue(ChecklistPrefixProperty)||scaffold.Text is not ("[ ] " or "[x] "))throw new InvalidDataException("Checklist scaffold missing/changed");}
                        var runs=ReadRuns(paragraph,ref count,scaffold);var entry=new JsonObject{["runs"]=runs};if(scaffold is not null)entry["checked"]=scaffold.Text=="[x] ";
                        items.Add(entry);
                    }
                    var node=new JsonObject{["type"]=checklist?"checklist":"list",["items"]=items};if(!checklist)node["ordered"]=list.MarkerStyle==TextMarkerStyle.Decimal;nodes.Add(node);break;
                case Table table:
                    var rows=new JsonArray();foreach(var group in table.RowGroups)foreach(var row in group.Rows){var cells=new JsonArray();foreach(var cell in row.Cells){if(cell.ColumnSpan!=1||cell.RowSpan!=1)throw new InvalidDataException("Merged cells outside supported model");cells.Add(new JsonObject{["runs"]=ReadRuns(OnlyParagraph(cell.Blocks),ref count)});}rows.Add(cells);}nodes.Add(new JsonObject{["type"]="table",["rows"]=rows});break;
                default:throw new InvalidDataException("Unsupported native block");
            }
        }
        return new(1,new JsonObject{["nodes"]=nodes}.ToJsonString());
    }
    private void Changed(object sender,TextChangedEventArgs e)
    {
        if(rebuilding||committing||!Live()||!editable)return;
        try{var captured=CaptureDocument();committing=true;workspace!.SetRichDocument(note!,captured);if(Live()){if(note!.Document==captured)projected=captured;else Rebuild();}}
        catch{if(Live()){Rebuild();notice?.Invoke("지원하지 않는 서식/내용 또는 한도입니다. 편집을 적용하지 않고 기존 문서와 이력을 보존했습니다.");}}
        finally{committing=false;}
    }
    private void Format(DependencyProperty property,object value)
    {if(!Live()||!editable)return;try{RichInput.Selection.ApplyPropertyValue(property,value);RichInput.Focus();}catch{if(Live())Rebuild();}}
    public void ApplyBold()=>Format(TextElement.FontWeightProperty,RichInput.Selection.GetPropertyValue(TextElement.FontWeightProperty).Equals(FontWeights.Bold)?FontWeights.Normal:FontWeights.Bold);
    public void ApplyFontSize(double size){if(!double.IsFinite(size)||size is <8 or >96)return;Format(TextElement.FontSizeProperty,size);}
    public void ApplyPreferences(UiPreferences preferences)
    {
        if(disposed)return;rebuilding=true;try{RichInput.Background=preferences.DarkMode?new SolidColorBrush(Color.FromRgb(42,47,57)):Brushes.White;RichInput.Foreground=RichInput.CaretBrush=preferences.DarkMode?Brushes.White:Brushes.Black;RichInput.Document.Foreground=RichInput.Foreground;RichInput.LayoutTransform=new ScaleTransform(preferences.FontSize/14,preferences.FontSize/14);}finally{rebuilding=false;}
    }
    public void PasteData(IDataObject data)
    {
        if(!Live()||!editable)return;
        var target=note!;var owner=workspace!;var source=target.Document;long version=target.EditVersion;var native=RichInput.Document;var start=RichInput.Selection.Start;var end=RichInput.Selection.End;
        bool Same()=>Live()&&ReferenceEquals(note,target)&&ReferenceEquals(workspace,owner)&&ReferenceEquals(target.Document,source)&&target.EditVersion==version&&ReferenceEquals(RichInput.Document,native)&&RichInput.Selection.Start.CompareTo(start)==0&&RichInput.Selection.End.CompareTo(end)==0;
        try
        {
            bool present=data.GetDataPresent(DataFormats.UnicodeText,false);if(!Same()||!present)return;
            object value=data.GetData(DataFormats.UnicodeText,false);if(!Same()||value is not string text||text.Length>RichDocumentCodec.MaxText||!RichDocumentCodec.IsWellFormedUnicode(text))return;
            string normalized=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');if(!Same())return;RichInput.Selection.Text=normalized;
        }
        catch{if(Live()){Rebuild();notice?.Invoke("붙여넣기 실패 — 기존 문서를 보존했습니다.");}}
    }
    private void Pasting(object sender,DataObjectPastingEventArgs e){e.CancelCommand();if(!e.IsDragDrop)PasteData(e.DataObject);}
    private static void RejectDrop(object sender,DragEventArgs e){e.Effects=DragDropEffects.None;e.Handled=true;}
    private void PreviewCommand(object sender,ExecutedRoutedEventArgs e)
    {
        if(e.Command is RoutedCommand command && command.Name is "ToggleItalic" or "AlignCenter" or "AlignRight" or "AlignJustify" or "IncreaseIndentation" or "DecreaseIndentation" or "ToggleSubscript" or "ToggleSuperscript")e.Handled=true;
    }
    public void ClearSensitive()
    {
        if(disposed)return;disposed=true;projectionGeneration++;refreshPending=false;editable=false;rebuilding=true;if(note is not null)note.PropertyChanged-=DraftChanged;
        RichInput.IsUndoEnabled=false;RichInput.IsReadOnly=true;RichInput.Document.Blocks.Clear();RichInput.DataContext=null;state.Text="";toolbar.IsEnabled=false;projected=null;note=null;workspace=null;current=null;notice=null;rebuilding=false;
    }
    public void Dispose()=>ClearSensitive();
}
