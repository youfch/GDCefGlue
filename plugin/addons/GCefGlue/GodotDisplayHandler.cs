using System;
using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// Handles display events from CEF such as address, title, and cursor changes.
    /// </summary>
    internal class GodotDisplayHandler : CefDisplayHandler
    {
        private readonly CefGlueControl _control;

        public GodotDisplayHandler(CefGlueControl control)
        {
            _control = control;
        }

        /// <summary>
        /// Called when the browser address changes.
        /// </summary>
        protected override void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
        {
            _control.OnAddressChange(browser, frame, url);
        }

        /// <summary>
        /// Called when the page title changes.
        /// </summary>
        protected override void OnTitleChange(CefBrowser browser, string title)
        {
            _control.OnTitleChange(browser, title);
        }

        /// <summary>
        /// Called when the mouse cursor type changes (e.g. text input → IBeam, link → Hand).
        /// Maps CefCursorType to Godot CursorShape and updates the control.
        /// </summary>
        protected override bool OnCursorChange(CefBrowser browser, IntPtr cursorHandle, CefCursorType type, CefCursorInfo customCursorInfo)
        {
            _control.OnCursorChanged(type);
            return _control.SyncCursor;
        }
    }
}
