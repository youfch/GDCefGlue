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
        /// 
        /// 坐标计算（参考 godot_wry lib.rs:488-504）：
        ///   screenX = Godot窗口屏幕X + control.GlobalPosition.X
        ///   screenY = Godot窗口屏幕Y + control.GlobalPosition.Y
        ///   width  = control.Size.X
        ///   height = control.Size.Y
        /// 坐标是屏幕绝对坐标，不是相对于父窗口客户区的坐标。
        /// </summary>
        private void ProcessEmbeddedMode(double delta)
        {
            if (_browserHost == null || _godotHwnd == IntPtr.Zero)
                return;

            // Godot 控件隐藏时同步隐藏 CEF 子窗口
            if (!Visible)
            {
                if (_cefChildHwnd != IntPtr.Zero)
                    NativeWindowMethods.SetPlatformWindowVisible(_cefChildHwnd, false);
                return;
            }

            var globalPos = GlobalPosition;
            var size = Size;
            if (size.X <= 0 || size.Y <= 0)
                return;

        // 先获取 HWND（异步创建，可能还没准备好）
        if (_cefChildHwnd == IntPtr.Zero)
        {
            _cefChildHwnd = _browserHost.GetWindowHandle();
            if (_cefChildHwnd == IntPtr.Zero)
                return;

            GD.Print($"[Embedded] CEF child window: 0x{_cefChildHwnd.ToInt64():X}, Godot parent: 0x{_godotHwnd.ToInt64():X}");

            // 强制首次定位：重置 _previousGlobalPos 让变化检测触发
            _previousGlobalPos = new Vector2(-1, -1);
        }

            // 从 DisplayServer 获取内容缩放比（物理像素 / 虚拟像素）
            float contentScale = DisplayServer.ScreenGetScale();

            // 获取 Godot 窗口在屏幕上的位置
            var windowPos = DisplayServer.WindowGetPosition();

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

            // 计算物理像素坐标（相对于父窗口客户区）
            // CEF 窗口是 Godot 窗口的 X11 子窗口，XMoveResizeWindow 使用
            // 相对于父窗口的坐标，不是屏幕绝对坐标。
            int physX = (int)(globalPos.X * contentScale);
            int physY = (int)(globalPos.Y * contentScale);
            int physW = (int)(size.X * contentScale);
            int physH = (int)(size.Y * contentScale);

            GD.Print($"[Embedded] MoveResize: pos=({physX},{physY}) size=({physW},{physH}) scale={contentScale} visible={Visible} winPos=({windowPos.X},{windowPos.Y}) globalPos=({globalPos.X},{globalPos.Y})");

            // 同步 CEF 子窗口位置和大小（坐标相对于父窗口客户区）
            NativeWindowMethods.MovePlatformWindow(
                _cefChildHwnd, physX, physY, physW, physH);

            // 确保 CEF 子窗口在可见时被映射到屏幕并提升到栈顶
            if (Visible)
                NativeWindowMethods.SetPlatformWindowVisible(_cefChildHwnd, true);

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
            }
        }
    }
}
