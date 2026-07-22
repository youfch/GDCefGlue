using System;
using System.Buffers;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    protected override void _Ready()
    {
        if (Godot.Engine.Singleton.IsEditorHint()) { return; }
        _renderMode = Mode;
        UseGpuAcceleration = GpuAcceleration; UseTransparent = Transparent;
        CefInitializer.Initialize();
        CustomMinimumSize = new Vector2(100, 100); FocusMode = FocusModeEnum.Click;
        _image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        CallDeferred("_create_browser_deferred");
    }

    protected override void _ExitTree()
    {
        _disposed = true;
        if (_browserHost != null) { _browserHost.CloseBrowser(true); _browserHost = null; _browser = null; }
        _client = null;
        if (_pixelBuffer != null && _pixelBufferSize > 0) { ArrayPool<byte>.Shared.Return(_pixelBuffer); _pixelBuffer = null; _pixelBufferSize = 0; }
        // 释放 GPU 纹理和 Image — Godot C# 绑定不会自动释放 RID
        if (_texture != null) { RenderingServer.Singleton.FreeRid(_texture.GetRid()); _texture.Dispose(); _texture = null; }
        if (_image != null) { _image.Dispose(); _image = null; }
        base._ExitTree();
    }

    private void CreateBrowserDeferred()
    {
        if (_browserCreated) return;
        var size = Size; if (size.X > 0 && size.Y > 0) { _browserCreated = true; CreateBrowser((int)size.X, (int)size.Y); }
    }

    private void CreateBrowser(int width, int height)
    {
        _width = width; _height = height; _controlWidth = width; _controlHeight = height;
        var frameRate = Math.Clamp(FrameRate, 1, 360);
        GD.Print($"CefGlueControl: Creating browser {width}x{height} @ {frameRate}fps (Mode: {_renderMode})");

        var windowInfo = CefWindowInfo.Create();

        if (_renderMode == RenderMode.EmbeddedWindow)
        {
            _godotHwnd = (IntPtr)DisplayServer.Singleton.WindowGetNativeHandle(
                DisplayServer.HandleType.WindowHandle, 0);

            if (_godotHwnd == IntPtr.Zero)
            {
                GD.PrintErr("CefGlueControl: Failed to get Godot window handle");
                return;
            }

            windowInfo.SetAsChild(_godotHwnd, new CefRectangle(0, 0, width, height));
        }
        else
        {
            windowInfo.SetAsWindowless(IntPtr.Zero, Transparent);
        }
        var settings = new CefBrowserSettings { WindowlessFrameRate = frameRate };
        _client = new GodotCefClient(this);
        try { CefBrowserHost.CreateBrowser(windowInfo, _client, settings, InitialUrl); }
        catch (Exception ex) { GD.PrintErr($"CefGlueControl: Failed to create browser - {ex.Message}"); }
    }

    internal void OnBrowserCreated(CefBrowser browser)
    {
        if (_browser != null) return;
        _browser = browser; _browserHost = browser.GetHost();

        if (_renderMode == RenderMode.EmbeddedWindow && _browserHost != null)
        {
            _cefChildHwnd = _browserHost.GetWindowHandle();
            if (_cefChildHwnd == IntPtr.Zero)
                GD.Print("CefGlueControl: GetWindowHandle returned zero, will retry in _Process");
        }

        CallDeferred("_notify_browser_initialized");
    }

    private void NotifyBrowserInitialized()
    {
        RegisterEventForwarder();
        BrowserInitialized?.Invoke();
        EmitSignal(new StringName("browser_initialized"));
        GD.Print("CefGlueControl: Browser initialized");
    }
}