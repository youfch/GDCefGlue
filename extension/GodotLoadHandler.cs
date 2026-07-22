using Xilium.CefGlue;

namespace GDCefGlueExtension;

internal class GodotLoadHandler : CefLoadHandler
{
    private readonly CefGlueControl _control;

    public GodotLoadHandler(CefGlueControl control)
    {
        _control = control;
    }

    protected override void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
    {
        if (_control.IsDisposed) return;
        _control.OnLoadStart(browser, frame, transitionType);
    }

    protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
    {
        if (_control.IsDisposed) return;
        _control.OnLoadEnd(browser, frame, httpStatusCode);
    }

    protected override void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
    {
        if (_control.IsDisposed) return;
        _control.OnLoadError(browser, frame, errorCode, errorText, failedUrl);
    }
}
