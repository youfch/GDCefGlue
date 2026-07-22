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
            _control.OnPaint(buffer, width, height, dirtyRects);
        }

        protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, CefAcceleratedPaintInfo info) { }

        protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y) { }

        protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRectangle[] characterBounds)
        {
            if (characterBounds != null && characterBounds.Length > 0)
            {
                // 使用第一个字符的边界更新 IME 候选窗位置
                // CEF 的 characterBounds 是相对于 GetViewRect 返回的 view 坐标
                // 转换为全局屏幕坐标后供 Godot DisplayServer 定位 IME 候选窗
                var bounds = characterBounds[0];
                _control.UpdateImePosition(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }
    }
}
