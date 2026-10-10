using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
namespace MemoApp.Windows;
internal sealed record StartupValue(bool Exists,bool StringKind,string? Command);
internal interface IStartupStore
{
    StartupValue Read();
    void Write(string command);
    void Delete();
}
internal sealed class WindowsStartupStore(string subkey=WindowsStartupStore.RunKey,string name="MemoAppSyntheticTrial"):IStartupStore
{
    internal const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public StartupValue Read()
    {
        using var key=Registry.CurrentUser.OpenSubKey(subkey,false);if(key is null)return new(false,false,null);
        uint length=0;int result=RegQueryValueExW(key.Handle,name,IntPtr.Zero,out uint kind,null,ref length);
        if(result==2)return new(false,false,null);
        if(result!=0)throw new IOException("Startup registration read refused");
        if(kind!=1||length is <2 or >522||(length&1)!=0)return new(true,false,null);
        byte[] bytes=new byte[length];uint capacity=length;
        try
        {
            result=RegQueryValueExW(key.Handle,name,IntPtr.Zero,out kind,bytes,ref capacity);
            if(result!=0||kind!=1||capacity!=length)throw new IOException("Startup registration changed while reading");
            if(bytes[^1]!=0||bytes[^2]!=0)return new(true,false,null);
            string command=new UnicodeEncoding(false,false,true).GetString(bytes,0,bytes.Length-2);
            if(command.Length>260||command.Any(char.IsControl))return new(true,false,null);
            return new(true,true,command);
        }
        catch(DecoderFallbackException){return new(true,false,null);}
        finally{Array.Clear(bytes);}
    }
    public void Write(string command)
    {
        using var key=Registry.CurrentUser.CreateSubKey(subkey,true)??throw new IOException("Startup registration write refused");key.SetValue(name,command,RegistryValueKind.String);
    }
    public void Delete(){using var key=Registry.CurrentUser.OpenSubKey(subkey,true);key?.DeleteValue(name,false);}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern int RegQueryValueExW(SafeRegistryHandle key,string name,IntPtr reserved,out uint kind,[Out]byte[]? bytes,ref uint length);
}
internal enum StartupRegistrationState{Off,On,Foreign}
internal sealed class StartupRegistration(string command,IStartupStore store)
{
    internal StartupRegistrationState State
    {
        get{var value=store.Read();return !value.Exists?StartupRegistrationState.Off:value.StringKind&&value.Command==command?StartupRegistrationState.On:StartupRegistrationState.Foreign;}
    }
    internal bool Apply(bool enabled,Func<bool> current)
    {
        var before=State;
        if(!current())return false;
        if(before==StartupRegistrationState.Foreign)throw new IOException("Different existing startup value is preserved");
        if(enabled&&before==StartupRegistrationState.Off){if(!current())return false;store.Write(command);}
        else if(!enabled&&before==StartupRegistrationState.On){if(!current())return false;store.Delete();}
        // Readback is confirmation, not a transaction/CAS defense against another writer.
        var after=State;return current()&&after==(enabled?StartupRegistrationState.On:StartupRegistrationState.Off);
    }
}
