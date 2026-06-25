using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// CEF client that provides handlers for rendering, lifespan, display, load, and audio events.
    /// </summary>
    internal class GodotCefClient : CefClient
    {
        private readonly GodotRenderHandler _renderHandler;
        private readonly GodotLifeSpanHandler _lifeSpanHandler;
        private readonly GodotDisplayHandler _displayHandler;
        private readonly GodotLoadHandler _loadHandler;
        private readonly GodotAudioHandler _audioHandler;

        public GodotCefClient(CefGlueControl control)
        {
            _renderHandler = new GodotRenderHandler(control);
            _lifeSpanHandler = new GodotLifeSpanHandler(control);
            _displayHandler = new GodotDisplayHandler(control);
            _loadHandler = new GodotLoadHandler(control);
            _audioHandler = new GodotAudioHandler();
        }

        protected override CefRenderHandler GetRenderHandler() => _renderHandler;
        protected override CefLifeSpanHandler GetLifeSpanHandler() => _lifeSpanHandler;
        protected override CefDisplayHandler GetDisplayHandler() => _displayHandler;
        protected override CefLoadHandler GetLoadHandler() => _loadHandler;
        protected override CefAudioHandler GetAudioHandler() => _audioHandler;
    }
}
