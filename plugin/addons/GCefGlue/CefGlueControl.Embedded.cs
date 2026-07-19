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
            float contentScale = DisplayServer.ScreenGetScale();

            // 获取 Godot 窗口在屏幕上的位置（用于检测窗口移动）
            var windowPos = DisplayServer.WindowGetPosition();

            // 先获取 HWND（异步创建，可能还没准备好）
            if (_cefChildHwnd == IntPtr.Zero)
            {
                _cefChildHwnd = _browserHost.GetWindowHandle();
                if (_cefChildHwnd == IntPtr.Zero)
                    return;

                GD.Print($"CefGlueControl: CEF child HWND acquired = 0x{_cefChildHwnd.ToInt64():X8}");
                // 强制首次定位：重置 _previousGlobalPos 让变化检测触发
                _previousGlobalPos = new Vector2(-1, -1);
            }

            // 仅当有任何变化时才触发 SetWindowPos
            if (globalPos == _previousGlobalPos
                && size == _previousSize
                && windowPos == _previousWindowPos
                && Math.Abs(contentScale - _previousContentScale) < 0.001f
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

            // 同步 CEF 子窗口位置和大小（坐标相对于父窗口客户区）
            NativeWindowMethods.MovePlatformWindow(
                _cefChildHwnd, physX, physY, physW, physH);

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