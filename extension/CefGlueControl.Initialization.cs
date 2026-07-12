using System;
using System.Buffers;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    protected override void _Ready()
    {
        GD.Print("CefGlueControl: _Ready() called");
        if (Godot.Engine.Singleton.IsEditorHint()) { GD.Print("CefGlueControl: Running in editor, skipping CEF initialization"); return; }
        UseGpuAcceleration = GpuAcceleration; UseTransparent = Transparent;
        CefInitializer.Initialize();
        CustomMinimumSize = new Vector2(100, 100); FocusMode = FocusModeEnum.Click;
        _image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        CallDeferred("_create_browser_deferred");
    }

    protected override void _ExitTree()
    {
        if (_browserHost != null) { _browserHost.CloseBrowser(true); _browserHost = null; _browser = null; }
        _client = null;
        if (_pixelBuffer != null && _pixelBufferSize > 0) { ArrayPool<byte>.Shared.Return(_pixelBuffer); _pixelBuffer = null; _pixelBufferSize = 0; }
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
        GD.Print($"CefGlueControl: Creating browser {width}x{height} @ {frameRate}fps");
        var windowInfo = CefWindowInfo.Create();
        windowInfo.SetAsWindowless(IntPtr.Zero, Transparent);
        var settings = new CefBrowserSettings { WindowlessFrameRate = frameRate };
        _client = new GodotCefClient(this);
        try { CefBrowserHost.CreateBrowser(windowInfo, _client, settings, InitialUrl); }
        catch (Exception ex) { GD.PrintErr($"CefGlueControl: Failed to create browser - {ex.Message}"); }
    }

    internal void OnBrowserCreated(CefBrowser browser)
    {
        if (_browser != null) return;
        _browser = browser; _browserHost = browser.GetHost();
        CallDeferred("_notify_browser_initialized");
    }

    private void NotifyBrowserInitialized()
    {
        BrowserInitialized?.Invoke();
        EmitSignal(new StringName(nameof(BrowserInitialized)));
        GD.Print("CefGlueControl: Browser initialized");
    }
}