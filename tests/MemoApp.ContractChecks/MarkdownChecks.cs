using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using System.Text.Json;
internal static class MarkdownChecks
{
    internal static void Run()
    {
        const string raw="# 합성 제목\r\n\r\n**굵게** *강조* `코드` 👩‍💻é\n\n- 하나\n- 둘\n\n```xaml\n<Window>그대로</Window>\n```\n\n<script>alert(1)</script>\n\n![그림](https://example.invalid/private-image) [링크](file:///private) <https://example.invalid/inert>";
        var preview=SafeMarkdown.Preview(raw);VaultChecks.Require(preview.Complete&&preview.Blocks.Length>0,"supported bounded Markdown preview");
        string text=string.Join("\n",preview.Blocks.Select(b=>string.Concat(b.Spans.Select(s=>s.Text))));
        VaultChecks.Require(text.Contains("합성 제목")&&text.Contains("👩‍💻é")&&text.Contains("<Window>그대로</Window>")&&text.Contains("<script>alert(1)</script>")&&text.Contains("그림")&&text.Contains("링크"),"HTML/code/image/link remain inert text labels and Unicode preserved");
        VaultChecks.Require(preview.Blocks.Any(b=>b.Kind=="heading")&&preview.Blocks.Any(b=>b.Spans.Any(s=>s.Strong))&&preview.Blocks.Any(b=>b.Spans.Any(s=>s.Italic))&&preview.Blocks.Any(b=>b.Spans.Any(s=>s.Code)),"safe AST heading/strong/emphasis/code projection");
        VaultChecks.Require(!SafeMarkdown.Preview(new string('x',65537)).Complete&&!SafeMarkdown.Preview("invalid\uD800").Complete,"preparse text/Unicode bounds");
        VaultChecks.Require(!SafeMarkdown.Preview(new string('[',33)+"x"+new string(']',33)).Complete&&!SafeMarkdown.Preview(new string('*',8193)).Complete,"preparse delimiter/nesting bounds before parser");
        VaultChecks.Require(!SafeMarkdown.Preview(new string('a',65531)+"\n\n---").Complete,"expanded divider output cannot bypass output budget");
        VaultChecks.Require(!SafeMarkdown.Preview(string.Concat(Enumerable.Repeat("[a)",33))).Complete,"mismatched closing delimiter cannot hide preparse nesting");
        VaultChecks.Require(!(bool)typeof(SafeMarkdown).GetMethod("Preflight",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[string.Concat(Enumerable.Repeat("[a)",33))])!,"preparse itself must reject hidden mismatched nesting before parser call");
        VaultChecks.Require(SafeMarkdown.Preview("").Complete&&SafeMarkdown.Preview("합성\n\n").Complete,"empty/trailing Markdown source safe");
        VaultChecks.Require(SafeMarkdown.Preview(string.Concat(Enumerable.Repeat("x\n\n",1024))).Complete&&!SafeMarkdown.Preview(string.Concat(Enumerable.Repeat("x\n\n",1025))).Complete,"actual AST block budget exact1024/1025 boundary");
        VaultChecks.Require(SafeMarkdown.Preview(string.Concat(Enumerable.Repeat("x  \n",4094))).Complete&&!SafeMarkdown.Preview(string.Concat(Enumerable.Repeat("x  \n",4100))).Complete,"actual AST sibling-node budget before scheduled stack growth");
        VaultChecks.Require(SafeMarkdown.Preview(new string('>',30)+" **x**").Complete&&!SafeMarkdown.Preview(new string('>',31)+" **x**").Complete,"actual combined block/inline depth32/33 boundary");
        var numbered=SafeMarkdown.Preview("3. first\n4. second\n   - nested\n\nparagraph\n\n1. restarted");var markers=numbered.Blocks.Where(b=>b.Kind=="list-item").Select(b=>(string?)typeof(MarkdownBlock).GetProperty("ListMarker")?.GetValue(b)).ToArray();VaultChecks.Require(markers.SequenceEqual(new[]{"3. ","4. ","• ","1. "}),"Ordered Markdown starts, increments, nested unordered and new list restart must survive preview");
        var loose=SafeMarkdown.Preview("7) first\n\n   continuation\n\n8) second\n\n   2. nested one\n   3. nested two");VaultChecks.Require(loose.Complete&&loose.Blocks.Select(b=>b.ListMarker).SequenceEqual(new string?[]{"7) ",null,"8) ","2. ","3. "}),"Ordered closing parenthesis, loose continuation and nested starts retain exactly one marker per item");
        var special=SafeMarkdown.Preview("3. # heading\n4. ```\n   code\n   ```\n5. next");VaultChecks.Require(special.Complete&&special.Blocks.Select(b=>b.ListMarker).SequenceEqual(new[]{"3. ","4. ","5. "})&&special.Blocks[0].Kind=="heading"&&special.Blocks[1].Kind=="code","List numbering also belongs to first heading/code block, independently of rendering kind");
        VaultChecks.Require(!SafeMarkdown.Preview("1. "+new string('a',65530)+"\n\n---").Complete,"List markers join expanded divider output in the same bounded output budget");
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();workspace.ConvertMode(note,"markdown",true);note.Text=raw;workspace.AcceptPrepared(workspace.Capture());long version=note.EditVersion;string before=JsonSerializer.Serialize(workspace.Capture());_ = SafeMarkdown.Preview(note.Text);
        VaultChecks.Require(note.Text==raw&&note.EditVersion==version&&JsonSerializer.Serialize(workspace.Capture())==before,"preview cannot write source/mode/history/timestamps/version");workspace.Clear();
        Console.WriteLine("PASS: actual bounded Markdown parser, inert HTML/code/links/images/Unicode projection, raw source invariance and preparse limits");
    }
}
