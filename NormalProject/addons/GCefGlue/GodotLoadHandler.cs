using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// Handles load events from CEF such as load start, load end, and load errors.
    /// </summary>
    internal class GodotLoadHandler : CefLoadHandler
    {
        private readonly CefGlueControl _control;

        public GodotLoadHandler(CefGlueControl control)
        {
            _control = control;
        }

        /// <summary>
        /// Called when a page starts loading.
        /// </summary>
        protected override void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
        {
            _control.OnLoadStart(browser, frame, transitionType);
        }

        /// <summary>
        /// Called when a page finishes loading.
        /// </summary>
        protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
        {
            _control.OnLoadEnd(browser, frame, httpStatusCode);
        }

        /// <summary>
        /// Called when a page fails to load.
        /// </summary>
        protected override void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
        {
            _control.OnLoadError(browser, frame, errorCode, errorText, failedUrl);
        }
    }
}
