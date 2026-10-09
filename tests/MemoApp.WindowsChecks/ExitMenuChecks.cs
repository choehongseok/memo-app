using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task ExitMenuRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-explicit-exit-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            // Pre-Show, Show->Close before Loaded, repeat close, and already loaded menu.
            foreach(int phase in new[]{0,1,2,3})
            {
                main=new MainWindow(root);bool closed=false;main.Closed+=(_,_)=>closed=true;
                if(phase>0)main.Show();if(phase>=2)await Idle();
                if(phase==3)Control<MenuItem>(main,"ExitMenu").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));else main.Close();
                if(phase==1)main.Close();
                await Idle();Require(closed&&!main.IsVisible,"Locked close unwinds Closing and completes before/after Loaded, including repeat close");main=null;
            }
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var session=Field<SaveCoordinator>(main,"session");var note=session.Workspace.CreateNote();note.Title="last synthetic edit before explicit exit";Invoke(main,"RefreshNotes",note);Invoke(main,"OpenSticky",note);await Idle();
            Require(main.FindName("ExitMenu") is MenuItem,"Explicit full-exit menu is missing");var menu=Control<MenuItem>(main,"ExitMenu");menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var deadline=DateTime.UtcNow.AddSeconds(10);while(main.IsVisible&&DateTime.UtcNow<deadline)await Idle();
            Require(!main.IsVisible&&session.IsLocked&&session.KeysReleased&&Field<SaveCoordinator?>(main,"session") is null,"Explicit menu completes existing save/lock/session release before closing");Require(Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==0,"Explicit exit closes sticky windows too");main=null;
            using var reopened=EncryptedVault.Open(root,secret);Require(reopened.Loaded.Notes.Single().Title=="last synthetic edit before explicit exit","Full-exit menu preserves last dirty edit in authenticated restart");
        }
        finally{if(main is not null){var session=Field<SaveCoordinator?>(main,"session");if(session is not null&&!session.IsLocked)await session.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
