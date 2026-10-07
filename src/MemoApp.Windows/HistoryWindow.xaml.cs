using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
namespace MemoApp.Windows;
public partial class HistoryWindow : Window
{
    private Action<Guid>? restore;
    public HistoryWindow(NoteDraft note, IReadOnlyList<StoredRevision> revisions, Action<Guid> restore)
    {
        InitializeComponent(); this.restore = restore;
        RestoreVersion.IsEnabled = !note.IsDeleted && revisions.Count != 0;
        Revisions.ItemsSource = revisions; Revisions.SelectedItem = revisions.FirstOrDefault();
        Closed += (_, _) => { this.restore = null; Revisions.ItemsSource = null; PastTitle.Clear(); PastText.Clear(); PastTitle.IsUndoEnabled = PastText.IsUndoEnabled = false; };
    }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Revisions.SelectedItem is StoredRevision revision) { PastTitle.Text = revision.Title; PastText.Text = revision.Text; }
        else { PastTitle.Clear(); PastText.Clear(); }
    }
    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (Revisions.SelectedItem is not StoredRevision revision || restore is null) return;
        if (MessageBox.Show("현재 상태를 이력에 남기고 선택한 과거 내용으로 새 리비전을 만들까요? 다른 메모는 변경하지 않습니다.", "버전 복원", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { restore(revision.RevisionId); Close(); } catch { MessageBox.Show("버전 복원 실패 — 기존 자료와 이력은 보존합니다."); }
    }
}
