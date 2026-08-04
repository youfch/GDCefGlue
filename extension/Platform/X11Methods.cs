using System;
using System.Runtime.InteropServices;
using System.Text;
using Godot;

namespace GDCefGlueExtension
{
    /// <summary>
    /// X11 (Linux) 原生窗口操作方法。通过 P/Invoke 调用 libX11。
    /// 仅在 Linux 上使用，Windows 上不加载。
    /// </summary>
    internal static class X11Methods
    {
        private static IntPtr _display;
        private static IntPtr _godotDisplay;
        private static readonly object _lock = new();
        private static bool _globalHandlerInstalled;

        // ── X11 常量 ──

        // XMapState
        internal const int IsUnmapped = 0;
        internal const int IsUnviewable = 1;
        internal const int IsViewable = 2;

        // XConfigureWindow value mask
        internal const int CWX = 1 << 0;
        internal const int CWY = 1 << 1;
        internal const int CWWidth = 1 << 2;
        internal const int CWHeight = 1 << 3;
        internal const int CWBorderWidth = 1 << 4;
        internal const int CWSibling = 1 << 5;
        internal const int CWStackMode = 1 << 6;

        // XChangeWindowAttributes value mask
        internal const int CWBackPixmap = 1 << 0;
        internal const int CWBackPixel = 1 << 1;
        internal const int CWBorderPixmap = 1 << 2;
        internal const int CWBorderPixel = 1 << 3;
        internal const int CWBitGravity = 1 << 4;
        internal const int CWWinGravity = 1 << 5;
        internal const int CWBackingStore = 1 << 6;
        internal const int CWBackingPlanes = 1 << 7;
        internal const int CWBackingPixel = 1 << 8;
        internal const int CWOverrideRedirect = 1 << 9;
        internal const int CWSaveUnder = 1 << 10;
        internal const int CWEventMask = 1 << 11;
        internal const int CWDontPropagate = 1 << 12;
        internal const int CWColormap = 1 << 13;
        internal const int CWCursor = 1 << 14;

        // Event masks
        internal const int NoEventMask = 0;
        internal const int StructureNotifyMask = 1 << 17;
        internal const int ExposureMask = 1 << 15;
        internal const int VisibilityChangeMask = 1 << 16;
        internal const int PropertyChangeMask = 1 << 18;
        internal const int SubstructureNotifyMask = 1 << 19;
        internal const int SubstructureRedirectMask = 1 << 20;

        // Event types
        internal const int MapNotify = 19;
        internal const int UnmapNotify = 18;
        internal const int ConfigureNotify = 22;
        internal const int DestroyNotify = 17;
        internal const int PropertyNotify = 28;

        // Stack mode for ConfigureWindow
        internal const int Above = 0;
        internal const int Below = 1;
        internal const int TopIf = 2;
        internal const int BottomIf = 3;
        internal const int Opposite = 4;

