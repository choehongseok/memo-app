using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.History;
namespace MemoApp.Windows;
public partial class HistoryWindow : Window
{
    private Action<Guid>? restore;
    private bool closed;
    private ComparisonChoice[]? comparisonSources;
    private sealed record ComparisonChoice(Guid RevisionId,bool Current,DateTimeOffset Date,string Title,string Text,NoteMetadata Metadata)
    {
        public string Label=>$"{Date:yyyy-MM-dd HH:mm:ss.fffffff} UTC · {(Current?"열었을 때 현재":"이력")} · {RevisionId.ToString("N")[..8]} · {Title}";
    }
    public void ApplyUiPreferences(UiPreferences preferences)
    {
        FontSize=preferences.FontSize;HistoryScale.ScaleX=HistoryScale.ScaleY=preferences.Scale;
        Background=preferences.DarkMode?new SolidColorBrush(Color.FromRgb(28,32,40)):Brushes.White;Foreground=preferences.DarkMode?Brushes.White:Brushes.Black;
        Brush controlBackground=preferences.DarkMode?new SolidColorBrush(Color.FromRgb(42,47,57)):Brushes.White;
        foreach(var control in new Control[]{Revisions,PastTitle,PastText,RestoreVersion,LeftRevision,RightRevision,LeftTitle,RightTitle,LeftText,RightText,DiffText}){control.Background=controlBackground;control.Foreground=Foreground;}
    }
    public HistoryWindow(NoteDraft note, IReadOnlyList<StoredRevision> revisions, Action<Guid> restore,StoredNote? current=null)
    {
        InitializeComponent(); this.restore = restore;
        if(revisions.Count>512||revisions.Any(r=>r.NoteId!=note.Id)||current is not null&&current.NoteId!=note.Id||revisions.Select(r=>r.RevisionId).Distinct().Count()!=revisions.Count||current is not null&&revisions.Any(r=>r.RevisionId==current.RevisionId))throw new ArgumentException("Comparison sources must be one consistent note head/history");
        RestoreVersion.IsEnabled = !note.IsDeleted && revisions.Count != 0;
        Revisions.ItemsSource = revisions; Revisions.SelectedItem = revisions.FirstOrDefault();
        comparisonSources=(current is null?Array.Empty<ComparisonChoice>():[new(current.RevisionId,true,current.ModifiedAt,current.Title,current.Text,current.Metadata)])
            .Concat(revisions.Select(r=>new ComparisonChoice(r.RevisionId,false,r.ModifiedAt,r.Title,r.Text,r.Metadata))).ToArray();
        LeftRevision.ItemsSource=RightRevision.ItemsSource=comparisonSources;LeftRevision.SelectedItem=comparisonSources.LastOrDefault();RightRevision.SelectedItem=comparisonSources.FirstOrDefault();
        Closed += (_, _) =>
        {
            closed=true;this.restore=null;comparisonSources=null;Revisions.ItemsSource=null;LeftRevision.ItemsSource=RightRevision.ItemsSource=null;
            foreach(var input in new[]{PastTitle,PastText,LeftTitle,RightTitle,LeftText,RightText,DiffText}){input.IsUndoEnabled=false;input.Clear();}
            ComparisonInfo.Text=DiffState.Text="";
        };
    }
    private void Compare_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(closed||comparisonSources is null||LeftRevision.SelectedItem is not ComparisonChoice left||RightRevision.SelectedItem is not ComparisonChoice right||!comparisonSources.Contains(left)||!comparisonSources.Contains(right))return;
        LeftTitle.Text=left.Title;RightTitle.Text=right.Title;LeftText.Text=left.Text;RightText.Text=right.Text;
        var result=BoundedHistoryDiff.Compare(left.Text,right.Text);DiffText.Text=result.Rendered;DiffState.Text=result.Message;
        ComparisonInfo.Text=$"이전 {left.Date:yyyy-MM-dd HH:mm:ss} UTC / 이후 {right.Date:yyyy-MM-dd HH:mm:ss} UTC · 제목 {(left.Title==right.Title?"동일":"변경")} · {BoundedHistoryDiff.MetadataChanges(left.Metadata,right.Metadata)}";
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
