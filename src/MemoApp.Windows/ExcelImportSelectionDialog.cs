using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Transfer;
namespace MemoApp.Windows;

internal sealed class ExcelImportSelectionDialog : Window
{
    private readonly WorkbookImportCatalog catalog;
    private readonly ComboBox sheet;
    private readonly TextBox title=new(){Text="1",Width=80};
    private readonly TextBox body=new(){Text="2",Width=80};
    private readonly CheckBox skip=new(){Content="첫 행 건너뛰기 (선택하면 첫 데이터 행은 가져오지 않음)",IsChecked=true};
    private readonly RadioButton mapping=new(){Content="시트·열을 직접 선택",IsChecked=true,GroupName="mode"};
    private readonly RadioButton template=new(){Content="기존 제목·본문 2열 / 메모앱 5열 템플릿 (첫 시트)",GroupName="mode"};
    private readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap};
    internal WorkbookImportSelection? Selection { get; private set; }
    internal ExcelImportSelectionDialog(WorkbookImportCatalog catalog)
    {
        this.catalog=catalog;Title="Excel 시트·열 선택";Width=530;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(20)};Content=panel;panel.Children.Add(mapping);panel.Children.Add(template);
        sheet=new ComboBox{ItemsSource=catalog.Sheets,DisplayMemberPath="Name",SelectedIndex=0,Margin=new Thickness(0,12,0,8)};panel.Children.Add(new TextBlock{Text="가져올 시트"});panel.Children.Add(sheet);
        var columns=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,12)};columns.Children.Add(new TextBlock{Text="제목 열 번호 (1~16384): ",VerticalAlignment=VerticalAlignment.Center});columns.Children.Add(title);columns.Children.Add(new TextBlock{Text="  본문 열 번호: ",VerticalAlignment=VerticalAlignment.Center});columns.Children.Add(body);panel.Children.Add(columns);panel.Children.Add(skip);
        panel.Children.Add(new TextBlock{Text="열 번호는 A=1, B=2, C=3입니다. 제목과 본문은 서로 다른 열을 선택하세요. 문자열·숫자 원문·TRUE/FALSE를 일반 텍스트로 가져옵니다. 날짜는 숫자 일련번호를 유지합니다. 수식이 있는 선택 시트는 거절합니다. 최대 100개 메모이며 초과하면 전체 가져오기를 거절합니다.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});panel.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};var cancel=new Button{Content="취소",IsCancel=true,MinWidth=75,Margin=new Thickness(0,0,8,0)};var accept=new Button{Content="매핑 확인",IsDefault=true,MinWidth=95};buttons.Children.Add(cancel);buttons.Children.Add(accept);panel.Children.Add(buttons);
        accept.Click+=(_,_)=>{if(TrySelect())DialogResult=true;};template.Checked+=(_,_)=>SetMapping(false);mapping.Checked+=(_,_)=>SetMapping(true);
    }
    internal void Revoke()
    {
        Selection=null;sheet.ItemsSource=null;title.Clear();body.Clear();error.Text="";Content=null;
        try{Close();}catch(InvalidOperationException){}
    }
    private void SetMapping(bool enabled){sheet.IsEnabled=enabled;title.IsEnabled=enabled;body.IsEnabled=enabled;skip.IsEnabled=enabled;}
    private bool TrySelect()
    {
        if(template.IsChecked==true){Selection=new(1,1,2,true,catalog.SourceSha256){UseKnownTemplate=true};return true;}
        if(sheet.SelectedItem is not WorkbookSheetChoice choice||!int.TryParse(title.Text,NumberStyles.None,CultureInfo.InvariantCulture,out int titleColumn)||!int.TryParse(body.Text,NumberStyles.None,CultureInfo.InvariantCulture,out int bodyColumn)||titleColumn is <1 or >16384||bodyColumn is <1 or >16384||titleColumn==bodyColumn){error.Text="시트와 서로 다른 제목·본문 열 번호 (1~16384)를 확인하세요.";return false;}
        Selection=new(choice.Index,titleColumn,bodyColumn,skip.IsChecked==true,catalog.SourceSha256);return true;
    }
}
