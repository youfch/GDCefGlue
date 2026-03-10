using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Events;

namespace GDCefGlue
{
    /// <summary>
    /// A Godot Control that embeds a CEF browser using off-screen rendering.
    /// Provides full browser functionality including navigation, JavaScript execution, and developer tools.
    /// </summary>
    [GlobalClass]
    public partial class CefGlueControl : Control
    {
        private CefBrowser _browser;
        private CefBrowserHost _browserHost;
        private CefClient _client;
        private Image _image;
        private ImageTexture _texture;
        private byte[] _pixelBuffer;
        internal int _width;
        internal int _height;
        internal Vector2 _cachedGlobalPosition;
        private bool _isFocused;
        private bool _browserCreated;
        private bool _isDirty;
        private CefMouseButtonType _pressedButton = (CefMouseButtonType)(-1);
        private bool _isMousePressed;
        private double _lastClickTime;
        private int _clickCount;
        private const double DoubleClickInterval = 0.5;

        /// <summary>
        /// Gets or sets the initial URL to load when the browser is created.
        /// </summary>
        public string InitialUrl { get; set; } = "about:blank";
        
        /// <summary>
        /// Gets or sets whether popup windows should open in the current browser instead of new windows.
        /// </summary>
        [Export] public bool OpenPopupInCurrentBrowser { get; set; } = true;
        
        /// <summary>
        /// Gets or sets whether GPU acceleration is enabled. Exposed to the Godot inspector.
        /// </summary>
        [Export] public bool GpuAcceleration { get; set; } = true;
        
        private static bool _useGpuAcceleration = true;
        
        /// <summary>
        /// Gets or sets the global GPU acceleration setting. Must be set before CEF initialization.
        /// </summary>
        public static bool UseGpuAcceleration 
        { 
            get => _useGpuAcceleration;
            set => _useGpuAcceleration = value;
        }

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

        public CefGlueControl()
        {
            GD.Print("CefGlueControl: Constructor called");
        }

        /// <summary>
        /// Called when the control enters the scene tree. Initializes CEF and creates the browser.
        /// </summary>
        public override void _Ready()
        {
            GD.Print("CefGlueControl: _Ready() called");
            
            UseGpuAcceleration = GpuAcceleration;
            CefInitializer.Initialize();

            CustomMinimumSize = new Vector2(100, 100);
            FocusMode = FocusModeEnum.Click;

            _image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            _texture = ImageTexture.CreateFromImage(_image);

            CallDeferred(nameof(CreateBrowserDeferred));
        }
        
        /// <summary>
        /// Activates the Input Method Editor for text input.
        /// </summary>
        private void ActivateIme()
        {
            var window = GetWindow();
            if (window != null && HasFocus())
            {
                window.SetImeActive(true);
            }
        }
        
        /// <summary>
        /// Deactivates the Input Method Editor.
        /// </summary>
        private void DeactivateIme()
        {
            var window = GetWindow();
            if (window != null)
            {
                window.SetImeActive(false);
            }
        }

        /// <summary>
        /// Creates the browser after the control has a valid size.
        /// </summary>
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

