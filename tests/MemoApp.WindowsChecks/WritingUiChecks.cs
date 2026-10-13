using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task WritingUiRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-writing-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;var files=new PreviewFaultFiles();
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();
            Require(!Control<Expander>(main,"CreateTools").IsExpanded&&!Control<Expander>(main,"RecoveryTools").IsExpanded,"Unlock is code-first with separate optional creation/repair");
            await Idle();UiValidationEvidence.Capture(main,"locked");
            Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret,files));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();
            main.Width=1000;var scale=Control<ScaleTransform>(main,"UiScale");scale.ScaleX=scale.ScaleY=1.75;await Idle();
            var scroll=Control<ScrollViewer>(main,"WorkspaceScroll");Require(scroll.ScrollableWidth>0&&Control<Grid>(main,"ViewRoot").ActualWidth>=1000,"Maximum scale retains horizontal access to full workspace");
            scroll.ScrollToRightEnd();await Idle();Require(scroll.HorizontalOffset>0,"Scaled editor is reachable by scrolling");scale.ScaleX=scale.ScaleY=1;main.Width=1280;await Idle();
            Invoke(main,"NewNote_Click",main,new RoutedEventArgs());await Idle();
            Require(active.Workspace.Notes.Count==1&&Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==0,"Manager-only creation stays in manager");
            Require(Control<TextBox>(main,"BodyEditor").IsKeyboardFocused,"Manager new note starts in body");
            Invoke(main,"NewStickyNote_Click",main,new RoutedEventArgs());await Idle();
            var windows=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows");Require(active.Workspace.Notes.Count==2&&windows.Count==1,"Primary creation opens exactly the new sticky");
            var sticky=windows.Values.Single();var note=(NoteDraft)sticky.DataContext;
            Require(Control<TextBox>(sticky,"BodyEditor").IsKeyboardFocused,"New sticky starts in body");
            Require(!Control<Expander>(sticky,"AttachmentTools").IsExpanded&&!Control<Expander>(sticky,"MoreTools").IsExpanded,"Sticky optional tools start closed");
            Control<Expander>(sticky,"AttachmentTools").IsExpanded=true;await Idle();Require(Control<ContentControl>(sticky,"AttachmentHost").IsVisible,"Attachment/OCR remains reachable");
            Control<Expander>(sticky,"MoreTools").IsExpanded=true;Control<CheckBox>(sticky,"FoldToggle").IsChecked=true;await Idle();
            var footer=Control<TextBlock>(sticky,"SaveState");var footerPoint=footer.TranslatePoint(new Point(0,0),sticky);
            Require(!Control<Expander>(sticky,"MoreTools").IsExpanded&&footer.IsVisible&&footer.ActualHeight>0&&footerPoint.Y+footer.ActualHeight<=sticky.ActualHeight,"Folded note keeps actual save footer visible");
            Control<CheckBox>(sticky,"FoldToggle").IsChecked=false;await Idle();
            note.Text="합성 한글 작성";Require(Control<TextBlock>(sticky,"SaveState").Text==active.Status&&active.IsDirty,"Sticky immediately shows coordinator dirty state");
            var states=new List<string>();active.Changed+=()=>states.Add(Control<TextBlock>(sticky,"SaveState").Text);
            Require(await active.SaveAsync(),"Shared synthetic save succeeds");Require(states.Contains("저장 중")&&Control<TextBlock>(sticky,"SaveState").Text=="저장됨","Sticky reports real saving and saved events");
            Control<Expander>(sticky,"AttachmentTools").IsExpanded=false;
            Control<Expander>(sticky,"MoreTools").IsExpanded=false;
            await Idle();UiValidationEvidence.Capture(main,"manager");UiValidationEvidence.Capture(sticky,"sticky");
            Control<CheckBox>(sticky,"ImportantToggle").IsChecked=true;Require(note.Important,"Importance icon changes same draft");
            Control<CheckBox>(sticky,"TopmostToggle").IsChecked=true;Require(sticky.Topmost&&!Control<CheckBox>(sticky,"PositionToggle").IsChecked.GetValueOrDefault(),"Topmost and position lock remain separate");
            var descriptor=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));
            EventHandler statusFault=(_,_)=>throw new IOException("Synthetic close status setter fault");descriptor.AddValueChanged(footer,statusFault);
            try{sticky.Close();}finally{descriptor.RemoveValueChanged(footer,statusFault);}await Idle();string closedState=footer.Text;
            Require(Field<Action?>(active,"Changed")?.GetInvocationList().All(d=>!ReferenceEquals(d.Target,sticky))!=false,"Native close status fault cannot retain coordinator observer");
            note.Text="닫은 뒤 합성 수정";Require(active.Workspace.Notes.Contains(note)&&!note.IsDeleted&&windows.Count==0,"Window close preserves note");
            Require(Control<TextBlock>(sticky,"SaveState").Text==closedState&&Control<TextBox>(sticky,"BodyEditor").Text.Length==0,"Closed observer detached and body purged");
            Invoke(main,"OpenSticky",note);await Idle();sticky=windows[note.Id];
            active.Workspace.ConvertMode(note,"rich",true);Invoke(main,"RefreshNotes",note);await Idle();
            var formatting=Field<StructuredNoteEditor>(main,"structuredEditor");var tools=((DockPanel)formatting.Content).Children.OfType<Expander>().Single();tools.IsExpanded=true;main.Activate();await Idle();
            foreach(var command in Field<WrapPanel>(formatting,"toolbar").Children.OfType<Button>())
            {Keyboard.Focus(command);Require(command.IsKeyboardFocused,"Every formatting command accepts keyboard focus");}
            byte[] priorCipher=File.ReadAllBytes(Path.Combine(root,"vault","current.vault"));note.Title="합성 저장 실패";files.Fail=true;files.Release.Set();
            Require(!await active.SaveAsync()&&active.IsDirty&&Control<TextBlock>(sticky,"SaveState").Text.Contains("저장 실패"),"Real flush failure is visible in sticky and retains dirty draft");
            Require(priorCipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"vault","current.vault"))),"Failed write preserves prior ciphertext");
            await active.LockAsync();await Idle();
            Require(active.PendingKind=="ciphertext","Failed-save recovery candidate is retained after lock");
            Require(!sticky.IsVisible&&Control<TextBox>(sticky,"BodyEditor").Text.Length==0&&Control<TextBlock>(sticky,"SaveState").Text=="잠금","Lock closes and clears sticky with locked status");
            Require(!Control<MenuItem>(main,"PdfExportButton").IsEnabled&&!Control<MenuItem>(main,"WholeTransferButton").IsEnabled,"Moved export commands retain lock gates");
        }
        finally
        {
            if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();if(active?.PendingKind!="none"){active?.Dispose();typeof(MainWindow).GetField("session",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(main,null);}else Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}
            CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);
        }
    }
}
