using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
namespace MemoApp.Windows;
public partial class MainWindow : Window
{
    private readonly EditingWorkspace workspace = new(TimeProvider.System);
    private readonly Dictionary<Guid, StickyNoteWindow> stickyWindows = [];
    private readonly bool editingPreview;
    public MainWindow()
    {
        InitializeComponent();
        editingPreview = Environment.GetCommandLineArgs().Contains("--editing-preview", StringComparer.Ordinal);
        if (editingPreview)
        {
            Notice.Text = "개발용 비영속 편집 모드입니다. 암호화·자동 저장·재실행 복원 없음. 합성 자료만 사용하세요.";
            PreviewPanel.Visibility = Visibility.Visible;
            Pending.Visibility = Visibility.Collapsed;
            NotesList.ItemsSource = workspace.Notes;
        }
        Closed += (_, _) =>
        {
            foreach (var window in stickyWindows.Values.ToArray()) window.Close();
            Editor.DataContext = null;
            workspace.Clear();
        };
    }
    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        if (editingPreview) NotesList.SelectedItem = workspace.CreateNote();
    }
    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Editor.DataContext = NotesList.SelectedItem;
        Editor.IsEnabled = NotesList.SelectedItem is NoteDraft;
    }
    private void OpenSticky_Click(object sender, RoutedEventArgs e)
    {
        if (!editingPreview || NotesList.SelectedItem is not NoteDraft draft) return;
        if (stickyWindows.TryGetValue(draft.Id, out var existing)) { existing.Activate(); return; }
        var window = new StickyNoteWindow(draft);
        stickyWindows.Add(draft.Id, window);
        window.Closed += (_, _) => stickyWindows.Remove(draft.Id);
        window.Show();
    }
}
