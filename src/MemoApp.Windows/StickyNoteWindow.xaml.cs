using System.Windows;
using MemoApp.Core.Editing;
namespace MemoApp.Windows;
public partial class StickyNoteWindow : Window
{
    public StickyNoteWindow(NoteDraft draft)
    {
        InitializeComponent();
        DataContext = draft;
        Closed += (_, _) => DataContext = null;
    }
}
