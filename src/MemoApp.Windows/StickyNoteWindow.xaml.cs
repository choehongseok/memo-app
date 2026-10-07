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
        if (e.PropertyName == nameof(NoteDraft.Color)) UpdateColor();
        if (e.PropertyName is nameof(NoteDraft.IsClosed) or nameof(NoteDraft.IsDeleted) && (draft.IsClosed || draft.IsDeleted)) { Hide(); Close(); }
    }
    private void UpdateColor() => Background = new SolidColorBrush(draft.Color switch
    {
        "blue" => Color.FromRgb(211, 233, 255), "green" => Color.FromRgb(218, 247, 222),
        "pink" => Color.FromRgb(255, 217, 230), "white" => Colors.White,
        "purple" => Color.FromRgb(233, 220, 255), _ => Color.FromRgb(255, 247, 186)
    });
    private void Fold_Changed(object sender, RoutedEventArgs e)
    {
        if (BodyEditor is null) return;
        bool folded = ((CheckBox)sender).IsChecked == true;
        if (folded) { expandedHeight = Height; BodyEditor.Visibility = Visibility.Collapsed; Height = 160; }
        else { BodyEditor.Visibility = Visibility.Visible; Height = expandedHeight; }
    }
}
