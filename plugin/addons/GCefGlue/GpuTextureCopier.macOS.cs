using System;
using System.Runtime.InteropServices;
using Godot;

// 条件编译：仅 macOS（csproj: $([MSBuild]::IsOSPlatform('OSX')) → GD_GPU_MACOS）
// 在 Windows/Linux 上此文件内容被完全排除，不影响编译。
namespace GDCefGlue
{
#if GD_GPU_MACOS
    // ══════════════════════════════════════════════════════════════
    //  Metal IOSurface GPU 纹理拷贝器 — macOS Metal 后端
    //  条件编译：仅 GD_GPU_MACOS 有效
    //
    //  核心思路：使用一个极小 Objective-C++ shim（MetalCopier.mm）暴露
    //  4 个扁平 C 函数，C# 通过 P/Invoke 调用。避免在 C# 中直接
    //  objc_msgSend 调用 Metal API（ABI 脆弱、不同架构行为不同）。
    //
    //  流程：
    //  1. Initialize()
    //     通过 RenderingDevice.GetDriverResource(LogicalDevice) 拿到
    //     Godot 的 MTLDevice 指针，传入 gdcef_metal_create 创建命令队列。
    //
    //  2. QueueCopy(IntPtr sharedTextureHandle, ...)
    //     CEF UI 线程回调。sharedTextureHandle 在 macOS 上就是 IOSurfaceRef。
    //     我们 CFRetain 它延长生命周期，然后登记待处理（非阻塞）。
    //     注意：不要在这里调用 Metal API（跨线程不安全）。
    //
    //  3. ProcessPendingCopy(Rid dstRdRid)
    //     Godot 主线程（每帧 _Process 中调用）。
    //     - 用 gdcef_metal_import_io_surface 把 IOSurface 包装成 MTLTexture
    //     - 用 GetDriverResource(Texture, rid) 拿到 Godot 的目标 MTLTexture
    //     - 用 gdcef_metal_copy blit 拷贝（同步等待完成）
    //     - 释放 IOSurface 和导入的临时纹理
    //
    //  4. WaitForCopy()
    //     blit 在 shim 内同步等待，所以此处无操作。
    //
    //  5. CreateDestinationTexture()
    //     创建 B8G8R8A8Unorm 格式的 RenderingDevice 纹理。
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Metal IOSurface-based GPU texture copier for macOS Metal rendering backend.
    /// </summary>
    internal unsafe class MetalTextureCopier : ITextureCopier
    {
        // ── 原生 shim 上下文（不透明指针，指向 MetalCopierContext） ──
        private nint _ctx;

        // ── 待处理拷贝（线程安全：QueueCopy 在 CEF 线程，ProcessPendingCopy 在 Godot 主线程） ──
        private readonly object _srcLock = new();
        private nint _pendingIosurface;   // IOSurfaceRef（已 CFRetain）
        private int _pendingWidth;
        private int _pendingHeight;

        private bool _disposed;

        private MetalTextureCopier() { }

        /// <summary>
        /// 尝试创建 MetalTextureCopier。失败时返回 null（C# 侧自动转 CPU fallback）。
        /// </summary>
        public static MetalTextureCopier TryCreate()
        {
            var copier = new MetalTextureCopier();
            if (copier.Initialize())
            {
                return copier;
            }
            copier.Dispose();
            return null;
        }

        /// <summary>
        /// 初始化：从 Godot 拿到 MTLDevice，创建 shim 上下文。
        /// </summary>
        private bool Initialize()
        {
            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null)
            {
                GD.PrintErr("[MetalCopier] No RenderingDevice available");
                return false;
            }

            // 1. 从 Godot 拿到 Metal 设备指针
            // GetDriverResource(LogicalDevice, Rid.Invalid, 0) 在 Metal 后端
            // 返回 MTLDevice 的指针（id<MTLDevice>）。
            var devicePtr = (nint)rd.GetDriverResource(
                RenderingDevice.DriverResource.LogicalDevice, new Rid(), 0);
            if (devicePtr == 0)
            {
                GD.PrintErr("[MetalCopier] Failed to get Metal device from Godot");
                return false;
            }

