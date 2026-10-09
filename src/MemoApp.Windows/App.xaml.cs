using System.Windows;
using MemoApp.Core.Lifecycle;
namespace MemoApp.Windows;
public partial class App : Application
{
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if(MainWindow is MainWindow main)main.SetTraySessionEnding(true);
        base.OnSessionEnding(e);
        if(e.Cancel&&MainWindow is MainWindow canceled)canceled.SetTraySessionEnding(false);
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.SequenceEqual(new[]{"--install"})){TrialInstaller.Prompt();Shutdown();return;}
        try
        {
            var options=StartupOptions.Parse(e.Args);
            string root = StartupPaths.Resolve(options.DataArguments, AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var profile=LocalUiIdentity.GetOrCreate(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MemoApp"));
            var main=new MainWindow(root,profile);main.ConfigureStartup(Environment.ProcessPath??"",options.Portable);main.Show();
            if(options.TrayStart)main.StartLockedTray();
        }
        catch { MessageBox.Show("실행 경로/인수 또는 로컬 표시 식별자 검증에 실패했습니다. 기존 자료/식별자는 보존했습니다. --portable 지정은 exe옆 Data를 사용하며 다른 폴더로 자동 전환하지 않습니다.", "메모앱"); Shutdown(2); }
    }
}
