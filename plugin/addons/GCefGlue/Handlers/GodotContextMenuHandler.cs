using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// Handles context menu events from CEF.
    /// Delegates all callbacks to <see cref="CefGlueControl"/> via thread-safe
    /// <c>CallDeferred</c> marshalling (CEF UI thread -> Godot main thread).
    /// </summary>
    /// <remarks>
    /// <para>OSR mode only. In EmbeddedWindow mode, <see cref="GodotCefClient"/>
    /// returns <c>null</c> from <c>GetContextMenuHandler</c> so CEF's native
    /// window menu is used.</para>
    /// <para>Thread safety: every CEF callback here runs on the CEF UI thread,
    /// which is NOT the Godot main thread. We never touch Godot APIs directly;
    /// we only call <c>_control.OnXxx</c> entry points which guard disposed
    /// state and marshal via <c>CallDeferred</c>.</para>
    /// </remarks>
    internal sealed class GodotContextMenuHandler : CefContextMenuHandler
    {
        private readonly CefGlueControl _control;

        public GodotContextMenuHandler(CefGlueControl control)
        {
            _control = control;
        }

        /// <summary>
        /// Called before a context menu is displayed. The <paramref name="model"/>
        /// initially contains the default menu; clearing or modifying it changes
        /// what gets shown. Do NOT retain references to <paramref name="state"/>
        /// or <paramref name="model"/> past this call — they are disposed by CEF.
        /// </summary>
        protected override void OnBeforeContextMenu(
            CefBrowser browser,
            CefFrame frame,
            CefContextMenuParams state,
            CefMenuModel model)
        {
            if (_control.IsDisposed) return;
            // CefContextMenuParams/CefMenuModel are wrapped in `using` by the
            // CefGlue trampoline after this call returns, so we must snapshot
            // all the info we need now and pass only plain data forward.
            _control.OnBeforeContextMenu(browser, frame, state, model);
        }

        /// <summary>
        /// Called to allow custom display of the context menu. We always return
        /// <c>true</c> (when <see cref="CefGlueControl.ContextMenuEnabled"/> is on)
        /// and render the menu using a Godot <c>PopupMenu</c>. The
        /// <paramref name="callback"/> is invoked asynchronously from the Godot
        /// main thread after the user picks an item or dismisses the menu.
        /// </summary>
        protected override bool RunContextMenu(
            CefBrowser browser,
            CefFrame frame,
            CefContextMenuParams parameters,
            CefMenuModel model,
            CefRunContextMenuCallback callback)
        {
            if (_control.IsDisposed) return false;
            return _control.OnRunContextMenu(browser, frame, parameters, model, callback);
        }

        /// <summary>
        /// Called when a command is selected. <paramref name="commandId"/> is a
        /// plain int — built-in IDs (Back/Forward/Copy/...) use <see cref="CefMenuId"/>,
        /// user-defined IDs should be within <c>UserFirst..UserLast</c>.
        /// </summary>
        /// <returns>True if handled; false to let CEF apply default behavior for built-in IDs.</returns>
        protected override bool OnContextMenuCommand(
            CefBrowser browser,
            CefFrame frame,
            CefContextMenuParams state,
            int commandId,
            CefEventFlags eventFlags)
        {
            if (_control.IsDisposed) return false;
            return _control.OnContextMenuCommand(browser, frame, state, commandId, eventFlags);
        }

        /// <summary>
        /// Called when the menu is dismissed (regardless of cancel or selection).
        /// </summary>
        protected override void OnContextMenuDismissed(
            CefBrowser browser,
            CefFrame frame)
        {
            if (_control.IsDisposed) return;
            _control.OnContextMenuDismissed(browser, frame);
        }
    }
}
