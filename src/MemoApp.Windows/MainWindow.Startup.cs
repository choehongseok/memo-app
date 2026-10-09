using System.Windows;
using MemoApp.Core.Lifecycle;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private StartupRegistration? startupRegistration;
    private bool startupBusy;
    internal void ConfigureStartup(string executable,bool portable,IStartupStore? store=null)
    {
        if(startupBusy||windowClosed||closing||confirmedExit)return;startupBusy=true;
        try{startupRegistration=new(StartupCommand.Create(executable,portable),store??new WindowsStartupStore());RefreshStartupMenu();}
        catch{startupRegistration=null;try{StartupMenu.IsChecked=false;StartupMenu.IsEnabled=false;Notice.Text="자동 실행 경로/기존 등록 확인 실패 — 등록은 변경하지 않았습니다.";}catch{}}
        finally{startupBusy=false;}
    }
    private void RefreshStartupMenu()
    {
        var state=startupRegistration?.State??StartupRegistrationState.Foreign;
        StartupMenu.IsChecked=state==StartupRegistrationState.On;StartupMenu.IsEnabled=startupRegistration is not null&&state!=StartupRegistrationState.Foreign;
        if(state==StartupRegistrationState.Foreign)Notice.Text="같은 이름의 다른 자동 실행 등록은 보존했습니다. 자동 실행 설정을 변경할 수 없습니다.";
    }
    private void StartupMenu_Click(object sender,RoutedEventArgs e)
    {
        if(startupBusy||startupRegistration is null||windowClosed||closing||confirmedExit||traySessionEnding)return;
        bool enabled=StartupMenu.IsChecked;var registration=startupRegistration;startupBusy=true;
        bool Current()=>!windowClosed&&!closing&&!confirmedExit&&!traySessionEnding&&!fullExitRequested&&ReferenceEquals(startupRegistration,registration);
        try
        {
            StartupMenu.IsEnabled=false;if(!Current())return;
            bool applied=registration.Apply(enabled,Current);
            if(!Current())return;RefreshStartupMenu();
            Notice.Text=applied?(enabled?"현재 계정의 로그인 실행 항목을 등록했습니다. 다음 로그인에서 복구 비밀 없이 잠긴 트레이로 시작합니다. Windows에서 실행을 지연/차단할 수 있습니다.":"현재 앱 경로의 로그인 실행 항목을 해제했습니다."):"자동 실행 등록 확인 실패 — 현재 등록 상태를 확인하세요.";
        }
        catch{if(Current())try{RefreshStartupMenu();Notice.Text="자동 실행 설정 실패 — 다른 등록은 보존합니다. 현재 상태를 확인하세요.";}catch{try{StartupMenu.IsEnabled=false;}catch{}}}
        finally{startupBusy=false;}
    }
    internal void StartLockedTray(Func<Action,Action,Action,ITrayIcon>? factory=null)
    {
        if(windowClosed||confirmedExit||session is {IsLocked:false})return;
        if(TryEnableTray(factory)&&!windowClosed&&!confirmedExit&&!closing&&session is not {IsLocked:false})Close();
    }
}
