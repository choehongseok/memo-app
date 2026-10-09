using System.Security.Cryptography;
using System.Text;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
internal static class PdfTextExportChecks
{
    internal static void Run()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Title="합성 PDF 제목";note.Text="한글 원문 漢字 ABC (literal) /JavaScript /OpenAction\r\n두 번째 줄\t표시\n끝";workspace.AcceptPrepared(workspace.Capture());byte[] original=SnapshotSerialization.Bytes(workspace.Capture());
        byte[] owned;using(var prepared=PdfTextTransfer.Capture([note])){owned=prepared.Bytes;VaultChecks.Require(owned.AsSpan().StartsWith("%PDF-1.7"u8)&&owned.Length<16*1024*1024&&Encoding.ASCII.GetString(owned[^200..]).Contains("%%EOF"),"Actual bounded PDF result");string syntax=Encoding.ASCII.GetString(owned);VaultChecks.Require(syntax.Contains("/FontFile2")&&syntax.Contains("/CIDToGIDMap")&&syntax.Contains("/ToUnicode")&&!syntax.Contains("/JavaScript")&&!syntax.Contains("/OpenAction"),"Embedded-font Unicode PDF has no active injected source operators");}VaultChecks.Require(owned.All(b=>b==0),"Owned PDF bytes are zeroed after export disposal");VaultChecks.Require(original.SequenceEqual(SnapshotSerialization.Bytes(workspace.Capture())),"PDF source/history/metadata exact invariance");
        foreach(string unsupported in new[]{"😀","e\u0301","\u1100\u1161","אבג","abc\u200ddef","abc\ufe0f","abc\u202e","bad\uD800","\0"}){note.Text=unsupported;VaultChecks.ExpectFailure(()=>PdfTextTransfer.Capture([note]),"Unsupported glyph/shaping/invalid source refused before destination creation");VaultChecks.Require(note.Text==unsupported,"Unsupported original never normalized or replaced");}
        note.Text=new string('\n',PdfTextTransfer.MaxPages*48);VaultChecks.ExpectFailure(()=>PdfTextTransfer.Capture([note]),"Page count construction limit without allocating unbounded document");note.Text="literal raw **Markdown** [[link]] <script>";workspace.ConvertMode(note,"markdown",true);using(var prepared=PdfTextTransfer.Capture([note]))VaultChecks.Require(note.Mode=="markdown"&&note.Text.Contains("<script>"),"Raw Markdown PDF projection never executes or modifies source");
        VaultChecks.ExpectFailure(()=>PdfTextTransfer.Capture([note,note]),"Duplicate note identity refused");VaultChecks.ExpectFailure(()=>PdfTextTransfer.Capture([]),"Empty selection refused");using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool canceled=false;try{PdfTextTransfer.Capture([note],cancel.Token);}catch(OperationCanceledException){canceled=true;}VaultChecks.Require(canceled,"Canceled PDF construction refused");}workspace.DeleteNote(note);VaultChecks.ExpectFailure(()=>PdfTextTransfer.Capture([note]),"Trash PDF refused");workspace.Clear();
        var now=DateTimeOffset.UnixEpoch;var opaque=new VaultSnapshot(4,Guid.NewGuid(),[new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"unknown","UNVERIFIED","rich"){Document=new(1,"{\"nodes\":[{\"type\":\"future-node\",\"opaque\":true}]}")}]);var unknown=new EditingWorkspace(TimeProvider.System,opaque);VaultChecks.ExpectFailure(()=>PdfTextTransfer.Capture(unknown.Notes),"Opaque unknown rich source cannot be silently exported");unknown.Clear();
        Console.WriteLine("PASS: real bounded embedded-font Unicode PDF, literal inert source, owned byte zero, exact original/history, unsupported shaping/glyph/malformed refusal, page/selection/cancellation/opaque/trash gates (independent display probe separate)");
    }
    internal static void WriteProbe(string destination)
    {
        var workspace=new EditingWorkspace(TimeProvider.System);try{var first=workspace.CreateNote();first.Title="합성 PDF 제목";first.Text="한글 원문 漢字 ABC (literal) /JavaScript /OpenAction\r\n두 번째 줄\t표시\n끝";var second=workspace.CreateNote();second.Title="합성 두 번째 메모";second.Text=string.Join("\n",Enumerable.Range(1,60).Select(i=>"합성 줄 "+i));using var prepared=PdfTextTransfer.Capture([first,second]);TextTransfer.WritePrepared(prepared,destination);}finally{workspace.Clear();}
    }
}
