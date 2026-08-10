using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Godot;
using Xilium.CefGlue;
using Xilium.CefGlue.Platform.Windows;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    protected override void _Ready()
    {
        if (Godot.Engine.Singleton.IsEditorHint()) { return; }
        _renderMode = Mode;
        UseGpuCompositing = GpuCompositing; UseTransparent = Transparent;
        UseGpuAcceleration = EnableGpuAcceleration;
        ActiveRenderMode = Mode;
        CefInitializer.CacheDirectory = CacheDirectory;
        CefInitializer.Initialize();
        CustomMinimumSize = new Vector2(100, 100); FocusMode = FocusModeEnum.Click;
        _image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        CallDeferred("_create_browser_deferred");
    }

    protected override void _ExitTree()
    {
        if (Godot.Engine.Singleton.IsEditorHint()) { return; }
        _disposed = true;
        DeactivateIme();
        // 清理 GPU 加速资源
        CleanupGpuAcceleration();
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

            GD.Print($"[Embedded] Godot window handle: 0x{_godotHwnd.ToInt64():X}, DisplayServer name: {DisplayServer.Singleton.GetName()}, OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

            if (_godotHwnd == IntPtr.Zero)
            {
                GD.PrintErr("CefGlueControl: Failed to get Godot window handle");
                return;
            }

            // 防止 CEF 子窗口被点击时抢走 Godot 主窗口的键盘焦点
            windowInfo.StyleEx |= WindowStyleEx.WS_EX_NOACTIVATE;
            windowInfo.SetAsChild(_godotHwnd, new CefRectangle(0, 0, width, height));
            GD.Print($"[Embedded] SetAsChild parent=0x{_godotHwnd.ToInt64():X} bounds=({0},{0},{width},{height})");
        }
        else
        {
            windowInfo.SetAsWindowless(IntPtr.Zero, Transparent);

            // 启用 GPU 加速 OSR (SharedTexture) — CEF 将调用 OnAcceleratedPaint 而非 OnPaint
            if (EnableGpuAcceleration)
            {
                InitializeGpuAcceleration();
                if (_gpuAccelerationActive)
                {
                    windowInfo.SharedTextureEnabled = true;
                    GD.Print("[CefGlueControl] SharedTextureEnabled=true, GPU acceleration activated");
                }
                else
                {
                    GD.Print("[CefGlueControl] GPU acceleration not available, using CPU OnPaint");
                }
            }
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
            GD.Print($"[Embedded] OnBrowserCreated: GetWindowHandle returned 0x{_cefChildHwnd.ToInt64():X}");

            // Linux 上 GetWindowHandle() 可能返回 0x1（哨兵值）而非真实 X11 Window ID。
            // 只有当返回值无效时（0 或极小值），才通过 XQueryTree 查找实际子窗口。
            // --ozone-platform=x11 生效后，GetWindowHandle 返回真实 XID，不需要回退。
            if (OperatingSystem.IsLinux() && _godotHwnd != IntPtr.Zero
                && (_cefChildHwnd.ToInt64() <= 0x100))
            {
                GD.Print("[Embedded] GetWindowHandle returned invalid value, using XQueryTree fallback...");
                var display = X11Methods.GetDisplay();
                if (display != IntPtr.Zero)
                {
                    X11Methods.XQueryTree(display, _godotHwnd, out var root, out var parent, out var children, out var nChildren);
                    GD.Print($"[Embedded] XQueryTree on Godot window 0x{_godotHwnd.ToInt64():X}: nChildren={nChildren}");

                    if (nChildren > 0 && children != IntPtr.Zero)
                    {
                        _cefChildHwnd = Marshal.ReadIntPtr(children);
                        GD.Print($"[Embedded] Found CEF child window via XQueryTree: 0x{_cefChildHwnd.ToInt64():X}");
                        X11Methods.XFree(children);
                    }
                    else
                    {
                        GD.Print("[Embedded] No child windows found on Godot window");
                    }
                }
            }
            else
            {
                GD.Print($"[Embedded] Using GetWindowHandle value directly: 0x{_cefChildHwnd.ToInt64():X}");
            }
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