using MemoApp.Core.Transfer;
namespace MemoApp.Windows;

// Native capture stays on the UI caller; workers receive only owned bounded data and cancellation.
internal class DibClipboardBackend
{
    internal virtual NativeDibCapture Capture(Func<bool> allowed,CancellationToken token)=>NativeClipboardDibCapture.Capture(NativeClipboardDib.Instance,allowed,token);
    internal virtual uint GetSequence()=>NativeClipboardDib.Instance.GetSequence();
    internal virtual ClipboardPngSource Convert(ReadOnlyMemory<byte> input,CancellationToken token)=>DibToPng.Convert(input.Span,token);
}
public sealed partial class AttachmentPanel
{
    private static Task<ClipboardPngSource> ConvertDibAsync(DibClipboardBackend backend,ReadOnlyMemory<byte> input,CancellationToken token)=>Task.Run(()=>backend.Convert(input,token));
}
