using System;
using System.Runtime.InteropServices;

namespace GDCefGlueExtension
{
    /// <summary>
    /// macOS 原生窗口操作方法。通过 Objective-C runtime 调用 Cocoa API。
    /// 仅在 macOS 上使用。
    /// </summary>
    internal static class MacMethods
    {
        /// <summary>
        /// 移动并调整 NSView 子视图的大小。调用 [view setFrame:NSRect]。
        /// macOS 坐标原点在左下角，x/y 为相对于父视图的位置。
        /// </summary>
        internal static void SetViewFrame(IntPtr nsView, int x, int y, int width, int height)
        {
            if (nsView == IntPtr.Zero) return;

            var setFrameSel = sel_registerName("setFrame:");
            var rect = new NSRect
            {
                Origin = new NSPoint { X = x, Y = y },
                Size = new NSSize { Width = width, Height = height }
            };
            objc_msgSend(nsView, setFrameSel, rect);
        }

        /// <summary>
        /// 将键盘焦点设置到指定 NSView。
        /// 调用 [[view window] makeFirstResponder:view]
        /// </summary>
        internal static void MakeFirstResponder(IntPtr nsView)
        {
            if (nsView == IntPtr.Zero) return;

            var windowSel = sel_registerName("window");
            var makeFirstResponderSel = sel_registerName("makeFirstResponder:");

            var nsWindow = objc_msgSend_retPtr(nsView, windowSel);
            if (nsWindow != IntPtr.Zero)
                objc_msgSend(nsWindow, makeFirstResponderSel, nsView);
        }

        // ── Cocoa / Objective-C runtime ──

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend(IntPtr self, IntPtr op, NSRect rect);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend(IntPtr self, IntPtr op, IntPtr arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_retPtr(IntPtr self, IntPtr op);

        [StructLayout(LayoutKind.Sequential)]
        private struct NSPoint
        {
            public double X;
            public double Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NSSize
        {
            public double Width;
            public double Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NSRect
        {
            public NSPoint Origin;
            public NSSize Size;
        }
    }
}