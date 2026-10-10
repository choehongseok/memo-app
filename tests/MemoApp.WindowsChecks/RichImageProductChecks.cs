using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Threading;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task RichImageProductRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-inline-product-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var session=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=session.Workspace.CreateNote();note.Text="before\nafter";session.Workspace.ConvertMode(note,"rich",true);Require(await session.PrepareAttachmentsAsync(),"H01 product root");Guid id=session.AttachBytes(note,PreviewPng,"product.png","image/png",note.EditVersion);session.AttachBytes(note,PreviewPng,"second.png","image/png",note.EditVersion);Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");Require(await session.SaveAsync(),"H01 product baseline saved");await Idle();
            Invoke(main,"OpenSticky",note);await Idle();var sticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id];var editor=(StructuredNoteEditor)Control<ContentControl>(main,"StructuredHost").Content;var panel=Field<AttachmentPanel>(main,"attachmentPanel");panel.FilesList.SelectedIndex=0;
            var original=note.Document!;editor.RichInput.Selection.Select(editor.RichInput.Document.ContentStart,editor.RichInput.Document.ContentEnd);
            Require(!await panel.InsertSelectedInlineImageAsync()&&ReferenceEquals(note.Document,original),"H01 nonempty rich selection cannot add an image");
            var paragraph=(Paragraph)editor.RichInput.Document.Blocks.FirstBlock!;var caret=paragraph.ContentStart.GetInsertionPosition(System.Windows.Documents.LogicalDirection.Forward);editor.RichInput.Selection.Select(caret,caret);
            var live=Field<Func<bool>>(editor,"current");bool caretReentry=false;
            SetField(editor,"current",(Func<bool>)(()=>{if(!caretReentry){caretReentry=true;var other=editor.RichInput.Document.Blocks.LastBlock!.ContentStart.GetInsertionPosition(LogicalDirection.Forward);editor.RichInput.Selection.Select(other,other);editor.RichInput.Selection.Select(caret,caret);}return live();}));
            try{Require(!editor.TryGetCollapsedBlockBoundary(out _)&&caretReentry&&ReferenceEquals(note.Document,original),"H01 current callback changing caret away and back cannot authorize a stale block boundary");}
            finally{SetField(editor,"current",live);}
            var insertButton=Field<Button>(panel,"inlineImage");var enabledDescriptor=DependencyPropertyDescriptor.FromProperty(System.Windows.UIElement.IsEnabledProperty,typeof(Button));
            foreach(bool returnToOriginal in new[]{false,true})
            {
                panel.FilesList.SelectedIndex=0;bool fired=false;var before=note.Document!;long version=note.EditVersion;
                EventHandler changeSelection=(_,_)=>{if(!insertButton.IsEnabled&&!fired){fired=true;panel.FilesList.SelectedIndex=1;if(returnToOriginal)panel.FilesList.SelectedIndex=0;}};
                enabledDescriptor.AddValueChanged(insertButton,changeSelection);
                try{Require(!await panel.InsertSelectedInlineImageAsync()&&fired&&ReferenceEquals(note.Document,before)&&note.EditVersion==version&&RichDocumentCodec.Images(note.Document!).IsEmpty,$"H01 real IsEnabled setter selected PNG reentry rejects stale ID even when selection returns: awayBack={returnToOriginal}");}
                finally{enabledDescriptor.RemoveValueChanged(insertButton,changeSelection);}
            }
            panel.FilesList.SelectedIndex=0;
            Require(await panel.InsertSelectedInlineImageAsync(),"H01 main explicit selected authenticated PNG inserts after collapsed caret block");await Idle();
            var mainHost=Control<ContentControl>(main,"StructuredHost");var stickyHost=Control<ContentControl>(sticky,"StructuredHost");Require(mainHost.Content is RichImageDocumentView&&stickyHost.Content is RichImageDocumentView&&editor.IsDisposed&&RichDocumentCodec.Images(note.Document!).Single() is {BlockIndex:1,AttachmentId:var reference}&&reference==id,"H01 main and sticky switch to canonical ordered readonly v2 viewer");
            var mainView=(RichImageDocumentView)mainHost.Content;var stickyView=(RichImageDocumentView)stickyHost.Content;Require(mainView.ImageForBlock(1).Source is null&&stickyView.ImageForBlock(1).Source is null&&!BindingOperations.IsDataBound(Control<TextBox>(main,"BodyEditor"),TextBox.TextProperty)&&!BindingOperations.IsDataBound(Control<TextBox>(sticky,"BodyEditor"),TextBox.TextProperty),"H01 insertion never paints pixels or adds lossy plain binding");
            Require(await mainView.DisplayImageAsync(1),"H01 actual main viewer clicked display");Require(await stickyView.DisplayImageAsync(1)&&mainView.ImageForBlock(1).Source is null,"H01 actual sticky replaces main image globally");
            panel.FilesList.SelectedIndex=-1;Require(await mainView.DisplayImageAsync(1),"H01 document pixels independent of current attachment list selection");panel.FilesList.SelectedIndex=0;Require(mainView.ImageForBlock(1).Source is null,"H01 changing matching host attachment selection revokes document pixels");
            Control<System.Windows.Controls.CheckBox>(sticky,"FoldToggle").IsChecked=true;Require(stickyView.ImageForBlock(1).Source is null&&!await stickyView.DisplayImageAsync(1),"H01 folded sticky cannot display pixels");Control<System.Windows.Controls.CheckBox>(sticky,"FoldToggle").IsChecked=false;
            var stickyPanel=Field<AttachmentPanel>(sticky,"attachmentPanel");stickyPanel.FilesList.SelectedIndex=0;Require(await stickyPanel.InsertSelectedInlineImageAsync(),"H01 readonly v2 explicit action appends without inventing a native caret");await Idle();Require(RichDocumentCodec.Images(note.Document!).Select(image=>image.BlockIndex).SequenceEqual([1,3])&&mainView.ImageForBlock(1).Source is null&&stickyView.ImageForBlock(1).Source is null,"H01 append preserves first image placement and clears pixels without automatic decode");
            string expected=note.Document!.SourceJson;Require(await session.SaveAsync(),"H01 product placement encrypted save");string backup=Path.Combine(Path.GetTempPath(),"memo-inline-backup-"+Guid.NewGuid().ToString("N")+".vault");
            try
            {
                Require(await session.BackupAsync(backup),"H01 actual manual encrypted backup");string restoreRoot=backup+".restore";string candidate=EncryptedVault.ImportEncryptedCopy(restoreRoot,backup,secret);using var restored=EncryptedVault.Open(restoreRoot,secret,candidate);Require(restored.Loaded.Notes.Single(n=>n.NoteId==note.Id).Document!.SourceJson==expected,"H01 manual backup preserves exact canonical image placement");
            }
            finally{if(File.Exists(backup))File.Delete(backup);if(Directory.Exists(backup+".restore"))Directory.Delete(backup+".restore",true);}
            await session.LockAsync();await Idle();Require(mainHost.Content is null&&stickyHost.Content is null&&mainView.IsDisposed&&stickyView.IsDisposed,"H01 actual main/sticky conceal purges viewer and source callbacks");Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
            using var reopened=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System);var reopenedNote=reopened.Workspace.Notes.Single(n=>n.Id==note.Id);Require(reopenedNote.Document!.SourceJson==expected&&RichDocumentCodec.Images(reopenedNote.Document).Length==2&&reopenedNote.AttachmentIds.Contains(id),"H01 encrypted restart preserves placement and attachment references");
            using var again=new RichImageDocumentView(reopened,reopenedNote,()=>true,_=>{});Require(again.ImageForBlock(1).Source is null&&again.ImageForBlock(3).Source is null,"H01 restarted viewer requires another explicit display click");
        }
        finally
        {
            if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();SetField(main,"confirmedExit",true);main.Close();active?.Dispose();}
            CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
