using CrestCore.Contracts;

namespace CrestCore.Domain;

public sealed partial class BrowserTabCollection {
    #region Actions - Lifetime

    /// Archives a Quick Window's page as the closed open tab `closed`.
    /// Refused with `TabAlreadyExists` when the Space holds its identity.
    public void ArchiveTransient(TabState closed, DateTimeOffset now) {
        if (tabs.Any(t => t.Id == closed.Id) || archive.Any(archived => archived.Tab.Id == closed.Id))
            throw new Rejected(new TabAlreadyExists(closed.Id));
        archive.Add(new(closed, now, ArchiveReason.QuickWindow));
    }

    /// Reopens `archived` as an open tab, in its folder while that folder is
    /// still open, where `RestoredIndex` finds its place.
    public BrowserTab RestoreArchived(ArchivedTabState archived, DateTimeOffset now) {
        var folder = archived.Tab.FolderId is { } id && folders.Any(f => f.Id == id && f.Location == TabPlacement.Current) ? id : (Guid?)null;
        var tab = BrowserTab.Restore(archived.Tab with {
            Placement = TabPlacement.Current,
            FolderId = folder,
            SplitGroupId = null,
            LastActivatedAt = now,
            PositionModifiedAt = BrowserEditTimestamp.Normalize(now)
        });
        InsertTab(tab, RestoredIndex(archived, folder));
        return tab;
    }

    /// Where a restored tab returns: after the tab that sat above it, or else
    /// before the one that sat below it, when that tab is still open, and
    /// inside `folder` while the folder holds other tabs; or else at the top
    /// of `folder`, or where it sits when empty. Null leaves the tab where its
    /// section puts a new one.
    private int? RestoredIndex(ArchivedTabState archived, Guid? folder) {
        var subtree = folder is { } id ? new FolderTree(folders).Subtree(id) : [];
        bool Within(BrowserTab tab) => tab.FolderId is { } member && subtree.Contains(member);
        bool held = tabs.Any(Within);
        BrowserTab? Beside(Guid? id) =>
            tabs.FirstOrDefault(t => t.Id == id && t.Placement == TabPlacement.Current && (!held || Within(t)));
        if (Beside(archived.PreviousTabId) is { } previous) return tabs.IndexOf(SplitMembers(previous.Id)[^1]) + 1;
        if (Beside(archived.NextTabId) is { } next) return tabs.IndexOf(SplitMembers(next.Id)[0]);
        if (folder is not { } restoredIn) return null;
        if (held) return tabs.IndexOf(SplitMembers(tabs.First(Within).Id)[0]);
        return folders.First(f => f.Id == restoredIn).OrderAnchorTabId is { } anchor && tabs.Any(t => t.Id == anchor)
            ? tabs.IndexOf(SplitMembers(anchor)[0]) : tabs.Count;
    }

    /// `value`, the archived form of `tab`, with the open tabs beside `tab`
    /// in its section while it is still listed.
    private ArchivedTabState Archived(BrowserTab tab, TabState value, DateTimeOffset now, ArchiveReason reason) {
        if (tab.Placement != TabPlacement.Current) return new(value, now, reason);
        int index = tabs.IndexOf(tab);
        Guid? Beside(int at) => at >= 0 && at < tabs.Count && tabs[at].Placement == TabPlacement.Current ? tabs[at].Id : null;
        return new(value, now, reason, Beside(index - 1), Beside(index + 1));
    }

    /// Puts a saved or pinned tab's page away, returning the tab to its saved
    /// address when `returnToSavedUrl`, and answers the tab its window shows
    /// next: `fallback` in place of the tab when it showed it.
    public Guid? CloseDurable(Guid id, Guid? selected, Guid? fallback, bool returnToSavedUrl) {
        var tab = Tab(id);
        if (returnToSavedUrl) tab.ReturnToSavedUrl();
        return selected == id ? fallback is { } other && other != id && tabs.Any(t => t.Id == other)
            ? other : null : selected;
    }

    /// Archives open tabs unused for longer than `lifetime`. The tab the caller
    /// shows and any tab in `kept` (tabs other windows show) survive.
    public Guid? CleanupCurrentTabs(Guid? selected, TimeSpan lifetime, DateTimeOffset now,
        IReadOnlyCollection<Guid>? kept = null) {
        var expired = tabs.Where(t => !t.Placement.IsDurable && !t.Content.IsStartPage
            && t.Id != selected && kept?.Contains(t.Id) != true && now - t.LastActivatedAt > lifetime).ToArray();
        var ids = expired.Select(t => t.Id).ToHashSet();
        var nextFolders = new FolderTree(folders).PreserveOrder(ids, tabs);
        foreach (var tab in expired)
            archive.Add(Archived(tab, tab.State with { SplitGroupId = null }, now, ArchiveReason.AutoCleanup));
        tabs.RemoveAll(t => ids.Contains(t.Id));
        folders.Clear(); folders.AddRange(nextFolders);
        // Maintenance preserves an empty selection and does not dissolve a
        // surviving split. User-driven dismissal has a different contract.
        if (selected is not null && !tabs.Any(t => t.Id == selected))
            selected = FallbackSelection();
        return selected;
    }

    #endregion
}
