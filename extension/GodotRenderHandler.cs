using System;
using System.Runtime.InteropServices;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

internal class GodotRenderHandler : CefRenderHandler
{
    private readonly CefGlueControl _control;

    public GodotRenderHandler(CefGlueControl control)
    {
        _control = control;
    }

    protected override CefAccessibilityHandler GetAccessibilityHandler() => null;

    protected override void GetViewRect(CefBrowser browser, out CefRectangle rect)
    {
        rect = new CefRectangle(0, 0, Math.Max(1, _control._controlWidth), Math.Max(1, _control._controlHeight));
    }

    protected override bool GetScreenInfo(CefBrowser browser, CefScreenInfo screenInfo)
    {
        screenInfo.DeviceScaleFactor = 1.0f;
        return true;
    }

    protected override bool GetScreenPoint(CefBrowser browser, int viewX, int viewY, ref int screenX, ref int screenY)
    {
        screenX = (int)_control._cachedGlobalPosition.X + viewX;
        screenY = (int)_control._cachedGlobalPosition.Y + viewY;
        return true;
    }

    protected override void OnPopupShow(CefBrowser browser, bool show) { }

    protected override void OnPopupSize(CefBrowser browser, CefRectangle rect) { }

    protected override void OnPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, IntPtr buffer, int width, int height)
    {
        try { _control.OnPaint(buffer, width, height, dirtyRects); }
        finally { browser.Dispose(); }
    }

    protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, CefAcceleratedPaintInfo info) { }

    protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y) { }

    protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRectangle[] characterBounds)
    {
        if (characterBounds != null && characterBounds.Length > 0)
        {
            var bounds = characterBounds[0];
            _control.OnCefImeCompositionChanged(true, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
    }
}
