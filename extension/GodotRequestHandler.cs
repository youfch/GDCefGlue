using Xilium.CefGlue;

namespace GDCefGlueExtension;

/// <summary>
/// 拦截 godot://bridge 导航, 用于 JS→C# 桥接.
/// JS: iframe.src = 'godot://bridge?type=X&cb=ID&payload=JSON'
/// C#: 解析 URL 并触发 BridgeRequest 事件.
/// </summary>
internal sealed class GodotRequestHandler : CefRequestHandler
{
    private readonly CefGlueControl _control;

    public GodotRequestHandler(CefGlueControl control)
    {
        _control = control;
    }

    protected override bool OnBeforeBrowse(
        CefBrowser browser, CefFrame frame, CefRequest request,
        bool userGesture, bool isRedirect)
    {
        if (_control.IsDisposed) return false;
        var url = request?.Url;
        if (url != null && url.StartsWith("godot://bridge", System.StringComparison.Ordinal))
        {
            _control.OnBridgeRequest(url);
            return true; // 取消导航
        }
        return false;
    }

    protected override CefResourceRequestHandler GetResourceRequestHandler(
        CefBrowser browser, CefFrame frame, CefRequest request,
        bool isNavigation, bool isDownload,
        string requestInitiator, ref bool disableDefaultHandling)
    {
        return null;
    }
}