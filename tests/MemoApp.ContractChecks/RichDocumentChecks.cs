using MemoApp.Core.Documents;
using System.Text.Json;
internal static class RichDocumentChecks
{
    internal static void Run()
    {
        const string source="""
        {"nodes":[{"type":"paragraph","runs":[{"text":"합성","bold":true,"fontFamily":"Malgun Gothic","fontSize":16,"foreground":"#123456","background":"#ABCDEF","underline":true,"strike":false}]},
        {"type":"list","ordered":false,"items":[{"runs":[{"text":"first"}]},{"runs":[{"text":"second"}]}]},
        {"type":"checklist","items":[{"checked":false,"runs":[{"text":"todo"}]},{"checked":true,"runs":[{"text":"done"}]}]},
        {"type":"table","rows":[[{"runs":[{"text":"left"}]},{"runs":[{"text":"right"}]}],[{"runs":[{"text":"last"}]},{"runs":[{"text":"셀"}]}]]},
        {"type":"paragraph","runs":[]}]}
        """;
        var document=new StyledDocument(1,source);var info=RichDocumentCodec.Inspect(document);
        VaultChecks.Require(info.Supported && info.Text=="합성\nfirst\nsecond\n[ ] todo\n[x] done\nleft\tright\nlast\t셀\n","known document has deterministic paragraph/list/checklist/table/empty-final projection");
        var plain=RichDocumentCodec.FromPlain("가👩‍💻e\u0301\r\n끝\n");VaultChecks.Require(RichDocumentCodec.Inspect(plain).Text=="가👩‍💻e\u0301\n끝\n","explicit plain conversion preserves text/empty final paragraph with declared newline normalization");
        const string opaque="  { \"nodes\" : [ {\"type\":\"future-node\", \"n\":1e+03, \"text\":\"\\uAC00\", \"future\":{\"keep\":true}} ] }  ";
        var future=new StyledDocument(1,opaque);var unknown=RichDocumentCodec.Inspect(future);
        VaultChecks.Require(!unknown.Supported && unknown.Text is null && JsonSerializer.Deserialize<StyledDocument>(JsonSerializer.Serialize(future))!.SourceJson==opaque,"opaque source whitespace/escapes/order/numeric lexeme retained, never partial text recomputation");
        var extra=new StyledDocument(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"x\",\"futureMark\":{\"raw\":1}}]}]}");VaultChecks.Require(!RichDocumentCodec.Inspect(extra).Supported,"unknown run attribute makes entire document readonly");
        var remoteFont=new StyledDocument(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"x\",\"fontFamily\":\"https://example.invalid/font/#x\"}]}]}");VaultChecks.Require(!RichDocumentCodec.Inspect(remoteFont).Supported,"URI font is preserved but not projected/editable");
        foreach(var invalid in new[]{new StyledDocument(3,source),new StyledDocument(1,"{}"),new StyledDocument(1,"{\"nodes\":null}"),new StyledDocument(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"x\",\"bold\":\"true\"}]}]}"),new StyledDocument(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"x\",\"fontSize\":999}]}]}"),new StyledDocument(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"x\",\"foreground\":\"url(file:///private)\"}]}]}"),new StyledDocument(1,"{\"nodes\":[],\"nodes\":[]}")})
            VaultChecks.ExpectFailure(()=>RichDocumentCodec.Inspect(invalid),"invalid schema/required/type/range/color/duplicate source reject");
        VaultChecks.ExpectFailure(()=>RichDocumentCodec.FromPlain("bad\uD800"),"invalid surrogate rejects before JSON replacement");
        VaultChecks.ExpectFailure(()=>RichDocumentCodec.Inspect(new(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"\\uD800\"}]}]}")),"escaped unpaired surrogate source reject");
        VaultChecks.ExpectFailure(()=>RichDocumentCodec.FromPlain(new string('x',65537)),"rich text bound reject");
        var tooMany="{\"nodes\":["+string.Join(',',Enumerable.Repeat("{\"type\":\"paragraph\",\"runs\":[]}",1025))+ "]}";VaultChecks.ExpectFailure(()=>RichDocumentCodec.Inspect(new(1,tooMany)),"document node count bound reject");
        VaultChecks.ExpectFailure(()=>RichDocumentCodec.Inspect(new(1,"{\"nodes\":[],\"future\":\""+new string('가',400000)+"\"}")),"UTF8 byte budget, not UTF16 count, protects opaque source");
        Console.WriteLine("PASS: canonical known rich projection, declared line rules, opaque exact source/read-only, strict Unicode/types/bounds and URI/active-value refusal");
    }
}
