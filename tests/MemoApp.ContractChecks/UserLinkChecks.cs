using System.Reflection;
namespace MemoApp.Core;
internal static class UserLinkChecks
{
 internal static void Run()
 {
  var type=typeof(MemoApp.Core.Storage.VaultSnapshot).Assembly.GetType("MemoApp.Core.Transfer.UserLinkTarget");VaultChecks.Require(type is not null,"Explicit inert HTTP link target boundary missing");
  var parse=type!.GetMethod("Parse",BindingFlags.Static|BindingFlags.Public)!;
  string Good(string value)=>(string)parse.Invoke(null,[value])!;
  void Bad(string value){bool refused=false;try{Good(value);}catch(TargetInvocationException e) when(e.InnerException is ArgumentException){refused=true;}VaultChecks.Require(refused,"Unsafe target refusal");}
  VaultChecks.Require(Good("https://example.invalid/path?q=%22test%22#fragment")=="https://example.invalid/path?q=%22test%22#fragment","Absolute explicit target preserved");
  foreach(string value in new[]{"file:///C:/secret","javascript:alert(1)","mailto:test@example.invalid","https://user:password@example.invalid/","//example.invalid","https://example.invalid/\\hidden"," https://example.invalid/","https://example.invalid/\n","https://example.invalid/\0","https://example.invalid/"+new string('x',2048),"https://example.invalid/\ud800"})Bad(value);
  Console.WriteLine("PASS: explicit HTTP link target normalization and file/script/credentials/control/backslash/Unicode/length refusal");
 }
}
