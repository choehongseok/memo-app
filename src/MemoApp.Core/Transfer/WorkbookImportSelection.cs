namespace MemoApp.Core.Transfer;

public sealed record WorkbookSheetChoice(int Index,string Name);
public sealed record WorkbookImportCatalog(IReadOnlyList<WorkbookSheetChoice> Sheets,string SourceSha256);
public sealed record WorkbookImportSelection(int SheetIndex,int TitleColumn,int BodyColumn,bool SkipFirstRow,string ExpectedSourceSha256)
{
    public bool UseKnownTemplate { get; init; }
}