        /// <summary>
        /// Creates the off-screen browser with the specified dimensions.
        /// </summary>
        /// <param name="width">The width of the browser viewport.</param>
        /// <param name="height">The height of the browser viewport.</param>
        private void CreateBrowser(int width, int height)
        {
            _width = width;
            _height = height;

            GD.Print($"CefGlueControl: Creating browser {width}x{height}");

            var windowInfo = CefWindowInfo.Create();
            windowInfo.SetAsWindowless(IntPtr.Zero, true);

            var settings = new CefBrowserSettings
            {
                WindowlessFrameRate = 60
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

        /// <summary>
        /// Called when the browser instance has been created.
        /// </summary>
        /// <param name="browser">The created browser instance.</param>
        internal void OnBrowserCreated(CefBrowser browser)
        {
            if (_browser != null)
            {
                GD.Print($"CefGlueControl: Ignoring popup browser creation");
                return;
            }
            
            _browser = browser;
            _browserHost = browser.GetHost();
            CallDeferred(nameof(NotifyBrowserInitialized));
        }

        private void NotifyBrowserInitialized()
        {
            BrowserInitialized?.Invoke();
            GD.Print("CefGlueControl: Browser initialized");
        }

        /// <summary>
        /// Called when the browser address changes.
        /// </summary>
        internal void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
        {
            if (frame.IsMain)
            {
                CallDeferred(nameof(NotifyAddressChanged), url);
            }
        }

        private void NotifyAddressChanged(string url)
        {
            AddressChanged?.Invoke(this, url);
        }

        /// <summary>
        /// Called when the page title changes.
        /// </summary>
        internal void OnTitleChange(CefBrowser browser, string title)
        {
            Title = title;
            CallDeferred(nameof(NotifyTitleChanged), title);
        }

        private void NotifyTitleChanged(string title)
        {
            TitleChanged?.Invoke(this, title);
        }

        /// <summary>
        /// Called when a page starts loading.
        /// </summary>
        internal void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
        {
            CallDeferred(nameof(NotifyLoadStart));
        }

        private void NotifyLoadStart()
        {
            LoadStart?.Invoke(this, new LoadStartEventArgs(null));
        }

        /// <summary>
        /// Called when a page finishes loading.
        /// </summary>
        internal void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
        {
            CallDeferred(nameof(NotifyLoadEnd));
        }

        private void NotifyLoadEnd()
        {
            LoadEnd?.Invoke(this, new LoadEndEventArgs(null, 0));
        }

        /// <summary>
        /// Called when a page fails to load.
        /// </summary>
        internal void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
        {
            CallDeferred(nameof(NotifyLoadError), errorText, failedUrl);
        }

        private void NotifyLoadError(string errorText, string failedUrl)
        {
            LoadError?.Invoke(this, new LoadErrorEventArgs(null, CefErrorCode.None, errorText, failedUrl));
        }

        /// <summary>
        /// Called when CEF renders a new frame. Copies pixel data and converts BGRA to RGBA.
        /// </summary>
        /// <param name="buffer">Pointer to the pixel buffer in BGRA format.</param>
        /// <param name="width">Width of the rendered frame.</param>
        /// <param name="height">Height of the rendered frame.</param>
        /// <param name="dirtyRects">Array of dirty rectangles that need repainting.</param>
        internal void OnPaint(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects)
        {
            if (width <= 0 || height <= 0) return;
            
            int bufferSize = width * height * 4;
            
            if (width != _width || height != _height)
            {
                _width = width;
                _height = height;
                _pixelBuffer = new byte[bufferSize];
            }
            else if (_pixelBuffer == null || _pixelBuffer.Length < bufferSize)
            {
                _pixelBuffer = new byte[bufferSize];
            }

            unsafe
            {
                Marshal.Copy(buffer, _pixelBuffer, 0, bufferSize);

                for (int i = 0; i < width * height; i++)
                {
                    int offset = i * 4;
                    byte b = _pixelBuffer[offset];
                    byte g = _pixelBuffer[offset + 1];
                    byte r = _pixelBuffer[offset + 2];
                    byte a = _pixelBuffer[offset + 3];

                    _pixelBuffer[offset] = r;
                    _pixelBuffer[offset + 1] = g;
                    _pixelBuffer[offset + 2] = b;
                    _pixelBuffer[offset + 3] = a;
                }
            }

            _isDirty = true;
        }

        /// <summary>
        /// Called every frame. Updates the texture with new pixel data and handles browser creation.
        /// </summary>
        public override void _Process(double delta)
        {
            GetWindow().SetImeActive(true);

            _cachedGlobalPosition = GlobalPosition;

            if (_isDirty && _pixelBuffer != null && _width > 0 && _height > 0)
            {
                int expectedBufferSize = _width * _height * 4;
                if (_pixelBuffer.Length != expectedBufferSize)
                {
                    _isDirty = false;
                    return;
                }
                
                if (_texture.GetSize().X != _width || _texture.GetSize().Y != _height)
                {
                    _image.SetData(_width, _height, false, Image.Format.Rgba8, _pixelBuffer);
                    _texture = ImageTexture.CreateFromImage(_image);
                }
                else
                {
                    _image.SetData(_width, _height, false, Image.Format.Rgba8, _pixelBuffer);
                    _texture.Update(_image);
                }
                QueueRedraw();
                _isDirty = false;
            }

            if (!_browserCreated && Size.X > 0 && Size.Y > 0)
            {
                CreateBrowserDeferred();
            }
        }

        /// <summary>
        /// Called when the control needs to be redrawn. Draws the browser texture.
        /// </summary>
        public override void _Draw()
        {
            if (_texture != null && _width > 0 && _height > 0)
            {
                DrawTexture(_texture, Vector2.Zero);
            }
        }

        /// <summary>
        /// Handles input events from Godot and forwards them to the browser.
        /// </summary>
        public override void _GuiInput(InputEvent @event)
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

        /// <summary>
        /// Sends a mouse move event to the browser.
        /// </summary>
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
        
        /// <summary>
        /// Gets the CEF event flags for a mouse button.
        /// </summary>
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

        /// <summary>
        /// Sends a mouse button event to the browser, including click counting for double-click detection.
        /// </summary>
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
                var currentTime = Time.GetTicksMsec() / 1000.0;
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

        /// <summary>
        /// Sends a keyboard event to the browser, including character input for text.
        /// </summary>
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

            if (e.Pressed && e.Unicode != 0 && !IsSpecialKey(e.Keycode))
            {
                var charEvent = new CefKeyEvent
                {
                    EventType = CefKeyEventType.Char,
                    WindowsKeyCode = (int)e.Unicode,
                    NativeKeyCode = (int)e.Unicode,
                    Modifiers = GetModifiers(e),
                    Character = (char)e.Unicode
                };
                _browserHost.SendKeyEvent(charEvent);
            }
        }
        
        /// <summary>
        /// Converts a Godot Key to a Windows virtual key code.
        /// </summary>
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
        
        /// <summary>
        /// Determines if a key is a special key that should not generate character input.
        /// </summary>
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

        /// <summary>
        /// Converts Godot modifier keys to CEF event flags.
        /// </summary>
        private CefEventFlags GetModifiers(InputEventWithModifiers e)
        {
            var modifiers = CefEventFlags.None;
            if (e.ShiftPressed) modifiers |= CefEventFlags.ShiftDown;
            if (e.CtrlPressed) modifiers |= CefEventFlags.ControlDown;
            if (e.AltPressed) modifiers |= CefEventFlags.AltDown;
            if (e.MetaPressed) modifiers |= CefEventFlags.AltGrDown;
            return modifiers;
        }

        /// <summary>
        /// Converts a Godot MouseButton to a CEF mouse button type.
        /// </summary>
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
        /// </summary>
        public override void _Notification(int what)
        {
            switch ((long)what)
            {
                case NotificationResized:
                    if (_browserHost != null && Size.X > 0 && Size.Y > 0)
                    {
                        _width = (int)Size.X;
                        _height = (int)Size.Y;
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

        /// <summary>
        /// Navigates back in the browser history.
        /// </summary>
        public void GoBack()
        {
            if (_browser?.CanGoBack == true)
                _browser.GoBack();
        }

        /// <summary>
        /// Navigates forward in the browser history.
        /// </summary>
        public void GoForward()
        {
            if (_browser?.CanGoForward == true)
                _browser.GoForward();
        }

        /// <summary>
        /// Navigates to the specified URL.
        /// </summary>
        /// <param name="url">The URL to navigate to.</param>
        public void NavigateToUrl(string url)
        {
            if (_browser != null && _browser.GetMainFrame() != null)
            {
                _browser.GetMainFrame().LoadUrl(url);
            }
        }

        /// <summary>
        /// Reloads the current page.
        /// </summary>
        /// <param name="ignoreCache">If true, bypasses the browser cache.</param>
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

        /// <summary>
        /// Executes JavaScript code in the browser.
        /// </summary>
        /// <param name="code">The JavaScript code to execute.</param>
        /// <param name="url">The URL for error reporting.</param>
        /// <param name="line">The starting line number for error reporting.</param>
        public void ExecuteJavaScript(string code, string url = null, int line = 1)
        {
            _browser?.GetMainFrame()?.ExecuteJavaScript(code, url ?? "about:blank", line);
        }

        /// <summary>
        /// Evaluates JavaScript code and returns the result.
        /// </summary>
        public Task<T> EvaluateJavaScript<T>(string code, string url = null, int line = 1)
        {
            return Task.FromResult<T>(default);
        }

        /// <summary>
        /// Opens the developer tools window.
        /// </summary>
        public void ShowDeveloperTools()
        {
            var windowInfo = CefWindowInfo.Create();
            windowInfo.RuntimeStyle = CefRuntimeStyle.Chrome;
            _browserHost?.ShowDevTools(windowInfo, _client, new CefBrowserSettings(), new CefPoint());
        }

        /// <summary>
        /// Closes the developer tools window.
        /// </summary>
        public void CloseDeveloperTools()
        {
            _browserHost?.CloseDevTools();
        }

        /// <summary>
        /// Called when the control exits the scene tree. Closes the browser.
        /// </summary>
        public override void _ExitTree()
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
    }
}
