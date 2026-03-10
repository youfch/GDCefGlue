using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// Handles display events from CEF such as address and title changes.
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
    }
}
