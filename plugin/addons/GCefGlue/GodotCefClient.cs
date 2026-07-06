using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// CEF client that provides handlers for rendering, lifespan, display, load,
    /// and request interception (custom godot:// protocol for JS↔C# bridging).
    /// </summary>
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

        protected override CefRenderHandler GetRenderHandler()
        {
            // 嵌入模式下，CEF 直接渲染到子 HWND，不需要离屏渲染处理器
            if (CefGlueControl.UseEmbeddedWindowGlobal)
                return null;
            return _renderHandler;
        }
        protected override CefLifeSpanHandler GetLifeSpanHandler() => _lifeSpanHandler;
        protected override CefDisplayHandler GetDisplayHandler() => _displayHandler;
        protected override CefLoadHandler GetLoadHandler() => _loadHandler;
        protected override CefRequestHandler GetRequestHandler() => _requestHandler;

        /// <summary>
        /// Receives IPC messages from the CEF renderer process (CefGlue.BrowserProcess).
        /// Dispatches to CefGlueControl for handling JS evaluation results and native method calls.
        /// </summary>
        protected override bool OnProcessMessageReceived(CefBrowser browser, CefFrame frame, CefProcessId sourceProcess, CefProcessMessage message)
        {
            using (message)
            {
                _control.HandleProcessMessage(message);
            }
            return true;
        }
    }
}
