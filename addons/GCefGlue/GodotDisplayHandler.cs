using Xilium.CefGlue;

namespace GDCefGlue
{
    internal class GodotDisplayHandler : CefDisplayHandler
    {
        private readonly CefGlueControl _control;

        public GodotDisplayHandler(CefGlueControl control)
        {
            _control = control;
        }

        protected override void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
        {
            _control.OnAddressChange(browser, frame, url);
        }

        protected override void OnTitleChange(CefBrowser browser, string title)
        {
            _control.OnTitleChange(browser, title);
        }
    }
}
