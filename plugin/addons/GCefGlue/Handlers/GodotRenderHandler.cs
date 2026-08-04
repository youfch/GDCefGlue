using System;
using System.Runtime.InteropServices;
using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// Handles off-screen rendering events from CEF.
    /// Responsible for providing view dimensions and processing pixel buffers.
    /// </summary>
    internal class GodotRenderHandler : CefRenderHandler
    {
        private readonly CefGlueControl _control;

        public GodotRenderHandler(CefGlueControl control)
        {
            _control = control;
        }

        protected override CefAccessibilityHandler GetAccessibilityHandler() => null;

        /// <summary>
        /// Returns the view rectangle for the browser.
        /// </summary>
        protected override void GetViewRect(CefBrowser browser, out CefRectangle rect)
        {
            rect = new CefRectangle(0, 0, Math.Max(1, _control._controlWidth), Math.Max(1, _control._controlHeight));
        }

        /// <summary>
        /// Returns screen information including device scale factor.
        /// </summary>
        protected override bool GetScreenInfo(CefBrowser browser, CefScreenInfo screenInfo)
        {
            screenInfo.DeviceScaleFactor = 1.0f;
            return true;
        }

        /// <summary>
        /// Converts view coordinates to screen coordinates.
        /// </summary>
        protected override bool GetScreenPoint(CefBrowser browser, int viewX, int viewY, ref int screenX, ref int screenY)
        {
            screenX = (int)_control._cachedGlobalPosition.X + viewX;
            screenY = (int)_control._cachedGlobalPosition.Y + viewY;
            return true;
        }

        protected override void OnPopupShow(CefBrowser browser, bool show) { }

        protected override void OnPopupSize(CefBrowser browser, CefRectangle rect) { }

        /// <summary>
        /// Called when CEF has rendered a frame. Forwards the pixel buffer to the control.
        /// </summary>
        protected override void OnPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, IntPtr buffer, int width, int height)
        {
            if (_control.IsDisposed) { browser.Dispose(); return; }
            try { _control.OnPaint(buffer, width, height, dirtyRects); }
            finally { browser.Dispose(); }
        }

protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, IntPtr sharedHandle) { }

        protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y) { }

        protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRectangle[] characterBounds)
        {
            // IME 组合范围变化时更新候选窗位置。
            // IME 激活/关闭由 JS focusin/focusout 事件驱动（GodotFocusWatcher），不依赖此回调。
            if (characterBounds != null && characterBounds.Length > 0)
            {
                var bounds = characterBounds[0];
                _control.UpdateImePosition(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }
    }
}
