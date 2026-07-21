using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Events;

namespace GDCefGlue
{
    /// <summary>
    /// Rendering mode for the CEF browser control.
    /// </summary>
    public enum RenderMode
    {
        /// <summary>
        /// Off-Screen Rendering: CEF renders to memory, Godot draws as a texture. Supports true alpha transparency.
        /// </summary>
        OSR = 0,
        /// <summary>
        /// Embedded Window: CEF renders directly to a child HWND. Better video/WebGL performance. No transparency.
        /// </summary>
        EmbeddedWindow = 1
    }

    /// <summary>
    /// A Godot Control that embeds a CEF browser. 
    /// Partial class split across files by responsibility:
    /// - Properties.cs: Export properties, static properties, read-only properties, events
    /// - Initialization.cs: CEF init, _Ready, _ExitTree, browser creation
    /// - Rendering.cs: OSR paint, _Process, _Draw, cursor
    /// - Input.cs: _GuiInput, mouse/key events, IME, _Notification
    /// - Bridge.cs: JS bridge, IPC, RegisterJavascriptObject, EvaluateJavaScript
    /// - Navigation.cs: GoBack/Forward, DevTools, CEF callbacks
    /// - Inspector.cs: _ValidateProperty
    /// - Events.cs: JS→Godot event forwarding (ForwardInputEvents)
    /// - Embedded.cs: Embedded window mode
    /// </summary>
    [Tool]
    [GlobalClass]
    public partial class CefGlueControl : Control
    {
        // ── CEF 核心对象 ──
        private CefBrowser _browser;
        private CefBrowserHost _browserHost;
        private CefClient _client;

        // ── OSR 纹理 ──
        private Image _image;
        private ImageTexture _texture;
        private byte[] _pixelBuffer;
        private byte[] _renderBuffer;
        private int _pixelBufferSize;
        private int _renderBufferSize;
        private SpinLock _spinLock = new SpinLock(false);
        internal int _width;
        internal int _height;
        internal int _controlWidth;
        internal int _controlHeight;
        internal Vector2 _cachedGlobalPosition;
        internal float _cachedContentScale = 1.0f;

        // ── 输入状态 ──
        private bool _isFocused;
        private bool _browserCreated;
        private bool _isDirty;
        private CefMouseButtonType _pressedButton = (CefMouseButtonType)(-1);
        private bool _isMousePressed;
        private double _lastClickTime;
        private int _clickCount;
        private const double DoubleClickInterval = 0.5;

        // ── Resize ──
        private int _pendingWidth;
        private int _pendingHeight;
        private int _resizeStableCount;
        private const int ResizeStableThreshold = 2;

        // ── 嵌入窗口模式 ──
        internal IntPtr _godotHwnd;
        internal IntPtr _cefChildHwnd;
        internal RenderMode _renderMode = RenderMode.OSR;
        private Vector2 _previousGlobalPos;
        private Vector2 _previousSize;
        private Vector2I _previousWindowPos;
        private float _previousContentScale = 1.0f;

        // ── IPC / JS bridge ──
        private int _lastEvalTaskId;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pendingEvals = new();
        private readonly ConcurrentDictionary<string, RegisteredObject> _registeredObjects = new();
        private bool _disposed;

        public CefGlueControl()
        {
        }
    }
}