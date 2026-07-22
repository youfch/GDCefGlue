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

        protected override CefRenderHandler GetRenderHandler()
        {
            // 嵌入模式下，CEF 直接渲染到子 HWND，不需要离屏渲染处理器
            if (CefGlueControl.ActiveRenderMode == RenderMode.EmbeddedWindow)
                return null;
            return _renderHandler;
        }
        protected override CefLifeSpanHandler GetLifeSpanHandler() => _lifeSpanHandler;
        protected override CefDisplayHandler GetDisplayHandler() => _displayHandler;
        protected override CefLoadHandler GetLoadHandler() => _loadHandler;
        protected override CefRequestHandler GetRequestHandler() => _requestHandler;
        protected override CefPermissionHandler GetPermissionHandler() => _permissionHandler;
        protected override CefFindHandler GetFindHandler() => _findHandler;

        /// <summary>
        /// Returns the focus handler for bridging CEF focus changes to Godot.
        /// </summary>
        protected override CefFocusHandler GetFocusHandler() => _focusHandler;

        /// <summary>
        /// Returns the context menu handler.
        /// <para>In OSR mode, always returns our handler — CEF's default
        /// menu runner requires a window handle which OSR doesn't have
        /// (see cef/native/menu_runner_views_aura.cc). Returning null would
        /// make CEF try the default impl and log errors. Our handler decides
        /// in <see cref="CefGlueControl.OnRunContextMenu"/> whether to show
        /// a PopupMenu (when <see cref="CefGlueControl.ContextMenuEnabled"/>)
        /// or silently cancel (when disabled, preserving prior "no menu" behavior).</para>
        /// <para>In EmbeddedWindow mode, returns null so CEF's native window
        /// menu is used (HWND is available).</para>
        /// </summary>
        protected override CefContextMenuHandler GetContextMenuHandler()
        {
            // 嵌入窗口模式：CEF 在原生子窗口上自行处理右键菜单
            if (CefGlueControl.ActiveRenderMode == RenderMode.EmbeddedWindow)
                return null;

            // OSR 模式：始终返回 handler（即便 ContextMenuEnabled=false），
            // 避免触发 CEF 默认菜单 runner 的 "Window handle is required" 错误。
            // handler 内部根据 ContextMenuEnabled 决定显示或静默取消。
            return _contextMenuHandler;
        }

        /// <summary>
        /// Receives IPC messages from the CEF renderer process (CefGlue.BrowserProcess).
        /// Dispatches to CefGlueControl for handling JS evaluation results and native method calls.
        /// </summary>
        protected override bool OnProcessMessageReceived(CefBrowser browser, CefFrame frame, CefProcessId sourceProcess, CefProcessMessage message)
        {
            if (_control.IsDisposed) { message.Dispose(); return false; }
            using (message)
            {
                _control.HandleProcessMessage(message);
            }
            return true;
        }
    }
}
