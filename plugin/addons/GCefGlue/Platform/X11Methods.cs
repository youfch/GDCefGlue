using System;
using System.Runtime.InteropServices;
using Godot;

namespace GDCefGlue
{
    /// <summary>
    /// X11 (Linux) 原生窗口操作方法。通过 P/Invoke 调用 libX11。
    /// 仅在 Linux 上使用，Windows 上不加载。
    /// </summary>
    internal static class X11Methods
    {
        private static IntPtr _display;
        private static readonly object _lock = new();

        /// <summary>
        /// 获取 X11 Display 连接（惰性初始化，单例）。
        /// </summary>
        internal static IntPtr GetDisplay()
        {
            if (_display == IntPtr.Zero)
            {
                lock (_lock)
                {
                    if (_display == IntPtr.Zero)
                    {
                        _display = XOpenDisplay(null);
                        if (_display == IntPtr.Zero)
                            GD.PrintErr("[X11Methods] Failed to open X11 display");
                    }
                }
            }
            return _display;
        }

        /// <summary>
        /// 移动并调整 X11 子窗口的大小。
        /// </summary>
        internal static void MoveResizeWindow(IntPtr window, int x, int y, int width, int height)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return;
            XMoveResizeWindow(display, window, x, y, width, height);
            XFlush(display);
        }

        /// <summary>
        /// 将键盘焦点设置到指定窗口。
        /// </summary>
internal static void SetInputFocus(IntPtr window)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return;
            XSetInputFocus(display, window, 1 /* RevertToParent */, IntPtr.Zero /* CurrentTime */);
            XFlush(display);
        }

        internal static void SetWindowVisible(IntPtr window, bool visible)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return;
            if (visible) XMapWindow(display, window);
            else XUnmapWindow(display, window);
            XFlush(display);
        }

        // ── libX11 P/Invoke ──

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr XOpenDisplay(string display);

        // Using int for Window (XID is typically 32-bit on most Linux systems)
        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XMoveResizeWindow(IntPtr display, IntPtr window, int x, int y, int width, int height);

        // RevertTo: 0=None, 1=Parent, 2=PointerRoot
        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XSetInputFocus(IntPtr display, IntPtr window, int revertTo, IntPtr time);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XMapWindow(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XUnmapWindow(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XFlush(IntPtr display);
    }
}