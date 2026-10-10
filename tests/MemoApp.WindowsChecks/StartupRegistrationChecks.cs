using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MemoApp.Core.Lifecycle;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private sealed class SyntheticStartupStore:IStartupStore
    {
        internal StartupValue Value=new(false,false,null);
        internal int Writes,Deletes;internal Action? Reading,Writing;
        public StartupValue Read(){Reading?.Invoke();return Value;}
        public void Write(string command){Writes++;Writing?.Invoke();Value=new(true,true,command);}
        public void Delete(){Deletes++;Value=new(false,false,null);}
    }
    private static async Task StartupRegistrationRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-startup-"+Guid.NewGuid().ToString("N")),key=@"Software\MemoApp\SyntheticChecks\"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(root);string exe=Path.Combine(root,"MemoApp.Windows.exe");File.WriteAllText(exe,"SYNTHETIC_NOT_EXECUTABLE");MainWindow? main=new MainWindow(Path.Combine(root,"vault"));
        try
        {
            var store=new SyntheticStartupStore();main.ConfigureStartup(exe,false,store);main.Show();await Idle();var item=Control<MenuItem>(main,"StartupMenu");Require(!item.IsChecked&&item.IsEnabled&&store.Writes==0&&store.Deletes==0,"Configure/default startup control reads only; no startup registration or vault is created");
            item.IsChecked=true;item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Require(store.Writes==1&&store.Deletes==0&&item.IsChecked&&store.Value.Command==StartupCommand.Create(exe,false),"Explicit opt-in registers exact command once");
            item.IsChecked=false;item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Require(store.Deletes==1&&!item.IsChecked&&!store.Value.Exists,"Explicit disable removes only own exact command");
            store.Value=new(true,true,"FOREIGN_SYNTHETIC_COMMAND");item.IsChecked=true;item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Require(store.Writes==1&&store.Deletes==1&&store.Value.Command=="FOREIGN_SYNTHETIC_COMMAND"&&!item.IsEnabled,"Foreign same-name command is preserved without write/delete");
            store.Value=new(false,false,null);main.ConfigureStartup(exe,true,store);store.Writing=()=>{item.IsChecked=false;item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));};item.IsChecked=true;item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Require(store.Writes==2&&store.Deletes==1&&item.IsChecked&&store.Value.Command==StartupCommand.Create(exe,true),"Reentry during one authorized write cannot cause additional deletion or lose readback state");store.Writing=null;
            store.Value=new(false,false,null);store.Reading=()=>{store.Reading=null;main.Close();};item.IsChecked=true;item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Idle();Require(store.Writes==2&&Field<bool>(main,"windowClosed"),"Close during registry read prevents later write");main=null;
            var native=new WindowsStartupStore(key,"TestOnly");var registration=new StartupRegistration(StartupCommand.Create(exe,false),native);Require(registration.State==StartupRegistrationState.Off&&!registration.Apply(true,()=>false),"Isolated HKCU canceled registration does not create value");
            Require(registration.Apply(true,()=>true)&&registration.State==StartupRegistrationState.On&&native.Read().Command==StartupCommand.Create(exe,false),"Actual isolated HKCU Unicode REG_SZ write/readback");
            using(var reg=Registry.CurrentUser.OpenSubKey(key,true)!)reg.SetValue("TestOnly",new string('x',261),RegistryValueKind.String);Require(registration.State==StartupRegistrationState.Foreign,"Native size probe refuses oversized value before allocation");try{registration.Apply(false,()=>true);throw new Exception("Foreign value incorrectly removed");}catch(IOException){}
            using(var reg=Registry.CurrentUser.OpenSubKey(key,true)!)reg.SetValue("TestOnly",new byte[]{1,2,3},RegistryValueKind.Binary);Require(registration.State==StartupRegistrationState.Foreign,"Wrong registry type preserved");
            using(var reg=Registry.CurrentUser.OpenSubKey(key,true)!)reg.SetValue("TestOnly",StartupCommand.Create(exe,false),RegistryValueKind.String);Require(registration.Apply(false,()=>true)&&registration.State==StartupRegistrationState.Off,"Actual isolated native exact-owned deletion");
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();SyntheticTray? tray=null;Require(main.TryEnableTray((a,b,c)=>tray=new SyntheticTray(a,b,c)),"Registered start fixture");main.StartLockedTray();await Idle();Require(!main.IsVisible&&Field<object?>(main,"session") is null&&Control<PasswordBox>(main,"RecoveryInput").Password.Length==0&&!Directory.Exists(Path.Combine(root,"vault")),"Tray startup remains locked without secret/vault creation");main.DisableTray();Require(main.IsVisible,"Tray-start loss/disable restores locked manager");
            main.SetTraySessionEnding(true);main.Close();await Idle();main=null;
            byte[] secret=EncryptedVault.GenerateRecoverySecret();try
            {
                main=new MainWindow(Path.Combine(root,"native-reentry-vault"));main.Show();main.StartLockedTray((a,b,c)=>{Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"native-reentry-vault"),secret,secret));return new SyntheticTray(a,b,c);});await Idle();var active=Field<SaveCoordinator>(main,"session");Require(main.IsVisible&&!active.IsLocked&&!Field<bool>(main,"trayHidePending"),"Native creation reentry unlock cannot be hidden by locked startup intent");await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
            }
            finally{CryptographicOperations.ZeroMemory(secret);}
            Console.WriteLine("PASS: startup opt-in/read-only default/foreign preservation/close/reentry/exact native REG_SZ in isolated HKCU; actual Run key untouched, actual Windows login not tested");
        }
        finally{if(main is not null){SetField(main,"confirmedExit",true);main.Close();}Registry.CurrentUser.DeleteSubKeyTree(key,false);Directory.Delete(root,true);}
    }
}
