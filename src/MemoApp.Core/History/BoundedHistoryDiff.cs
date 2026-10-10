using MemoApp.Core.Storage;
using System.Text;
namespace MemoApp.Core.History;
public sealed record TextComparison(bool Complete,string Message,string Rendered);
public static class BoundedHistoryDiff
{
    private const int MaxText=65536,MaxLines=4096,MaxCells=1000000,MaxOutput=131072;
    private sealed record Line(string Text,string Ending);
    public static TextComparison Compare(string left,string right)
    {
        ArgumentNullException.ThrowIfNull(left);ArgumentNullException.ThrowIfNull(right);
        TextComparison Refuse(string budget)=>new(false,$"{budget} 비교 한도 초과 — 양쪽 원문은 그대로 유지합니다.","");
        if(left.Length>MaxText||right.Length>MaxText)return Refuse("입력");
        var a=Lines(left);var b=Lines(right);if(a is null||b is null)return Refuse("라인");
        bool identical=left==right;long cells=checked((long)(a.Length+1)*(b.Length+1));if(!identical&&cells>MaxCells)return Refuse("계산/메모리");
        var lcs=identical?null:new int[a.Length+1,b.Length+1];
        if(lcs is not null)for(int i=a.Length-1;i>=0;i--)for(int j=b.Length-1;j>=0;j--)lcs[i,j]=a[i]==b[j]?lcs[i+1,j+1]+1:Math.Max(lcs[i+1,j],lcs[i,j+1]);
        var output=new StringBuilder(Math.Min(left.Length+right.Length+32,1024));int x=0,y=0;
        bool Add(string prefix,Line line)
        {
            string ending=line.Ending switch{"\r\n"=>" [CRLF]","\n"=>" [LF]","\r"=>" [CR]",_=>" [END]"};
            if((long)output.Length+prefix.Length+line.Text.Length+ending.Length+1>MaxOutput)return false;
            output.Append(prefix).Append(line.Text).Append(ending).Append('\n');return true;
        }
        while(x<a.Length||y<b.Length)
        {
            bool fits;
            if(x<a.Length&&y<b.Length&&a[x]==b[y]){fits=Add("  ",a[x++]);y++;}
            else if(x<a.Length&&(y==b.Length||lcs![x+1,y]>=lcs[x,y+1]))fits=Add("- ",a[x++]);
            else fits=Add("+ ",b[y++]);
            if(!fits)return Refuse("출력");
        }
        return new(true,left==right?"본문 동일 (대소문자/Unicode/줄 끝 포함)":"- 이전 / + 이후 / 공백 문맥 · 줄 끝도 정확 비교",output.ToString());
    }
    private static Line[]? Lines(string text)
    {
        int count=1;for(int i=0;i<text.Length;i++)if(text[i] is '\r' or '\n'){if(++count>MaxLines)return null;if(text[i]=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;}
        var lines=new Line[count];int start=0,index=0;
        for(int i=0;i<text.Length;i++)if(text[i] is '\r' or '\n')
        {
            int size=text[i]=='\r'&&i+1<text.Length&&text[i+1]=='\n'?2:1;lines[index++]=new(text[start..i],text.Substring(i,size));i+=size-1;start=i+1;
        }
        lines[index]=new(text[start..],"");return lines;
    }
    public static string MetadataChanges(NoteMetadata left,NoteMetadata right)
    {
        ArgumentNullException.ThrowIfNull(left);ArgumentNullException.ThrowIfNull(right);var changed=new List<string>();
        if(left.FolderId!=right.FolderId)changed.Add("폴더");if(!left.TagIds.SequenceEqual(right.TagIds))changed.Add("태그");if(left.Color!=right.Color)changed.Add("색");
        if(left.Important!=right.Important)changed.Add("중요");if(left.Favorite!=right.Favorite)changed.Add("즐겨찾기");if(left.Pinned!=right.Pinned)changed.Add("목록고정");
        if(left.Archived!=right.Archived)changed.Add("보관");if(left.Deleted!=right.Deleted)changed.Add("삭제");if(left.Order!=right.Order)changed.Add("사용자 순서");
        return changed.Count==0?"분류/표시 metadata 동일":"변경된 metadata: "+string.Join(", ",changed);
    }
}
