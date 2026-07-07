using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Godot.Bridge;
using Godot.Collections;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Events;

namespace GDCefGlueExtension;

public partial class CefGlueControl : Control
{
    private CefBrowser _browser;
    private CefBrowserHost _browserHost;
    private CefClient _client;
    private Image _image;
    private ImageTexture _texture;
    private byte[] _pixelBuffer;
    private PackedByteArray _packedBuffer;
    private readonly object _bufferLock = new object();
    internal int _width;
    internal int _height;
    internal int _controlWidth;
    internal int _controlHeight;
    internal Vector2 _cachedGlobalPosition;
    private bool _isFocused;
    private bool _browserCreated;
    private bool _isDirty;
    private CefMouseButtonType _pressedButton = (CefMouseButtonType)(-1);
    private bool _isMousePressed;
    private double _lastClickTime;
    private int _clickCount;
    private const double DoubleClickInterval = 0.5;
    private int _pendingWidth;
    private int _pendingHeight;
    private int _resizeStableCount;
    private const int ResizeStableThreshold = 2;

    // ── IPC / JS bridge ────────────────────────────────────────────────
    private int _lastEvalTaskId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pendingEvals = new();
    private readonly Dictionary<string, Callable> _jsHandlers = new();

    /// <summary>
    /// C# 快速路径: 订阅后 JS 方法调用直接走 C# delegate, 不经过 Godot Callable.
    /// 参数: (objectName, method, argsJson, replyCallback)
    /// </summary>
    public event Action<string, string, string, Action<string>> NativeCall;

    /// <summary>
    /// JS → C# 桥接请求事件 (godot://bridge URL 拦截).
    /// 参数: (type, payload, cbId)
    /// </summary>
    public event Action<string, string, string> BridgeRequest;

    // ── Inspector properties (registered via BindMembers) ──────────
    public string InitialUrl { get; set; } = "about:blank";
    public int FrameRate { get; set; } = 60;
    public bool Transparent { get; set; } = false;
    public bool GpuAcceleration { get; set; } = true;
    public bool OpenPopupInCurrentBrowser { get; set; } = true;
    public bool SyncCursor { get; set; } = false;

    private static bool _useGpuAcceleration = true;
    private static bool _useTransparent = false;
    
    public static bool UseGpuAcceleration 
    { 
        get => _useGpuAcceleration;
        set => _useGpuAcceleration = value;
    }
    
    public static bool UseTransparent 
    { 
        get => _useTransparent;
        set => _useTransparent = value;
    }

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

    public bool IsBrowserInitialized => _browser != null;
    public bool IsLoading => _browser?.IsLoading ?? false;
    public string Title { get; private set; }

    public event Action BrowserInitialized;
    public event AddressChangedEventHandler AddressChanged;
    public event TitleChangedEventHandler TitleChanged;
    public event LoadStartEventHandler LoadStart;
    public event LoadEndEventHandler LoadEnd;
    public event LoadErrorEventHandler LoadError;

    public CefGlueControl()
    {
        GD.Print("CefGlueControl: Constructor called");
    }

    protected override void _Ready()
    {
        GD.Print("CefGlueControl: _Ready() called");
        
        if (Godot.Engine.Singleton.IsEditorHint())
        {
            GD.Print("CefGlueControl: Running in editor, skipping CEF initialization");
            return;
        }
        
        UseGpuAcceleration = GpuAcceleration;
        UseTransparent = Transparent;
        CefInitializer.Initialize();

        CustomMinimumSize = new Vector2(100, 100);
        FocusMode = FocusModeEnum.Click;

        _image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);

