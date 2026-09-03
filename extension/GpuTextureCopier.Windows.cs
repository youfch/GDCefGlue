using System;
using System.Runtime.InteropServices;
using Godot;

#if GD_GPU_WINDOWS
using SharpGen.Runtime;
using SharpGen.Runtime.Win32;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D12;
using Vortice.Direct3D11on12;
using Vortice.DXGI;
using D3D12ResourceFlags = Vortice.Direct3D12.ResourceFlags;
#endif

namespace GDCefGlueExtension
{
#if GD_GPU_WINDOWS
    // ══════════════════════════════════════════════════════════════
    //  D3D11on12 GPU 纹理拷贝器 — Windows D3D12 后端
    //  条件编译：仅 GD_GPU_WINDOWS 有效（csproj: $([MSBuild]::IsOSPlatform('Windows'))）
    //
    //  使用 Vortice.Windows 类型安全的 COM 绑定，避免手写 vtable 偏移。
    //  流程：
    //  1. 从 Godot 的 RenderingDevice 拿到 ID3D12Device 指针
    //  2. 创建独立的 D3D12 command queue（避免与 Godot 同步冲突）
    //  3. 创建 D3D11on12 设备，包装 Godot 的 D3D12 设备
    //  4. OnAcceleratedPaint 回调中：OpenSharedResource1 → CreateWrappedResource → CopyResource
    //  5. 用 fence 同步 GPU 拷贝完成
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// D3D11on12-based GPU texture copier for Windows D3D12 rendering backend.
    /// </summary>
    internal unsafe class D3D11on12TextureCopier : ITextureCopier
    {
        // ── Godot 的 D3D12 设备（不拥有，通过 AddRef 保护） ──
        private ID3D12Device _d3d12Device;
        private nint _d3d12DeviceRawPtr;

        // ── 我们创建并拥有的资源 ──
        private ID3D12CommandQueue _commandQueue;
        private ID3D12Fence _fence;
        private ulong _fenceValue;
        private ID3D11Device _d3d11Device;
        private ID3D11DeviceContext _d3d11Context;
        private ID3D11On12Device _d3d11on12Device;
        private nint _fenceEvent;
        private bool _copyInFlight;
        private bool _disposed;

        // ── 待处理拷贝（线程安全：QueueCopy 在 CEF 线程，ProcessPendingCopy 在 Godot 主线程） ──
        private readonly object _srcLock = new();
        private ID3D11Resource _pendingSrc;     // 保护锁: _srcLock
        private ID3D11Resource _retiredSrc;     // 上一帧的纹理，安全释放用
        private int _pendingWidth;
        private int _pendingHeight;

        private static readonly FeatureLevel[] s_featureLevels = { global::Vortice.Direct3D.FeatureLevel.Level_11_0 };

        private D3D11on12TextureCopier() { }

        public static D3D11on12TextureCopier TryCreate()
        {
            var copier = new D3D11on12TextureCopier();
            if (copier.Initialize())
            {
                return copier;
            }
            copier.Dispose();
            return null;
        }

        private bool Initialize()
        {
            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null)
            {
                GD.PrintErr("[D3D11on12] No RenderingDevice available");
                return false;
            }

            // 1. 从 Godot 拿到 D3D12 设备指针
            // 注意：仅在 D3D12 渲染后端下有效。若 Godot 使用 Vulkan 后端，
            // GetDriverResource(LogicalDevice) 返回的是 VkDevice，后续操作会失败
            // 并被 try-catch 捕获。
            var devicePtr = (nint)rd.GetDriverResource(
                RenderingDevice.DriverResource.LogicalDevice, new Rid(), 0);
            if (devicePtr == 0)
            {
                GD.PrintErr("[D3D11on12] Failed to get D3D12 device from Godot");
                return false;
            }

            try
            {
                _d3d12DeviceRawPtr = devicePtr;
                _d3d12Device = new ID3D12Device(devicePtr);
                // AddRef 保护 Godot 持有的引用，Dispose 时平衡
                Marshal.AddRef(devicePtr);

                // 2. 创建自己的 command queue（不借用 Godot 的，避免同步冲突）
                _commandQueue = _d3d12Device.CreateCommandQueue(
                    CommandListType.Direct,
                    CommandQueuePriority.Normal,
                    CommandQueueFlags.None,
                    0);

                // 3. 创建 fence
                _fence = _d3d12Device.CreateFence(0, Vortice.Direct3D12.FenceFlags.None);
                _fenceValue = 0;

                // 4. 创建 fence event
                _fenceEvent = Kernel32.CreateEventW(IntPtr.Zero, false, false, null);
                if (_fenceEvent == 0)
                {
                    GD.PrintErr("[D3D11on12] CreateEventW failed");
                    return false;
                }

                // 5. 创建 D3D11on12 设备
                var result = Apis.D3D11On12CreateDevice(
                    _d3d12Device,
                    DeviceCreationFlags.BgraSupport,
                    s_featureLevels,
                    new IUnknown[] { _commandQueue },
                    0,
                    out _d3d11Device,
                    out _d3d11Context,
                    out _);

                if (result.Failure)
                {
                    GD.PrintErr($"[D3D11on12] D3D11On12CreateDevice failed: 0x{result.Code:X8}");
                    return false;
                }

                // 6. Query ID3D11On12Device
                _d3d11on12Device = _d3d11Device.QueryInterface<ID3D11On12Device>();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[D3D11on12] Initialization failed: {ex.Message}");
                return false;
            }

