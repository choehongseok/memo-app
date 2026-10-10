using System.Globalization;
using System.Text;
namespace MemoApp.Core.Search;

// Detached, bounded display projection only; never replaces canonical note text.
public sealed record SearchExcerpt(string Before,string Match,string After,bool PrefixOmitted,bool SuffixOmitted)
{
    public bool HasMatch=>Match.Length>0;
    public string Text=>(PrefixOmitted?"…":"")+Before+Match+After+(SuffixOmitted?"…":"");
    private static readonly SearchExcerpt Empty=new("","","",false,false);
    public static SearchExcerpt Project(string source,string query,int maximumCharacters=512)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(query);
        if(source.Length>65536||query.Length>256||maximumCharacters is <64 or >512)throw new ArgumentException("Search excerpt budget exceeded");
        if(query.Length==0)return Empty;
        string text=LiteralSearch.Normalize(source),needle=LiteralSearch.Normalize(query);
        int index=text.IndexOf(needle,StringComparison.OrdinalIgnoreCase);if(index<0)return Empty;
        var starts=StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        int Floor(int value){int found=Array.BinarySearch(starts,value);return starts[found>=0?found:~found-1];}
        int Ceiling(int value){int found=Array.BinarySearch(starts,value);return starts[found>=0?found:~found];}
        int hitStart=Floor(index),hitEnd=Ceiling(index+needle.Length),budget=maximumCharacters-2;
        if(hitEnd-hitStart>budget)return Empty; // Never split a giant combining/emoji cluster.
        int start=Ceiling(Math.Max(0,hitStart-Math.Min(128,budget-(hitEnd-hitStart))));
        int end=Floor(Math.Min(text.Length,start+budget));
        return new(text[start..hitStart],text[hitStart..hitEnd],text[hitEnd..end],start>0,end<text.Length);
    }
}
internal static class LiteralSearch
{
    internal static string Normalize(string value)
    {
        try{return value.Normalize(NormalizationForm.FormC);}catch(ArgumentException){return value;}
    }
}
