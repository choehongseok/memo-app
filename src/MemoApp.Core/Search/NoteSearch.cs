using System.Text;
using MemoApp.Core.Editing;
namespace MemoApp.Core.Search;
public enum SearchField { All, Title, Body }
public enum SearchView { Active, Archive, Trash, All }
public enum SearchSort { Modified, Created, Name, Custom }
public sealed record SearchOptions
{
    public string Query { get; init; } = "";
    public SearchField Field { get; init; }
    public SearchView View { get; init; }
    public SearchSort Sort { get; init; }
    public Guid? FolderId { get; init; }
    public bool UnfiledOnly { get; init; }
    public bool IncludeDescendants { get; init; } = true;
    public string? Tag { get; init; }
    public bool FavoriteOnly { get; init; }
    public bool ImportantOnly { get; init; }
    public DateTimeOffset? ModifiedFrom { get; init; }
    public DateTimeOffset? ModifiedUntil { get; init; }
}
public static class NoteSearch
{
    public static NoteDraft[] Find(EditingWorkspace workspace, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(workspace); ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.View) || !Enum.IsDefined(options.Sort)) throw new ArgumentException("Unsupported search option");
        var found = Find(workspace.Notes, options.Query, options.Field).AsEnumerable();
        found = found.Where(n => options.View switch
        {
            SearchView.Active => !n.IsDeleted && !n.Archived,
            SearchView.Archive => !n.IsDeleted && n.Archived,
            SearchView.Trash => n.IsDeleted,
            _ => true
        });
        if (options.FolderId is Guid folder)
        {
            var folders = options.IncludeDescendants ? workspace.DescendantFolders(folder) : [folder];
            found = found.Where(n => n.FolderId is Guid id && folders.Contains(id));
        }
        if (options.UnfiledOnly) found = found.Where(n => n.FolderId is null);
        if (!string.IsNullOrWhiteSpace(options.Tag))
        {
            var tagIds = workspace.Tags.Where(t => string.Equals(Normalize(t.Name), Normalize(options.Tag.Trim()), StringComparison.OrdinalIgnoreCase)).Select(t => t.TagId).ToArray();
            found = found.Where(n => n.Metadata.TagIds.Any(tagIds.Contains));
        }
        if (options.FavoriteOnly) found = found.Where(n => n.Favorite);
        if (options.ImportantOnly) found = found.Where(n => n.Important);
        if (options.ModifiedFrom is DateTimeOffset from) found = found.Where(n => n.ModifiedAt >= from);
        if (options.ModifiedUntil is DateTimeOffset until) found = found.Where(n => n.ModifiedAt < until);
        var ordered = found.OrderByDescending(n => n.Pinned);
        return (options.Sort switch
        {
            SearchSort.Name => ordered.ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase),
            SearchSort.Created => ordered.ThenByDescending(n => n.CreatedAt),
            SearchSort.Custom => ordered.ThenBy(n => n.Metadata.Order),
            _ => ordered.ThenByDescending(n => n.ModifiedAt)
        }).ThenBy(n => n.Id).ToArray();
    }
    public static NoteDraft[] Find(IEnumerable<NoteDraft> notes, string query, SearchField field = SearchField.All)
    {
        ArgumentNullException.ThrowIfNull(notes); ArgumentNullException.ThrowIfNull(query);
        if (!Enum.IsDefined(field)) throw new ArgumentOutOfRangeException(nameof(field));
        string needle = Normalize(query);
        bool Contains(string text) => Normalize(text).Contains(needle, StringComparison.OrdinalIgnoreCase);
        return notes.Where(n => !n.IsClosed && (field != SearchField.Body && Contains(n.Title) || field != SearchField.Title && Contains(n.Text))).ToArray();
    }
    private static string Normalize(string value)
    {
        // WPF may briefly expose an unpaired UTF-16 surrogate during editing. Literal fallback is safe.
        try { return value.Normalize(NormalizationForm.FormC); } catch (ArgumentException) { return value; }
    }
}
