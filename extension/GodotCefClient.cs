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

    public GodotCefClient(CefGlueControl control)
    {
        _control = control;
        _renderHandler = new GodotRenderHandler(control);
        _lifeSpanHandler = new GodotLifeSpanHandler(control);
        _displayHandler = new GodotDisplayHandler(control);
        _loadHandler = new GodotLoadHandler(control);
        _requestHandler = new GodotRequestHandler(control);
    }

    protected override CefRenderHandler GetRenderHandler() => _renderHandler;
    protected override CefLifeSpanHandler GetLifeSpanHandler() => _lifeSpanHandler;
    protected override CefDisplayHandler GetDisplayHandler() => _displayHandler;
    protected override CefLoadHandler GetLoadHandler() => _loadHandler;
    protected override CefRequestHandler GetRequestHandler() => _requestHandler;

    protected override bool OnProcessMessageReceived(CefBrowser browser, CefFrame frame, CefProcessId sourceProcess, CefProcessMessage message)
    {
        using (message)
        {
            _control.HandleProcessMessage(message);
        }
        return true;
    }
}