            GD.Print($"[D3D11on12] Initialized successfully (device=0x{devicePtr.ToInt64():X})");
            return true;
        }

        public bool IsValid => _d3d12Device != null && !_disposed;

        public CopyResult QueueCopy(IntPtr sharedTextureHandle, int width, int height)
        {
            if (sharedTextureHandle == IntPtr.Zero || width <= 0 || height <= 0)
                return CopyResult.Failed;

            // 关键：在 CEF UI 线程中立即打开共享纹理。
            // 若推迟到 ProcessPendingCopy（Godot 主线程）才打开，CEF 可能已释放
            // 该共享句柄下的纹理，导致 OpenSharedResource1 返回 E_HANDLE。
            // 打开后持有 ID3D11Resource 引用，即使 CEF 释放原句柄纹理也仍然有效。
            var newSrc = OpenSharedTexture(sharedTextureHandle);
            if (newSrc == null)
            {
                GD.PrintErr("[D3D11on12] QueueCopy: OpenSharedResource1 failed");
                return CopyResult.Failed;
            }

            // 线程安全地换入新纹理，旧纹理延迟到 ProcessPendingCopy 后释放
            lock (_srcLock)
            {
                // 将上一帧的 retired 安全释放（此时 ProcessPendingCopy 已经在用新纹理了）
                if (_retiredSrc != null)
                {
                    _retiredSrc.Dispose();
                    _retiredSrc = null;
                }
                // 当前 pending 成为 retired
                _retiredSrc = _pendingSrc;
                // 新纹理成为 pending
                _pendingSrc = newSrc;
                _pendingWidth = width;
                _pendingHeight = height;
            }

            return CopyResult.Success;
        }

        public CopyResult ProcessPendingCopy(Rid dstRdRid)
        {
            // 线程安全地获取当前 pending 纹理
            ID3D11Resource srcTexture;
            lock (_srcLock)
            {
                srcTexture = _pendingSrc;
            }
            if (srcTexture == null)
                return CopyResult.Success; // 没有待处理拷贝

            if (!dstRdRid.IsValid)
            {
                GD.PrintErr("[D3D11on12] Invalid destination RID");
                return CopyResult.Failed;
            }

            // 等待之前的拷贝完成
            if (_copyInFlight && _fence != null)
            {
                var completed = _fence.CompletedValue;
                if (completed < _fenceValue)
                {
                    return CopyResult.RetryLater; // 还没完成，下一帧再试
                }
                _copyInFlight = false;
            }

            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null) return CopyResult.Failed;

            // 获取 Godot 的目标 D3D12 纹理
            var dstResourcePtr = (nint)rd.GetDriverResource(
                RenderingDevice.DriverResource.Texture, dstRdRid, 0);
            if (dstResourcePtr == 0)
            {
                GD.PrintErr("[D3D11on12] Failed to get destination texture");
                return CopyResult.Failed;
            }

            var width = _pendingWidth;
            var height = _pendingHeight;

            ID3D12Resource dstResource = null;
            ID3D11Resource wrappedDst = null;

