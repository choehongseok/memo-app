using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemoApp.Core.Editing;
namespace MemoApp.Windows;
public partial class StickyNoteWindow : Window
{
    private readonly NoteDraft draft;
    private double expandedHeight = 400;
    public DesktopWindowController? Placement {get;private set;}
    private bool restoringLayout;
    public void SetPlacement(DesktopWindowController controller)
    {
        Placement=controller;restoringLayout=true;
        FoldToggle.IsChecked=controller.State.Folded;PositionToggle.IsChecked=controller.State.PositionLocked;
        BodyEditor.Visibility=controller.State.Folded?Visibility.Collapsed:Visibility.Visible;expandedHeight=controller.State.Height;restoringLayout=false;
    }
    public void ApplyUiPreferences(MemoApp.Core.Storage.UiPreferences preferences)
    {
        FontSize=preferences.FontSize;ContentScale.ScaleX=ContentScale.ScaleY=preferences.Scale;
        Foreground=preferences.DarkMode?Brushes.White:Brushes.Black;dark=preferences.DarkMode;UpdateColor();
        foreach(var input in new[]{TitleEditor,BodyEditor}){input.Background=Background;input.Foreground=Foreground;input.CaretBrush=Foreground;}
    }
    private bool dark;
    public StickyNoteWindow(NoteDraft draft)
    {
        InitializeComponent(); this.draft = draft; DataContext = draft; UpdateColor();
        draft.PropertyChanged += DraftChanged;
        Closed += (_, _) =>
        {
            draft.PropertyChanged -= DraftChanged;
            BodyEditor.IsUndoEnabled = TitleEditor.IsUndoEnabled = false;
            DataContext = null; BodyEditor.Clear(); TitleEditor.Clear();
        };
    }
    private void DraftChanged(object? sender, PropertyChangedEventArgs e)
    {
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
        bool folded = ((CheckBox)sender).IsChecked == true;
        if(Placement is not null){Placement.SetFolded(folded);BodyEditor.Visibility=folded?Visibility.Collapsed:Visibility.Visible;return;}
        if (folded) { expandedHeight = Height; BodyEditor.Visibility = Visibility.Collapsed; Height = 160; }
        else { BodyEditor.Visibility = Visibility.Visible; Height = expandedHeight; }
    }
}
