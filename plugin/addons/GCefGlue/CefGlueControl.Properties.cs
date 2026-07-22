using System;
using Godot;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Events;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  Inspector 导出属性、静态属性、只读属性、事件
    // ══════════════════════════════════════════════════════════════
    public partial class CefGlueControl
    {
        // ══════════════════════════════════════════════════════════════
        //  Browser Settings
        // ══════════════════════════════════════════════════════════════

        [ExportGroup("Browser Settings")]

        /// <summary>
        /// The URL to load when the browser is created.
        /// </summary>
        [Export]
        public string InitialUrl { get; set; } = "about:blank";

        /// <summary>
        /// CEF 缓存目录。使用 Godot 路径格式，如 user://cef_cache。
        /// 浏览器缓存、Cookie、LocalStorage 等数据存储在此目录。
        /// 需在浏览器初始化前设置。
        /// </summary>
        [Export]
        public string CacheDirectory { get; set; } = "user://cef_cache";

        private RenderMode _mode = RenderMode.OSR;

        /// <summary>
        /// Rendering mode. OSR renders to a Godot texture with alpha transparency support.
        /// EmbeddedWindow renders directly to a child HWND for better video/WebGL performance.
        /// Must be set before the browser is created.
        /// </summary>
        [Export]
        public RenderMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                NotifyPropertyListChanged();
            }
        }

        /// <summary>
        /// Browser frame rate in frames per second. Range 1-360. Default 60.
        /// Only applies to OSR mode.
        /// </summary>
        [Export(PropertyHint.Range, "1,360")]
        public int FrameRate { get; set; } = 60;

        /// <summary>
        /// Enables transparent background. Only works in OSR mode.
        /// </summary>
        [Export]
        public bool Transparent { get; set; } = false;

        // ══════════════════════════════════════════════════════════════
        //  Feature Toggles
        // ══════════════════════════════════════════════════════════════

        [ExportGroup("Feature Toggles")]

        /// <summary>
        /// Enables GPU hardware acceleration.
        /// </summary>
        [Export]
        public bool GpuAcceleration { get; set; } = true;

        /// <summary>
        /// If true, popup windows navigate in the current browser instead of opening new windows.
        /// </summary>
        [Export]
        public bool OpenPopupInCurrentBrowser { get; set; } = false;

        /// <summary>
        /// If true, the mouse cursor changes to match web content (e.g. I-beam, hand).
        /// </summary>
        [Export]
        public bool SyncCursor { get; set; } = false;

        /// <summary>
        /// If true, enables media stream access (microphone, camera).
        /// When true, pages using getUserMedia() will be granted permission automatically
        /// via CefPermissionHandler. When false, media requests are denied.
        /// </summary>
        [Export]
        public bool EnableMediaStream { get; set; } = false;

        /// <summary>
        /// Enables right-click context menu in OSR mode. When true, a Godot
        /// <c>PopupMenu</c> is shown on right-click; subscribe to
        /// <see cref="BeforeContextMenu"/> to customize the menu and
        /// <see cref="ContextMenuCommand"/> to handle selections. When false
        /// (default), right-clicks are forwarded to the web page and no menu
        /// is shown. Only effective when <see cref="Mode"/> = <see cref="RenderMode.OSR"/>.
        /// </summary>
        [Export]
        public bool ContextMenuEnabled { get; set; } = false;

        // ══════════════════════════════════════════════════════════════
        //  Embedded Mode (only applies when Mode=EmbeddedWindow)
        // ══════════════════════════════════════════════════════════════

        [ExportGroup("Embedded Mode")]

        private bool _forwardInputEvents;

        /// <summary>
        /// Forward browser input events to Godot via JS IPC.
        /// When enabled, mouse/keyboard events inside the browser are forwarded
        /// to the Godot event system. Default disabled — browser handles input natively.
        /// Only effective when Mode=EmbeddedWindow.
        /// </summary>
        [Export]
        public bool ForwardInputEvents
        {
            get => _forwardInputEvents;
            set
            {
                _forwardInputEvents = value;
                NotifyPropertyListChanged();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  静态属性（CEF 初始化前设置）
        // ══════════════════════════════════════════════════════════════

        private static bool _useGpuAcceleration = true;
        private static bool _useTransparent = false;
        private static RenderMode _activeRenderMode = RenderMode.OSR;

        /// <summary>
        /// Gets or sets the global GPU acceleration setting. Must be set before CEF initialization.
        /// </summary>
        public static bool UseGpuAcceleration
        {
            get => _useGpuAcceleration;
            set => _useGpuAcceleration = value;
        }

        /// <summary>
        /// Gets or sets the global transparency setting. Must be set before CEF initialization.
        /// </summary>
        public static bool UseTransparent
        {
            get => _useTransparent;
            set => _useTransparent = value;
        }

        /// <summary>
        /// Gets or sets the global rendering mode. Must be set before CEF initialization.
        /// </summary>
        public static RenderMode ActiveRenderMode
        {
            get => _activeRenderMode;
            set => _activeRenderMode = value;
        }

        // ══════════════════════════════════════════════════════════════
        //  只读属性
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets or sets the current URL of the browser.
        /// Setting this property navigates the browser to the specified URL.
        /// </summary>
        public string Address
        {
            get => _browser?.GetMainFrame()?.Url ?? InitialUrl;
            set
            {
                if (_browser != null && _browser.GetMainFrame() != null)
                {
                    _browser.GetMainFrame().LoadUrl(value);
                }
                else
                {
                    InitialUrl = value;
                }
            }
        }

        /// <summary>
        /// Gets whether the browser has been initialized.
        /// </summary>
        public bool IsBrowserInitialized => _browser != null;

        /// <summary>
        /// Gets whether the browser is currently loading a page.
        /// </summary>
        public bool IsLoading => _browser?.IsLoading ?? false;

        /// <summary>
        /// Gets the current page title.
        /// </summary>
        public string Title { get; private set; }

        // ══════════════════════════════════════════════════════════════
        //  事件
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Raised when the browser has been initialized.
        /// </summary>
        public event Action BrowserInitialized;

        /// <summary>
        /// Raised when the browser address changes.
        /// </summary>
        public event AddressChangedEventHandler AddressChanged;

        /// <summary>
        /// Raised when the page title changes.
        /// </summary>
        public event TitleChangedEventHandler TitleChanged;

        /// <summary>
        /// Raised when a page starts loading.
        /// </summary>
        public event LoadStartEventHandler LoadStart;

        /// <summary>
        /// Raised when a page finishes loading.
        /// </summary>
        public event LoadEndEventHandler LoadEnd;

        /// <summary>
        /// Raised when a page fails to load.
        /// </summary>
        public event LoadErrorEventHandler LoadError;

        /// <summary>
        /// JS → C# 桥接请求事件。JS 调用 window.__hostBridge.send({type:'...', payload:{}}) 时触发。
        /// 参数: (type, payload, cbId) — cbId 可能为 null(无回调) 或字符串(需通过 SendResponse 回复)。
        /// </summary>
        public event Action<string, string, string> BridgeRequest;

        /// <summary>
        /// 浏览器请求打开新窗口/新标签时触发。参数为目标 URL。
        /// 需在 OnBeforePopup 中拦截并触发此事件，由上层 UI（如多标签 demo）创建新标签。
        /// </summary>
        public event Action<string, bool> NewWindowRequested;

        internal void RaiseNewWindowRequested(string url, bool isNewWindow) => NewWindowRequested?.Invoke(url, isNewWindow);
        internal bool HasNewWindowSubscribers => NewWindowRequested != null;

        // ── 右键菜单事件（OSR 模式） ──

        /// <summary>
        /// 右键菜单即将显示时触发。可在事件处理中修改 <paramref name="model"/>
        /// （清空、添加、移除项）来定制菜单内容。不订阅则显示 CEF 默认菜单。
        /// 在 Godot 主线程触发（已从 CEF UI 线程 Marshal 过来）。
        /// </summary>
        /// <remarks>
        /// 参数: (model, params) — model 为 <see cref="Xilium.CefGlue.CefMenuModel"/> 快照，
        /// params 为 <see cref="ContextMenuParams"/>（CEF 原生 CefContextMenuParams 的安全副本）。
        /// </remarks>
        public event Action<ContextMenuModel, ContextMenuParams> BeforeContextMenu;

        /// <summary>
        /// 右键菜单命令被选中时触发。参数: (commandId, parameters, eventFlags)。
        /// 返回 true 表示已处理；返回 false 让 CEF 应用默认行为（对内置 ID 有效）。
        /// 在 Godot 主线程触发。
        /// </summary>
        public event Func<int, ContextMenuParams, CefEventFlags, bool> ContextMenuCommand;

        internal void RaiseBeforeContextMenu(ContextMenuModel model, ContextMenuParams parameters)
            => BeforeContextMenu?.Invoke(model, parameters);

        internal bool RaiseContextMenuCommand(int commandId, ContextMenuParams parameters, CefEventFlags eventFlags)
        {
            var handler = ContextMenuCommand;
            return handler != null && handler(commandId, parameters, eventFlags);
        }

        internal bool HasBeforeContextMenuSubscribers => BeforeContextMenu != null;
        internal bool HasContextMenuCommandSubscribers => ContextMenuCommand != null;
    }
}