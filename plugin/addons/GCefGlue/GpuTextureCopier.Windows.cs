using System;
using System.Runtime.InteropServices;
using Godot;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  D3D11on12 GPU 纹理拷贝器 — Windows D3D12 后端
    //
    //  参考 Godot CEF (Rust) 的 D3D11on12 桥接方案：
    //  1. 从 Godot 的 RenderingDevice 拿到 ID3D12Device 指针
    //  2. 创建独立的 D3D12 command queue（避免与 Godot 同步冲突）
    //  3. 创建 D3D11on12 设备，包装 Godot 的 D3D12 设备
    //  4. OnAcceleratedPaint 回调中：DuplicateHandle → OpenSharedResource1 → CopyResource
    //  5. 自己管理 fence 同步
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// D3D11on12-based GPU texture copier for Windows D3D12 rendering backend.
    /// </summary>
    internal unsafe class D3D11on12TextureCopier : ITextureCopier
    {
        // ── COM 接口指针 ──
        private IntPtr _d3d12Device;          // ID3D12Device*
        private IntPtr _commandQueue;         // ID3D12CommandQueue*
        private IntPtr _d3d11Device;          // ID3D11Device*
        private IntPtr _d3d11Context;         // ID3D11DeviceContext*
        private IntPtr _d3d11on12Device;      // ID3D11On12Device*
        private IntPtr _fence;                // ID3D12Fence*
        private ulong _fenceValue;
        private IntPtr _fenceEvent;
        private bool _copyInFlight;
        private bool _disposed;

        // ── 待处理拷贝 ──
        private IntPtr _pendingDuplicatedHandle; // HANDLE (需 CloseHandle)
        private int _pendingWidth;
        private int _pendingHeight;

        private D3D11on12TextureCopier() { }

        public static D3D11on12TextureCopier TryCreate()
        {
            // D3D11on12 暂不可用，返回 null 使用 CPU 路径
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

            // 1. 从 Godot 拿到设备指针
            var devicePtr = rd.GetDriverResource(RenderingDevice.DriverResource.LogicalDevice, new Rid(), 0);
            if (devicePtr == 0)
            {
                GD.PrintErr("[D3D11on12] Failed to get device from Godot");
                return false;
            }
            _d3d12Device = (IntPtr)(nint)devicePtr;

            // 跳过 COM 验证，直接使用设备指针
            // devicePtr 由 Godot 管理，我们不释放

            // 2. 创建自己的 command queue（不借用 Godot 的，避免同步冲突）
            var queueDesc = new D3D12_COMMAND_QUEUE_DESC
            {
                Type = D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,
                Priority = D3D12_COMMAND_QUEUE_PRIORITY.D3D12_COMMAND_QUEUE_PRIORITY_NORMAL,
                Flags = D3D12_COMMAND_QUEUE_FLAGS.D3D12_COMMAND_QUEUE_FLAG_NONE,
                NodeMask = 0,
            };

            Guid iidQueue = IID.ID3D12CommandQueue;
            int hr;
            IntPtr commandQueue;
            hr = ComVtbl.ID3D12Device_CreateCommandQueue(_d3d12Device, &queueDesc, &iidQueue, out commandQueue);
            _commandQueue = commandQueue;
            if (hr < 0)
            {
                GD.PrintErr($"[D3D11on12] CreateCommandQueue failed: 0x{hr:X8}");
                return false;
            }

// 3. 创建 fence (flags=0 即 D3D12_FENCE_FLAG_NONE)
            var fenceIid = new Guid("0A753DCF-C4D8-4B91-ADF6-BE5A60D95A76");
            IntPtr fence;
            Guid* pIid = &fenceIid;

            hr = ComVtbl.ID3D12Device_CreateFence(_d3d12Device, 0, 0u, (IntPtr)pIid, out fence);
            if (hr < 0)
            {
                GD.PrintErr($"[D3D11on12] CreateFence failed: 0x{hr:X8}, continuing without fence");
                _fence = IntPtr.Zero;
            }
            else
            {
                _fence = fence;
                _fenceValue = 0;
                _copyInFlight = false;
            }

            // 4. 创建 fence event
            _fenceEvent = Kernel32.CreateEventW(IntPtr.Zero, false, false, null);
            if (_fenceEvent == IntPtr.Zero)
            {
                GD.PrintErr("[D3D11on12] CreateEventW failed");
                return false;
            }

            // 5. 创建 D3D11on12 设备
            var d3d11Device = IntPtr.Zero;
            var d3d11Context = IntPtr.Zero;
            var d3d11on12Device = IntPtr.Zero;

            var queueArray = stackalloc IntPtr[1];
            queueArray[0] = _commandQueue;

            GD.Print("[D3D11on12] Calling D3D11On12CreateDevice...");
                try
                {
                    hr = D3D11.D3D11On12CreateDevice(
                        _d3d12Device,
                        (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                        IntPtr.Zero,
                        0,
                        (IntPtr)queueArray,
                        1,
                        0,
                        ref d3d11Device,
                        IntPtr.Zero,
                        ref d3d11Context
                    );
                    GD.Print($"[D3D11on12] D3D11On12CreateDevice result: 0x{hr:X8}");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[D3D11on12] D3D11On12CreateDevice exception: {ex}");
                    return false;
                }
            if (hr < 0)
            {
                GD.PrintErr($"[D3D11on12] D3D11On12CreateDevice failed: 0x{hr:X8}");
                return false;
            }

            _d3d11Device = d3d11Device;
            _d3d11Context = d3d11Context;

            // Query ID3D11On12Device from the D3D11 device
            Guid iidD3D11On12 = IID.ID3D11On12Device;
            hr = ComVtbl.IUnknown_QueryInterface(d3d11Device, &iidD3D11On12, out d3d11on12Device);
            if (hr < 0)
            {
                GD.PrintErr($"[D3D11on12] QueryInterface(ID3D11On12Device) failed: 0x{hr:X8}");
                return false;
            }
            _d3d11on12Device = d3d11on12Device;

            GD.Print($"[D3D11on12] Initialized successfully (device=0x{_d3d12Device.ToInt64():X})");
            return true;
        }

        public bool IsValid => _d3d12Device != IntPtr.Zero && !_disposed;

        public CopyResult QueueCopy(IntPtr sharedTextureHandle, int width, int height)
        {
            if (sharedTextureHandle == IntPtr.Zero || width <= 0 || height <= 0)
                return CopyResult.Failed;

            // Duplicate 句柄 —— 回调返回后 CEF 可能释放原句柄
            var currentProcess = Kernel32.GetCurrentProcess();
            IntPtr duplicatedHandle;
            if (!Kernel32.DuplicateHandle(currentProcess, sharedTextureHandle,
                    currentProcess, out duplicatedHandle,
                    0, false, Kernel32.DUPLICATE_SAME_ACCESS))
            {
                GD.PrintErr("[D3D11on12] DuplicateHandle failed");
                return CopyResult.Failed;
            }

            // 替换之前的 pending copy（会自动关闭旧句柄）
            CleanupPendingHandle();
            _pendingDuplicatedHandle = duplicatedHandle;
            _pendingWidth = width;
            _pendingHeight = height;

            return CopyResult.Success;
        }

        public CopyResult ProcessPendingCopy(Rid dstRdRid)
        {
            if (_pendingDuplicatedHandle == IntPtr.Zero)
                return CopyResult.Success; // 没有待处理拷贝

            if (!dstRdRid.IsValid)
            {
                GD.PrintErr("[D3D11on12] Invalid destination RID");
                return CopyResult.Failed;
            }

            // 等待之前的拷贝完成
            if (_copyInFlight && _fence != IntPtr.Zero)
            {
                var completed = ComVtbl.ID3D12Fence_GetCompletedValue(_fence);
                if (completed < _fenceValue)
                {
                    return CopyResult.RetryLater; // 还没完成，下一帧再试
                }
                _copyInFlight = false;
            }
            else if (_copyInFlight)
            {
                // 无 fence 模式：假设拷贝已完成后清除标记
                _copyInFlight = false;
            }

            // 获取 Godot 的目标 D3D12 纹理
            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null) return CopyResult.Failed;

            var dstResourcePtr = (IntPtr)(nint)rd.GetDriverResource(RenderingDevice.DriverResource.Texture, dstRdRid, 0);
            if (dstResourcePtr == IntPtr.Zero)
            {
                GD.PrintErr("[D3D11on12] Failed to get destination texture");
                return CopyResult.Failed;
            }

            var width = _pendingWidth;
            var height = _pendingHeight;

            // 打开 CEF 的共享纹理 (D3D11)
            // 通过 OpenSharedResource1 打开
            var srcTexture = OpenSharedTexture(_pendingDuplicatedHandle);
            if (srcTexture == IntPtr.Zero)
            {
                CleanupPendingHandle();
                return CopyResult.Failed;
            }

            // 包装 Godot 的 D3D12 纹理为 D3D11 资源
            var wrappedResource = WrapD3D12Texture(dstResourcePtr);
            if (wrappedResource == IntPtr.Zero)
            {
                ComVtbl.IUnknown_Release(srcTexture);
                CleanupPendingHandle();
                return CopyResult.Failed;
            }

            // GPU 拷贝: D3D11CopyResource(wrappedDst, src)
            ComVtbl.ID3D11DeviceContext_CopyResource(_d3d11Context, wrappedResource, srcTexture);
            ComVtbl.ID3D11On12Device_ReleaseWrappedResources(_d3d11on12Device, &wrappedResource, 1);
            ComVtbl.ID3D11DeviceContext_Flush(_d3d11Context);

            // Signal fence 以同步
            if (_fence != IntPtr.Zero)
            {
                _fenceValue++;
                ComVtbl.ID3D12CommandQueue_Signal(_commandQueue, _fence, _fenceValue);
            }
            _copyInFlight = true;

            // 释放临时资源
            ComVtbl.IUnknown_Release(wrappedResource);
            ComVtbl.IUnknown_Release(srcTexture);

            // 清理 pending 句柄（拷贝已完成提交）
            CleanupPendingHandle();

            return CopyResult.Success;
        }

        public void WaitForCopy()
        {
            if (!_copyInFlight || _fence == IntPtr.Zero) return;

            var completed = ComVtbl.ID3D12Fence_GetCompletedValue(_fence);
            if (completed < _fenceValue)
            {
                ComVtbl.ID3D12Fence_SetEventOnCompletion(_fence, _fenceValue, _fenceEvent);
                Kernel32.WaitForSingleObject(_fenceEvent, Kernel32.INFINITE);
            }
            _copyInFlight = false;
        }

        public Rid CreateDestinationTexture(int width, int height)
        {
            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null) return new Rid();

            // 使用 TextureCreateFromExtension 创建 GPU 纹理
            // 这比 TextureCreate 更轻量，不需要 RDTextureFormat/RDTextureView 类型
            return rd.TextureCreateFromExtension(
                RenderingDevice.TextureType.Type2D,
                RenderingDevice.DataFormat.B8G8R8A8Unorm,
                RenderingDevice.TextureSamples.Samples1,
                RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyToBit,
                (ulong)Math.Max(1, width),
                (ulong)Math.Max(1, height),
                1,   // depth
                1,   // arrayLayers
                1    // mipmaps
            );
        }

        private IntPtr OpenSharedTexture(IntPtr handle)
        {
            // 需要 ID3D11Device1 来调用 OpenSharedResource1
            // 通过 QueryInterface 获取
            Guid iidDevice1 = IID.ID3D11Device1;
            IntPtr device1;
            int hr = ComVtbl.IUnknown_QueryInterface(_d3d11Device, &iidDevice1, out device1);
            if (hr < 0) return IntPtr.Zero;

            Guid iidTexture2D = IID.ID3D11Texture2D;
            IntPtr texture;
            hr = ComVtbl.ID3D11Device1_OpenSharedResource1(device1, handle, &iidTexture2D, out texture);

            ComVtbl.IUnknown_Release(device1);

            return hr >= 0 ? texture : IntPtr.Zero;
        }

        private IntPtr WrapD3D12Texture(IntPtr d3d12Resource)
        {
            var flags = new D3D11_RESOURCE_FLAGS
            {
                BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
            };

            Guid iidResource = IID.ID3D11Resource;
            IntPtr wrappedResource;
            int hr = ComVtbl.ID3D11On12Device_CreateWrappedResource(
                _d3d11on12Device, d3d12Resource, &flags,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                &iidResource, out wrappedResource);

            return hr >= 0 ? wrappedResource : IntPtr.Zero;
        }

        private void CleanupPendingHandle()
        {
            if (_pendingDuplicatedHandle != IntPtr.Zero)
            {
                Kernel32.CloseHandle(_pendingDuplicatedHandle);
                _pendingDuplicatedHandle = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                WaitForCopy();
                CleanupPendingHandle();

                if (_d3d11on12Device != IntPtr.Zero)
                { ComVtbl.IUnknown_Release(_d3d11on12Device); _d3d11on12Device = IntPtr.Zero; }
                if (_d3d11Context != IntPtr.Zero)
                { ComVtbl.IUnknown_Release(_d3d11Context); _d3d11Context = IntPtr.Zero; }
                if (_d3d11Device != IntPtr.Zero)
                { ComVtbl.IUnknown_Release(_d3d11Device); _d3d11Device = IntPtr.Zero; }
                if (_commandQueue != IntPtr.Zero)
                { ComVtbl.IUnknown_Release(_commandQueue); _commandQueue = IntPtr.Zero; }
                if (_fence != IntPtr.Zero)
                { ComVtbl.IUnknown_Release(_fence); _fence = IntPtr.Zero; }
                if (_fenceEvent != IntPtr.Zero)
                { Kernel32.CloseHandle(_fenceEvent); _fenceEvent = IntPtr.Zero; }

                // _d3d12Device 由 Godot 管理，不释放
                _d3d12Device = IntPtr.Zero;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[D3D11on12] Error during Dispose: {ex.Message}");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  D3D12 / D3D11 / DXGI 结构体定义
    // ══════════════════════════════════════════════════════════════

    #pragma warning disable CS0649 // 字段未赋值（通过 COM 返回）

    internal enum D3D12_COMMAND_LIST_TYPE : int
    {
        D3D12_COMMAND_LIST_TYPE_DIRECT = 0,
    }

    internal enum D3D12_COMMAND_QUEUE_PRIORITY : int
    {
        D3D12_COMMAND_QUEUE_PRIORITY_NORMAL = 0,
    }

    internal enum D3D12_COMMAND_QUEUE_FLAGS : int
    {
        D3D12_COMMAND_QUEUE_FLAG_NONE = 0,
    }

    internal enum D3D12_FENCE_FLAGS : int
    {
        D3D12_FENCE_FLAG_NONE = 0,
    }

    internal enum D3D12_RESOURCE_STATES : int
    {
        D3D12_RESOURCE_STATE_COMMON = 0,
        D3D12_RESOURCE_STATE_COPY_DEST = 4,
    }

    internal enum D3D11_BIND_FLAG : uint
    {
        D3D11_BIND_SHADER_RESOURCE = 8,
    }

    internal enum D3D11_CREATE_DEVICE_FLAG : uint
    {
        D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20,
    }

    internal enum D3D_FEATURE_LEVEL : int
    {
        D3D_FEATURE_LEVEL_11_0 = 0xb000,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D12_COMMAND_QUEUE_DESC
    {
        public D3D12_COMMAND_LIST_TYPE Type;
        public D3D12_COMMAND_QUEUE_PRIORITY Priority;
        public D3D12_COMMAND_QUEUE_FLAGS Flags;
        public int NodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11_RESOURCE_FLAGS
    {
        public uint BindFlags;
        public uint MiscFlags;
        public uint CPUAccessFlags;
        public uint StructureByteStride;
    }

    // ══════════════════════════════════════════════════════════════
    //  COM GUID 常量
    // ══════════════════════════════════════════════════════════════

    internal static class IID
    {
        // D3D12
        public static readonly Guid ID3D12CommandQueue = new Guid("0EC870A6-5D7E-4C22-8CFC-5BAAE07616ED");
        public static readonly Guid ID3D12Fence = new Guid("0A753DCF-C4D8-4B91-ADF6-BE5A60D95A76");

        // D3D11
        public static readonly Guid ID3D11Device1 = new Guid("A04BFB29-08EF-43D6-A49C-A9BDBDCBE686");
        public static readonly Guid ID3D11Texture2D = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
        public static readonly Guid ID3D11Resource = new Guid("DC8E63F3-D12B-4952-B47B-5E45026A862D");
        public static readonly Guid ID3D11On12Device = new Guid("85611E73-70A9-490E-9614-A9E302777904");
    }

    // ══════════════════════════════════════════════════════════════
    //  COM vtable 函数调用 (使用函数指针从 vtable 获取)
    //
    //  vtable 索引计算说明：
    //  每个 COM 接口从 IUnknown 继承 3 个方法 (QueryInterface/AddRef/Release)，
    //  然后依次排列基类方法，最后是接口自身方法。
    //
    //  ID3D12Device: IUnknown(3) + ID3D12Object(5) + 自身方法
    //    CreateCommandQueue = 8  (idx 0 in ID3D12Device)
    //    CreateFence        = 31 (idx 23)
    //    GetAdapterLuid     = 32 (idx 24)
    //
    //  ID3D12CommandQueue: IUnknown(3) + ID3D12Object(5) + ID3D12DeviceChild(1) + ID3D12Pageable(0) + 自身
    //    Signal             = 13 (idx 4)
    //
    //  ID3D12Fence: IUnknown(3) + ID3D12Object(5) + ID3D12DeviceChild(1) + ID3D12Pageable(0) + 自身
    //    GetCompletedValue  = 9  (idx 0)
    //    SetEventOnCompletion = 10 (idx 1)
    //
    //  ID3D11Device1: IUnknown(3) + ID3D11Device(40) + 自身
    //    OpenSharedResource1 = 43 (idx 1 in ID3D11Device1)
    //
    //  ID3D11On12Device: IUnknown(3) + 自身
    //    CreateWrappedResource   = 3 (idx 0)
    //    ReleaseWrappedResources = 4 (idx 1)
    //
    //  ID3D11DeviceContext: IUnknown(3) + ID3D11Object(5) + ID3D11DeviceChild(1) + 自身
    //    CopyResource = 19 (idx 10)
    //    Flush        = 50 (idx 41)
    // ══════════════════════════════════════════════════════════════

    internal static unsafe class ComVtbl
    {
        // ── IUnknown (所有 COM 对象共用) ──
        public static int IUnknown_QueryInterface(IntPtr obj, Guid* riid, out IntPtr ppv)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtable[0];
            fixed (IntPtr* p = &ppv) return func(obj, riid, p);
        }

        public static uint IUnknown_AddRef(IntPtr obj)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtable[1];
            return func(obj);
        }

        public static uint IUnknown_Release(IntPtr obj)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtable[2];
            return func(obj);
        }

// ── ID3D12Device ──
        //  vtable: IUnknown(3) + ID3D12Object(5) + ID3D12Device methods
        //  CreateCommandQueue = 8  (idx 0 in ID3D12Device)
        //  CreateFence        = 38 (idx 30, verified from winapi crate ID3D12DeviceVtbl)
        //  使用 Marshal.GetDelegateForFunctionPointer 而非 delegate* unmanaged，
        //  避免 .NET 函数指针调用约定与 Win32 COM 的兼容性问题。
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandQueueDelegate(IntPtr device, D3D12_COMMAND_QUEUE_DESC* desc, IntPtr riid, out IntPtr queue);

        public static int ID3D12Device_CreateCommandQueue(IntPtr obj, D3D12_COMMAND_QUEUE_DESC* desc, Guid* riid, out IntPtr queue)
        {
            var vtable = *(IntPtr**)obj;
            var funcPtr = vtable[8];
            var func = Marshal.GetDelegateForFunctionPointer<CreateCommandQueueDelegate>(funcPtr);
            return func(obj, desc, (IntPtr)riid, out queue);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateFenceDelegate(IntPtr device, ulong initialValue, int flags, IntPtr riid, out IntPtr ppFence);

        public static int ID3D12Device_CreateFence(IntPtr obj, ulong initialValue, uint flags, IntPtr riid, out IntPtr fence)
        {
            var vtable = *(IntPtr**)obj;
            var funcPtr = vtable[31];
            var func = Marshal.GetDelegateForFunctionPointer<CreateFenceDelegate>(funcPtr);
            return func(obj, initialValue, (int)flags, riid, out fence);
        }

        //  ── ID3D12Fence ──
        //  vtable: IUnknown(3) + ID3D12Object(5) + ID3D12DeviceChild(1) + ID3D12Fence methods
        //  GetCompletedValue      = 9  (idx 0)
        //  SetEventOnCompletion   = 10 (idx 1)
        //  Signal                 = 11 (idx 2)
        public static ulong ID3D12Fence_GetCompletedValue(IntPtr obj)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, ulong>)vtable[9];
            return func(obj);
        }

        public static int ID3D12Fence_SetEventOnCompletion(IntPtr obj, ulong value, IntPtr hEvent)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, int>)vtable[10];
            return func(obj, value, hEvent);
        }

        // ── ID3D12CommandQueue ──
        public static int ID3D12CommandQueue_Signal(IntPtr obj, IntPtr fence, ulong value)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ulong, int>)vtable[13];
            return func(obj, fence, value);
        }

        // ── ID3D11Device1 ──
        public static int ID3D11Device1_OpenSharedResource1(IntPtr obj, IntPtr handle, Guid* riid, out IntPtr resource)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtable[43];
            fixed (IntPtr* p = &resource) return func(obj, handle, riid, p);
        }

        // ── ID3D11On12Device ──
        public static int ID3D11On12Device_CreateWrappedResource(
            IntPtr obj, IntPtr d3d12Resource, D3D11_RESOURCE_FLAGS* flags,
            D3D12_RESOURCE_STATES inState, D3D12_RESOURCE_STATES outState,
            Guid* riid, out IntPtr wrappedResource)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, D3D11_RESOURCE_FLAGS*, D3D12_RESOURCE_STATES, D3D12_RESOURCE_STATES, Guid*, IntPtr*, int>)vtable[3];
            fixed (IntPtr* p = &wrappedResource) return func(obj, d3d12Resource, flags, inState, outState, riid, p);
        }

        public static void ID3D11On12Device_ReleaseWrappedResources(IntPtr obj, IntPtr* resources, int count)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int, void>)vtable[4];
            func(obj, resources, count);
        }

        // ── ID3D11DeviceContext ──
        public static void ID3D11DeviceContext_CopyResource(IntPtr obj, IntPtr dst, IntPtr src)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)vtable[19];
            func(obj, dst, src);
        }

        public static void ID3D11DeviceContext_Flush(IntPtr obj)
        {
            var vtable = *(IntPtr**)obj;
            var func = (delegate* unmanaged[Stdcall]<IntPtr, void>)vtable[50];
            func(obj);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  D3D11 flat API (DllImport)
    // ══════════════════════════════════════════════════════════════

    internal static unsafe class D3D11
    {
        // D3D11On12CreateDevice 的原始签名
        // 来自 d3d11.h (Windows SDK)
        // HRESULT D3D11On12CreateDevice(
        //     ID3D12Device* pDevice, UINT Flags,
        //     const D3D_FEATURE_LEVEL* pFeatureLevels, UINT FeatureLevels,
        //     IUnknown* const* ppCommandQueues, UINT NumQueues, UINT NodeMask,
        //     ID3D11Device** ppDevice, ID3D11DeviceContext** ppImmediateContext,
        //     ID3D11DeviceContext** ppD3D11Context);
[DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int D3D11On12CreateDevice(
            IntPtr pDevice,
            uint Flags,
            IntPtr pFeatureLevels,
            uint FeatureLevels,
            IntPtr ppCommandQueues,
            uint NumQueues,
            uint NodeMask,
            ref IntPtr ppDevice,
            IntPtr ppImmediateContext,
            ref IntPtr ppD3D11Context
        );
    }

    // ══════════════════════════════════════════════════════════════
    //  Kernel32 flat API (DllImport)
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

    #pragma warning restore CS0649
}