            // 2. 调用 shim 创建命令队列包装
            // 注意：__Internal DllImport 在 NativeAOT 下直接链接符号，
            // 在 JIT/Plugin 模式下如果 shim 未加载会抛出 DllNotFoundException，
            // 被外层 catch 捕获后返回 false → CPU fallback。
            try
            {
                _ctx = gdcef_metal_create(devicePtr);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MetalCopier] gdcef_metal_create failed: {ex.Message}");
                return false;
            }

            if (_ctx == 0)
            {
                GD.PrintErr("[MetalCopier] gdcef_metal_create returned null");
                return false;
            }

            GD.Print($"[MetalCopier] Initialized successfully (device=0x{devicePtr.ToInt64():X})");
            return true;
        }

        /// <summary>
        /// 是否有效初始化且未释放。
        /// </summary>
        public bool IsValid => _ctx != 0 && !_disposed;

        /// <summary>
        /// 队列化一个从 CEF 共享 IOSurface 的拷贝操作。
        /// 在 CEF UI 线程调用——必须非阻塞，只做 CFRetain + 登记，不做 Metal 操作。
        /// </summary>
        public CopyResult QueueCopy(IntPtr sharedTextureHandle, int width, int height)
        {
            if (sharedTextureHandle == IntPtr.Zero || width <= 0 || height <= 0)
                return CopyResult.Failed;

            // 在 macOS 上，sharedTextureHandle 就是 IOSurfaceRef。
            // CFRetain 延长其生命周期，保证 ProcessPendingCopy 在 Godot 主线程
            // 处理时 IOSurface 仍然有效（CEF 可能在下一帧释放该 surface）。
            var retained = CFRetain(sharedTextureHandle);
            if (retained == IntPtr.Zero)
            {
                GD.PrintErr("[MetalCopier] QueueCopy: CFRetain failed");
                return CopyResult.Failed;
            }

            // 线程安全地换入新 IOSurface，释放旧的 pending
            lock (_srcLock)
            {
                if (_pendingIosurface != 0)
                {
                    CFRelease(_pendingIosurface);
                }
                _pendingIosurface = retained;
                _pendingWidth = width;
                _pendingHeight = height;
            }

            return CopyResult.Success;
        }

        /// <summary>
        /// 处理待处理的拷贝——将 IOSurface 数据 blit 到 Godot 的目标纹理。
        /// 在 Godot 主线程（_Process）调用。
        /// </summary>
        public CopyResult ProcessPendingCopy(Rid dstRdRid)
        {
            // 线程安全地取出当前 pending 的 IOSurface（并清空 pending）
            nint iosurface;
            int width, height;
            lock (_srcLock)
            {
                iosurface = _pendingIosurface;
                _pendingIosurface = 0;
                width = _pendingWidth;
                height = _pendingHeight;
            }

            if (iosurface == 0)
                return CopyResult.Success; // 没有待处理拷贝

            if (!dstRdRid.IsValid)
            {
                GD.PrintErr("[MetalCopier] Invalid destination RID");
                CFRelease(iosurface);
                return CopyResult.Failed;
            }

            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null)
            {
                CFRelease(iosurface);
                return CopyResult.Failed;
            }

            // 获取 Godot 的目标 Metal 纹理指针
            // GetDriverResource(Texture, rid, 0) 在 Metal 后端返回 MTLTexture 指针
            var dstTexPtr = (nint)rd.GetDriverResource(
                RenderingDevice.DriverResource.Texture, dstRdRid, 0);
            if (dstTexPtr == 0)
            {
                GD.PrintErr("[MetalCopier] Failed to get destination Metal texture");
                CFRelease(iosurface);
                return CopyResult.Failed;
            }

            try
            {
                // 1. 把 IOSurface 包装成 Metal 纹理（源）
                // format=0 表示 BGRA8Unorm_sRGB（默认，对齐 CEF 的 BGRA 输出）
                var srcTex = gdcef_metal_import_io_surface(_ctx, iosurface, width, height, 0);
                if (srcTex == 0)
                {
                    GD.PrintErr("[MetalCopier] Failed to import IOSurface as Metal texture");
                    return CopyResult.Failed;
                }

                try
                {
                    // 2. Blit 拷贝：src → dst（同步等待 GPU 完成）
                    var ok = gdcef_metal_copy(_ctx, srcTex, dstTexPtr, width, height);
                    if (ok == 0)
                    {
                        GD.PrintErr("[MetalCopier] Blit copy failed");
                        return CopyResult.Failed;
                    }
                }
                finally
                {
                    // 释放导入的临时纹理（平衡 import 的 +1 引用）
                    gdcef_metal_release_texture(srcTex);
                }

                // 同步拷贝：无需 fence 等待，blit 已在 shim 内 waitUntilCompleted
                return CopyResult.Success;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MetalCopier] ProcessPendingCopy failed: {ex.Message}");
                return CopyResult.Failed;
            }
            finally
            {
                // 释放 IOSurface（平衡 QueueCopy 中的 CFRetain）
                CFRelease(iosurface);
            }
        }

        /// <summary>
        /// 等待所有拷贝完成。
        /// 同步拷贝模式：blit 已在 shim 内 waitUntilCompleted，无需额外等待。
        /// </summary>
        public void WaitForCopy()
        {
            // 无操作：gdcef_metal_copy 内部同步等待 GPU 完成
        }

        /// <summary>
        /// 创建 Godot RenderingDevice 纹理作为拷贝目标。
        /// 使用 B8G8R8A8Unorm 格式，对齐 CEF 的 BGRA 输出。
        /// 注意：必须调用 AddShareableFormat 标记纹理为可共享，
        /// 否则 Godot 的 Metal 后端可能不会分配可外部访问的资源。
        /// </summary>
        public Rid CreateDestinationTexture(int width, int height)
        {
            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null) return new Rid();

            var format = new RDTextureFormat();
            format.AddShareableFormat(RenderingDevice.DataFormat.B8G8R8A8Unorm);
            format.Format = RenderingDevice.DataFormat.B8G8R8A8Unorm;
            format.Width = (uint)Math.Max(1, width);
            format.Height = (uint)Math.Max(1, height);
            format.Depth = 1;
            format.ArrayLayers = 1;
            format.Mipmaps = 1;
            format.TextureType = RenderingDevice.TextureType.Type2D;
            format.Samples = RenderingDevice.TextureSamples.Samples1;
            format.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit
                             | RenderingDevice.TextureUsageBits.CanCopyToBit;

            var view = new RDTextureView();

            return rd.TextureCreate(format, view);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // 清理待处理的 IOSurface
                lock (_srcLock)
                {
                    if (_pendingIosurface != 0)
                    {
                        CFRelease(_pendingIosurface);
                        _pendingIosurface = 0;
                    }
                }

                // 销毁 shim 上下文
                if (_ctx != 0)
                {
                    gdcef_metal_destroy(_ctx);
                    _ctx = 0;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MetalCopier] Error during Dispose: {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  P/Invoke 声明 — 调用 MetalCopier.mm shim
        //  使用 __Internal 作为 DllImport 库名：
        //    - NativeAOT (extension) 模式下，__Internal 直接链接当前模块
        //      的符号（静态链接）。
        //    - Plugin (JIT) 模式下，__Internal 会尝试在当前进程或加载的
        //      dylib 中查找符号。若 shim 未编译/尚未加载，调用会抛出
        //      DllNotFoundException / EntryPointNotFoundException。
        //      被外层 try-catch 捕获后返回 Cpu fallback。
        //  两个模式都正常工作：shim 在 → 加速；不在 → CPU fallback。
        // ──────────────────────────────────────────────────────────────

        [DllImport("__Internal", EntryPoint = "gdcef_metal_create")]
        private static extern nint gdcef_metal_create(nint mtlDevice);

        [DllImport("__Internal", EntryPoint = "gdcef_metal_destroy")]
        private static extern void gdcef_metal_destroy(nint ctx);

        [DllImport("__Internal", EntryPoint = "gdcef_metal_import_io_surface")]
        private static extern nint gdcef_metal_import_io_surface(
            nint ctx, nint ioSurface, int width, int height, int format);

        [DllImport("__Internal", EntryPoint = "gdcef_metal_copy")]
        private static extern int gdcef_metal_copy(
            nint ctx, nint srcTexture, nint dstTexture, int width, int height);

        [DllImport("__Internal", EntryPoint = "gdcef_metal_release_texture")]
        private static extern void gdcef_metal_release_texture(nint texturePtr);

        // ── CoreFoundation — IOSurface 生命周期管理 ──

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern nint CFRetain(nint cf);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(nint cf);
    }
#endif
}