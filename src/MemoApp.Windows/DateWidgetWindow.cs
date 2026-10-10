using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MemoApp.Core.Storage;
namespace MemoApp.Windows;
public sealed class DateWidgetWindow : Window
{
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly TextBlock time=new(){FontSize=28,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(10)};
    private readonly StackPanel panel=new(){Margin=new(12)};
    private readonly ScaleTransform scale=new();
    public DateWidgetWindow(string kind)
    {
        Title=kind=="clock"?"메모앱 시계":"메모앱 날짜 달력";Width=kind=="clock"?280:340;Height=kind=="clock"?150:330;MinWidth=200;MinHeight=100;
        panel.LayoutTransform=scale;panel.Children.Add(new TextBlock{Text="로컬 날짜 표시 · 일정/할 일 없음",TextWrapping=TextWrapping.Wrap});
        if(kind=="clock"){panel.Children.Add(time);timer.Tick+=(_,_)=>time.Text=DateTime.Now.ToString("HH:mm:ss");time.Text=DateTime.Now.ToString("HH:mm:ss");timer.Start();}
        else panel.Children.Add(new Calendar{DisplayDate=DateTime.Today,SelectedDate=DateTime.Today,Foreground=Brushes.Black,Background=Brushes.White});
        Content=panel;Closed+=(_,_)=>timer.Stop();
    }
    public void ApplyUiPreferences(UiPreferences preferences)
    {
        FontSize=preferences.FontSize;time.FontSize=preferences.FontSize*2;scale.ScaleX=scale.ScaleY=preferences.Scale;Background=preferences.DarkMode?new SolidColorBrush(Color.FromRgb(28,32,40)):Brushes.White;Foreground=preferences.DarkMode?Brushes.White:Brushes.Black;
    }
}
