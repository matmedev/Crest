using CrestCore.Application;
using CrestCore.Contracts;

using Xunit;

namespace CrestCore.Tests;

/// Pages an engine opens by itself, links it asks the core about, and links it
/// stages for a Peek's first load: the core decides where an offered page
/// goes and refuses what a Space may not take, answers what a link does by
/// the link rules, and routes a staged link only to a page of its source's
/// engine and profile.
public sealed partial class BrowserContractsTests {
    /// A current tab showing `url`, opened and shown in `window`, with a live
    /// page of its own there.
    private static (Guid Tab, Guid Page) LiveTab(CrestApp app, Engine engine, Guid workspace, Guid window, Guid space,
        string url = "https://source.example/") {
        var (tab, page) = (Guid.NewGuid(), Guid.NewGuid());
        app.Send(new OpenTab(workspace, window, space, tab, new TabContent(url, View: null, Title: null),
            TabPlacement.Current, AfterTabId: null, Shows: true));
        app.Send(new OpenPage(page, workspace, space, tab, window));
        app.Report(engine, new PageCreated(page));
        return (tab, page);
    }

    /// The current tabs of a Space, in order.
    private static List<TabState> CurrentTabs(CrestApp app, Guid workspace, Guid space) =>
        [.. app.Workspace(workspace).Current.Spaces.Single(held => held.Id == space).Tabs.Where(tab => tab.Placement == TabPlacement.Current)];

    /// The tab `window` shows in `space`, as the last window change says.
    private static Guid? ShownTab(IReadOnlyList<Change> changes, Guid window, Guid space) =>
        changes.OfType<WindowChanged>().Last(change => change.Window.Id == window).Window.ShownTabs
            .SingleOrDefault(shown => shown.SpaceId == space)?.TabId;

    [Fact]
    public void NoSpaceTakesAnOfferedPageItMayNotHost() {
        var session = GuardedSession(withOpenSecondSpace: true);
        var locked = Identity(session);
        var open = session["spaces"]![1]!;
        var (app, engine, binding, workspace, window) = PageHost(session);
        using var disposal = app;
        int tabs = app.Workspace(workspace).Current.Spaces.Sum(space => space.Tabs.Count);
        void Refused(PageOffered offer) {
            app.Report(engine, offer);
            Assert.Equal(new RejectOfferedPage(offer.OfferId), binding.Commands[^1]);
            Assert.Empty(app.Drain().OfType<OfferedPageAdopted>());
            Assert.Equal(tabs, app.Workspace(workspace).Current.Spaces.Sum(space => space.Tabs.Count));
        }

        // A locked Space, one of another profile, and a window that is not
        // open or not named take nothing.
        Refused(new PageOffered(Guid.NewGuid(), locked.Profile, null, window, locked.Space, "https://locked.example/", true));
        Refused(new PageOffered(Guid.NewGuid(), ProfileId(open), null, window, locked.Space, "https://other.example/", true));
        Refused(new PageOffered(Guid.NewGuid(), ProfileId(open), null, Guid.NewGuid(), SpaceId(open), "https://closed.example/", true));
        Refused(new PageOffered(Guid.NewGuid(), ProfileId(open), null, WindowId: null, SpaceId(open), "https://nowhere.example/", true));

        // Unlocked, the Space takes it.
        Unlock(app.Send, workspace, locked.Space);
        app.Report(engine, new PageOffered(Guid.NewGuid(), locked.Profile, null, window, locked.Space, "https://unlocked.example/", true));
        Assert.Single(app.Drain().OfType<OfferedPageAdopted>());
        tabs++;

        // A Space being deleted takes nothing.
        app.Send(new BeginDeletingSpace(workspace, window, SpaceId(open), Guid.NewGuid()));
        app.Drain();
        Refused(new PageOffered(Guid.NewGuid(), ProfileId(open), null, window, SpaceId(open), "https://deleting.example/", true));
    }

