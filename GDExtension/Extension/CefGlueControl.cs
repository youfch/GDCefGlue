using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
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

    public string InitialUrl { get; set; } = "about:blank";
    public bool OpenPopupInCurrentBrowser { get; set; } = true;
    public bool GpuAcceleration { get; set; } = true;
    public int FrameRate { get; set; } = 60;
    public bool Transparent { get; set; } = false;
    
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
    }

    internal void OnTitleChange(CefBrowser browser, string title)
    {
        Title = title;
        CallDeferred("_notify_title_changed", title);
    }

    private void NotifyTitleChanged(string title)
    {
        TitleChanged?.Invoke(this, title);
    }

    internal void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
    {
        CallDeferred("_notify_load_start");
    }

    private void NotifyLoadStart()
    {
        LoadStart?.Invoke(this, new LoadStartEventArgs(null));
    }

    internal void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
    {
        CallDeferred("_notify_load_end");
    }

    private void NotifyLoadEnd()
    {
        LoadEnd?.Invoke(this, new LoadEndEventArgs(null, 0));
    }

    internal void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
    {
        CallDeferred("_notify_load_error", errorText, failedUrl);
    }

    private void NotifyLoadError(string errorText, string failedUrl)
    {
        LoadError?.Invoke(this, new LoadErrorEventArgs(null, CefErrorCode.None, errorText, failedUrl));
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

    protected override void _Process(double delta)
    {
        _cachedGlobalPosition = GlobalPosition;

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

    protected override void _Draw()
    {
        if (_texture != null && _width > 0 && _height > 0)
        {
            if (Transparent)
            {
                DrawTextureRect(_texture, new Rect2(Vector2.Zero, _width, _height), false);
            }
            else
            {
                DrawTexture(_texture, Vector2.Zero);
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

    protected override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationResized:
                if (_browserHost != null && Size.X > 0 && Size.Y > 0)
                {
                    _controlWidth = (int)Size.X;
                    _controlHeight = (int)Size.Y;
                    _browserHost.WasResized();
                }
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

    public Task<T> EvaluateJavaScript<T>(string code, string url = "about:blank", int line = 1)
    {
        return Task.FromResult<T>(default);
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

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(InitialUrl)), VariantType.String)
            {
                Usage = PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.InitialUrl,
            static (CefGlueControl instance, string value) => instance.InitialUrl = value);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(OpenPopupInCurrentBrowser)), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.OpenPopupInCurrentBrowser,
            static (CefGlueControl instance, bool value) => instance.OpenPopupInCurrentBrowser = value);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(GpuAcceleration)), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.Default
            },
            static (CefGlueControl instance) => instance.GpuAcceleration,
            static (CefGlueControl instance, bool value) => instance.GpuAcceleration = value);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(FrameRate)), VariantType.Int)
            {
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
    }
}
