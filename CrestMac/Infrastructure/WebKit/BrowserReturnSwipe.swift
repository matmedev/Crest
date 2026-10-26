import AppKit

/// Reads a two-finger swipe back on a WebKit page whose tab returns to the tab
/// a link opened it from. WebKit's own swipe walks only its history, so a tab
/// with no earlier page never starts one; this follows the scroll events the
/// page left alone instead. Only a gesture the page left alone from its start
/// counts, so a swipe that scrolled a carousel or a wide table to its edge
/// never closes the tab.
@MainActor
final class BrowserReturnSwipe {
    // MARK: - Static Variables

    /// How far the fingers travel back, in points, before the tab returns.
    private static let threshold: CGFloat = 120

    // MARK: - Variables

    /// Whether the gesture under way began with the page leaving it alone,
    /// while the tab could return, and has not returned yet.
    private var isTracking = false
    /// How far the fingers have travelled back and across in that gesture.
    private var back: CGFloat = 0
    private var across: CGFloat = 0

    // MARK: - Actions - Gestures

    /// Takes one scroll event the page did not handle, and answers whether the
    /// gesture it belongs to just carried far enough back to return. Momentum
    /// after the fingers lift never counts, and neither does a gesture that
    /// moves more up or down than across, or one the person's trackpad
    /// settings keep from swiping between pages.
    func returns(after event: NSEvent, canReturn: Bool) -> Bool {
        guard event.momentumPhase.isEmpty else { return false }
        if event.phase.contains(.began) {
            isTracking = canReturn && NSEvent.isSwipeTrackingFromScrollEventsEnabled
            (back, across) = (0, 0)
        }
        guard isTracking, event.phase.contains(.changed) else {
            if event.phase.contains(.ended) || event.phase.contains(.cancelled) { isTracking = false }
            return false
        }
        // A finger moving right goes back, whichever way scrolling runs.
        back += event.isDirectionInvertedFromDevice ? event.scrollingDeltaX : -event.scrollingDeltaX
        across += abs(event.scrollingDeltaY)
        guard back > Self.threshold, back > across else { return false }
        isTracking = false
        return true
    }
}
