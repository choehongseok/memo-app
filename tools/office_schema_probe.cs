using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
string root=Path.Combine(Path.GetTempPath(),"memo-office-independent-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Title="=SUM(1,2) _x0041_ 합성";note.Text=new string('a',32766)+"😀"+new string('b',32768);var second=workspace.CreateNote();second.Title="합성";second.Text="한글\t공백\r\n다음 줄";
try
{
 foreach(var format in new[]{OfficeTextFormat.Spreadsheet,OfficeTextFormat.Word})
 {
  string path=Path.Combine(root,format==OfficeTextFormat.Spreadsheet?"synthetic.xlsx":"synthetic.docx");using(var prepared=OfficeTextTransfer.Capture(workspace.Notes,format))TextTransfer.WritePrepared(prepared,path);
  using OpenXmlPackage package=format==OfficeTextFormat.Spreadsheet?SpreadsheetDocument.Open(path,false):WordprocessingDocument.Open(path,false);var errors=new OpenXmlValidator().Validate(package).Take(21).ToArray();if(errors.Length!=0){foreach(var error in errors.Take(20))Console.WriteLine("FAIL: "+format+" "+error.Description);return 1;}Console.WriteLine("PASS: independent OpenXML SDK 3.4.1 schema/relationship validation "+format);
 }
 return 0;
}
finally{workspace.Clear();Directory.Delete(root,true);}
