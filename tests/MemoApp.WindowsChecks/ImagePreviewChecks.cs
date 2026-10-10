using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static partial class Program
{
    private static Image PreviewImage(object panel)=>(Image)(panel.GetType().GetProperty("PreviewImage")?.GetValue(panel)??throw new Exception("Selected encrypted PNG Image UI is missing"));
    private static Task<bool> PreviewSelected(object panel)=>(Task<bool>)panel.GetType().GetMethod("PreviewSelectedAsync")!.Invoke(panel,null)!;
    private static async Task ImagePreviewRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-png-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();
        byte[] png=Convert.FromHexString("89504e470d0a1a0a0000000d4948445200000002000000010806000000f4227f8a0000000e49444154789c63f8cfc000420d000f7a037e77e97f970000000049454e44ae426082");
        Window? window=null;
        try
        {
            using var session=new SaveCoordinator(EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret),TimeProvider.System);var note=session.Workspace.CreateNote();Require(await session.PrepareAttachmentsAsync(),"Image root");
            Guid id=session.AttachBytes(note,png,"opaque.txt","application/octet-stream",note.EditVersion);session.AttachBytes(note,new byte[]{1,2,3},"spoof.png","image/png",note.EditVersion);Require(await session.SaveAsync(),"Image fixture save");
            var first=CreateAttachmentPanel(session,note,()=>true,_=>{});var second=CreateAttachmentPanel(session,note,()=>true,_=>{});var host=new StackPanel();host.Children.Add(first);host.Children.Add(second);window=new Window{Content=host,Width=500,Height=700};window.Show();await Idle();
            var image=PreviewImage(first);Require(image.Source is null,"Selection never automatically decrypts or decodes");AttachmentList(first).SelectedIndex=0;AttachmentList(second).SelectedIndex=0;
            string before=System.Text.Json.JsonSerializer.Serialize(session.Workspace.Capture());Task<bool> pending=PreviewSelected(first);Require(!await PreviewSelected(second),"Main and sticky share one app-wide slot before decryption");Require(await pending,"Selected exact bounded PNG displayed");
            var bitmap=image.Source as BitmapSource??throw new Exception("PNG bitmap missing");var pixels=new byte[8];bitmap.CopyPixels(pixels,8,0);Require(bitmap.IsFrozen&&bitmap.PixelWidth==2&&bitmap.PixelHeight==1&&bitmap.DpiX==96&&pixels.SequenceEqual(new byte[]{0,0,255,255,255,0,0,128}),"WPF detached straight-alpha BGRA exact bytes and fixed DPI");
            var visual=new Image{Source=bitmap,Stretch=Stretch.None};visual.Measure(new Size(2,1));visual.Arrange(new Rect(0,0,2,1));var rendered=new RenderTargetBitmap(2,1,96,96,PixelFormats.Pbgra32);rendered.Render(visual);var native=new byte[8];rendered.CopyPixels(native,8,0);Require(native.SequenceEqual(new byte[]{0,0,255,255,128,0,0,128}),"Independent WPF native rendering oracle preserves alpha and channel order");visual.Source=null;
            Require(await PreviewSelected(second)&&image.Source is null&&PreviewImage(second).Source is BitmapSource,"Only one visible image globally; second host replaces first");
            AttachmentList(second).SelectedIndex=1;Require(PreviewImage(second).Source is null&&!await PreviewSelected(second),"Selected opaque bad bytes clear old image and spoofed MIME cannot activate generic decoder");
            Require(System.Text.Json.JsonSerializer.Serialize(session.Workspace.Capture())==before,"Preview success/failure preserves exact snapshot/ciphertext/refs/history");
            AttachmentList(first).SelectedIndex=0;pending=PreviewSelected(first);AttachmentList(first).SelectedIndex=1;Require(!await pending&&image.Source is null,"Selection changes before posted publication discard result");
            AttachmentList(first).SelectedIndex=0;pending=PreviewSelected(first);note.Title="changed while decoding";Require(!await pending&&image.Source is null,"Edit version changes discard result and source authority");
            Require(await session.SaveAsync(),"Image mutation saved");AttachmentList(first).SelectedIndex=0;Require(await PreviewSelected(first),"Preview retry after invalidation returns capacity");
            Require(System.Text.Json.JsonSerializer.Serialize(session.Workspace.Capture()).Contains(id.ToString()),"Original reference survives preview");
            AttachmentList(first).SelectedIndex=0;pending=PreviewSelected(first);await session.LockAsync();Require(image.Source is null&&PreviewImage(second).Source is null&&!await pending,"Lock immediately clears both WPF images and rejects late publication");await session.WhenAttachmentReadsIdle;
            Require(PanelDisposed(first)&&PanelDisposed(second),"Conceal closes both hosts");

        }
        finally{window?.Close();CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}