        CallDeferred("_create_browser_deferred");
    }
    
    private void ActivateIme()
    {
        var window = GetWindow();
        if (window != null && HasFocus())
        {
            window.SetImeActive(true);
        }
    }
    
    private void DeactivateIme()
    {
        var window = GetWindow();
        if (window != null)
        {
            window.SetImeActive(false);
        }
    }

    internal void OnCursorChanged(CefCursorType type)
    {
        if (!SyncCursor)
            return;
        CallDeferred(nameof(UpdateCursorShape), (int)type);
    }

    private void UpdateCursorShape(int cefCursorType)
    {
        var shape = cefCursorType switch
        {
            (int)CefCursorType.IBeam => Control.CursorShape.Ibeam,
            (int)CefCursorType.Hand => Control.CursorShape.PointingHand,
            (int)CefCursorType.Cross => Control.CursorShape.Cross,
            (int)CefCursorType.Wait or
            (int)CefCursorType.Progress => Control.CursorShape.Wait,
            (int)CefCursorType.Help => Control.CursorShape.Help,
            (int)CefCursorType.NotAllowed => Control.CursorShape.Forbidden,
            (int)CefCursorType.NorthSouthResize or
            (int)CefCursorType.NorthResize or
            (int)CefCursorType.SouthResize or
            (int)CefCursorType.RowResize => Control.CursorShape.Vsize,
            (int)CefCursorType.EastWestResize or
            (int)CefCursorType.EastResize or
            (int)CefCursorType.WestResize or
            (int)CefCursorType.ColumnResize => Control.CursorShape.Hsize,
            (int)CefCursorType.Move => Control.CursorShape.Move,
            _ => Control.CursorShape.Arrow,
        };
        MouseDefaultCursorShape = (Control.CursorShape)shape;
    }

    private void CreateBrowserDeferred()
    {
        if (_browserCreated)
            return;

        var size = Size;
        if (size.X > 0 && size.Y > 0)
        {
            _browserCreated = true;
            CreateBrowser((int)size.X, (int)size.Y);
        }
    }

    private void CreateBrowser(int width, int height)
    {
        _width = width;
        _height = height;
        _controlWidth = width;
        _controlHeight = height;

        var frameRate = Math.Clamp(FrameRate, 1, 360);
        GD.Print($"CefGlueControl: Creating browser {width}x{height} @ {frameRate}fps (Transparent: {Transparent})");

        var windowInfo = CefWindowInfo.Create();
        windowInfo.SetAsWindowless(IntPtr.Zero, Transparent);

        var settings = new CefBrowserSettings
        {
            WindowlessFrameRate = frameRate
        };

        _client = new GodotCefClient(this);

        try
        {
            CefBrowserHost.CreateBrowser(windowInfo, _client, settings, InitialUrl);
            GD.Print($"CefGlueControl: Browser creation initiated for {InitialUrl}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CefGlueControl: Failed to create browser - {ex.Message}");
        }
    }

    internal void OnBrowserCreated(CefBrowser browser)
    {
        if (_browser != null)
        {
            GD.Print($"CefGlueControl: Ignoring popup browser creation");
            return;
        }
        
        _browser = browser;
        _browserHost = browser.GetHost();
        CallDeferred("_notify_browser_initialized");
    }

    private void NotifyBrowserInitialized()
    {
        BrowserInitialized?.Invoke();
        EmitSignal(new StringName(nameof(BrowserInitialized)));
        GD.Print("CefGlueControl: Browser initialized");
    }

    internal void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
    {
        if (frame.IsMain)
        {
            CallDeferred("_notify_address_changed", url);
        }
    }

    private void NotifyAddressChanged(string url)
    {
        AddressChanged?.Invoke(this, url);
        EmitSignal(new StringName(nameof(AddressChanged)), url);
    }

    internal void OnTitleChange(CefBrowser browser, string title)
    {
        Title = title;
        CallDeferred("_notify_title_changed", title);
    }

    private void NotifyTitleChanged(string title)
    {
        TitleChanged?.Invoke(this, title);
        EmitSignal(new StringName(nameof(TitleChanged)), title);
    }

    internal void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
    {
        CallDeferred("_notify_load_start");
    }

    private void NotifyLoadStart()
    {
        LoadStart?.Invoke(this, new LoadStartEventArgs(null));
        EmitSignal(new StringName(nameof(LoadStart)));
    }

    internal void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
    {
        CallDeferred("_notify_load_end");
    }

    private void NotifyLoadEnd()
    {
        LoadEnd?.Invoke(this, new LoadEndEventArgs(null, 0));
        EmitSignal(new StringName(nameof(LoadEnd)));
    }

    internal void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
    {
        CallDeferred("_notify_load_error", errorText, failedUrl);
    }

    private void NotifyLoadError(string errorText, string failedUrl)
    {
        LoadError?.Invoke(this, new LoadErrorEventArgs(null, CefErrorCode.None, errorText, failedUrl));
        EmitSignal(new StringName(nameof(LoadError)), errorText, failedUrl);
    }

    internal void OnPaint(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects)
    {
        if (width <= 0 || height <= 0) return;
        
        int bufferSize = width * height * 4;
        
        lock (_bufferLock)
        {
            _width = width;
            _height = height;
            
            if (_pixelBuffer == null || _pixelBuffer.Length != bufferSize)
            {
                _pixelBuffer = new byte[bufferSize];
            }

            Marshal.Copy(buffer, _pixelBuffer, 0, bufferSize);
            ConvertBgraToRgba(_pixelBuffer, width * height);
            _isDirty = true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConvertBgraToRgba(byte[] buffer, int pixelCount)
    {
        if (Ssse3.IsSupported)
        {
            int vectorSize = 16;
            int vectorCount = pixelCount / 4;
            
            var shuffleMask = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
            
            fixed (byte* ptr = buffer)
            {
                for (int i = 0; i < vectorCount; i++)
                {
                    int offset = i * vectorSize;
                    var data = Sse2.LoadVector128(ptr + offset);
                    var shuffled = Ssse3.Shuffle(data, shuffleMask);
                    Sse2.Store(ptr + offset, shuffled);
                }
                
                for (int i = vectorCount * 4; i < pixelCount; i++)
                {
                    int offset = i * 4;
                    byte b = ptr[offset];
                    ptr[offset] = ptr[offset + 2];
                    ptr[offset + 2] = b;
                }
            }
        }
        else
        {
            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * 4;
                byte b = buffer[offset];
                buffer[offset] = buffer[offset + 2];
                buffer[offset + 2] = b;
            }
        }
    }

    /// <summary>
    /// Called every frame. Updates the texture with new pixel data and handles browser creation.
    /// Implements a delayed resize mechanism to prevent flickering during rapid window resizing.
    /// </summary>
    protected override void _Process(double delta)
    {
        _cachedGlobalPosition = GlobalPosition;

        if (_browserHost != null && Size.X > 0 && Size.Y > 0)
        {
            int newWidth = (int)Size.X;
            int newHeight = (int)Size.Y;
            
            if (newWidth != _controlWidth || newHeight != _controlHeight)
            {
                _controlWidth = newWidth;
                _controlHeight = newHeight;
                _pendingWidth = newWidth;
                _pendingHeight = newHeight;
                _resizeStableCount = 0;
                QueueRedraw();
            }
            else if (_pendingWidth > 0 && _pendingHeight > 0)
            {
                _resizeStableCount++;
                if (_resizeStableCount >= ResizeStableThreshold)
                {
                    _browserHost.WasResized();
                    _browserHost.Invalidate(CefPaintElementType.View);
                    _pendingWidth = 0;
                    _pendingHeight = 0;
                }
            }
            else if (_width != _controlWidth || _height != _controlHeight)
            {
                _browserHost.Invalidate(CefPaintElementType.View);
            }
        }

        if (_isDirty && _pixelBuffer != null && _width > 0 && _height > 0)
        {
            int expectedBufferSize = _width * _height * 4;
            if (_pixelBuffer.Length == expectedBufferSize)
            {
                if (_packedBuffer == null || _packedBuffer.Count != expectedBufferSize)
                {
                    _packedBuffer = new PackedByteArray();
                    _packedBuffer.Resize(expectedBufferSize);
                }
                
                lock (_bufferLock)
                {
                    _packedBuffer.Clear();
                    _packedBuffer.AddRange(_pixelBuffer);
                }
                
                if (_texture.GetSize().X != _width || _texture.GetSize().Y != _height)
                {
                    _image.SetData(_width, _height, false, Image.Format.Rgba8, _packedBuffer);
                    _texture = ImageTexture.CreateFromImage(_image);
                }
                else
                {
                    _image.SetData(_width, _height, false, Image.Format.Rgba8, _packedBuffer);
                    _texture.Update(_image);
                }
                QueueRedraw();
            }
            _isDirty = false;
        }

        if (!_browserCreated && Size.X > 0 && Size.Y > 0)
        {
            CreateBrowserDeferred();
        }
    }

    /// <summary>
    /// Called when the control needs to be redrawn. Draws the browser texture.
    /// Uses control size for drawing to ensure proper scaling during resize operations.
    /// </summary>
    protected override void _Draw()
    {
        if (_texture != null && _controlWidth > 0 && _controlHeight > 0)
        {
            if (Transparent)
            {
                DrawTextureRect(_texture, new Rect2(Vector2.Zero, _controlWidth, _controlHeight), false);
            }
            else
            {
                if (_width == _controlWidth && _height == _controlHeight)
                {
                    DrawTexture(_texture, Vector2.Zero);
                }
                else
                {
                    DrawTextureRect(_texture, new Rect2(Vector2.Zero, _controlWidth, _controlHeight), false);
                }
            }
        }
    }

    protected override void _GuiInput(InputEvent @event)
    {
        if (_browserHost == null)
            return;

        switch (@event)
        {
            case InputEventMouseMotion mouseMotion:
                SendMouseMoveEvent(mouseMotion);
                break;
            case InputEventMouseButton mouseButton:
                SendMouseButtonEvent(mouseButton);
                break;
            case InputEventKey key:
                SendKeyEvent(key);
                break;
        }
    }

    private void SendMouseMoveEvent(InputEventMouseMotion e)
    {
        if (_browserHost == null) return;
        
        var localPos = GetLocalMousePosition();
        var modifiers = GetModifiers(e);
        
        if (_isMousePressed && _pressedButton != (CefMouseButtonType)(-1))
        {
            modifiers |= GetMouseButtonModifier(_pressedButton);
        }
        
        var mouseEvent = new CefMouseEvent
        {
            X = (int)localPos.X,
            Y = (int)localPos.Y,
            Modifiers = modifiers
        };
        
        _browserHost.SendMouseMoveEvent(mouseEvent, false);
    }
    
    private CefEventFlags GetMouseButtonModifier(CefMouseButtonType button)
    {
        return button switch
        {
            CefMouseButtonType.Left => CefEventFlags.LeftMouseButton,
            CefMouseButtonType.Right => CefEventFlags.RightMouseButton,
            CefMouseButtonType.Middle => CefEventFlags.MiddleMouseButton,
            _ => CefEventFlags.None
        };
    }

    private void SendMouseButtonEvent(InputEventMouseButton e)
    {
        if (_browserHost == null) return;
        
        var localPos = GetLocalMousePosition();
        var mouseEvent = new CefMouseEvent
        {
            X = (int)localPos.X,
            Y = (int)localPos.Y,
            Modifiers = GetModifiers(e)
        };

        var button = ConvertMouseButton(e.ButtonIndex);

        if (e.ButtonIndex == MouseButton.WheelUp || e.ButtonIndex == MouseButton.WheelDown)
        {
            int delta = e.ButtonIndex == MouseButton.WheelUp ? 120 : -120;
            _browserHost.SendMouseWheelEvent(mouseEvent, 0, delta);
            return;
        }

        if (button == (CefMouseButtonType)(-1))
            return;

        if (e.Pressed)
        {
            var currentTime = Godot.Time.Singleton.GetTicksMsec() / 1000.0;
            if (currentTime - _lastClickTime < DoubleClickInterval)
            {
                _clickCount++;
            }
            else
            {
                _clickCount = 1;
            }
            _lastClickTime = currentTime;
            
            _pressedButton = button;
            _isMousePressed = true;
            _browserHost.SendMouseClickEvent(mouseEvent, button, false, _clickCount);
            GrabFocus();
            _browserHost?.SetFocus(true);
            ActivateIme();
        }
        else
        {
            _isMousePressed = false;
            _pressedButton = (CefMouseButtonType)(-1);
            _browserHost.SendMouseClickEvent(mouseEvent, button, true, 1);
        }
    }

    private void SendKeyEvent(InputEventKey e)
    {
        var windowsKeyCode = GetWindowsKeyCode(e.Keycode);
        
        var keyEvent = new CefKeyEvent
        {
            EventType = e.Pressed ? CefKeyEventType.KeyDown : CefKeyEventType.KeyUp,
            Modifiers = GetModifiers(e),
            WindowsKeyCode = windowsKeyCode,
            NativeKeyCode = (int)e.PhysicalKeycode,
            IsSystemKey = false
        };

        _browserHost.SendKeyEvent(keyEvent);

        if (e.Pressed && e.Unicode.Value != 0 && !IsSpecialKey(e.Keycode))
        {
            var charEvent = new CefKeyEvent
            {
                EventType = CefKeyEventType.Char,
                WindowsKeyCode = e.Unicode.Value,
                NativeKeyCode = e.Unicode.Value,
                Modifiers = GetModifiers(e),
                Character = (char)e.Unicode.Value
            };
            _browserHost.SendKeyEvent(charEvent);
        }
    }
    
    private int GetWindowsKeyCode(Key keycode)
    {
        return keycode switch
        {
            Key.Backspace => 0x08,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.Shift => 0x10,
            Key.Ctrl => 0x11,
            Key.Alt => 0x12,
            Key.Pause => 0x13,
            Key.Capslock => 0x14,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.Pageup => 0x21,
            Key.Pagedown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.F1 => 0x70,
            Key.F2 => 0x71,
            Key.F3 => 0x72,
            Key.F4 => 0x73,
            Key.F5 => 0x74,
            Key.F6 => 0x75,
            Key.F7 => 0x76,
            Key.F8 => 0x77,
            Key.F9 => 0x78,
            Key.F10 => 0x79,
            Key.F11 => 0x7A,
            Key.F12 => 0x7B,
            Key.Numlock => 0x90,
            Key.Scrolllock => 0x91,
            _ => (int)keycode
        };
    }
    
    private bool IsSpecialKey(Key keycode)
    {
        return keycode switch
        {
            Key.Backspace => true,
            Key.Tab => true,
            Key.Enter => true,
            Key.Escape => true,
            Key.Delete => true,
            Key.Insert => true,
            Key.Home => true,
            Key.End => true,
            Key.Pageup => true,
            Key.Pagedown => true,
            Key.Left => true,
            Key.Right => true,
            Key.Up => true,
            Key.Down => true,
            Key.F1 => true,
            Key.F2 => true,
            Key.F3 => true,
            Key.F4 => true,
            Key.F5 => true,
            Key.F6 => true,
            Key.F7 => true,
            Key.F8 => true,
            Key.F9 => true,
            Key.F10 => true,
            Key.F11 => true,
            Key.F12 => true,
            _ => false
        };
    }

    private CefEventFlags GetModifiers(InputEventWithModifiers e)
    {
        var modifiers = CefEventFlags.None;
        if (e.ShiftPressed) modifiers |= CefEventFlags.ShiftDown;
        if (e.CtrlPressed) modifiers |= CefEventFlags.ControlDown;
        if (e.AltPressed) modifiers |= CefEventFlags.AltDown;
        if (e.MetaPressed) modifiers |= CefEventFlags.AltGrDown;
        return modifiers;
    }

    private CefMouseButtonType ConvertMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => CefMouseButtonType.Left,
            MouseButton.Right => CefMouseButtonType.Right,
            MouseButton.Middle => CefMouseButtonType.Middle,
            _ => (CefMouseButtonType)(-1)
        };
    }

    /// <summary>
    /// Handles notifications from Godot such as resize, mouse exit, and focus changes.
    /// Note: Resize handling is done in _Process for smoother resizing experience.
    /// </summary>
    protected override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationResized:
                break;

            case NotificationMouseExit:
                if (_browserHost != null)
                {
                    var mouseEvent = new CefMouseEvent { X = 0, Y = 0, Modifiers = CefEventFlags.None };
                    _browserHost.SendMouseMoveEvent(mouseEvent, true);
                    _isMousePressed = false;
                    _pressedButton = (CefMouseButtonType)(-1);
                }
                break;

            case NotificationFocusEnter:
                _isFocused = true;
                _browserHost?.SetFocus(true);
                ActivateIme();
                break;

            case NotificationFocusExit:
                _isFocused = false;
                _browserHost?.SetFocus(false);
                DeactivateIme();
                break;
        }
    }

    public void GoBack()
    {
        if (_browser?.CanGoBack == true)
            _browser.GoBack();
    }

    public void GoForward()
    {
        if (_browser?.CanGoForward == true)
            _browser.GoForward();
    }

    public void NavigateToUrl(string url)
    {
        if (_browser != null && _browser.GetMainFrame() != null)
        {
            _browser.GetMainFrame().LoadUrl(url);
        }
    }

    public void Reload(bool ignoreCache = false)
    {
        if (_browser != null)
        {
            if (ignoreCache)
                _browser.ReloadIgnoreCache();
            else
                _browser.Reload();
        }
    }

    public void ExecuteJavaScript(string code, string url = "about:blank", int line = 1)
    {
        _browser?.GetMainFrame()?.ExecuteJavaScript(code, url, line);
    }

    // ── EvalJs (AOT 安全, 通过信号返回) ───────────────────────────────

    /// <summary>
    /// 异步执行 JS 代码, 结果通过 eval_completed(result, error) 信号返回.
    /// GDScript 中用 await $Browser.eval_completed 接收.
    /// </summary>
    public void EvalJs(string code)
    {
        _ = EvalJsAsync(code);
    }

    private async Task EvalJsAsync(string code)
    {
        string result = null;
        string error = null;
        try
        {
            result = await InternalEvalRaw($"return {code};");
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        CallDeferred(nameof(OnEvalDone), result ?? "", error ?? "");
    }

    private void OnEvalDone(string result, string error)
    {
        EmitSignal(new StringName("eval_completed"), result, error);
    }

    /// <summary>
    /// 内部: 发送 JS 求值请求, 返回原始 JSON 字符串.
    /// 无泛型, 无反射, AOT 安全.
    /// </summary>
    private Task<string> InternalEvalRaw(string code)
    {
        var frame = _browser?.GetMainFrame();
        if (frame == null)
            return Task.FromResult<string>(null);

        var taskId = Interlocked.Increment(ref _lastEvalTaskId);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingEvals.TryAdd(taskId, tcs);

        var msg = CefProcessMessage.Create("JsEvaluationRequest");
        using (var args = msg.Arguments)
        {
            args.SetInt(0, taskId);
            args.SetString(1, code);
            args.SetString(2, "about:blank");
            args.SetInt(3, 1);
        }
        frame.SendProcessMessage(CefProcessId.Renderer, msg);

        return tcs.Task;
    }

    // ── RegisterJsHandler (GDScript Callable 派发) ────────────────────

    /// <summary>
    /// 注册 GDScript Callable 处理 JS 方法调用.
    /// Callable 签名: handler(method: String, argsJson: String, reply: Callable)
    /// </summary>
    public void RegisterJsHandler(string name, Callable handler)
    {
        if (_browser == null || _browser.GetMainFrame() == null)
        {
            GD.PrintErr("[CefGlueControl] Cannot register handler: browser not initialized");
            return;
        }

        _jsHandlers[name] = handler;

        // 通知 BrowserProcess 创建 V8 绑定.
        // 方法名列表传空数组, 因为 BrowserProcess 只需要知道对象名.
        var msg = CefProcessMessage.Create("NativeObjectRegistrationRequest");
        using (var args = msg.Arguments)
        {
            args.SetString(0, name);
            args.SetString(1, "[]");
        }
        _browser.GetMainFrame().SendProcessMessage(CefProcessId.Renderer, msg);

        GD.Print($"[CefGlueControl] Registered JS handler '{name}'");
    }

    /// <summary>
    /// 注销 JS handler.
    /// </summary>
    public void UnregisterJsHandler(string name)
    {
        _jsHandlers.Remove(name);

        if (_browser?.GetMainFrame() != null)
        {
            var msg = CefProcessMessage.Create("NativeObjectUnregistrationRequest");
            using (var args = msg.Arguments)
            {
                args.SetString(0, name);
            }
            _browser.GetMainFrame().SendProcessMessage(CefProcessId.Renderer, msg);
        }
    }

    // ── C# → JS 推送 ──────────────────────────────────────────────────

    /// <summary>
    /// C# → JS 推送消息. JS 侧通过 window._godotBridge._onMessage(json) 接收.
    /// </summary>
    public void SendToJs(string jsonMessage)
    {
        if (_browser == null || _browser.GetMainFrame() == null)
        {
            GD.PrintErr("[CefGlueControl] Cannot send to JS: browser not initialized");
            return;
        }

        var escaped = jsonMessage
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");

        var jsCode = $"window._godotBridge && window._godotBridge._onMessage(\"{escaped}\");";
        _browser.GetMainFrame().ExecuteJavaScript(jsCode, "godot://send", 1);
    }

    /// <summary>
    /// C# → JS 回复特定请求. JS 侧通过 window._godotBridge._onResponse(cbId, json) 接收.
    /// </summary>
    public void SendResponse(string cbId, string jsonResponse)
    {
        if (string.IsNullOrEmpty(cbId)) return;
        if (_browser == null || _browser.GetMainFrame() == null)
        {
            GD.PrintErr("[CefGlueControl] Cannot send response: browser not initialized");
            return;
        }

        var escaped = jsonResponse
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");

        var jsCode = $"window._godotBridge && window._godotBridge._onResponse('{cbId}',\"{escaped}\");";
        _browser.GetMainFrame().ExecuteJavaScript(jsCode, "godot://response", 1);
    }

    /// <summary>
    /// 内部: GodotRequestHandler 调用, 解析 godot://bridge URL 并转发到 BridgeRequest 事件.
    /// </summary>
    internal void OnBridgeRequest(string url)
    {
        try
        {
            var uri = new System.Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            string type = query.Get("type") ?? "";
            string cbId = query.Get("cb");
            string payloadStr = query.Get("payload") ?? "";

            GD.Print($"[CefGlueControl] Bridge request: type={type}, cb={cbId ?? "none"}, payloadLen={payloadStr.Length}");

            BridgeRequest?.Invoke(type, payloadStr, cbId);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CefGlueControl] Failed to parse bridge URL '{url}': {ex.Message}");
        }
    }

    // ── IPC 消息派发 (由 GodotCefClient.OnProcessMessageReceived 调用) ──

    internal void HandleProcessMessage(CefProcessMessage message)
    {
        switch (message.Name)
        {
            case "JsEvaluationResult":
                HandleJsEvaluationResult(message);
                break;

            case "NativeObjectCallRequest":
                HandleNativeObjectCallRequest(message);
                break;

            // JsContextCreated / JsContextReleased / UnhandledException — 忽略
        }
    }

    private void HandleJsEvaluationResult(CefProcessMessage message)
    {
        int taskId;
        bool success;
        string resultJson;
        string exception;

        using (var args = message.Arguments)
        {
            taskId = args.GetInt(0);
            success = args.GetBool(1);
            resultJson = args.GetString(2);
            exception = args.GetString(3);
        }

        if (_pendingEvals.TryRemove(taskId, out var tcs))
        {
            if (success)
                tcs.TrySetResult(resultJson);
            else
                tcs.TrySetException(new Exception(exception ?? "Unknown JS error"));
        }
    }

    private void HandleNativeObjectCallRequest(CefProcessMessage message)
    {
        int callId;
        string objectName;
        string memberName;
        string argsJson;

        using (var args = message.Arguments)
        {
            callId = args.GetInt(0);
            objectName = args.GetString(1);
            memberName = args.GetString(2);
            argsJson = args.GetString(3);
        }

        bool handled = false;

        // 1. 优先 C# event (最快, 零 Godot 开销)
        if (NativeCall != null)
        {
            handled = true;
            NativeCall(objectName, memberName, argsJson,
                result => SendNativeObjectCallResult(callId, result, null));
        }

        // 2. 后备 GDScript Callable
        if (!handled && _jsHandlers.TryGetValue(objectName, out var callable))
        {
            handled = true;
            var reply = Callable.From<string>(r => SendNativeObjectCallResult(callId, r, null));
            callable.Call(memberName, argsJson, reply);
        }

        if (!handled)
            SendNativeObjectCallResult(callId, null, $"No handler for '{objectName}'");
    }

    private void SendNativeObjectCallResult(int callId, string result, string errorMessage)
    {
        var frame = _browser?.GetMainFrame();
        if (frame == null) return;

        var msg = CefProcessMessage.Create("NativeObjectCallResult");
        using (var args = msg.Arguments)
        {
            args.SetInt(0, callId);

            if (errorMessage != null)
            {
                args.SetBool(1, false);
                args.SetString(2, null);
                args.SetString(3, errorMessage);
            }
            else
            {
                args.SetBool(1, true);
                args.SetString(2, result);
                args.SetString(3, null);
            }
        }
        frame.SendProcessMessage(CefProcessId.Renderer, msg);
    }

    public void ShowDeveloperTools()
    {
        var windowInfo = CefWindowInfo.Create();
        windowInfo.RuntimeStyle = CefRuntimeStyle.Chrome;
        _browserHost?.ShowDevTools(windowInfo, _client, new CefBrowserSettings(), new CefPoint());
    }

    public void CloseDeveloperTools()
    {
        _browserHost?.CloseDevTools();
    }

    protected override void _ExitTree()
    {
        if (_browserHost != null)
        {
            _browserHost.CloseBrowser(true);
            _browserHost = null;
            _browser = null;
        }
        _client = null;
        base._ExitTree();
    }

    internal static void BindMembers(ClassRegistrationContext context)
    {
        context.BindConstructor(() => new CefGlueControl());

        // ── Browser Settings ──
        context.AddPropertyGroup("Browser Settings");

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(InitialUrl)), VariantType.String)
            {
                Usage = PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.InitialUrl,
            static (CefGlueControl instance, string value) => instance.InitialUrl = value);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(FrameRate)), VariantType.Int)
            {
                Hint = PropertyHint.Range,
                HintString = "1,360",
                Usage = PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.FrameRate,
            static (CefGlueControl instance, int value) => instance.FrameRate = value);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(Transparent)), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.Transparent,
            static (CefGlueControl instance, bool value) => instance.Transparent = value);

        // ── Feature Toggles ──
        context.AddPropertyGroup("Feature Toggles");

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(GpuAcceleration)), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.GpuAcceleration,
            static (CefGlueControl instance, bool value) => instance.GpuAcceleration = value);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(OpenPopupInCurrentBrowser)), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.OpenPopupInCurrentBrowser,
            static (CefGlueControl instance, bool value) => instance.OpenPopupInCurrentBrowser = value);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(SyncCursor)), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.SyncCursor,
            static (CefGlueControl instance, bool value) => instance.SyncCursor = value);

        context.BindMethod(new StringName(nameof(GoBack)),
            static (CefGlueControl instance) =>
            {
                instance.GoBack();
            });

        context.BindMethod(new StringName(nameof(GoForward)),
            static (CefGlueControl instance) =>
            {
                instance.GoForward();
            });

        context.BindMethod(new StringName(nameof(NavigateToUrl)),
            new ParameterInfo(new StringName("url"), VariantType.String),
            static (CefGlueControl instance, string url) =>
            {
                instance.NavigateToUrl(url);
            });

        context.BindMethod(new StringName(nameof(Reload)),
            new ParameterInfo(new StringName("ignoreCache"), VariantType.Bool, VariantTypeMetadata.None, Variant.CreateFrom(false)),
            static (CefGlueControl instance, bool ignoreCache) =>
            {
                instance.Reload(ignoreCache);
            });

        context.BindMethod(new StringName(nameof(ExecuteJavaScript)),
            new ParameterInfo(new StringName("code"), VariantType.String),
            new ParameterInfo(new StringName("url"), VariantType.String, VariantTypeMetadata.None, Variant.CreateFrom("about:blank")),
            new ParameterInfo(new StringName("line"), VariantType.Int, VariantTypeMetadata.Int32, Variant.CreateFrom(1)),
            static (CefGlueControl instance, string code, string url, int line) =>
            {
                instance.ExecuteJavaScript(code, url, line);
            });

        context.BindMethod(new StringName(nameof(EvalJs)),
            new ParameterInfo(new StringName("code"), VariantType.String),
            static (CefGlueControl instance, string code) =>
            {
                instance.EvalJs(code);
            });

        context.BindMethod(new StringName(nameof(RegisterJsHandler)),
            new ParameterInfo(new StringName("name"), VariantType.String),
            new ParameterInfo(new StringName("handler"), VariantType.Callable),
            static (CefGlueControl instance, string name, Callable handler) =>
            {
                instance.RegisterJsHandler(name, handler);
            });

        context.BindMethod(new StringName(nameof(UnregisterJsHandler)),
            new ParameterInfo(new StringName("name"), VariantType.String),
            static (CefGlueControl instance, string name) =>
            {
                instance.UnregisterJsHandler(name);
            });

        context.BindMethod(new StringName(nameof(SendToJs)),
            new ParameterInfo(new StringName("jsonMessage"), VariantType.String),
            static (CefGlueControl instance, string jsonMessage) =>
            {
                instance.SendToJs(jsonMessage);
            });

        context.BindMethod(new StringName(nameof(SendResponse)),
            new ParameterInfo(new StringName("cbId"), VariantType.String),
            new ParameterInfo(new StringName("jsonResponse"), VariantType.String),
            static (CefGlueControl instance, string cbId, string jsonResponse) =>
            {
                instance.SendResponse(cbId, jsonResponse);
            });

        context.BindMethod(new StringName(nameof(ShowDeveloperTools)),
            static (CefGlueControl instance) =>
            {
                instance.ShowDeveloperTools();
            });

        context.BindMethod(new StringName(nameof(CloseDeveloperTools)),
            static (CefGlueControl instance) =>
            {
                instance.CloseDeveloperTools();
            });

        context.BindMethod(new StringName("_create_browser_deferred"),
            static (CefGlueControl instance) =>
            {
                instance.CreateBrowserDeferred();
            });

        context.BindMethod(new StringName("_notify_browser_initialized"),
            static (CefGlueControl instance) =>
            {
                instance.NotifyBrowserInitialized();
            });

        context.BindMethod(new StringName("_notify_address_changed"),
            new ParameterInfo(new StringName("url"), VariantType.String),
            static (CefGlueControl instance, string url) =>
            {
                instance.NotifyAddressChanged(url);
            });

        context.BindMethod(new StringName("_notify_title_changed"),
            new ParameterInfo(new StringName("title"), VariantType.String),
            static (CefGlueControl instance, string title) =>
            {
                instance.NotifyTitleChanged(title);
            });

        context.BindMethod(new StringName("_notify_load_start"),
            static (CefGlueControl instance) =>
            {
                instance.NotifyLoadStart();
            });

        context.BindMethod(new StringName("_notify_load_end"),
            static (CefGlueControl instance) =>
            {
                instance.NotifyLoadEnd();
            });

        context.BindMethod(new StringName("_notify_load_error"),
            new ParameterInfo(new StringName("errorText"), VariantType.String),
            new ParameterInfo(new StringName("failedUrl"), VariantType.String),
            static (CefGlueControl instance, string errorText, string failedUrl) =>
            {
                instance.NotifyLoadError(errorText, failedUrl);
            });

        // Signals
        context.BindSignal(new SignalInfo(new StringName(nameof(BrowserInitialized))));
        context.BindSignal(new SignalInfo(new StringName(nameof(AddressChanged))));
        context.BindSignal(new SignalInfo(new StringName(nameof(TitleChanged))));
        context.BindSignal(new SignalInfo(new StringName(nameof(LoadStart))));
        context.BindSignal(new SignalInfo(new StringName(nameof(LoadEnd))));
        context.BindSignal(new SignalInfo(new StringName(nameof(LoadError))));
        context.BindSignal(new SignalInfo(new StringName("eval_completed")));
        context.BindSignal(new SignalInfo(new StringName("bridge_request")));

        // Read-only properties
        context.BindProperty(
            new PropertyInfo(new StringName(nameof(Address)), VariantType.String)
            {
                Usage = PropertyUsageFlags.ReadOnly | PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.Address,
            static (CefGlueControl instance, string value) => { });

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(IsBrowserInitialized)), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.ReadOnly | PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.IsBrowserInitialized,
            static (CefGlueControl instance, bool value) => { });

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(IsLoading)), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.ReadOnly | PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.IsLoading,
            static (CefGlueControl instance, bool value) => { });

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(Title)), VariantType.String)
            {
                Usage = PropertyUsageFlags.ReadOnly | PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.Title,
            static (CefGlueControl instance, string value) => { });
    }
}
