using MemoApp.Core.Documents;
namespace MemoApp.Core.Transfer;
// Explicit browser-open boundary. No network, shell command or document mutation here.
public static class UserLinkTarget
{
    public static string Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.Length is <1 or >2048||!RichDocumentCodec.IsWellFormedUnicode(value)||value.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)||c=='\\')||!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.Host.Length==0||uri.UserInfo.Length!=0)throw new ArgumentException("Only an explicit bounded credential-free HTTP target is allowed");
        return uri.AbsoluteUri;
    }
}
