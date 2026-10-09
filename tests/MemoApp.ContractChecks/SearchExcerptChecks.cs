using System.Reflection;
using MemoApp.Core.Search;
internal static class SearchExcerptChecks
{
    public static void Run()
    {
        var type=typeof(NoteSearch).Assembly.GetType("MemoApp.Core.Search.SearchExcerpt");Require(type is not null,"Bounded search match excerpt is missing");
        var method=type!.GetMethod("Project",BindingFlags.Public|BindingFlags.Static)!;
        object Project(string text,string query,int budget=512)=>method.Invoke(null,[text,query,budget])!;
        string Text(object result,string name)=>(string)type.GetProperty(name)!.GetValue(result)!;
        bool Flag(object result,string name)=>(bool)type.GetProperty(name)!.GetValue(result)!;
        object excerpt=Project("첫 문장. 합성 SEARCH 문장. 끝 문장.","search");Require(Text(excerpt,"Match")=="SEARCH"&&Text(excerpt,"Before")=="첫 문장. 합성 "&&Text(excerpt,"After")==" 문장. 끝 문장.","Literal insensitive match splits actual surrounding sentence");
        string source=new string('a',60000)+" 끝의 한글 일치 문장 😀 끝";excerpt=Project(source,"한글 일치");Require(Text(excerpt,"Match")=="한글 일치"&&Text(excerpt,"Text").Length<=512&&Flag(excerpt,"PrefixOmitted")&&Text(excerpt,"After").Contains("😀"),"Far-tail hit brings bounded context into preview rather than leading nonmatch text");Require(source.Length>60000,"Source stays unchanged");
        excerpt=Project("Cafe\u0301와 \u1100\u1161 문장","CAFÉ");Require(Text(excerpt,"Match")=="Café","Excerpt matches same NFC/case search contract without mutating source");
        excerpt=Project("x 👩‍💻 e\u0301 y","👩");Require(Text(excerpt,"Match")=="👩‍💻","Highlight expands to full grapheme rather than splitting emoji sequence");
        excerpt=Project("a"+new string('\u0301',1000),"á");Require(!Flag(excerpt,"HasMatch")&&Text(excerpt,"Text")=="","Oversized single grapheme refuses display instead of exceeding limit");
        foreach(string query in new[]{"","absent"})Require(!Flag(Project("synthetic",query),"HasMatch"),"Empty/absent literal has no match highlight");
        excerpt=Project("raw <script>.* [text] "+"\uD800",".*");Require(Text(excerpt,"Match")==".*"&&Text(excerpt,"Text").Contains("<script>"),"HTML/regex-looking input is literal and transient malformed UTF16 safely falls back");
        foreach((string text,string query,int budget) in new[]{(new string('x',65537),"x",512),("x",new string('x',257),512),("x","x",513)})
        {bool failed=false;try{Project(text,query,budget);}catch(TargetInvocationException e)when(e.GetBaseException() is ArgumentException){failed=true;}Require(failed,"Preprojection source/query/output budgets refuse oversized inputs");}
        Console.WriteLine("PASS: bounded literal/NFC search sentence context, far-tail hits, grapheme-safe highlights, giant-cluster refusal and source invariance; no UI/cache");
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
