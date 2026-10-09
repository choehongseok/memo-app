using System.Collections.Immutable;
using MemoApp.Core.Search;
namespace MemoApp.Core.Storage;
public sealed record UiPreferences(bool DarkMode=false,double FontSize=14,double Scale=1);
public sealed record StoredWindowLayout(string Kind,Guid? NoteId,string Monitor,double X,double Y,double Width,double Height,double Dpi,
    bool Open=true,bool Topmost=false,double Opacity=1,bool Folded=false,bool PositionLocked=false);
public sealed record StoredSavedSearch(Guid Id,string Name,SearchOptions Options);
public sealed record StoredDeviceUi(Guid UiDeviceId,UiPreferences Preferences,ImmutableArray<StoredWindowLayout> Windows)
{
    public StoredAutomaticBackupPolicy? AutomaticBackupPolicy{get;init;}
    public ImmutableArray<Guid> RecentNoteIds{get;init;}=[];
    public ImmutableArray<StoredSavedSearch> SavedSearches{get;init;}=[];
}
