using System.Collections.Immutable;
using MemoApp.Core.History;
using MemoApp.Core.Storage;
internal static class HistoryDiffChecks
{
    internal static void Run()
    {
        var diff=BoundedHistoryDiff.Compare("첫줄\n합성 이전\n끝","첫줄\n합성 이후\n끝");
        VaultChecks.Require(diff.Complete && diff.Rendered.Contains("- 합성 이전") && diff.Rendered.Contains("+ 합성 이후") && diff.Rendered.Contains("  첫줄"),"history diff must show selected line removal/addition and shared context");
        foreach(string text in new[]{"","합성","\n","합성\n","\r\n\r\n","한글👩‍💻e\u0301"})
        {var same=BoundedHistoryDiff.Compare(text,text);VaultChecks.Require(same.Complete && same.Message.Contains("동일") && !same.Rendered.StartsWith("- ",StringComparison.Ordinal),"same/empty/trailing/Unicode diff must preserve identical state");}
        var unicode=BoundedHistoryDiff.Compare("e\u0301한글👩‍💻","é한글👩‍💻");VaultChecks.Require(unicode.Complete && unicode.Rendered.Contains("- e\u0301한글👩‍💻") && unicode.Rendered.Contains("+ é한글👩‍💻"),"diff must not normalize away actual Unicode edits");
        var endings=BoundedHistoryDiff.Compare("a\r\n","a\n");VaultChecks.Require(endings.Complete && endings.Rendered.Contains("[CRLF]") && endings.Rendered.Contains("[LF]") && endings.Rendered.Contains("- a") && endings.Rendered.Contains("+ a"),"diff must expose CRLF/LF difference and preserve final empty line");
        var finalNewline=BoundedHistoryDiff.Compare("a","a\n");VaultChecks.Require(finalNewline.Complete && finalNewline.Rendered.Contains("+ a"),"ending newline must not disappear in line diff");
        foreach(var pair in new[]{(new string('\n',4096),""),(string.Join('\n',Enumerable.Repeat("a",1000)),string.Join('\n',Enumerable.Repeat("b",1000))),(new string('x',65536),new string('y',65536)),(new string('x',65537),"")})
        {var over=BoundedHistoryDiff.Compare(pair.Item1,pair.Item2);VaultChecks.Require(!over.Complete && over.Rendered=="" && over.Message.Contains("한도") && over.Message.Contains("원문"),"line/cell/output/input budget must explicitly refuse incomplete diff and retain source ownership");}
        var metadata=new NoteMetadata();var changed=metadata with{FolderId=Guid.NewGuid(),TagIds=ImmutableArray.Create(Guid.NewGuid()),Color="blue",Important=true,Favorite=true,Pinned=true,Archived=true,Deleted=true,Order=9};
        var names=BoundedHistoryDiff.MetadataChanges(metadata,changed);foreach(string name in new[]{"폴더","태그","색","중요","즐겨찾기","목록고정","보관","삭제","순서"})VaultChecks.Require(names.Contains(name),"history metadata difference includes "+name);
        VaultChecks.Require(BoundedHistoryDiff.MetadataChanges(metadata,metadata).Contains("동일"),"same metadata must be identified");
        Console.WriteLine("PASS: bounded exact line history comparison, empty/trailing/CRLF/Unicode changes, explicit budget fallback and all metadata differences");
    }
}
