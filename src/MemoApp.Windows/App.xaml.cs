using System.Windows;
using MemoApp.Core.Lifecycle;
namespace MemoApp.Windows;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string root = StartupPaths.Resolve(e.Args, AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            new MainWindow(root).Show();
        }
        catch { MessageBox.Show("실행 경로/인수가 지원되지 않습니다. --portable 지정은 exe옆 Data를 사용하며 다른 폴더로 자동 전환하지 않습니다.", "메모앱"); Shutdown(2); }
    }
}
