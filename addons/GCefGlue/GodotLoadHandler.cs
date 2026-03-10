using Xilium.CefGlue;

namespace GDCefGlue
{
    internal class GodotLoadHandler : CefLoadHandler
    {
        private readonly CefGlueControl _control;

        public GodotLoadHandler(CefGlueControl control)
        {
            _control = control;
        }

        protected override void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
        {
            _control.OnLoadStart(browser, frame, transitionType);
        }

        protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
        {
            _control.OnLoadEnd(browser, frame, httpStatusCode);
        }

        protected override void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
        {
            _control.OnLoadError(browser, frame, errorCode, errorText, failedUrl);
        }
    }
}