            try
            {
                // CEF 的共享纹理带有 SHARED_KEYEDMUTEX (0x802 = NTHandle | KeyedMutex)，
                // 拷贝前必须 AcquireSync，拷贝后 ReleaseSync，否则跨进程数据不一致。
                // 若 AcquireSync 失败/超时，说明 CEF 正在渲染该纹理（未释放 mutex），
                // 此时拷贝会读到半写入数据 → 黑屏。应跳过此帧，保留上一帧数据。
                IDXGIKeyedMutex keyedMutex = null;
                bool mutexAcquired = false;
                try
                {
                    keyedMutex = srcTexture.QueryInterfaceOrNull<IDXGIKeyedMutex>();
                    if (keyedMutex != null)
                    {
                        try { keyedMutex.AcquireSync(0, 100); mutexAcquired = true; }
                        catch { /* 超时/失败：CEF 正持有 mutex，跳过本帧 */ }
                    }
                    else
                    {
                        mutexAcquired = true; // 无 keyed mutex 的纹理直接拷贝
                    }
                }
                catch { }

                if (!mutexAcquired)
                {
                    keyedMutex?.Dispose();
                    return CopyResult.RetryLater; // 下一帧再试，保留上一帧数据
                }

                // 包装 Godot 的 D3D12 纹理为 D3D11 资源
                dstResource = new ID3D12Resource(dstResourcePtr);
                Marshal.AddRef(dstResourcePtr); // 保护 Godot 的引用

                var flags = new Vortice.Direct3D11on12.ResourceFlags
                {
                    BindFlags = Vortice.Direct3D11.BindFlags.ShaderResource,
                };

                // 对齐 godot-cef: CopyDest 作为 inState（Godot 纹理创建后处于 COPY_DEST），
                // Common 作为 outState（拷贝完释放回给 Godot 采样）。
                wrappedDst = _d3d11on12Device.CreateWrappedResource<ID3D11Resource>(
                    dstResource, flags,
                    ResourceStates.CopyDest,
                    ResourceStates.Common);

                // GPU 拷贝: D3D11CopyResource(wrappedDst, src)
                _d3d11Context.CopyResource(wrappedDst, srcTexture);
                _d3d11on12Device.ReleaseWrappedResources(new[] { wrappedDst });
                _d3d11Context.Flush();

                // 释放 keyed mutex（让 CEF 可以继续渲染下一帧）
                if (keyedMutex != null)
                {
                    try { keyedMutex.ReleaseSync(0); } catch { }
                    keyedMutex.Dispose();
                }

                // Signal fence 以同步
                _fenceValue++;
                _commandQueue.Signal(_fence, _fenceValue);

                // 异步 GPU 拷贝：不阻塞主线程等待 fence。
                // 下一帧 ProcessPendingCopy 会通过 _copyInFlight 检查 fence 是否完成。
                // 若未完成则返回 RetryLater，保留上一帧数据。
                _copyInFlight = true;

                return CopyResult.Success;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[D3D11on12] ProcessPendingCopy failed: {ex.Message}");
                return CopyResult.Failed;
            }
            finally
            {
                wrappedDst?.Dispose();
                if (dstResource != null)
                {
                    dstResource.Dispose(); // 平衡上面的 AddRef
                }
            }
        }

        public void WaitForCopy()
        {
            if (!_copyInFlight || _fence == null) return;

            var completed = _fence.CompletedValue;
            if (completed < _fenceValue)
            {
                _fence.SetEventOnCompletion(_fenceValue, _fenceEvent);
                Kernel32.WaitForSingleObject(_fenceEvent, Kernel32.INFINITE);
            }
            _copyInFlight = false;
        }

        public Rid CreateDestinationTexture(int width, int height)
        {
            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null) return new Rid();

            // 使用 TextureCreate 创建标准 GPU 纹理。
            // TextureCreateFromExtension 用于包装已有的原生资源，不适合这里。
            // 纹理创建后，ProcessPendingCopy 通过 GetDriverResource(Texture, rid, 0)
            // 获取其 D3D12 资源指针，然后用 D3D11on12 CopyResource 拷贝数据。
            //
            // 注意：必须调用 AddShareableFormat 标记纹理为可共享，
            // 否则 Godot 的 D3D12 后端不会分配可外部访问的资源。
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
            format.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyToBit;

            var view = new RDTextureView();

            return rd.TextureCreate(format, view);
        }

        private ID3D11Resource OpenSharedTexture(IntPtr handle)
        {
            // 需要 ID3D11Device1 来调用 OpenSharedResource1
            var device1 = _d3d11Device.QueryInterfaceOrNull<ID3D11Device1>();
            if (device1 == null)
            {
                GD.PrintErr("[D3D11on12] Failed to get ID3D11Device1");
                return null;
            }

            using (device1)
            {
                return device1.OpenSharedResource1<ID3D11Texture2D>(handle);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                WaitForCopy();

                // 清理待处理纹理
                lock (_srcLock)
                {
                    _retiredSrc?.Dispose();
                    _retiredSrc = null;
                    _pendingSrc?.Dispose();
                    _pendingSrc = null;
                }

                _d3d11on12Device?.Dispose();
                _d3d11Context?.Dispose();
                _d3d11Device?.Dispose();
                _fence?.Dispose();
                _commandQueue?.Dispose();

                // 释放 D3D12 设备包装（平衡初始化时的 AddRef）
                // Godot 自己的引用不受影响
                if (_d3d12Device != null)
                {
                    _d3d12Device.Dispose();
                    _d3d12Device = null;
                }

                if (_fenceEvent != 0)
                {
                    Kernel32.CloseHandle(_fenceEvent);
                    _fenceEvent = 0;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[D3D11on12] Error during Dispose: {ex.Message}");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Kernel32 flat API (DllImport) — Vortice 未覆盖的部分
    // ══════════════════════════════════════════════════════════════

    internal static unsafe class Kernel32
    {
        public const uint INFINITE = 0xFFFFFFFF;
        public const uint DUPLICATE_SAME_ACCESS = 2;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwOptions);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateEventW(
            IntPtr lpEventAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
            [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
            string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    }
}
#endif