using System;
using Godot;

namespace GDCefGlue
{
    // ── 嵌入窗口模式 — partial class 拆分 ─────────────────────────────
    // 参考 godot_wry 的窗口嵌入 + 每帧位置同步
    public partial class CefGlueControl
    {
        /// <summary>
        /// 嵌入窗口模式每帧处理：同步 CEF 子窗口位置/大小，跳过 OSR 纹理更新。
        /// 参考 godot_wry 的做法：检测 GlobalPosition / Size / WindowPosition / ContentScale 变化后
        /// 才调用 SetWindowPos，避免不必要的帧率开销。
        /// </summary>
        private void ProcessEmbeddedMode(double delta)
        {
            if (_browserHost == null || _godotHwnd == IntPtr.Zero)
                return;

            var globalPos = GlobalPosition;
            var size = Size;
            if (size.X <= 0 || size.Y <= 0)
                return;

            // 从 DisplayServer 获取内容缩放比（物理像素 / 虚拟像素）
            // 等价于 godot_wry 的 screen_get_content_scale_ex
            float contentScale = DisplayServer.ScreenGetScale();

            // 获取 Godot 窗口在屏幕上的位置（用于检测窗口移动）
            var windowPos = DisplayServer.WindowGetPosition();

            // 仅当有任何变化时才触发 SetWindowPos
            if (globalPos == _previousGlobalPos
                && size == _previousSize
                && windowPos == _previousWindowPos
                && Math.Abs(contentScale - _previousContentScale) < 0.001f
                && _cefChildHwnd != IntPtr.Zero
                && size.X == _controlWidth && size.Y == _controlHeight)
            {
                return; // 无变化，跳过
            }

            _previousGlobalPos = globalPos;
            _previousSize = size;
            _previousWindowPos = windowPos;
            _previousContentScale = contentScale;

            // 计算物理像素坐标
            int physX = (int)(globalPos.X * contentScale);
            int physY = (int)(globalPos.Y * contentScale);
            int physW = (int)(size.X * contentScale);
            int physH = (int)(size.Y * contentScale);

            if (_cefChildHwnd == IntPtr.Zero)
            {
                // GetWindowHandle 可能还没准备好（异步创建），重试
                _cefChildHwnd = _browserHost.GetWindowHandle();
                if (_cefChildHwnd == IntPtr.Zero)
                    return;

                GD.Print($"CefGlueControl: CEF child HWND acquired = 0x{_cefChildHwnd.ToInt64():X8}");

                // 透明+鼠标穿透（参考 godot_wry + i3D WebView2Edge 例）
                // - WS_EX_TRANSPARENT: 鼠标点击穿透 CEF 窗口 → Godot 控件接收事件
                // - 视觉透明: CEF windowed 模式不支持每像素 alpha。
                //   页面需配合 CSS background: #000000，设 CEF BackgroundColor=0 让
                //   合成器用透明底色，这样透明区域由 DWM 混合到 Godot 窗口上。
                if (Transparent)
                {
                    int exStyle = NativeWindowMethods.GetWindowLong(_cefChildHwnd, NativeWindowMethods.GWL_EXSTYLE);
                    int newExStyle = exStyle | NativeWindowMethods.WS_EX_TRANSPARENT;
                    if (newExStyle != exStyle)
                    {
                        NativeWindowMethods.SetWindowLong(_cefChildHwnd, NativeWindowMethods.GWL_EXSTYLE, newExStyle);
                        GD.Print("CefGlueControl: Applied WS_EX_TRANSPARENT for mouse passthrough");
                    }
                }
            }

            // 同步 CEF 子窗口位置和大小（坐标相对于 Godot 窗口客户区）
            NativeWindowMethods.SetWindowPos(
                _cefChildHwnd,
                NativeWindowMethods.HWND_TOP,
                physX, physY, physW, physH,
                NativeWindowMethods.SWP_NOZORDER | NativeWindowMethods.SWP_NOACTIVATE);

            // 通知 CEF 窗口大小变化
            if (physW != _controlWidth || physH != _controlHeight)
            {
                _controlWidth = physW;
                _controlHeight = physH;
                _browserHost.WasResized();
            }

            // 确保浏览器已创建标记
            if (!_browserCreated)
            {
                _browserCreated = true;
                GD.Print("CefGlueControl: Embedded browser fully created and positioned");
            }
        }
    }
}