using System;
using Godot;

namespace GDCefGlueExtension;

// ── 嵌入窗口模式 ─────────────────────────────────────────────
// 参考 plugin CefGlueControl.Embedded.cs + Platform/
public partial class CefGlueControl
{
    /// <summary>
    /// 嵌入窗口模式每帧处理：同步 CEF 子窗口位置/大小，跳过 OSR 纹理更新。
    /// 检测 GlobalPosition / Size / WindowPosition / ContentScale 变化后
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

        // 同步 CEF 子窗口可见性（节点隐藏时也隐藏嵌入窗口）
        if (_cefChildHwnd != IntPtr.Zero)
            NativeWindowMethods.SetPlatformWindowVisible(_cefChildHwnd, Visible);

        float contentScale = DisplayServer.Singleton.ScreenGetScale();
        var windowPos = DisplayServer.Singleton.WindowGetPosition();

        if (_cefChildHwnd == IntPtr.Zero)
        {
            _cefChildHwnd = _browserHost.GetWindowHandle();
            if (_cefChildHwnd == IntPtr.Zero)
                return;
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
            return;
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

        // 同步 CEF 子窗口位置和大小
        NativeWindowMethods.MovePlatformWindow(
            _cefChildHwnd, physX, physY, physW, physH);

        // 通知 CEF 窗口大小变化
        if (physW != _controlWidth || physH != _controlHeight)
        {
            _controlWidth = physW;
            _controlHeight = physH;
            _browserHost.WasResized();
        }

        if (!_browserCreated)
        {
            _browserCreated = true;
        }
    }
}