using Xilium.CefGlue;

namespace GDCefGlueExtension;

internal class GodotCefClient : CefClient
{
    private readonly CefGlueControl _control;
    private readonly GodotRenderHandler _renderHandler;
    private readonly GodotLifeSpanHandler _lifeSpanHandler;
    private readonly GodotDisplayHandler _displayHandler;
    private readonly GodotLoadHandler _loadHandler;
    private readonly GodotRequestHandler _requestHandler;
    private readonly GodotPermissionHandler _permissionHandler;
    private readonly GodotContextMenuHandler _contextMenuHandler;
    private readonly GodotFocusHandler _focusHandler;
    private readonly GodotFindHandler _findHandler;

    public GodotCefClient(CefGlueControl control)
    {
        _control = control;
        _renderHandler = new GodotRenderHandler(control);
        _lifeSpanHandler = new GodotLifeSpanHandler(control);
        _displayHandler = new GodotDisplayHandler(control);
        _loadHandler = new GodotLoadHandler(control);
        _requestHandler = new GodotRequestHandler(control);
        _permissionHandler = new GodotPermissionHandler(control);
        _contextMenuHandler = new GodotContextMenuHandler(control);
        _focusHandler = new GodotFocusHandler(control);
        _findHandler = new GodotFindHandler(control);
    }

    protected override CefRenderHandler GetRenderHandler() => _renderHandler;
    protected override CefLifeSpanHandler GetLifeSpanHandler() => _lifeSpanHandler;
    protected override CefDisplayHandler GetDisplayHandler() => _displayHandler;
    protected override CefLoadHandler GetLoadHandler() => _loadHandler;
    protected override CefRequestHandler GetRequestHandler() => _requestHandler;
    protected override CefPermissionHandler GetPermissionHandler() => _permissionHandler;
    protected override CefFindHandler GetFindHandler() => _findHandler;

    protected override CefContextMenuHandler GetContextMenuHandler()
    {
        // 嵌入窗口模式：CEF 在原生子窗口上自行处理右键菜单
        if (CefGlueControl.ActiveRenderMode == RenderMode.EmbeddedWindow)
            return null;

        return _contextMenuHandler;
    }

    protected override CefFocusHandler GetFocusHandler() => _focusHandler;

    protected override bool OnProcessMessageReceived(CefBrowser browser, CefFrame frame, CefProcessId sourceProcess, CefProcessMessage message)
    {
        using (message)
        {
            _control.HandleProcessMessage(message);
        }
        return true;
    }
}
