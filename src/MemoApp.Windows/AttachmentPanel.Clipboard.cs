using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MemoApp.Core.Transfer;
namespace MemoApp.Windows;
public sealed partial class AttachmentPanel
{
    private readonly Button pastePng=new(){Content="클립보드 이미지 붙이기",Padding=new(6,3,6,3),Margin=new(6,0,0,0)};
    private async void PastePngClicked(object sender,RoutedEventArgs e)=>await ImportClipboardImageAsync(Clipboard.GetDataObject);
    private async void ClipboardKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.V||Keyboard.Modifiers!=ModifierKeys.Control||!IsKeyboardFocusWithin)return;
        e.Handled=true;await ImportClipboardImageAsync(Clipboard.GetDataObject);
    }
    public Task<bool> ImportClipboardPngAsync(Func<IDataObject?> getData)=>ImportClipboardAsync(getData,null);
    internal Task<bool> ImportClipboardImageAsync(Func<IDataObject?> getData,DibClipboardBackend? backend=null)=>ImportClipboardAsync(getData,backend??new DibClipboardBackend());
    private async Task<bool> ImportClipboardAsync(Func<IDataObject?> getData,DibClipboardBackend? dibBackend)
    {
        Dispatcher.VerifyAccess();if(!Current()||busy||!ImagePreviewAdmission.TryEnter(Dispatcher))return false;
        var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch;var token=cancellation.Token;busy=true;ClipboardPngSource? owned=null;NativeDibCapture? captured=null;ClipboardPngSource? converted=null;long selection=inlineSelectionGeneration;bool dibSelectionAuthority=false;uint initialClipboardSequence=0;
        bool Scalars()=>!IsDisposed&&ReferenceEquals(session,active)&&ReferenceEquals(note,source)&&!active.IsLocked&&source.EditVersion==version&&active.AttachmentPreviewEpoch==epoch&&!token.IsCancellationRequested&&(!dibSelectionAuthority||inlineSelectionGeneration==selection);
        bool Allowed()=>Scalars()&&Same(active,source,version)&&Scalars();
        bool SequenceAllowed(){if(!Allowed())return false;if(captured is null)return true;uint now=dibBackend!.GetSequence();return Allowed()&&now!=0&&now==captured.Sequence;}
        try
        {
            Refresh();if(!Allowed())return false;selection=inlineSelectionGeneration;
            if(dibBackend is not null){initialClipboardSequence=dibBackend.GetSequence();if(!Allowed())return false;}
            var data=getData();if(!Allowed())return false;
            bool raw=data is not null&&data.GetDataPresent("PNG",false);if(!Allowed())return false;
            if(raw)
            {
                object borrowed=data!.GetData("PNG",false);if(!Allowed())return false;
                if(borrowed is byte[] bytes)owned=ClipboardPngSource.Capture(bytes);
                else if(borrowed is MemoryStream stream&&stream.GetType()==typeof(MemoryStream))owned=CaptureClipboardStream(stream);
                else return false;
            }
            else
            {
                if(dibBackend is null)return false;dibSelectionAuthority=true;if(!Allowed()||initialClipboardSequence==0)return false;
                uint fallbackSequence=dibBackend.GetSequence();if(!Allowed()||fallbackSequence!=initialClipboardSequence)return false;
                captured=dibBackend.Capture(Allowed,token);if(captured.Sequence!=initialClipboardSequence||!SequenceAllowed())return false;
                converted=await ConvertDibAsync(dibBackend,captured.Content,token);if(!SequenceAllowed())return false;
            }
            if(!SequenceAllowed())return false;
            var preparation=active.PrepareAttachments(epoch);long expectedEpoch=preparation.PreviewEpoch;var preparing=preparation.Completion;
            // First anchoring synchronously accepts exactly one prepared snapshot. Any other revocation remains a refusal.
            if(active.AttachmentPreviewEpoch!=expectedEpoch)return false;epoch=expectedEpoch;
            if(!await preparing||!SequenceAllowed())return false;
            if(!SequenceAllowed())return false;
            if(converted is not null)active.AttachBytes(source,converted.Content,"clipboard-image.png","image/png",version);
            else active.AttachBytes(source,owned!.Content,"clipboard-image.png","image/png",version);
            if(!Current()||!ReferenceEquals(session,active))return false;bool saved=await active.SaveAsync();
            if(!Current()||!ReferenceEquals(session,active))return false;
            Report(saved&&!active.IsDirty?(converted is not null?"클립보드 DIB 픽셀을 PNG로 변환해 암호 첨부로 저장했습니다. OS 클립보드는 앱 잠금 후에도 남습니다.":"클립보드 PNG 원본을 암호 첨부로 저장했습니다. OS 클립보드에는 원본이 남으며 앱 잠금으로 지워지지 않습니다."):"클립보드 PNG 첨부를 반영했지만 저장 완료는 확인하지 못했습니다. 전체 저장 상태를 확인하세요.");return saved;
        }
        catch(OperationCanceledException){return false;}
        catch{Report("이미지 붙이기 실패 — 제한 PNG와 4 MiB 이하 CF_DIB의 24/32비트 BI_RGB만 지원합니다. OS 클립보드는 보존했습니다. 첨부·암호 저장 상태를 확인하세요.");return false;}
        finally{converted?.Dispose();captured?.Dispose();owned?.Dispose();busy=false;ImagePreviewAdmission.Exit();if(!IsDisposed)Refresh();}
    }
    private static ClipboardPngSource CaptureClipboardStream(MemoryStream borrowed,Action<byte[]>? copied=null,Action? beforeRestore=null)
    {
        long length=borrowed.Length;if(length is <8 or >4194304)throw new InvalidDataException("Clipboard PNG stream limit");byte[] copy=new byte[checked((int)length)];long position=borrowed.Position;
        try
        {
            copied?.Invoke(copy);
            try{borrowed.Position=0;int offset=0;while(offset<copy.Length){int read=borrowed.Read(copy,offset,Math.Min(65536,copy.Length-offset));if(read==0)throw new InvalidDataException("Short clipboard PNG stream");offset+=read;}if(borrowed.ReadByte()!=-1||borrowed.Length!=length)throw new InvalidDataException("Changed clipboard PNG stream");}
            finally{try{beforeRestore?.Invoke();}finally{borrowed.Position=position;}}
            return ClipboardPngSource.Capture(copy);
        }
        finally{CryptographicOperations.ZeroMemory(copy);}
    }
}
