using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private ITrayIcon? trayIcon;
    private ContextMenu? trayContextMenu;
    private RoutedEventHandler? trayClosedHandler;
    private Func<bool>? trayMenuCurrent;
    private readonly List<(MenuItem Item,RoutedEventHandler Handler)> trayHandlers=[];
    private long trayGeneration,trayMenuGeneration;
    private bool trayCreating,trayHidePending,fullExitRequested,traySessionEnding;
    private void TrayMenu_Click(object sender,RoutedEventArgs e)
    {
        if(TrayMenu.IsChecked)TryEnableTray();else DisableTray();
    }
    internal bool TryEnableTray(Func<Action,Action,Action,ITrayIcon>? factory=null)
    {
        if(windowClosed||confirmedExit||trayCreating||traySessionEnding)return false;
        if(trayIcon is not null)return true;
        long generation=++trayGeneration;trayCreating=true;ITrayIcon? candidate=null;
        bool Current()=>!windowClosed&&!confirmedExit&&!traySessionEnding&&generation==trayGeneration;
        try
        {
            candidate=(factory??((a,b,c)=>new NativeTrayIcon(a,b,c)))(()=>{if(Current())RevealTrayWindow();},()=>{if(Current())OpenTrayMenu();},()=>{if(Current())DisableTray();});
            if(!Current())return false;
            if(!candidate.Registered){Notice.Text="트레이 아이콘 등록 실패 — 관리 창을 계속 표시합니다.";return false;}
            trayIcon=candidate;candidate=null;TrayMenu.IsChecked=true;
            if(!Current()){DisableTray();return false;}
            Notice.Text="이번 실행에서 트레이 상주를 켰습니다. 창 닫기는 숨김이며 잠금이 아닙니다. 파일 메뉴의 완전히 종료로 저장·잠금 후 종료하세요.";
            return true;
        }
        catch{if(Current())DisableTray();return false;}
        finally{trayCreating=false;try{candidate?.Dispose();}catch{}if(trayIcon is null)try{TrayMenu.IsChecked=false;}catch{}}
    }
    internal void DisableTray()
    {
        ++trayGeneration;ClearTrayMenu();var old=trayIcon;trayIcon=null;
        try{TrayMenu.IsChecked=false;}catch{}
        try{old?.Dispose();}catch{}
        if(!windowClosed&&!confirmedExit&&!closing)RevealTrayWindow();
    }
    private void ClearTrayMenu()
    {
        ++trayMenuGeneration;trayMenuCurrent=null;var old=trayContextMenu;trayContextMenu=null;var closedHandler=trayClosedHandler;trayClosedHandler=null;
        try{CompositionTarget.Rendering-=TrayRendering;}catch{}
        var handlers=trayHandlers.ToArray();trayHandlers.Clear();
        foreach(var pair in handlers)
        {
            try{pair.Item.Click-=pair.Handler;}catch{}
            try{pair.Item.Tag=null;}catch{}
            try{pair.Item.Header="";}catch{}
        }
        if(old is null)return;
        try{if(closedHandler is not null)old.Closed-=closedHandler;}catch{}
        foreach(Action clear in new Action[]{()=>old.IsOpen=false,()=>old.Items.Clear(),()=>old.PlacementTarget=null})try{clear();}catch{}
    }
    private void RevealTrayWindow()
    {
        if(windowClosed||confirmedExit||closing)return;
        ClearTrayMenu();try{RevealWithoutTrayRevocation();}catch{}
    }
    private bool TryHideToTray()
    {
        if(fullExitRequested||traySessionEnding||trayCreating||trayIcon is null)return false;
        var icon=trayIcon;long generation=trayGeneration;
        try
        {
            if(!icon.Registered){DisableTray();return true;}
            if(trayHidePending)return true;
            ClearTrayMenu();ClearSecretControls();trayHidePending=true;
            // WPF forbids changing Visibility during Closing, even after e.Cancel=true.
            _=Dispatcher.BeginInvoke(new Action(()=>HideTrayFrame(icon,generation)));
            return true;
        }
        catch{trayHidePending=false;DisableTray();RevealTrayWindow();return true;}
    }
    private void HideTrayFrame(ITrayIcon icon,long generation)
    {
        bool Current()=>!windowClosed&&!confirmedExit&&!closing&&!fullExitRequested&&!traySessionEnding&&generation==trayGeneration&&ReferenceEquals(trayIcon,icon)&&icon.Registered;
        try
        {
            if(!Current())return;
            Hide();if(!Current())RevealTrayWindow();
        }
        catch{DisableTray();RevealTrayWindow();}
        finally{trayHidePending=false;}
    }
    internal void SetTraySessionEnding(bool ending){traySessionEnding=ending;if(ending)ClearTrayMenu();}
    private void TrayRendering(object? sender,EventArgs e)
    {if(trayMenuCurrent is {} current&&!current())ClearTrayMenu();}
    private void OpenTrayMenu()
    {
        ClearTrayMenu();var icon=trayIcon;long generation=trayGeneration,menuGeneration=trayMenuGeneration,epoch=uiEpoch;
        var active=session;long sourceEpoch=active?.AttachmentPreviewEpoch??0;
        bool Current()=>!windowClosed&&!confirmedExit&&!closing&&!concealing&&generation==trayGeneration&&menuGeneration==trayMenuGeneration&&epoch==uiEpoch&&ReferenceEquals(trayIcon,icon)&&ReferenceEquals(session,active)&&(active is null||active.AttachmentPreviewEpoch==sourceEpoch);
        bool Unlocked()=>Current()&&active is {IsLocked:false};
        var context=new ContextMenu{Placement=PlacementMode.MousePoint,PlacementTarget=this};
        MenuItem Add(string title,Action command,bool enabled=true)
        {
            var item=new MenuItem{Header=title,IsEnabled=enabled,StaysOpenOnClick=true};
            RoutedEventHandler handler=(_,_)=>{if(!Current()){if(ReferenceEquals(trayContextMenu,context))ClearTrayMenu();return;}try{command();}catch{if(!windowClosed)try{Notice.Text="트레이 동작 실패 — 메모 관리 창에서 상태를 확인하세요.";}catch{}}finally{if(ReferenceEquals(trayContextMenu,context))ClearTrayMenu();}};
            item.Click+=handler;trayHandlers.Add((item,handler));context.Items.Add(item);return item;
        }
        try
        {
            if(!Current()||icon is null||!icon.Registered)return;
            trayContextMenu=context;
            trayMenuCurrent=Current;CompositionTarget.Rendering+=TrayRendering;
            Add("메모 관리 창 열기",RevealTrayWindow);
            Add("새 메모",()=>{if(!Unlocked())return;RevealWithoutTrayRevocation();if(Unlocked())NewNote_Click(this,new RoutedEventArgs());},Unlocked());
            var recent=active is {IsLocked:false}?active.Workspace.RecentNotes(uiDeviceId).Take(20).Select(n=>(n.Id,n.Title)).ToArray():[];
            foreach(var note in recent)
            {
                Guid id=note.Id;
                Add("최근: "+note.Title,()=>{if(!Unlocked())return;RevealWithoutTrayRevocation();if(!Unlocked())return;var draft=active!.Workspace.RecentNotes(uiDeviceId).FirstOrDefault(n=>n.Id==id);if(draft is not null)OpenSticky(draft);});
                if(!Current()){ClearTrayMenu();return;}
            }
            Add("잠금",()=>{if(Unlocked())Lock_Click(this,new RoutedEventArgs());},Unlocked());
            Add("완전히 종료",()=>Exit_Click(this,new RoutedEventArgs()));
            trayClosedHandler=(_,_)=>{if(ReferenceEquals(trayContextMenu,context))ClearTrayMenu();};context.Closed+=trayClosedHandler;
            if(!Current()||!icon.Focus()||!Current()){ClearTrayMenu();RevealTrayWindow();return;}
            context.IsOpen=true;if(!Current())ClearTrayMenu();
        }
        catch{ClearTrayMenu();RevealTrayWindow();}
    }
    private void RevealWithoutTrayRevocation()
    {
        if(windowClosed||confirmedExit||closing)return;
        if(!IsVisible)Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();
    }
}
