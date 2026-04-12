use serde::Serialize;

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DisplayInfo {
    pub screen_width: f64,
    pub screen_height: f64,
    pub has_notch: bool,
    pub notch_width: f64,
    pub top_inset: f64,
}

impl Default for DisplayInfo {
    fn default() -> Self {
        Self {
            screen_width: 1920.0,
            screen_height: 1080.0,
            has_notch: false,
            notch_width: 0.0,
            top_inset: 0.0,
        }
    }
}

#[cfg(target_os = "macos")]
pub fn detect() -> DisplayInfo {
    use objc2::msg_send;
    use objc2::runtime::AnyObject;
    use objc2_app_kit::NSScreen;
    use objc2_foundation::{MainThreadMarker, NSEdgeInsets, NSRect};

    let Some(mtm) = MainThreadMarker::new() else {
        return DisplayInfo::default();
    };
    let Some(screen) = NSScreen::mainScreen(mtm) else {
        return DisplayInfo::default();
    };

    let frame: NSRect = screen.frame();
    let screen_width = frame.size.width;
    let screen_height = frame.size.height;

    let screen_ref: &AnyObject = screen.as_ref();

    let insets: NSEdgeInsets = unsafe { msg_send![screen_ref, safeAreaInsets] };
    let top_inset = insets.top;

    if top_inset <= 0.0 {
        return DisplayInfo {
            screen_width,
            screen_height,
            has_notch: false,
            notch_width: 0.0,
            top_inset: 0.0,
        };
    }

    let aux_left: NSRect = unsafe { msg_send![screen_ref, auxiliaryTopLeftArea] };
    let aux_right: NSRect = unsafe { msg_send![screen_ref, auxiliaryTopRightArea] };
    let notch_width = (screen_width - aux_left.size.width - aux_right.size.width).max(0.0);

    DisplayInfo {
        screen_width,
        screen_height,
        has_notch: notch_width > 0.0,
        notch_width,
        top_inset,
    }
}

#[cfg(not(target_os = "macos"))]
pub fn detect() -> DisplayInfo {
    DisplayInfo::default()
}
