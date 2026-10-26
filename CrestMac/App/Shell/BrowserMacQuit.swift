import AppKit

/// Holds a quit until the core agrees to it and the edits the app accepted
/// are saved and staged for sync, then lets the entry point that asked finish
/// it: the engine that owns the process quits itself, and AppKit replies to
/// its own termination request. Nothing else ends the process before the
/// core's storage worker wrote the newest revision or its stager staged it.
@MainActor
final class BrowserMacQuit {
    // MARK: - Static Variables

    /// The keys that quit, which sit next to ⌘W and may be pressed by mistake.
    private static let quitShortcut = BrowserShortcut(key: .character("q"), modifiers: .command)

    // MARK: - Variables

    private unowned let application: BrowserMacApplication
    private unowned let windows: BrowserMacWindows
    /// A quit is being prepared or finished, so nothing new opens.
    private(set) var isQuitting = false
    /// The quit was allowed and handed to its entry point to finish.
    private var hasFinished = false
    private let dialogs = BrowserDialogPresenter()

    /// Whether this quit asks the person first: it came from ⌘Q, which they
    /// asked to confirm, while a window with pages is open. A quit from the
    /// menu, the Dock, a logout or an update never asks.
    private var asksFirst: Bool {
        guard BrowserAppPreferenceStore.shared.asksBeforeQuitting, windows.isPageWindowOpen,
            let event = NSApp.currentEvent, event.type == .keyDown
        else { return false }
        return BrowserShortcut(event: event) == Self.quitShortcut
    }

    // MARK: - Initializers

    init(application: BrowserMacApplication, windows: BrowserMacWindows) {
        self.application = application
        self.windows = windows
    }

    // MARK: - Actions - Quitting

    /// Asks to quit, which `finish` completes with whether it may: on a later
    /// turn of the main actor, once the person confirmed a ⌘Q they asked to
    /// confirm, and the core asked every page and the person about downloads
    /// in progress. Once allowed, the core keeps the windows open now for the
    /// next launch, the Quick Windows close and every edit is flushed before
    /// `finish` runs. Answers false when the shell has nothing
    /// to hold, having finished already, so the entry point quits at once.
    ///
    /// A quit the core defers while a Space is deleted is asked for again once
    /// the deletion moves on, through the application's own `terminate`, which
    /// reaches this again from the entry point.
    func request(finish: @escaping @MainActor (_ allowed: Bool) -> Void) -> Bool {
        guard !hasFinished else { return false }
        guard !isQuitting else { return true }
        isQuitting = true
        guard asksFirst else {
            prepare(finish: finish)
            return true
        }
        Task { @MainActor in
            let confirmation = await dialogs.confirmQuit()
            if !confirmation.keepsAsking { BrowserAppPreferenceStore.shared.asksBeforeQuitting = false }
            guard confirmation.quits else {
                isQuitting = false
                finish(false)
                return
            }
            prepare(finish: finish)
        }
        return true
    }

    /// Has the core prepare the quit, then keeps the windows for the next
    /// launch and flushes every edit before `finish` allows it.
    private func prepare(finish: @escaping @MainActor (_ allowed: Bool) -> Void) {
        application.quitPreparation.prepare(
            retry: { NSApp.terminate(nil) },
            completion: { [weak self] allowed in
                guard let self else { return }
                guard allowed else {
                    isQuitting = false
                    // An update relaunch that asked for this quit is offered again.
                    application.softwareUpdates.model.applicationStayedOpen()
                    finish(false)
                    return
                }
                // The core keeps the windows open now for the next launch
                // before any of them closes as the app goes.
                application.rememberWindowsForLaunch()
                Task { @MainActor in
                    self.windows.closeQuickWindowsWithoutAsking()
                    await self.application.flushPendingPersistenceBeforeQuit()
                    self.hasFinished = true
                    finish(true)
                }
            })
    }
}
