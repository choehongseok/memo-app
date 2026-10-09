using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private sealed class SyntheticTray(Action open,Action menu,Action lost):ITrayIcon
    {
        public bool Registered{get;set;}=true;
        public bool Disposed{get;private set;}
        public bool FocusResult{get;set;}=true;
        public Action? DuringFocus{get;set;}
        public bool Focus(){DuringFocus?.Invoke();return FocusResult;}
        public void Open()=>open();public void Menu()=>menu();public void Lose(){Registered=false;lost();}
        public void Dispose(){Disposed=true;Registered=false;}
    }
    private static async Task TrayRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-tray-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=new MainWindow(root);
        try
        {
            main.Show();await Idle();Require(main.FindName("TrayMenu") is MenuItem {IsCheckable:true,IsChecked:false},"Tray session opt-in control must exist and default off");
            SyntheticTray? fake=null;
            bool Enable()=>main.TryEnableTray((a,b,c)=>fake=new SyntheticTray(a,b,c));
            SyntheticTray? refused=null;Require(!main.TryEnableTray((a,b,c)=>refused=new SyntheticTray(a,b,c){Registered=false})&&refused!.Disposed&&main.IsVisible&&!Control<MenuItem>(main,"TrayMenu").IsChecked,"Unregistered icon cannot hide or enable resident setting");
            SyntheticTray? reentrant=null;Require(!main.TryEnableTray((a,b,c)=>{reentrant=new(a,b,c);main.DisableTray();return reentrant;})&&reentrant!.Disposed&&main.IsVisible,"Disable during creation revokes candidate before publication");
            Require(Enable(),"Synthetic registered tray");fake!.Menu();var menu=Field<ContextMenu>(main,"trayContextMenu");Require(!menu.Items.OfType<MenuItem>().Single(i=>(string)i.Header=="새 메모").IsEnabled&&!menu.Items.OfType<MenuItem>().Any(i=>((string)i.Header).StartsWith("최근:")),"Locked generic tray menu cannot expose titles or create notes");main.DisableTray();
            Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Title="SYNTHETIC_TRAY_PRIVATE_TITLE";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");await Idle();Require(await active.SaveAsync(),"Tray baseline save");await Idle();
            Require(Enable(),"Unlocked synthetic tray");main.Close();await Idle();Require(!main.IsVisible&&!active.IsLocked&&!Field<bool>(main,"closing")&&!Field<bool>(main,"windowClosed"),"Close hides after Closing unwinds without locking/releasing session");
            Field<DispatcherTimer>(main,"timer").Start();Require(Field<DispatcherTimer>(main,"timer").IsEnabled,"Hidden manager retains idle/save timer");Field<DispatcherTimer>(main,"timer").Stop();fake!.Open();Require(main.IsVisible,"Tray open restores manager");
            main.Close();await Idle();note.Text="HIDDEN_SYNTHETIC_SAVE";SetField(main,"activity",DateTimeOffset.UtcNow);Invoke(main,"Timer_Tick",null!,EventArgs.Empty);var savedByTimer=DateTime.UtcNow.AddSeconds(5);while(active.IsDirty&&DateTime.UtcNow<savedByTimer)await Idle();Require(!active.IsDirty&&!main.IsVisible&&!active.IsLocked,"Existing hidden timer handler saves genuine dirty snapshot");fake.Open();await Idle();
            fake.Menu();menu=Field<ContextMenu>(main,"trayContextMenu");var acceptedHeader=menu.Items.OfType<MenuItem>().First(i=>((string)i.Header).StartsWith("최근:"));long version=note.EditVersion;active.Workspace.AcceptPrepared(active.Workspace.Capture());Invoke(main,"TrayRendering",null!,EventArgs.Empty);Require(note.EditVersion==version&&Field<ContextMenu?>(main,"trayContextMenu") is null&&Equals(acceptedHeader.Header,"")&&Field<Func<bool>?>(main,"trayMenuCurrent") is null,"Same-version source acceptance clears recent titles before next render and releases authority closure");acceptedHeader.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Require(Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==0,"Same-version stale handler cannot open note");
            fake.Menu();menu=Field<ContextMenu>(main,"trayContextMenu");var stale=menu.Items.OfType<MenuItem>().First(i=>((string)i.Header).StartsWith("최근:"));note.Title="SYNTHETIC_NEW_SOURCE";Require(Field<ContextMenu?>(main,"trayContextMenu") is null&&Equals(stale.Header,"")&&stale.Tag is null,"Source changes revoke retained title/menu before native cleanup");stale.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Require(Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==0,"Stale recent click cannot open new-source note");await Idle();await Field<Task>(main,"recentTask");await Idle();
            fake.Menu();menu=Field<ContextMenu>(main,"trayContextMenu");menu.Items.OfType<MenuItem>().First(i=>((string)i.Header).StartsWith("최근:")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Require(Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").ContainsKey(note.Id),"Current recent tray command opens genuine bound sticky");await Idle();
            fake.Menu();menu=Field<ContextMenu>(main,"trayContextMenu");int before=active.Workspace.Notes.Count;menu.Items.OfType<MenuItem>().Single(i=>(string)i.Header=="새 메모").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Require(active.Workspace.Notes.Count==before+1&&Field<ContextMenu?>(main,"trayContextMenu") is null,"Current new-note command creates through existing guarded manager");await Idle();await Field<Task>(main,"recentTask");await Idle();
            fake.FocusResult=false;main.Close();await Idle();fake.Menu();Require(main.IsVisible&&Field<ContextMenu?>(main,"trayContextMenu") is null,"Native foreground/menu failure restores manager");fake.FocusResult=true;
            main.Close();await Idle();fake.Lose();Require(main.IsVisible&&fake.Disposed&&!Control<MenuItem>(main,"TrayMenu").IsChecked,"Icon loss while hidden restores manager and clears resident setting");fake.Open();Require(main.IsVisible,"Old native callback remains revoked");
            Require(Enable(),"Enable before visibility reentry");bool once=true;DependencyPropertyChangedEventHandler hiding=(_,e)=>{if(once&&e.NewValue is false){once=false;fake!.Lose();}};main.IsVisibleChanged+=hiding;main.Close();await Idle();main.IsVisibleChanged-=hiding;Require(main.IsVisible&&!Field<bool>(main,"windowClosed")&&fake!.Disposed,"Native icon loss during Hide cannot strand invisible manager");
            Require(Enable(),"Enable before focus reentry");Task<bool>? locking=null;fake!.DuringFocus=()=>locking=active.LockAsync();fake.Menu();await locking!;Require(active.KeysReleased&&Field<ContextMenu?>(main,"trayContextMenu") is null&&Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==0,"Lock during menu focus cannot reinsert private titles or sticky views");fake.DuringFocus=null;
            main.DisableTray();bool native=main.TryEnableTray();if(native){Require(Field<ITrayIcon>(main,"trayIcon").Registered,"Native shell registration is checked directly");main.DisableTray();}else Require(main.IsVisible&&Field<ITrayIcon?>(main,"trayIcon") is null,"Actual shell refusal retains visible fallback");var type=typeof(MainWindow).Assembly.GetType("MemoApp.Windows.NativeTrayIcon+NotifyIconData")!;Require(Marshal.SizeOf(type)==976,"Native x64 Unicode NOTIFYICONDATA ABI");Console.WriteLine("PASS: actual Shell_NotifyIcon registration="+native+"; synthetic lifecycle/commands independent; physical tray/Explorer restart not tested");
            Require(Enable(),"Enable before full exit");
            Control<MenuItem>(main,"ExitMenu").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Idle();Require(Field<bool>(main,"windowClosed")&&fake!.Disposed&&Field<SaveCoordinator?>(main,"session") is null,"Full exit bypasses enabled tray and disposes native hooks after settled session");main=null;
            using(var reopened=EncryptedVault.Open(root,secret))Require(reopened.Loaded.Notes.Any(n=>n.Title=="SYNTHETIC_NEW_SOURCE"),"Hidden resident changes survive authenticated restart");
            main=new MainWindow(root);main.Show();Require(Enable(),"Enable before OS ending");main.SetTraySessionEnding(true);main.Close();await Idle();Require(Field<bool>(main,"windowClosed")&&fake!.Disposed,"Session-ending close bypasses hide; forced OS flush is not claimed");main=null;
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
