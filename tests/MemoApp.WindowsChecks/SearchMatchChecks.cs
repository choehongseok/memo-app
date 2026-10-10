using System.IO;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Windows.Threading;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task SearchMatchRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-search-match-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var session=Field<SaveCoordinator>(main,"session");var note=session.Workspace.CreateNote();note.Title="합성 제목";note.Text=new string('x',60000)+" 먼 끝의 한글 찾기 문장 😀 마지막";Invoke(main,"RefreshNotes",note);await Idle();
            Require(main.FindName("SearchMatchPreview") is TextBlock,"Selected search-result sentence highlight is missing");
            var preview=Control<TextBlock>(main,"SearchMatchPreview");Run Match()=>(Run)main.FindName("SearchMatch");string All()=>string.Concat(preview.Inlines.OfType<Run>().Select(x=>x.Text));
            Field<DispatcherTimer>(main,"timer").Stop();await Field<Task>(main,"recentTask");Require(await session.SaveAsync(),"Accepted immutable search baseline, including settled recent metadata, saved");
            string before=JsonSerializer.Serialize(session.Workspace.Capture());long version=note.EditVersion;var query=Control<TextBox>(main,"SearchInput");query.Text="한글 찾기";await Idle();
            Require(preview.IsVisible&&Match().Text=="한글 찾기"&&Match().Background is not null&&All().Length<=512&&All().Contains("😀"),"Actual selected far-tail result exposes bounded sentence with literal emphasized match");Require(JsonSerializer.Serialize(session.Workspace.Capture())==before&&note.EditVersion==version,"Search context is display-only and preserves exact note/history/snapshot");
            Control<ComboBox>(main,"SearchFieldFilter").SelectedIndex=1;await Idle();Require(!preview.IsVisible&&All()=="","Title-only absent hit removes body context");Control<ComboBox>(main,"SearchFieldFilter").SelectedIndex=0;await Idle();
            note.Text="new changed source without hit";Require(All()==""&&!preview.IsVisible,"Post-mutation source event clears old excerpt synchronously before queued refresh");await Idle();
            note.Text="Cafe\u0301 literal .* <script>";query.Text="CAFÉ";await Idle();Require(Match().Text=="Café","Actual NFC/case match uses inert display projection");query.Text=".*";await Idle();Require(Match().Text==".*"&&All().Contains("<script>"),"Native UI treats HTML/regex-looking body as literal text");
            bool clearFault=false;var beforeRun=(Run)main.FindName("SearchMatchBefore");var beforeDescriptor=DependencyPropertyDescriptor.FromProperty(System.Windows.Documents.Run.TextProperty,typeof(Run));EventHandler nativeClearFault=(_,_)=>{if(!clearFault&&beforeRun.Text.Length==0){clearFault=true;throw new InvalidOperationException("Synthetic native search clear callback failure");}};beforeDescriptor.AddValueChanged(beforeRun,nativeClearFault);
            try{query.Clear();Require(clearFault&&All()==""&&!preview.IsVisible,"Every derived field clears despite one throwing native callback");}finally{beforeDescriptor.RemoveValueChanged(beforeRun,nativeClearFault);}
            Require(All()==""&&!preview.IsVisible,"Clearing query clears native derived text immediately");query.Text=".*";await Idle();Require(preview.IsVisible,"Preview returns for current match");
            Field<DispatcherTimer>(main,"timer").Stop();Require(await session.PrepareAttachmentsAsync(),"Attachment-name highlight fixture root");session.AttachBytes(note,new byte[]{1,2,3},"source-match-old.txt","application/octet-stream",note.EditVersion);Require(await session.SaveAsync(),"Exact attachment baseline saved");
            var original=session.Workspace.Capture();var replaced=original with{AttachmentObjects=original.AttachmentObjects.Select(item=>item with{Name="source-match-new.txt"}).ToImmutableArray()};
            Control<ComboBox>(main,"SearchFieldFilter").SelectedIndex=3;query.Text="source-match";await Idle();Require(preview.IsVisible&&All().Contains("old.txt"),"Filename match reads metadata only");version=note.EditVersion;
            session.Workspace.AcceptPrepared(replaced);await Idle();Invoke(main,"SearchPreviewRendering",null!,EventArgs.Empty);Require(!All().Contains("old.txt")&&note.EditVersion==version,"Unchanged-version attachment replacement discards old filename before next render");session.Workspace.AcceptPrepared(original);Invoke(main,"RenderSearchResultPreview");
            bool fired=false;var descriptor=DependencyPropertyDescriptor.FromProperty(System.Windows.Documents.Run.TextProperty,typeof(Run));EventHandler replacement=(_,_)=>{if(!fired&&Match().Text.Length>0){fired=true;session.Workspace.AcceptPrepared(replaced);}};descriptor.AddValueChanged(Match(),replacement);
            try{Invoke(main,"RenderSearchResultPreview");Require(fired&&!preview.IsVisible&&All()=="","Replacement during native matched Run setter cannot publish stale filename");}finally{descriptor.RemoveValueChanged(Match(),replacement);session.Workspace.AcceptPrepared(original);}
            Control<ComboBox>(main,"SearchFieldFilter").SelectedIndex=0;query.Text=".*";await Idle();Require(preview.IsVisible,"Current body excerpt after original attachment restoration");
            bool lockFired=false;Task? pending=null;EventHandler nativeLock=(_,_)=>{if(!lockFired&&Match().Text.Length>0){lockFired=true;pending=session.LockAsync();}};descriptor.AddValueChanged(Match(),nativeLock);
            try{Invoke(main,"RenderSearchResultPreview");Require(lockFired&&All()==""&&!preview.IsVisible,"Lock inside matched Run native setter clears all text before save wait and refuses stale publication");}finally{descriptor.RemoveValueChanged(Match(),nativeLock);}await pending!;
        }
        finally{if(main is not null){var session=Field<SaveCoordinator?>(main,"session");if(session is not null&&!session.IsLocked)await session.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
