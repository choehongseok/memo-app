using System.Windows;
using System.Windows.Controls;
namespace MemoApp.Windows;
public sealed partial class AttachmentPanel
{
    private readonly Button inlineImage=new(){Content="문서에 이미지 추가",Padding=new(6,3,6,3),Margin=new(6,0,0,0),IsEnabled=false,Focusable=false};
    private Func<Guid,long,Func<bool>,Task<bool>>? insertInlineImage;
    private Func<bool>? canInsertInlineImage;
    private Action? invalidateInlineImage;
    private long inlineSelectionGeneration;
    internal void SetInlineImageInsertion(Func<Guid,long,Func<bool>,Task<bool>> insert,Func<bool> available,Action invalidate)
    {Dispatcher.VerifyAccess();insertInlineImage=insert;canInsertInlineImage=available;invalidateInlineImage=invalidate;RefreshInlineImage();}
    private void RefreshInlineImage()
    {
        long selection=inlineSelectionGeneration;Guid? id=(FilesList.SelectedItem as Entry)?.Id;
        bool Selected()=>selection==inlineSelectionGeneration&&(FilesList.SelectedItem as Entry)?.Id==id;
        try
        {
            bool allowed=Current()&&!busy&&FilesList.SelectedItem is Entry {Mime:"image/png"}&&canInsertInlineImage?.Invoke()==true&&Selected()&&!IsDisposed;
            inlineImage.IsEnabled=allowed;
            if(!Selected()||IsDisposed)inlineImage.IsEnabled=false;
        }
        catch{try{inlineImage.IsEnabled=false;}catch{}}
    }
    private async void InlineImageClicked(object sender,RoutedEventArgs e)=>await InsertSelectedInlineImageAsync();
    public async Task<bool> InsertSelectedInlineImageAsync()
    {
        Dispatcher.VerifyAccess();
        if(IsDisposed||busy||FilesList.SelectedItem is not Entry {Mime:"image/png"} selected||insertInlineImage is not { } insert||canInsertInlineImage is not { } available||session is not { } active||note is not { } source)return false;
        long version=source.EditVersion,selection=inlineSelectionGeneration;
        // Callbacks can change selection away and back without changing the note.
        // Repeat the scalar checks after Current/available and each native setter.
        bool Unchanged()=>!IsDisposed&&selection==inlineSelectionGeneration&&FilesList.SelectedItem is Entry item&&item.Id==selected.Id&&ReferenceEquals(session,active)&&ReferenceEquals(note,source)&&!active.IsLocked&&source is {IsClosed:false,IsDeleted:false}&&source.EditVersion==version&&source.AttachmentIds.Contains(selected.Id);
        bool Allowed()
        {
            if(!Unchanged()||!Same(active,source,version)||!Unchanged())return false;
            if(!available())return false;
            return Unchanged();
        }
        try{if(!Allowed())return false;}catch{return false;}
        busy=true;
        try
        {
            RefreshInlineImage();if(!Allowed())return false;
            return await insert(selected.Id,version,Allowed);
        }
        catch{try{Report("이미지 추가를 적용하지 못했습니다. 현재 문서·선택과 저장 상태를 확인하세요.");}catch{}return false;}
        finally{busy=false;if(!IsDisposed)RefreshInlineImage();}
    }
}
