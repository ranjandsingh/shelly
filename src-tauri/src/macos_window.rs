//! macOS-only window transparency helpers.
//!
//! Tauri v2's `transparent: true` in `tauri.conf.json` sets the NSWindow to
//! non-opaque, but the WKWebView inside keeps an opaque backing layer by
//! default — so CSS with rgba backgrounds appears to "fade" the panel color
//! against a solid white bottom instead of the desktop showing through.
//!
//! The canonical fix is to set `drawsBackground = NO` on the WKWebView via
//! KVC. This module finds the WKWebView in the window's view hierarchy and
//! applies that, plus explicitly reasserts the NSWindow's opaque/background
//! state in case Tauri's init got overridden during webview setup.
//!
//! The whole module is gated on `target_os = "macos"` — nothing here compiles
//! on Windows, so Windows behavior is unchanged.

#![cfg(target_os = "macos")]

use objc2::runtime::AnyObject;
use objc2::{class, msg_send};
use tauri::WebviewWindow;

pub fn apply_transparency(window: &WebviewWindow) {
    let ns_window_ptr = match window.ns_window() {
        Ok(p) => p,
        Err(e) => {
            log::warn!("macos_window::apply_transparency: ns_window() failed: {e:?}");
            return;
        }
    };
    if ns_window_ptr.is_null() {
        log::warn!("macos_window::apply_transparency: ns_window is null");
        return;
    }

    unsafe {
        let ns_window = ns_window_ptr as *mut AnyObject;

        // Reassert transparency on the NSWindow. Tauri sets this when
        // `transparent: true` is in the config, but doing it again after the
        // window is fully constructed guards against webview-init overrides.
        let _: () = msg_send![ns_window, setOpaque: false];
        let clear: *mut AnyObject = msg_send![class!(NSColor), clearColor];
        let _: () = msg_send![ns_window, setBackgroundColor: clear];

        // Walk the content view hierarchy and force drawsBackground=NO on any
        // WKWebView we find. This is what actually fixes the "solid white
        // behind faded panel color" symptom.
        let content_view: *mut AnyObject = msg_send![ns_window, contentView];
        if !content_view.is_null() {
            let mut found = 0;
            disable_webview_draws_background(content_view, &mut found);
            log::info!(
                "macos_window::apply_transparency: cleared drawsBackground on {found} WKWebView(s)"
            );
        }
    }
}

/// Recursively walk `view` and set `drawsBackground = NO` via KVC on any
/// WKWebView found. KVC is used because `drawsBackground` is not a public
/// Swift/ObjC API on WKWebView, but the property is KVC-accessible and this
/// has been the idiomatic transparency trick for years.
unsafe fn disable_webview_draws_background(view: *mut AnyObject, found: &mut u32) {
    // `WKWebView` lives in the WebKit framework. `Class::get` via `class!`
    // returns a static reference; if WebKit isn't linked we'd not be running
    // a webview anyway.
    let wk_cls = class!(WKWebView);
    let is_webview: bool = msg_send![view, isKindOfClass: wk_cls];

    if is_webview {
        // Build NSString @"drawsBackground" for KVC.
        let key_cstr = c"drawsBackground".as_ptr();
        let key: *mut AnyObject = msg_send![class!(NSString), stringWithUTF8String: key_cstr];
        let no_val: *mut AnyObject = msg_send![class!(NSNumber), numberWithBool: false];
        let _: () = msg_send![view, setValue: no_val, forKey: key];
        *found += 1;
    }

    // Recurse into subviews regardless — Tauri sometimes nests WKWebView
    // inside wrapper NSViews.
    let subviews: *mut AnyObject = msg_send![view, subviews];
    if !subviews.is_null() {
        let count: usize = msg_send![subviews, count];
        for i in 0..count {
            let sv: *mut AnyObject = msg_send![subviews, objectAtIndex: i];
            if !sv.is_null() {
                disable_webview_draws_background(sv, found);
            }
        }
    }
}
