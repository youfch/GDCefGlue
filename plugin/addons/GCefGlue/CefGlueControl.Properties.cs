using System;
using Godot;
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
        public bool OpenPopupInCurrentBrowser { get; set; } = true;

        /// <summary>
        /// If true, the mouse cursor changes to match web content (e.g. I-beam, hand).
        /// </summary>
        [Export]
        public bool SyncCursor { get; set; } = false;

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
        /// JS → C# 桥接请求事件。JS 调用 window._godotBridge.sendToGodot(msg) 时触发。
        /// 参数: (type, payload, cbId) — cbId 可能为 null(无回调) 或字符串(需通过 SendResponse 回复)。
        /// </summary>
        public event Action<string, string, string> BridgeRequest;
    }
}