    [Fact]
    public void AStagedLinkLoadsOnlyInANewPageOfItsSourcesEngineAndProfile() {
        var session = TwoSpaceSession();
        var (first, second) = (SpaceId(session["spaces"]![0]!), SpaceId(session["spaces"]![1]!));
        var (app, engine, binding, workspace, window) = PageHost(session);
        using var disposal = app;
        var (_, source) = LiveTab(app, engine, workspace, window, first);
        var (peek, elsewhere) = (Guid.NewGuid(), Guid.NewGuid());
        app.Send(new OpenPage(peek, workspace, first, null, window, TransientPresentation.Peek));
        app.Send(new OpenPage(elsewhere, workspace, second, null, window, TransientPresentation.Peek));
        var link = Guid.NewGuid();

        app.Send(new StageLink(peek, source, link, "https://followed.example/"));
        Assert.Equal(new StageNavigation(peek, link, "https://followed.example/"), binding.Commands[^1]);
        Assert.Equal(new StagedLinkElsewhere(elsewhere, source),
            Refusal(app, new StageLink(elsewhere, source, link, "https://followed.example/")));
        var missing = Guid.NewGuid();
        Assert.Equal(new UnknownPage(missing), Refusal(app, new StageLink(missing, source, link, "https://followed.example/")));

        // An engine that creates its page at once, as WebKit does, stages the
        // link in a page that has loaded nothing yet; once the page heads
        // anywhere, it has had its first load.
        app.Report(engine, new PageCreated(peek));
        app.Send(new StageLink(peek, source, link, "https://followed.example/"));
        Assert.Equal(new StageNavigation(peek, link, "https://followed.example/"), binding.Commands[^1]);
        app.Send(new Navigate(peek, "https://followed.example/"));
        Assert.Equal(new StagedLinkElsewhere(peek, source), Refusal(app, new StageLink(peek, source, link, "https://followed.example/")));

        // A link that no longer applies leaves the page where it was.
        app.Drain();
        app.Report(engine, new StagedLinkUnavailable(peek));
        var live = Assert.IsType<PageChanged>(Assert.Single(app.Drain())).Page.Live;
        Assert.Equal((null, false), (live.PendingUrl, live.IsLoading));

        // A link no page loads is forgotten by its source's engine; a source
        // that is gone took its links with it.
        int delivered = binding.Commands.Count;
        app.Send(new DiscardStagedLink(source, link));
        Assert.Equal(new DropStagedLink(link), binding.Commands[^1]);
        app.Send(new DiscardStagedLink(Guid.NewGuid(), link));
        Assert.Equal(delivered + 1, binding.Commands.Count);
    }

    [Fact]
    public void GoingBackFromATabALinkOpenedWithNoEarlierPageClosesItAndReturnsToItsOpener() {
        var session = TwoSpaceSession();
        var (space, profile) = (SpaceId(session["spaces"]![0]!), ProfileId(session["spaces"]![0]!));
        var (app, engine, _, workspace, window) = PageHost(session);
        using var disposal = app;
        var (sourceTab, source) = LiveTab(app, engine, workspace, window, space);
        (Guid Tab, Guid Page) Linked() {
            app.Report(engine, new PageOffered(Guid.NewGuid(), profile, source, window, SpaceId: null, "https://linked.example/",
                Foreground: true));
            var adopted = Assert.Single(app.Drain().OfType<OfferedPageAdopted>());
            app.Report(engine, new PageCreated(adopted.PageId));
            app.Drain();
            return (adopted.TabId, adopted.PageId);
        }
        Guid? ReturnTab(IReadOnlyList<Change> changes) => changes.OfType<WindowChanged>().Last(change => change.Window.Id == window).Window
            .ReturnTabId;
        void ReturnsToOpener(bool returns) {
            var preferences = app.Workspace(workspace).Current.AppPreferences ?? AppPreferences.Default;
            app.Send(new SetAppPreferences(workspace, preferences with { ReturnsToOpenerOnBack = returns }));
        }

        // The window names the opener while it shows the tab, and going back
        // closes the tab and shows its opener, though another tab was shown
        // since.
        var (linked, page) = Linked();
        LiveTab(app, engine, workspace, window, space, "https://other.example/");
        Assert.Equal(sourceTab, ReturnTab(app.Send(new ShowTab(window, space, linked))));
        var back = app.Send(new StepHistory(page, -1));
        Assert.DoesNotContain(CurrentTabs(app, workspace, space), tab => tab.Id == linked);
        Assert.Equal(sourceTab, ShownTab(back, window, space));

        // With the preference off, going back keeps the tab.
        (linked, page) = Linked();
        ReturnsToOpener(false);
        Assert.Null(ReturnTab(app.Send(new ShowTab(window, space, sourceTab)).Concat(app.Send(new ShowTab(window, space, linked))).ToList()));
        app.Send(new StepHistory(page, -1));
        Assert.Contains(CurrentTabs(app, workspace, space), tab => tab.Id == linked);
        ReturnsToOpener(true);

        // So does a tab whose opener another window took over.
        var other = Guid.NewGuid();
        app.Send(new OpenWindow(other, workspace, Saved: false, null, null, [], RestoresTabs: true));
        Assert.Null(ReturnTab(app.Send(new MoveTabToWindow(workspace, window, space, sourceTab, other))));
        app.Send(new StepHistory(page, -1));
        Assert.Contains(CurrentTabs(app, workspace, space), tab => tab.Id == linked);

        // Once this window shows the opener again, the tab returns there again.
        app.Send(new ShowTab(window, space, sourceTab));
        Assert.Equal(sourceTab, ReturnTab(app.Send(new ShowTab(window, space, linked))));

        // A tab whose opener closed keeps its place too.
        Assert.Null(ReturnTab(app.Send(new CloseTab(workspace, window, space, sourceTab))));
        app.Send(new StepHistory(page, -1));
        Assert.Contains(CurrentTabs(app, workspace, space), tab => tab.Id == linked);
    }
}
