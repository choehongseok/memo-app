using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using MemoApp.Core.Transfer;

internal static class SpreadsheetScalarChecks
{
    private static readonly XNamespace Main="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static string Read(XElement cell)
    {
        var type=typeof(TextTransfer).Assembly.GetType("MemoApp.Core.Transfer.SpreadsheetScalarText");
        VaultChecks.Require(type is not null,"Bounded lossless spreadsheet scalar conversion is missing");
        var method=type!.GetMethod("Read",BindingFlags.Public|BindingFlags.Static,null,[typeof(XElement)],null);
        VaultChecks.Require(method is not null,"Spreadsheet scalar conversion Read(XElement) is missing");
        try{return (string)method!.Invoke(null,[cell])!;}
        catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static XElement Cell(string value,string? type=null)=>new(Main+"c",new XAttribute("r","B2"),type is null?null:new XAttribute("t",type),new XElement(Main+"v",value));
    private static void Refuse(XElement cell,string reason)
    {
        bool refused=false;try{Read(cell);}catch(InvalidDataException){refused=true;}
        VaultChecks.Require(refused,reason);
    }
    internal static void Run()
    {
        // Parsing through double/decimal or applying a date style would make these assertions fail.
        var culture=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
            foreach(string value in new[]{"0","-0","+0","001.2300","9007199254740993","123456789012345678901234567890.123456789",".5","1.","-2.50e+03","1E-9999","1e9999"})
                foreach(string? type in new string?[]{null,"n"})
                {
                    var cell=Cell(value,type);string before=cell.ToString(SaveOptions.DisableFormatting);
                    VaultChecks.Require(Read(cell)==value&&cell.ToString(SaveOptions.DisableFormatting)==before,"Numeric lexical value survives without rounding, locale conversion, or source mutation");
                }
            foreach(string serial in new[]{"0","59","60","61","45292.5000"})
            {
                var cell=Cell(serial,"n");cell.SetAttributeValue("s","1");
                VaultChecks.Require(Read(cell)==serial,"Style-indexed date serial stays exact text without date-system or display-format guesses");
            }
            VaultChecks.Require(Read(Cell("0","b"))=="FALSE"&&Read(Cell("1","b"))=="TRUE","Boolean scalar maps exactly to locale-independent TRUE/FALSE");
            string boundary=new('1',32767);
            VaultChecks.Require(Read(Cell(boundary))==boundary,"Numeric lexical length at existing cell bound is preserved");
            Refuse(Cell(boundary+"1"),"Oversized numeric lexical scalar is refused");
        }
        finally{CultureInfo.CurrentCulture=culture;}
        foreach(string value in new[]{""," "," 1","1 ","1\n","1,5","NaN","INF","-Infinity","0x10","1_000","١","+","-",".","1e","1e+","1e-","1.2.3","--1","1e2e3","=1+1","1\0"})
            Refuse(Cell(value),"Invalid, nonfinite, non-ASCII or active numeric lexical scalar is refused");
        foreach(string value in new[]{"","true","false","TRUE","FALSE","01","-0","2","1.0"," 1","1 "})Refuse(Cell(value,"b"),"Boolean accepts only exact 0 or 1");
        foreach(string type in new[]{"s","inlineStr","str","e","d","unknown"})Refuse(Cell("1",type),"String, date, error and unknown cell types are outside scalar conversion");
        var formula=Cell("2","n");formula.AddFirst(new XElement(Main+"f","1+1"));Refuse(formula,"Formula cached value is refused");
        var duplicate=Cell("1");duplicate.Add(new XElement(Main+"v","2"));Refuse(duplicate,"Duplicate scalar values are refused");
        var nested=Cell("1");nested.Element(Main+"v")!.Add(new XElement(Main+"t","2"));Refuse(nested,"Nested scalar value markup is refused");
        var attributed=Cell("1");attributed.Element(Main+"v")!.SetAttributeValue("extra","x");Refuse(attributed,"Unsupported scalar value attributes are refused");
        var extra=Cell("1");extra.Add(new XElement(Main+"is",new XElement(Main+"t","2")));Refuse(extra,"Mixed string and scalar children are refused");
        var direct=Cell("1");direct.AddFirst(new XText("extra"));Refuse(direct,"Unaccounted direct scalar cell text is refused");
        var foreign=Cell("1");foreign.Element(Main+"v")!.Name="v";Refuse(foreign,"Foreign scalar value namespace is refused");
        Refuse(new XElement("c",new XElement(Main+"v","1")),"Foreign scalar cell namespace is refused");
        Refuse(new XElement(Main+"c",new XAttribute("t","n")),"Scalar helper requires one nonempty scalar value");
        var whitespace=Cell("1");whitespace.AddFirst(new XText("\n  "));whitespace.Add(new XText("\n"));
        VaultChecks.Require(Read(whitespace)=="1","Pretty-printed scalar cell whitespace is accepted without altering value");
        Console.WriteLine("PASS: bounded lossless numeric lexical/boolean scalar, locale/source preservation, raw styled date serial and formula/unsupported/ambiguous markup refusal");
    }
}
