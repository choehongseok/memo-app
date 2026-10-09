using System.IO;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Storage;
using MemoApp.Core.Search;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task DeviceSearchRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-search-state-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(root);main.Show();
            Require(main.FindName("SavedSearchList") is ComboBox&&main.FindName("RecentNotesList") is ComboBox,"Recent and saved-search native controls are missing");
            Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var active=Field<MemoApp.Core.Editing.SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();
            var first=active.Workspace.CreateNote();first.Title="합성 첫 메모";first.Text="exact needle";var second=active.Workspace.CreateNote();second.Text="different";Invoke(main,"RefreshNotes",first);await Idle();
            await (Task)typeof(MainWindow).GetField("recentTask",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
            var profile=Field<Guid>(main,"uiDeviceId");Require(active.Workspace.GetUiDevice(profile).RecentNoteIds.First()==first.Id,"Selected editor records recent note");
            Control<TextBox>(main,"SearchInput").Text="exact needle";Control<TextBox>(main,"SavedSearchName").Text="합성 조건";
            var method=typeof(MainWindow).GetMethod("SaveCurrentSearchAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;Require(await (Task<bool>)method.Invoke(main,[])!,"Actual UI saves current conditions");var saved=active.Workspace.GetUiDevice(profile).SavedSearches.Single();
            Control<TextBox>(main,"SearchInput").Clear();Require((bool)typeof(MainWindow).GetMethod("ApplySavedSearch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,[saved.Id])!,"Saved conditions apply");Require(Control<ListBox>(main,"NotesList").Items.Count==1&&Control<TextBox>(main,"SearchInput").Text=="exact needle","Exact saved query filters results");
            var missing=active.Workspace.SaveSearch(profile,"missing",new SearchOptions{Query="different",FolderId=Guid.NewGuid()});Require(!(bool)typeof(MainWindow).GetMethod("ApplySavedSearch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,[missing.Id])!&&Control<TextBox>(main,"SearchInput").Text=="exact needle","Missing folder refuses before touching filters");
            var revoked=active.Workspace.SaveSearch(profile,"revoked",saved.Options with{Query="different"});bool armed=true;
            TextChangedEventHandler revoke=(_,_)=>{if(armed){armed=false;active.Workspace.RemoveSavedSearch(profile,revoked.Id);}};var search=Control<TextBox>(main,"SearchInput");search.TextChanged+=revoke;
            Require(!(bool)typeof(MainWindow).GetMethod("ApplySavedSearch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,[revoked.Id])!&&search.Text=="exact needle"&&Control<ListBox>(main,"NotesList").Items.Count==1,"Reentrant record removal rolls back exact filter and never queries partial controls");search.TextChanged-=revoke;
            var fault=active.Workspace.SaveSearch(profile,"fault",saved.Options with{Query="different"});TextChangedEventHandler throws=(_,_)=>throw new InvalidOperationException("Synthetic native setter failure");search.TextChanged+=throws;
            Require(!(bool)typeof(MainWindow).GetMethod("ApplySavedSearch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,[fault.Id])!,"Setter failure refuses apply");typeof(MainWindow).GetMethod("RefreshNotes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,[null]);Require(Field<bool>(main,"searchBlocked")&&Control<ListBox>(main,"NotesList").Items.Count==0,"Failed rollback blocks queued and ordinary queries");search.TextChanged-=throws;
            Invoke(main,"ResetSearch_Click",main,new System.Windows.RoutedEventArgs());Require(!Field<bool>(main,"searchBlocked")&&search.Text.Length==0&&Control<ListBox>(main,"NotesList").Items.Count==2,"Explicit reset remains available and commits all defaults");
            await active.LockAsync();Require(Control<ComboBox>(main,"RecentNotesList").Items.Count==0&&Control<ComboBox>(main,"SavedSearchList").Items.Count==0&&Control<TextBox>(main,"SavedSearchName").Text.Length==0,"Lock purges sensitive labels and name");
        }
        finally{if(main is not null){var active=Field<MemoApp.Core.Editing.SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
