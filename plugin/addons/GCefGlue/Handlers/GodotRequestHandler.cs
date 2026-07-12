using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// CEF Request Handler — intercepts custom-protocol navigations for JS↔C# bridging.
    ///
    /// JS → C#:  iframe.src = "godot://bridge?type=X&cb=ID&payload=JSON"
    ///           (triggers OnBeforeBrowse; we cancel & dispatch via BridgeRequest event)
    /// C# → JS:  control.SendToJs(json)  →  window._godotBridge._onMessage(msg)
    ///           control.SendResponse(cbId, json)  →  window._godotBridge._onResponse(id, msg)
    /// </summary>
    internal sealed class GodotRequestHandler : CefRequestHandler
    {
        private readonly CefGlueControl _control;

        public GodotRequestHandler(CefGlueControl control)
        {
            _control = control;
        }

    /// <summary>
    /// Intercept navigations to godot://bridge/... — parse & dispatch, then cancel.
    /// </summary>
    protected override bool OnBeforeBrowse(
        CefBrowser browser, CefFrame frame, CefRequest request,
        bool userGesture, bool isRedirect)
    {
        var url = request?.Url;
        if (url != null && url.StartsWith("godot://bridge", System.StringComparison.Ordinal))
        {
            _control.OnBridgeRequest(url);
            return true; // cancel navigation — iframe stays empty
        }
        return false;
    }

    /// <summary>
    /// Return null to use default resource handling for all non-bridge requests.
    /// </summary>
    protected override CefResourceRequestHandler GetResourceRequestHandler(
        CefBrowser browser, CefFrame frame, CefRequest request,
        bool isNavigation, bool isDownload,
        string requestInitiator, ref bool disableDefaultHandling)
    {
        return null;
    }
}
}
