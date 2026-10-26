import Foundation

/// When the address field shows a page's path beneath its domain on this
/// device: always, only while the pointer rests on the field, or never.
///
/// The path is everything after the host: route, query and fragment. Editing
/// always shows the whole address, whichever is chosen.
struct BrowserAddressPathVisibility: Hashable, Sendable {
    // MARK: - Static Variables

    static let always = BrowserAddressPathVisibility(
        name: "always", title: "Always", showsAtRest: true, showsOnHover: true)
    static let onHover = BrowserAddressPathVisibility(
        name: "onHover", title: "On hover", showsAtRest: false, showsOnHover: true)
    static let never = BrowserAddressPathVisibility(
        name: "never", title: "Never", showsAtRest: false, showsOnHover: false)
    static let all = [always, onHover, never]
    static let standard = always

    // MARK: - Variables

    /// The persisted spelling.
    let name: String
    let title: LocalizedStringResource
    /// Whether the path shows while nothing points at the field.
    let showsAtRest: Bool
    /// Whether the path shows while the pointer rests on the field.
    let showsOnHover: Bool

    // MARK: - Initializers

    private init(name: String, title: LocalizedStringResource, showsAtRest: Bool, showsOnHover: Bool) {
        self.name = name
        self.title = title
        self.showsAtRest = showsAtRest
        self.showsOnHover = showsOnHover
    }

    // MARK: - Actions - Presentation

    func showsPath(isHovered: Bool) -> Bool {
        isHovered ? showsOnHover : showsAtRest
    }

    // MARK: - Actions - Lookup

    static func named(_ name: String?) -> BrowserAddressPathVisibility? {
        all.first { $0.name == name }
    }
}

// MARK: - Hashable

extension BrowserAddressPathVisibility {
    static func == (lhs: BrowserAddressPathVisibility, rhs: BrowserAddressPathVisibility) -> Bool {
        lhs.name == rhs.name
    }

    func hash(into hasher: inout Hasher) {
        hasher.combine(name)
    }
}
