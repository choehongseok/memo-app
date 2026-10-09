using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MemoApp.Core.Editing;
namespace MemoApp.Windows;
public partial class StickyNoteWindow : Window
{
    private readonly NoteDraft draft;
    private double expandedHeight = 400;
    public DesktopWindowController? Placement {get;private set;}
    private bool restoringLayout,closed;
    private EditingWorkspace? workspace;
    private Func<bool>? current;
    private Action<string>? notice;
    private MarkdownNotePreview? markdownPreview;
    private StructuredNoteEditor? structuredEditor;
    private AttachmentPanel? attachmentPanel;
    private string? bodyMode;
    public void SetEditingContext(EditingWorkspace owner,Func<bool> valid,Action<string> status)
    {workspace=owner;current=valid;notice=status;ConfigureBody();}
    public void SetAttachmentContext(SaveCoordinator owner,Func<bool> valid,Action<string> status,Guid uiDeviceId=default)
    {
        ClearAttachmentPanel();if(closed||!valid()||draft.IsClosed||draft.IsDeleted)return;
        bool attached=false;AttachmentPanel? created=null;
        bool Current()=>!closed&&valid()&&!draft.IsClosed&&!draft.IsDeleted&&(!attached||ReferenceEquals(AttachmentHost.Content,created));
        created=new(owner,draft,Current,status,uiDeviceId);if(Current()){attachmentPanel=created;AttachmentHost.Content=created;attached=true;AttachmentHost.Visibility=FoldToggle.IsChecked==true?Visibility.Collapsed:Visibility.Visible;}else created.Dispose();
    }
    private void ClearAttachmentPanel()
    {
        var previous=attachmentPanel;attachmentPanel=null;previous?.Dispose();
        try{AttachmentHost.Content=null;}catch{}
        try{AttachmentHost.Visibility=Visibility.Collapsed;}catch{}
    }
    private void ClearMarkdownPreview()
    {var previous=markdownPreview;markdownPreview=null;MarkdownHost.Content=null;MarkdownHost.Visibility=Visibility.Collapsed;previous?.Dispose();}
    private void ClearStructuredEditor()
    {var previous=structuredEditor;structuredEditor=null;StructuredHost.Content=null;StructuredHost.Visibility=Visibility.Collapsed;previous?.Dispose();}
    private void ConfigureBody()
    {
        if(closed)return;
        if(draft.Mode=="rich")
        {
            BindingOperations.ClearBinding(BodyEditor,TextBox.TextProperty);BodyEditor.IsUndoEnabled=false;BodyEditor.IsReadOnly=true;BodyEditor.Clear();
            if(workspace is not null&&current?.Invoke()==true&&!draft.IsClosed&&!draft.IsDeleted)
            {
                BodyEditor.Visibility=Visibility.Collapsed;StructuredHost.Visibility=Visibility.Visible;
                if(structuredEditor is null)
                {
                    var owner=workspace;bool attached=false;StructuredNoteEditor? created=null;
                    bool Valid()=>!closed&&ReferenceEquals(workspace,owner)&&current?.Invoke()==true&&(!attached||ReferenceEquals(StructuredHost.Content,created));
                    created=new(owner,draft,Valid,message=>{if(Valid())notice?.Invoke(message);});
                    if(Valid()){structuredEditor=created;StructuredHost.Content=created;attached=true;}else created.Dispose();
                }
            }
            else{ClearStructuredEditor();BodyEditor.Text=draft.Text;BodyEditor.Visibility=Visibility.Visible;}
        }
        else
        {
            ClearStructuredEditor();BodyEditor.Visibility=FoldToggle.IsChecked==true?Visibility.Collapsed:Visibility.Visible;BodyEditor.IsReadOnly=false;
            if(bodyMode!=draft.Mode){BodyEditor.IsUndoEnabled=false;BindingOperations.ClearBinding(BodyEditor,TextBox.TextProperty);BodyEditor.Clear();}
            if(!BindingOperations.IsDataBound(BodyEditor,TextBox.TextProperty))BodyEditor.SetBinding(TextBox.TextProperty,new Binding(nameof(NoteDraft.Text)){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});BodyEditor.IsUndoEnabled=true;
        }
        if(draft.Mode=="markdown"&&workspace is not null&&current?.Invoke()==true&&!draft.IsClosed&&!draft.IsDeleted)
        {
            MarkdownHost.Visibility=Visibility.Visible;if(markdownPreview is null)
            {
                var owner=workspace;bool attached=false;MarkdownNotePreview? created=null;
                bool Valid()=>!closed&&ReferenceEquals(workspace,owner)&&current?.Invoke()==true&&owner.Notes.Contains(draft)&&draft is {IsClosed:false,IsDeleted:false,Mode:"markdown"}&&(!attached||ReferenceEquals(MarkdownHost.Content,created));
                created=new(draft,Valid);if(Valid()){markdownPreview=created;MarkdownHost.Content=created;attached=true;}else created.Dispose();
            }
        }
        else ClearMarkdownPreview();
        bodyMode=draft.Mode;
    }
    public void SetPlacement(DesktopWindowController controller)
    {
        Placement=controller;restoringLayout=true;
        FoldToggle.IsChecked=controller.State.Folded;PositionToggle.IsChecked=controller.State.PositionLocked;
        BodyPanel.Visibility=controller.State.Folded?Visibility.Collapsed:Visibility.Visible;BodyEditor.Visibility=controller.State.Folded?Visibility.Collapsed:Visibility.Visible;expandedHeight=controller.State.Height;restoringLayout=false;
    }
    public void ApplyUiPreferences(MemoApp.Core.Storage.UiPreferences preferences)
    {
        FontSize=preferences.FontSize;ContentScale.ScaleX=ContentScale.ScaleY=preferences.Scale;structuredEditor?.ApplyPreferences(preferences);markdownPreview?.ApplyPreferences(preferences);
        Foreground=preferences.DarkMode?Brushes.White:Brushes.Black;dark=preferences.DarkMode;UpdateColor();
        foreach(var input in new[]{TitleEditor,BodyEditor}){input.Background=Background;input.Foreground=Foreground;input.CaretBrush=Foreground;}
    }
    private bool dark;
    public StickyNoteWindow(NoteDraft draft)
    {
        InitializeComponent(); this.draft = draft; DataContext = draft; UpdateColor();ConfigureBody();
        draft.PropertyChanged += DraftChanged;
        Closed += (_, _) =>
        {
            closed=true;workspace=null;current=null;notice=null;draft.PropertyChanged -= DraftChanged;ClearMarkdownPreview();ClearStructuredEditor();ClearAttachmentPanel();
            BodyEditor.IsUndoEnabled = TitleEditor.IsUndoEnabled = false;
            DataContext = null; BodyEditor.Clear(); TitleEditor.Clear();
        };
    }
    private void DraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(NoteDraft.Mode))ConfigureBody();
        if (e.PropertyName == nameof(NoteDraft.Color)){UpdateColor();TitleEditor.Background=BodyEditor.Background=Background;}
        if (e.PropertyName is nameof(NoteDraft.IsClosed) or nameof(NoteDraft.IsDeleted) && (draft.IsClosed || draft.IsDeleted)) { Hide(); Close(); }
    }
    private void UpdateColor() => Background = new SolidColorBrush(dark ? draft.Color switch
    {
        "blue"=>Color.FromRgb(28,48,69),"green"=>Color.FromRgb(27,54,34),"pink"=>Color.FromRgb(68,35,47),"white"=>Color.FromRgb(35,39,47),"purple"=>Color.FromRgb(49,36,67),_=>Color.FromRgb(59,53,29)
    } : draft.Color switch
    {
        "blue" => Color.FromRgb(211, 233, 255), "green" => Color.FromRgb(218, 247, 222),
        "pink" => Color.FromRgb(255, 217, 230), "white" => Colors.White,
        "purple" => Color.FromRgb(233, 220, 255), _ => Color.FromRgb(255, 247, 186)
    });
    private void Position_Changed(object sender,RoutedEventArgs e){if(!restoringLayout)Placement?.SetPositionLocked(((CheckBox)sender).IsChecked==true);}
    private void Fold_Changed(object sender, RoutedEventArgs e)
    {
        if (BodyEditor is null || restoringLayout) return;
        bool folded = ((CheckBox)sender).IsChecked == true;BodyPanel.Visibility=folded?Visibility.Collapsed:Visibility.Visible;AttachmentHost.Visibility=folded||attachmentPanel is null?Visibility.Collapsed:Visibility.Visible;
        if(Placement is not null){Placement.SetFolded(folded);BodyEditor.Visibility=folded?Visibility.Collapsed:Visibility.Visible;return;}
        if (folded) { expandedHeight = Height; BodyEditor.Visibility = Visibility.Collapsed; Height = 160; }
        else { BodyEditor.Visibility = Visibility.Visible; Height = expandedHeight; }
    }
}
