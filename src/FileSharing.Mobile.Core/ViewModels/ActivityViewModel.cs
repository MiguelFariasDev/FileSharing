using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileSharing.Mobile.Core.Services.Activity;

namespace FileSharing.Mobile.Core.ViewModels;

/// <summary>
/// Backs the "Atividade" tab — a thin read-only view over ActivityFeedService's singleton feed
/// (see its own remarks for why this is session-only, not a full historical log).
/// </summary>
public partial class ActivityViewModel : ObservableObject
{
    private readonly ActivityFeedService _feed;

    public ObservableCollection<ActivityEntry> Entries => _feed.Entries;

    public bool IsEmpty => Entries.Count == 0;

    public ActivityViewModel(ActivityFeedService feed)
    {
        _feed = feed;
        _feed.Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }
}
