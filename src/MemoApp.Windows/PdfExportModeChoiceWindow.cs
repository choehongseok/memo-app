using System.Windows;
using System.Windows.Controls;

namespace MemoApp.Windows;

internal enum PdfExportMode { Text,Visual }

internal sealed class PdfExportModeChoiceWindow:Window,IDisposable
{
    private readonly Func<bool> current;
    private readonly RadioButton text,visual;
    internal PdfExportMode? SelectedMode{get;private set;}
    internal PdfExportModeChoiceWindow(Func<bool> current)
    {
        this.current=current;Title="PDF 내보내기 방식";Width=470;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var panel=new StackPanel{Margin=new Thickness(18)};NameScope.SetNameScope(this,new NameScope());
        panel.Children.Add(new TextBlock{Text="내보낼 PDF 방식을 선택하세요.",Margin=new Thickness(0,0,0,12),TextWrapping=TextWrapping.Wrap});
        text=new RadioButton{Name="TextModeChoice",Content="텍스트 PDF (지원 문자·검색/복사 가능)",IsChecked=true,Margin=new Thickness(0,0,0,10)};
        visual=new RadioButton{Name="VisualModeChoice",Content="표시용 PDF (텍스트 검색·복사 미지원)",Margin=new Thickness(0,0,0,10)};
        RegisterName(text.Name,text);RegisterName(visual.Name,visual);panel.Children.Add(text);panel.Children.Add(visual);
        panel.Children.Add(new TextBlock{Text="표시용 PDF는 plain 메모의 제목·본문만 지원하며, 한 페이지씩 이미지로 저장합니다. 글꼴·문자 조합·출력 한도에 따라 거절할 수 있습니다.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Name="CancelButton",Content="취소",IsCancel=true,MinWidth=80,Margin=new Thickness(0,0,8,0)};
        var next=new Button{Name="ContinueButton",Content="계속",MinWidth=80};RegisterName(cancel.Name,cancel);RegisterName(next.Name,next);
        next.Click+=(_,_)=>{if(!this.current()){Close();return;}SelectedMode=visual.IsChecked==true?PdfExportMode.Visual:PdfExportMode.Text;DialogResult=true;};
        buttons.Children.Add(cancel);buttons.Children.Add(next);panel.Children.Add(buttons);Content=panel;
    }
    public void Dispose()=>Close();
}
