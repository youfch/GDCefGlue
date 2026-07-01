using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// CEF client that provides handlers for rendering, lifespan, display, load,
    /// and request interception (custom godot:// protocol for JS↔C# bridging).
    /// </summary>
    internal class GodotCefClient : CefClient
    {
        private readonly GodotRenderHandler _renderHandler;
        private readonly GodotLifeSpanHandler _lifeSpanHandler;
        private readonly GodotDisplayHandler _displayHandler;
        private readonly GodotLoadHandler _loadHandler;
        private readonly GodotRequestHandler _requestHandler;

        public GodotCefClient(CefGlueControl control)
        {
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
    }
}
