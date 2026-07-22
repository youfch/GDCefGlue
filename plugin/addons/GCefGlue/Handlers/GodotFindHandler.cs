using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// Handles find result callbacks from CEF.
    /// Delegates to <see cref="CefGlueControl"/> via thread-safe
    /// <c>CallDeferred</c> marshalling (CEF UI thread -> Godot main thread).
    /// </summary>
    internal sealed class GodotFindHandler : CefFindHandler
    {
        private readonly CefGlueControl _control;

        public GodotFindHandler(CefGlueControl control)
        {
            _control = control;
        }

        /// <summary>
        /// Called to report find results. Runs on the CEF UI thread.
        /// Delegates to <see cref="CefGlueControl.OnFindResult"/> which marshals
        /// to the Godot main thread via <c>CallDeferred</c>.
        /// </summary>
        protected override void OnFindResult(
            CefBrowser browser,
            int identifier,
            int count,
            CefRectangle selectionRect,
            int activeMatchOrdinal,
            bool finalUpdate)
        {
            _control.OnFindResult(browser, identifier, count, selectionRect, activeMatchOrdinal, finalUpdate);
        }
    }
}
