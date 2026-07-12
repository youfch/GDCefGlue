using System;
using System.Buffers;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  CEF 初始化、浏览器创建、生命周期
    // ══════════════════════════════════════════════════════════════
    public partial class CefGlueControl
    {
        public override void _Ready()
        {
            GD.Print("CefGlueControl: _Ready() called");

            if (Engine.IsEditorHint())
                return;

            UseGpuAcceleration = GpuAcceleration;
            UseTransparent = Transparent;
            ActiveRenderMode = Mode;
            _renderMode = Mode;
            CefInitializer.Initialize();

            CustomMinimumSize = new Vector2(100, 100);
            FocusMode = FocusModeEnum.Click;

            _image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            _texture = ImageTexture.CreateFromImage(_image);

            CallDeferred(nameof(CreateBrowserDeferred));
        }

        public override void _ExitTree()
        {
            if (Engine.IsEditorHint())
                return;

            if (_browserHost != null)
            {
                _browserHost.CloseBrowser(true);
                _browserHost = null;
                _browser = null;
            }
            _client = null;

            if (_pixelBuffer != null && _pixelBufferSize > 0)
            {
                ArrayPool<byte>.Shared.Return(_pixelBuffer);
                _pixelBuffer = null;
                _pixelBufferSize = 0;
            }
            _renderBuffer = null;
            _renderBufferSize = 0;

            base._ExitTree();
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
            GD.Print($"CefGlueControl: Creating browser {width}x{height} @ {frameRate}fps (Transparent: {Transparent}, Mode: {_renderMode})");

            var windowInfo = CefWindowInfo.Create();

            if (_renderMode == RenderMode.EmbeddedWindow)
            {
                _godotHwnd = (IntPtr)DisplayServer.WindowGetNativeHandle(
                    DisplayServer.HandleType.WindowHandle, 0);

                if (_godotHwnd == IntPtr.Zero)
                {
                    GD.PrintErr("CefGlueControl: Failed to get Godot window handle");
                    return;
                }

                GD.Print($"CefGlueControl: Godot HWND = 0x{_godotHwnd.ToInt64():X8}");

                int currentStyle = NativeWindowMethods.GetWindowLong(_godotHwnd, NativeWindowMethods.GWL_STYLE);
                if ((currentStyle & NativeWindowMethods.WS_CLIPCHILDREN) != 0)
                {
                    int newStyle = currentStyle & ~(int)NativeWindowMethods.WS_CLIPCHILDREN;
                    NativeWindowMethods.SetWindowLong(_godotHwnd, NativeWindowMethods.GWL_STYLE, newStyle);
                    _nativeStylesPatched = true;
                    GD.Print("CefGlueControl: Removed WS_CLIPCHILDREN from Godot window");
                }

                windowInfo.SetAsChild(_godotHwnd, new CefRectangle(0, 0, width, height));
            }
            else
            {
                windowInfo.SetAsWindowless(IntPtr.Zero, Transparent);
            }

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
                GD.Print("CefGlueControl: Ignoring popup browser creation");
                return;
            }

            _browser = browser;
            _browserHost = browser.GetHost();

            if (_renderMode == RenderMode.EmbeddedWindow && _browserHost != null)
            {
                _cefChildHwnd = _browserHost.GetWindowHandle();

                if (_cefChildHwnd != IntPtr.Zero)
                {
                    GD.Print($"CefGlueControl: CEF child HWND = 0x{_cefChildHwnd.ToInt64():X8}");
                }
                else
                {
                    GD.Print("CefGlueControl: GetWindowHandle returned zero, will retry in _Process");
                }
            }

            CallDeferred(nameof(NotifyBrowserInitialized));
        }

        private void NotifyBrowserInitialized()
        {
            RegisterEventForwarder();
            BrowserInitialized?.Invoke();
            GD.Print("CefGlueControl: Browser initialized");
        }
    }
}