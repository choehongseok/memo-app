using System.Xml.Linq;

namespace MemoApp.Core.Transfer;

/// <summary>
/// Converts a bounded, non-formula SpreadsheetML scalar to plain text.
/// Numeric values retain their exact XML lexical representation; styles and
/// workbook date systems are not interpreted. This does not reproduce Excel's display formatting.
/// </summary>
public static class SpreadsheetScalarText
{
    public const int MaximumValueLength=32767;
    private static readonly XNamespace Main="http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static string Read(XElement cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        string? type=(string?)cell.Attribute("t");
        if(cell.Name!=Main+"c"||type is not(null or "n" or "b"))throw new InvalidDataException("Unsupported spreadsheet scalar type");
        XElement? value=null;int nodes=0;
        foreach(var node in cell.Nodes())
        {
            if(++nodes>16)throw new InvalidDataException("Spreadsheet scalar node limit");
            if(node is XElement element&&element.Name==Main+"v"&&value is null)value=element;
            else if(node is XText text&&text.Value.Length<=MaximumValueLength&&text.Value.All(c=>c is ' ' or '\t' or '\r' or '\n'))continue;
            else throw new InvalidDataException("Unsupported spreadsheet scalar markup");
        }
        // Check the text node before constructing a concatenated XElement.Value.
        if(value is null||value.HasAttributes||value.FirstNode is not XText scalar||scalar.NextNode is not null||scalar.Value.Length is <1 or >MaximumValueLength)
            throw new InvalidDataException("Spreadsheet scalar value shape/limit");
        string lexical=scalar.Value;
        if(type=="b")return lexical switch{"0"=>"FALSE","1"=>"TRUE",_=>throw new InvalidDataException("Invalid spreadsheet boolean")};
        if(!NumericLexical(lexical))throw new InvalidDataException("Invalid spreadsheet numeric lexical value");
        return lexical;
    }

    private static bool NumericLexical(string value)
    {
        int position=0;
        if(value[position] is '+' or '-')position++;
        int digits=ConsumeDigits(value,ref position);
        if(position<value.Length&&value[position]=='.')
        {
            position++;
            digits+=ConsumeDigits(value,ref position);
        }
        if(digits==0)return false;
        if(position<value.Length&&value[position] is 'e' or 'E')
        {
            position++;
            if(position<value.Length&&value[position] is '+' or '-')position++;
            if(ConsumeDigits(value,ref position)==0)return false;
        }
        return position==value.Length;
    }

    private static int ConsumeDigits(string value,ref int position)
    {
        int start=position;
        while(position<value.Length&&value[position] is >= '0' and <= '9')position++;
        return position-start;
    }
}
