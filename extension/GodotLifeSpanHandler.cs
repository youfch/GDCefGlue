using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

internal class GodotLifeSpanHandler : CefLifeSpanHandler
{
    private readonly CefGlueControl _control;

    public GodotLifeSpanHandler(CefGlueControl control)
    {
        _control = control;
    }

    protected override void OnAfterCreated(CefBrowser browser)
    {
        _control.OnBrowserCreated(browser);
    }

    protected override bool OnBeforePopup(CefBrowser browser, CefFrame frame, int popupId, string targetUrl, string targetFrameName, CefWindowOpenDisposition targetDisposition, bool userGesture, CefPopupFeatures popupFeatures, CefWindowInfo windowInfo, ref CefClient client, CefBrowserSettings settings, ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess)
    {
        if (_control.OpenPopupInCurrentBrowser)
        {
            switch (targetDisposition)
            {
                case CefWindowOpenDisposition.NewBackgroundTab:
                case CefWindowOpenDisposition.NewForegroundTab:
                case CefWindowOpenDisposition.NewWindow:
                case CefWindowOpenDisposition.NewPopup:
                    _control.CallDeferred("NavigateToUrl", targetUrl);
                    return true;
            }
        }

        if (_control.HasNewWindowSubscribers)
        {
            bool isNewWindow = targetDisposition == CefWindowOpenDisposition.NewWindow
                            || targetDisposition == CefWindowOpenDisposition.NewPopup;
            _control.RaiseNewWindowRequested(targetUrl, isNewWindow);
            return true;
        }
        return false;
    }
}
