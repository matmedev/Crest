import SwiftUI

/// Tells the address inside a field whether the pointer rests on the field, so
/// a path shown only on hover can appear.
///
/// Editing forgets the hover: the command palette covers the field as it
/// opens, and the field may never hear the pointer leave while covered. The
/// pointer's next move over the field brings it back, since a pointer resting
/// there when editing ends never enters it again.
struct BrowserAddressFieldHoverTracking: ViewModifier {
    let isEditing: Bool

    @State private var isHovered = false

    func body(content: Content) -> some View {
        content
            .environment(\.browserAddressFieldIsHovered, isHovered && !isEditing)
            .onContinuousHover { phase in
                switch phase {
                case .active: isHovered = true
                case .ended: isHovered = false
                }
            }
            .onChange(of: isEditing) { _, editing in
                if editing { isHovered = false }
            }
    }
}

extension EnvironmentValues {
    /// Whether the pointer rests on the address field around this address.
    @Entry var browserAddressFieldIsHovered = false
}

extension View {
    func tracksBrowserAddressFieldHover(isEditing: Bool) -> some View {
        modifier(BrowserAddressFieldHoverTracking(isEditing: isEditing))
    }
}
