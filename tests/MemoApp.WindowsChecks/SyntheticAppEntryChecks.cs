using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Windows;
internal static partial class Program
{
    // Runs the actual product App.OnStartup with the real --synthetic-test process argument.
    // Only the project runner's own synthetic app, with no physical input, clipboard or system registration.
    private static int SyntheticAppEntryRun()
    {
        int result=1;var app=new MemoApp.Windows.App();
        app.Startup+=(_,_)=>app.Dispatcher.BeginInvoke(new Action(async()=>
        {
            MainWindow? main=null;
            try
            {
                await Idle();main=app.MainWindow as MainWindow??throw new IOException("Synthetic product startup did not create MainWindow");
                string trial=Path.Combine(AppContext.BaseDirectory,"SyntheticTest");
                Require(Field<string>(main,"root")==Path.Combine(trial,"Data"),"Actual App startup must pass the synthetic vault root");
                Require(File.Exists(Path.Combine(trial,"UiIdentity","ui-device.id")),"Actual App startup must create only its synthetic UI identity");
                Require(!Directory.Exists(Path.Combine(trial,"Data")),"Locked actual startup must not create a vault");
                Require(!Control<MenuItem>(main,"StartupMenu").IsEnabled&&Field<object?>(main,"startupRegistration") is null,"Actual App startup must disable native startup store access");
                Require(Control<FrameworkElement>(main,"LockPanel").IsVisible&&main.IsVisible,"Actual product starts with visible locked UI");
                UiValidationEvidence.Capture(main,"product-startup-locked");
                using var id=File.OpenRead(Path.Combine(trial,"UiIdentity","ui-device.id"));
                Console.WriteLine("SYNTHETIC_UI_ID_SHA256: "+Convert.ToHexStringLower(SHA256.HashData(id)));
                Console.WriteLine("PASS: actual product App startup selected synthetic vault/UI identity and disabled system startup registration; no vault or physical input");
                result=0;
            }
            catch(Exception error){Console.Error.WriteLine("FAIL: synthetic product startup "+error.GetType().Name+": "+error.Message);}
            finally{if(main is not null){SetField(main,"confirmedExit",true);main.Close();}app.Shutdown();}
        }));
        app.Run();return result;
    }
}
