import SwiftUI

/// One direction of page history: the chevron, and the stack behind it.
struct BrowserSidebarNavigationHistoryControl: View {
    let direction: BrowserSidebarNavigationControl
    let port: BrowserSidebarNavigationPort
    let metrics: BrowserSidebarNavigationControlMetrics

    var body: some View {
        Button(action: navigate) {
            Image(systemName: direction.systemImage)
                .font(metrics.historySymbolFont)
        }
        .accessibilityLabel(Text(direction.accessibilityLabel))
        .accessibilityIdentifier(direction.accessibilityIdentifier)
        .disabled(!direction.isAvailable(port))
        .help(help)
        .modifier(
            BrowserPlatformNavigationHistoryMenuModifier(
                items: { direction.history(port) },
                emptyTitle: direction.emptyHistoryTitle,
                choose: navigate(to:),
                showFullHistory: port.showHistory
            )
        )
    }

    /// The tooltip, naming the tab going this way returns to when it closes
    /// the page's tab.
    private var help: Text {
        guard let title = direction.returnTitle(port) else { return Text(direction.tooltip) }
        return Text("Close and return to \(title)")
    }

    private func navigate() {
        direction.navigate(port)
    }

    private func navigate(to item: BrowserNavigationHistoryItem) {
        direction.navigateToItem(port, item)
    }
}