        // Atom names for WM_STATE / _NET_WM_STATE
        internal const string AtomWmState = "WM_STATE";
        internal const int WmStateWithdrawn = 0;
        internal const int WmStateNormal = 1;
        internal const int WmStateIconic = 3;

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
                        _display = OpenDisplay(null);
                        if (_display == IntPtr.Zero)
                            GD.PrintErr("[X11Methods] Failed to open X11 display");
                    }
                }
            }
            return _display;
        }

        /// <summary>
        /// 关闭 X11 Display 连接（程序退出时调用）。
        /// </summary>
        internal static void CloseDisplay()
        {
            lock (_lock)
            {
                if (_display != IntPtr.Zero)
                {
                    XCloseDisplay(_display);
                    _display = IntPtr.Zero;
                }
            }
        }

        /// <summary>
        /// 全局安装 X11 错误处理器，记录错误信息以便调试。
        ///
        /// 这些错误在 CEF 嵌入窗口模式下频繁出现：CEF 子窗口尚未创建或已被销毁时，
        /// 对无效 Window ID 的 X11 操作（MapWindow、MoveResizeWindow 等）会触发 BadWindow。
        ///
        /// 必须全局安装且不恢复 Godot 的处理器，因为错误来自 CEF 内部的 X11 调用，
        /// 不是我们的代码能包裹的。
        ///
        /// 注意：此方法应尽早调用（CefInitializer.Initialize 之后立即调用），
        /// 确保 Godot 的 default_window_error_handler 被我们的覆盖。
        /// </summary>
        internal static void InstallGlobalErrorHandler()
        {
            if (_globalHandlerInstalled) return;
            _globalHandlerInstalled = true;

            try
            {
                var funcPtr = Marshal.GetFunctionPointerForDelegate(_loggingHandler);
                // 安装我们的处理器，返回值是 Godot 之前的处理器（我们丢弃它）
                _ = XSetErrorHandler(funcPtr);
                GD.Print("[X11Methods] Global error handler installed (logging mode)");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[X11Methods] Failed to install global error handler: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取 X11 错误事件的文本描述。
        /// </summary>
        internal static string GetErrorText(IntPtr display, byte errorCode)
        {
            try
            {
                var buf = Marshal.AllocHGlobal(1024);
                try
                {
                    XGetErrorText(display, errorCode, buf, 1024);
                    return Marshal.PtrToStringUTF8(buf) ?? $"Unknown error code {errorCode}";
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            catch
            {
                return $"Unknown error code {errorCode}";
            }
        }

        private static readonly XErrorHandlerDelegate _loggingHandler = LoggingErrorHandler;

        /// <summary>
        /// 日志错误处理器：记录 X11 错误信息到 Godot 控制台，返回 0 阻止崩溃。
        /// 会解析 XErrorEvent 结构体，显示错误码、请求码、资源 ID 等详细信息。
        /// </summary>
        private static int LoggingErrorHandler(IntPtr display, IntPtr errorEventPtr)
        {
            try
            {
                var errorEvent = Marshal.PtrToStructure<XErrorEvent>(errorEventPtr);
                var errorDesc = GetErrorText(display, errorEvent.error_code);

                // 解析请求的操作名
                string requestName = errorEvent.request_code switch
                {
                    0 => "X_CreateWindow",
                    1 => "X_ChangeWindow",
                    2 => "X_MapWindow",
                    3 => "X_MapSubwindows",
                    4 => "X_UnmapWindow",
                    5 => "X_UnmapSubwindows",
                    6 => "X_DestroyWindow",
                    7 => "X_DestroySubwindows",
                    8 => "X_ChangeSaveSet",
                    9 => "X_ReparentWindow",
                    10 => "X_MapRaised",
                    12 => "X_ConfigureWindow",
                    18 => "X_ChangeWindowAttributes",
                    25 => "X_SetInputFocus",
                    42 => "X_SetInputFocus",
                    61 => "X_Sync",
                    _ => $"request_code={errorEvent.request_code}"
                };

                GD.Print($"[X11Error] {errorDesc} | op={requestName} | resource=0x{errorEvent.resourceid:X} | seq={errorEvent.serial} | minor={errorEvent.minor_code}");
            }
            catch (Exception ex)
            {
                GD.Print($"[X11Error] Failed to parse error event: {ex.Message}");
            }
            return 0; // 返回 0 阻止崩溃
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int XErrorHandlerDelegate(IntPtr display, IntPtr errorEvent);

        /// <summary>
        /// 移动并调整 X11 子窗口的大小。
        /// </summary>
        internal static void MoveResizeWindow(IntPtr window, int x, int y, int width, int height)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return;
            XMoveResizeWindow(display, window, x, y, width, height);
            // 将子窗口提升到栈顶，确保不被 Godot 渲染覆盖
            XRaiseWindow(display, window);
            XFlush(display);
        }

        /// <summary>
        /// 使用 XConfigureWindow 移动并调整 X11 子窗口的大小（更可靠的替代方案）。
        /// </summary>
        internal static void ConfigureWindow(IntPtr window, int x, int y, int width, int height)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return;

            var changes = new XWindowChanges
            {
                x = x,
                y = y,
                width = width,
                height = height
            };

            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<XWindowChanges>());
            try
            {
                Marshal.StructureToPtr(changes, ptr, false);
                XConfigureWindow(display, window, CWX | CWY | CWWidth | CWHeight, ptr);
                XSync(display, false);
                XRaiseWindow(display, window);
                XFlush(display);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 设置窗口的 override_redirect 属性，绕过窗口管理器。
        /// 在 XWayland 下，某些子窗口操作需要此标志才能正常工作。
        /// </summary>
        internal static void SetOverrideRedirect(IntPtr window, bool enable)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return;

            // 使用 XChangeWindowAttributes 设置 override_redirect
            var attrs = new XSetWindowAttributes
            {
                override_redirect = enable ? 1 : 0
            };

            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<XSetWindowAttributes>());
            try
            {
                Marshal.StructureToPtr(attrs, ptr, false);
                XChangeWindowAttributes(display, window, CWOverrideRedirect, ptr);
                XSync(display, false);
                GD.Print($"[X11Methods] override_redirect set to {enable} for window 0x{window.ToInt64():X}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[X11Methods] Failed to set override_redirect: {ex.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 获取 Godot 自身的 X11 Display 连接。
        /// 通过 DisplayServer.WindowGetNativeHandle(HandleType.DisplayHandle) 获取。
        /// 
        /// 重要：对 Godot 窗口调用 XChangeWindowAttributes 等修改属性的操作时，
        /// 必须使用 Godot 自己的 Display 连接，否则会导致 Godot 事件循环崩溃。
        /// </summary>
        internal static IntPtr GetGodotDisplay()
        {
            if (_godotDisplay == IntPtr.Zero)
            {
                _godotDisplay = (IntPtr)DisplayServer.Singleton.WindowGetNativeHandle(
                    DisplayServer.HandleType.DisplayHandle, 0);
                if (_godotDisplay != IntPtr.Zero)
                    GD.Print($"[X11Methods] Godot Display handle: 0x{_godotDisplay.ToInt64():X}");
                else
                    GD.PrintErr("[X11Methods] Failed to get Godot Display handle");
            }
            return _godotDisplay;
        }

        /// <summary>
        /// 准备 Godot 父窗口以接收 CEF 子窗口。
        /// 
        /// 关键操作：移除 Godot 窗口上的 SubstructureNotifyMask 和 SubstructureRedirectMask。
        /// 
        /// 为什么这是必要的：
        /// - 在 XWayland 下，XWM 持有 SubstructureRedirectMask，拦截所有 MapRequest/ConfigureRequest
        /// - 当 CEF 创建子窗口并尝试 XMapWindow 时，MapRequest 被 XWM 拦截
        /// - XWM 可能不正确处理来自非 WM 客户端的外部 MapRequest，导致窗口永远 map_state=0
        /// 
        /// 参考 godot_wry 的 Linux 实现：
        /// https://github.com/doceazedo/godot_wry/blob/main/rust/src/godot_window.rs
        /// 
        /// 重要：必须使用 Godot 自己的 Display 连接（HandleType.DisplayHandle），
        /// 不能用 XOpenDisplay 开的独立连接，否则会导致 Godot 事件循环崩溃。
        /// </summary>
        internal static void PrepareParentWindow(IntPtr parentWindow)
        {
            // 使用 Godot 自己的 Display 连接，不是 XOpenDisplay 的独立连接
            var display = GetGodotDisplay();
            if (display == IntPtr.Zero)
            {
                GD.PrintErr("[X11Methods] PrepareParentWindow: No Godot Display, skipping");
                return;
            }

            // 1. 获取当前窗口属性
            if (XGetWindowAttributes(display, parentWindow, out var attrs) == 0)
            {
                GD.PrintErr($"[X11Methods] PrepareParentWindow: XGetWindowAttributes failed for 0x{parentWindow.ToInt64():X}");
                return;
            }

            GD.Print($"[X11Methods] PrepareParentWindow: window=0x{parentWindow.ToInt64():X} all_event_masks=0x{attrs.all_event_masks:X}");

            // 2. 计算新的事件掩码：移除 SubstructureNotifyMask 和 SubstructureRedirectMask
            long newEventMask = attrs.all_event_masks & ~SubstructureNotifyMask & ~SubstructureRedirectMask;

            // 3. 使用 XChangeWindowAttributes 设置新的事件掩码
            var setAttrs = new XSetWindowAttributes
            {
                event_mask = newEventMask
            };

            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<XSetWindowAttributes>());
            try
            {
                Marshal.StructureToPtr(setAttrs, ptr, false);
                GD.Print($"[X11Methods] PrepareParentWindow: calling XChangeWindowAttributes...");
                int result = XChangeWindowAttributes(display, parentWindow, CWEventMask, ptr);
                GD.Print($"[X11Methods] PrepareParentWindow: XChangeWindowAttributes returned {result}");
                // 不调用 XSync — XSync 会处理待处理事件，可能干扰 Godot 事件循环导致崩溃。
                // 参考 godot_wry 的实现：只调用 XChangeWindowAttributes，不调用 XSync。
                // XFlush 只刷新输出缓冲区，不等待服务器响应，不会处理事件。
                XFlush(display);
                GD.Print($"[X11Methods] PrepareParentWindow: event_mask changed to 0x{newEventMask:X} (removed SubstructureNotify+SubstructureRedirect)");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[X11Methods] PrepareParentWindow: XChangeWindowAttributes failed: {ex.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 选择指定窗口的事件掩码，用于接收子窗口的结构变化通知。
        /// </summary>
        internal static void SelectInput(IntPtr window, int eventMask)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return;
            XSelectInput(display, window, eventMask);
            XFlush(display);
        }

        /// <summary>
        /// 递归查询窗口树，返回所有子窗口的 XID 列表。
        /// 用于查找 CEF 可能的深层渲染窗口。
        /// </summary>
        internal static void RecursiveQueryTree(IntPtr window, int depth)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero || depth > 5) return;

            XQueryTree(display, window, out var root, out var parent, out var children, out var nChildren);
            GD.Print($"[X11QueryTree] depth={depth} window=0x{window.ToInt64():X} parent=0x{parent.ToInt64():X} root=0x{root.ToInt64():X} nChildren={nChildren}");

            if (nChildren > 0 && children != IntPtr.Zero)
            {
                for (int i = 0; i < nChildren; i++)
                {
                    var child = Marshal.ReadIntPtr(children, i * IntPtr.Size);
                    GD.Print($"[X11QueryTree] depth={depth} child[{i}] = 0x{child.ToInt64():X}");

                    // 检查子窗口的属性
                    var childAttrs = GetWindowAttributes(child);
                    if (childAttrs.HasValue)
                    {
                        var a = childAttrs.Value;
                        GD.Print($"[X11QueryTree] child[{i}] attrs: map_state={a.map_state} ({MapStateToString(a.map_state)}) x={a.x} y={a.y} w={a.width} h={a.height} override_redirect={a.override_redirect}");

                        // 递归查询更深层
                        RecursiveQueryTree(child, depth + 1);
                    }
                }
                XFree(children);
            }
        }

        /// <summary>
        /// 获取窗口属性，失败返回 null。
        /// </summary>
        internal static XWindowAttributes? GetWindowAttributes(IntPtr window)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return null;

            if (XGetWindowAttributes(display, window, out var attrs) != 0)
                return attrs;

            return null;
        }

        /// <summary>
        /// 查询 WM_STATE 属性，判断窗口是否被窗口管理器管理。
        /// </summary>
        internal static int? GetWmState(IntPtr window)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return null;

            var wmStateAtom = XInternAtom(display, AtomWmState, true);
            if (wmStateAtom == IntPtr.Zero)
                return null;

            if (XGetWindowProperty(display, window, wmStateAtom, 0, 2, false, wmStateAtom,
                out var actualType, out var actualFormat, out var nitems, out var bytesAfter, out var prop) == 0
                && prop != IntPtr.Zero)
            {
                try
                {
                    if (nitems > 0)
                    {
                        // WM_STATE 的第一个 long 是 state
                        if (actualFormat == 32)
                        {
                            var data = new int[nitems];
                            Marshal.Copy(prop, data, 0, (int)nitems);
                            return data[0];
                        }
                    }
                }
                finally
                {
                    XFree(prop);
                }
            }

            return null;
        }

        internal static string MapStateToString(int mapState)
        {
            return mapState switch
            {
                0 => "Unmapped",
                1 => "Unviewable",
                2 => "Viewable",
                _ => $"Unknown({mapState})"
            };
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
            if (visible) XMapRaised(display, window);
            else XUnmapWindow(display, window);
            XFlush(display);
        }

        /// <summary>
        /// 查找 Ozone X11 内容窗口（CEF 顶层窗口的孙子窗口）。
        /// 
        /// CEF 在 Linux/X11 下创建三层窗口结构（CEF Issue #3396）：
        ///   CEF 顶层窗口 (xwindow_) → Ozone X11 窗口 → 实际渲染内容窗口
        /// 
        /// 此方法递归查找两层，找到 Ozone X11 窗口（即 xwindow_ 的唯一的直接子窗口），
        /// 返回该窗口的 XID。对 Ozone 窗口调用 XConfigureWindow/XResizeWindow 才能
        /// 真正改变内容大小。
        /// </summary>
        /// <returns>Ozone X11 窗口的 XID，如果未找到则返回 null。</returns>
        internal static IntPtr? FindOzoneChild(IntPtr topLevelWindow)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return null;

            // 第一层：查询 CEF 顶层窗口的子窗口
            XQueryTree(display, topLevelWindow, out var root, out var parent, out var children, out var nChildren);

            IntPtr? result = null;

            if (nChildren > 0 && children != IntPtr.Zero)
            {
                // 取第一个子窗口（Ozone X11 窗口）
                var firstChild = Marshal.ReadIntPtr(children);
                GD.Print($"[FindOzoneChild] Level1: 0x{firstChild.ToInt64():X} (nChildren={nChildren})");

                // 查询 Ozone 窗口的子窗口
                XQueryTree(display, firstChild, out var root2, out var parent2, out var children2, out var nChildren2);
                if (nChildren2 > 0 && children2 != IntPtr.Zero)
                {
                    var contentChild = Marshal.ReadIntPtr(children2);
                    GD.Print($"[FindOzoneChild] Level2 (content): 0x{contentChild.ToInt64():X} (nChildren={nChildren2})");
                    result = contentChild;
                    XFree(children2);
                }
                else
                {
                    // Ozone 窗口可能没有子窗口（内容直接渲染在 Ozone 窗口上）
                    GD.Print($"[FindOzoneChild] Level1 only (no deeper children)");
                    result = firstChild;
                }

                XFree(children);
            }
            else
            {
                GD.Print($"[FindOzoneChild] No children found for 0x{topLevelWindow.ToInt64():X}");
            }

            return result;
        }

        /// <summary>
        /// 强制映射并提升窗口，打印详细诊断信息。
        /// 返回 true 如果窗口最终处于 Viewable 状态。
        /// </summary>
        internal static bool ForceMapWindow(IntPtr window, int x, int y, int w, int h)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return false;

            GD.Print($"[X11ForceMap] Attempting to force map window 0x{window.ToInt64():X} to ({x},{y},{w},{h})");

            // 步骤 1: 设置 override_redirect 绕过 WM
            SetOverrideRedirect(window, true);

            // 步骤 2: 使用 XConfigureWindow 设置位置和大小
            ConfigureWindow(window, x, y, w, h);

            // 步骤 3: XMapRaised
            XMapRaised(display, window);
            XSync(display, false);

            // 步骤 4: XRaiseWindow
            XRaiseWindow(display, window);
            XFlush(display);

            // 检查结果
            var attrs = GetWindowAttributes(window);
            if (attrs.HasValue)
            {
                var a = attrs.Value;
                GD.Print($"[X11ForceMap] Result: map_state={a.map_state} ({MapStateToString(a.map_state)}) x={a.x} y={a.y} w={a.width} h={a.height}");
                return a.map_state == IsViewable;
            }

            return false;
        }

        // ── libX11 P/Invoke ──

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl, EntryPoint = "XOpenDisplay")]
        private static extern IntPtr XOpenDisplay(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr XSetErrorHandler(IntPtr handler);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern void XGetErrorText(IntPtr display, byte code, IntPtr buffer, int length);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XSync(IntPtr display, bool discard);

        /// <summary>
        /// 打开 X11 显示连接。null = 使用 $DISPLAY 环境变量。
        /// </summary>
        internal static IntPtr OpenDisplay(string display)
        {
            if (display == null)
                return XOpenDisplay(IntPtr.Zero);

            var bytes = Encoding.UTF8.GetBytes(display);
            var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            try
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                Marshal.WriteByte(ptr, bytes.Length, 0);
                return XOpenDisplay(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XMoveResizeWindow(IntPtr display, IntPtr window, int x, int y, int width, int height);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XSetInputFocus(IntPtr display, IntPtr window, int revertTo, IntPtr time);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XMapWindow(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XMapRaised(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XRaiseWindow(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XUnmapWindow(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr root, out IntPtr parent, out IntPtr children, out int nChildren);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XReparentWindow(IntPtr display, IntPtr window, IntPtr parent, int x, int y);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XFlush(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XFree(IntPtr data);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes attributes);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XConfigureWindow(IntPtr display, IntPtr window, int valueMask, IntPtr changes);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XChangeWindowAttributes(IntPtr display, IntPtr window, int valueMask, IntPtr attributes);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XSelectInput(IntPtr display, IntPtr window, int eventMask);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr atom,
            long offset, long length, bool delete, IntPtr reqType,
            out IntPtr actualType, out int actualFormat, out long nitems,
            out long bytesAfter, out IntPtr prop);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XCheckTypedEvent(IntPtr display, int eventType, out XEvent eventReturn);

        // ── X11 结构体 ──

        [StructLayout(LayoutKind.Sequential)]
        internal struct XErrorEvent
        {
            public int type;
            public IntPtr display;
            public IntPtr resourceid;
            public ulong serial;
            public byte error_code;
            public byte request_code;
            public byte minor_code;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XWindowAttributes
        {
            public int x;
            public int y;
            public int width;
            public int height;
            public int border_width;
            public int depth;
            public IntPtr visual;
            public IntPtr root;
            public int @class;
            public int bit_gravity;
            public int win_gravity;
            public int backing_store;
            public ulong backing_planes;
            public ulong backing_pixel;
            public int save_under;
            public IntPtr colormap;
            public int map_installed;
            public int map_state;        // 0=Unmapped, 1=Unviewable, 2=Viewable
            public long all_event_masks;
            public long your_event_mask;
            public int do_not_propagate_mask;
            public int override_redirect;
            public IntPtr screen;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XWindowChanges
        {
            public int x;
            public int y;
            public int width;
            public int height;
            public int border_width;
            public IntPtr sibling;
            public int stack_mode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XSetWindowAttributes
        {
            public IntPtr background_pixmap;
            public ulong background_pixel;
            public IntPtr border_pixmap;
            public ulong border_pixel;
            public int bit_gravity;
            public int win_gravity;
            public int backing_store;
            public ulong backing_planes;
            public ulong backing_pixel;
            public int save_under;
            public long event_mask;
            public long do_not_propagate_mask;
            public int override_redirect;
            public IntPtr colormap;
            public IntPtr cursor;
        }

        /// <summary>
        /// 通用 XEvent 结构体（最小大小，用于检查事件类型）。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct XEvent
        {
            public int type; // 前 4 字节 = 事件类型，所有事件共用
            // 剩余 96 bytes 用于具体事件数据。我们只关心 type，不需要完整结构
        }
    }
}