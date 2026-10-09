using System.IO;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task ExcelImportRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-excel-import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;var fixture=new EditingWorkspace(TimeProvider.System);
        try
        {
            var a=fixture.CreateNote();a.Title="첫 합성";a.Text="한글 😀 =SUM(A1)";var b=fixture.CreateNote();b.Title="둘째 합성";b.Text="body";string input=Path.Combine(root,"input.xlsx");using(var prepared=OfficeTextTransfer.Capture([a,b],OfficeTextFormat.Spreadsheet))TextTransfer.WritePrepared(prepared,input);byte[] original=File.ReadAllBytes(input);
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Require(main.FindName("ExcelImportButton") is Button,"Explicit Excel text import command is missing");Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var method=typeof(MainWindow).GetMethod("ImportExcelAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var closures=typeof(MainWindow).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Where(t=>t.GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).Any(m=>m.Name.StartsWith("<ReadExcelAsync>",StringComparison.Ordinal))).ToArray();Require(closures.Length==1&&closures[0].GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).All(f=>f.FieldType==typeof(string)||f.FieldType==typeof(CancellationToken)),"Compiled Excel reader receives only path/token without UI/workspace/key owner");
            Task<bool> Import(Func<string?> choose,Func<int,bool> confirm)=>(Task<bool>)method.Invoke(main,[choose,confirm])!;
            Require(!await Import(()=>null,_=>throw new Exception("No confirmation without file"))&&active.Workspace.Notes.Count==0,"Cancelled picker adds nothing");
            Require(!await Import(()=>input,_=>false)&&active.Workspace.Notes.Count==0,"Cancelled conversion adds nothing");
            Require(await Import(()=>input,count=>count==2)&&active.Workspace.Notes.Count==2&&active.Workspace.Notes.All(n=>n.Mode=="plain")&&active.Workspace.Notes.Any(n=>n.Text==a.Text),"Accepted first-sheet literal texts import entire plain batch");
            Require(File.ReadAllBytes(input).SequenceEqual(original),"Original workbook remains exact");await Field<Task>(main,"recentTask");byte[] committed=File.ReadAllBytes(Path.Combine(root,"vault","current.vault"));
            Require(!await Import(()=>input,_=>{active.Workspace.Notes[0].Text="newer source";return true;})&&active.Workspace.Notes.Count==2&&File.ReadAllBytes(Path.Combine(root,"vault","current.vault")).SequenceEqual(committed),"Confirmation edit revokes old import authority without adding rows or committing dirty edit");
            Require(await active.SaveAsync(),"Edited import baseline saved");await Field<Task>(main,"recentTask");Action throwing=()=>throw new IOException("Synthetic collection observer fault");active.Workspace.Changed+=throwing;
            try{Require(!await Import(()=>input,_=>true)&&active.Workspace.Notes.Count==4&&Control<TextBlock>(main,"Notice").Text.Contains("추가 이후",StringComparison.Ordinal),"Throwing post-application observer preserves whole batch and reports applied state truthfully");}finally{active.Workspace.Changed-=throwing;}
            var locking=Task.CompletedTask;Require(!await Import(()=>input,_=>{locking=active.LockAsync();return true;}),"Confirmation lock discards parsed result");await locking;Require(!Control<Button>(main,"ExcelImportButton").IsEnabled,"Locked import disabled");
        }
        finally{fixture.Clear();if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}
