using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;

#if GD_GPU_LINUX

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  Linux Vulkan DMA-BUF GPU 纹理拷贝器
    //  条件编译：仅 GD_GPU_LINUX 有效（csproj: $([MSBuild]::IsOSPlatform('Linux'))）
    //
    //  使用纯 P/Invoke 调用 libvulkan.so.1，无任何 NuGet 绑定库。
    //  流程：
    //  1. 从 Godot RenderingDevice 获取 VkDevice / VkPhysicalDevice
    //  2. 通过 vkGetDeviceProcAddr 解析所有需要的 Vulkan 函数指针
    //  3. 检查 vkGetMemoryFdPropertiesKHR 是否可用（需要 GDCefGlue Vulkan Layer）
    //  4. QueueCopy（CEF UI 线程）：提取 DMA-BUF fd(s)，dup 延长生命周期
    //  5. ProcessPendingCopy（Godot 主线程）：导入 DMA-BUF → VkImage，vkCmdCopyImage 到 Godot 纹理
    //  6. 用 fence 非阻塞检查 GPU 拷贝完成
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Linux Vulkan DMA-BUF GPU texture copier.
    /// 使用原始 Vulkan P/Invoke，无托管 Vulkan 绑定。
    /// </summary>
    internal unsafe class LinuxVulkanTextureCopier : ITextureCopier
    {
        // ── Godot 的 Vulkan 设备句柄（不拥有，不销毁） ──
        private ulong _device;          // VkDevice
        private ulong _physicalDevice;  // VkPhysicalDevice
        private ulong _queue;           // VkQueue
        private uint _queueFamilyIndex;

        // ── 我们创建并拥有的 Vulkan 资源 ──
        private ulong _commandPool;     // VkCommandPool
        private ulong _commandBuffer;   // VkCommandBuffer
        private ulong _fence;           // VkFence（初始为 signaled 状态）
        private bool _copyInFlight;

        // ── Vulkan 函数指针 ──
        private PFN_vkGetDeviceQueue _vkGetDeviceQueue;
        private PFN_vkCreateCommandPool _vkCreateCommandPool;
        private PFN_vkDestroyCommandPool _vkDestroyCommandPool;
        private PFN_vkAllocateCommandBuffers _vkAllocateCommandBuffers;
        private PFN_vkCreateFence _vkCreateFence;
        private PFN_vkDestroyFence _vkDestroyFence;
        private PFN_vkBeginCommandBuffer _vkBeginCommandBuffer;
        private PFN_vkEndCommandBuffer _vkEndCommandBuffer;
        private PFN_vkCmdPipelineBarrier _vkCmdPipelineBarrier;
        private PFN_vkCmdCopyImage _vkCmdCopyImage;
        private PFN_vkQueueSubmit _vkQueueSubmit;
        private PFN_vkWaitForFences _vkWaitForFences;
        private PFN_vkResetFences _vkResetFences;
        private PFN_vkResetCommandBuffer _vkResetCommandBuffer;
        private PFN_vkCreateImage _vkCreateImage;
        private PFN_vkDestroyImage _vkDestroyImage;
        private PFN_vkGetImageMemoryRequirements _vkGetImageMemoryRequirements;
        private PFN_vkAllocateMemory _vkAllocateMemory;
        private PFN_vkFreeMemory _vkFreeMemory;
        private PFN_vkBindImageMemory _vkBindImageMemory;
        private PFN_vkGetMemoryFdPropertiesKHR _vkGetMemoryFdPropertiesKHR;

        // ── 待处理拷贝（线程安全：QueueCopy 在 CEF 线程，ProcessPendingCopy 在 Godot 主线程） ──
        private readonly object _srcLock = new();
        private PendingLinuxCopy _pendingCopy;
        private PendingLinuxCopy _retiredCopy;

        // ── DMA-BUF 导入缓存（key = inode） ──
        private ulong _frameCount;
        private const int CacheMaxSize = 10;
        private ImportedImage[] _cache = new ImportedImage[CacheMaxSize];
        private int _cacheCount;

        // ── 状态 ──
        private bool _disposed;

        // 日志标签
        private const string Tag = "[LinuxVulkan]";

        // ── Vulkan 常量 ──
        private const int VK_SUCCESS = 0;
        private const int VK_TIMEOUT = 2;
        private const ulong VK_TRUE = 1;
        private const ulong VK_FALSE = 0;
        private const uint VK_QUEUE_FAMILY_IGNORED = 0xFFFFFFFF;

        private const uint VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO = 35;
        private const uint VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO = 36;
        private const uint VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO = 38;
        private const uint VK_STRUCTURE_TYPE_FENCE_CREATE_INFO = 82;
        private const uint VK_STRUCTURE_TYPE_SUBMIT_INFO = 39;
        private const uint VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO = 10;
        private const uint VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER = 45;
        private const uint VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO = 20;
        private const uint VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO = 1000071002;
        private const uint VK_STRUCTURE_TYPE_IMPORT_MEMORY_FD_INFO_KHR = 1000074002;
        private const uint VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO = 1000070001;
        private const uint VK_STRUCTURE_TYPE_MEMORY_FD_PROPERTIES_KHR = 1000074003;

        private const uint VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT = 2;
        private const uint VK_COMMAND_BUFFER_LEVEL_PRIMARY = 0;
        private const uint VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT = 1;
        private const uint VK_FENCE_CREATE_SIGNALED_BIT = 1;
        private const uint VK_IMAGE_TYPE_2D = 1;
        private const int VK_IMAGE_TILING_LINEAR = 0;
        private const int VK_IMAGE_TILING_DRM_FORMAT_MODIFIER_EXT = 1000158000;
        private const uint VK_IMAGE_USAGE_TRANSFER_SRC_BIT = 1;
        private const uint VK_IMAGE_LAYOUT_UNDEFINED = 0;
        private const uint VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL = 3;
        private const uint VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL = 4;
        private const uint VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL = 5;
        private const uint VK_IMAGE_ASPECT_COLOR_BIT = 1;
        private const uint VK_SAMPLE_COUNT_1_BIT = 1;
        private const uint VK_SHARING_MODE_EXCLUSIVE = 0;
        private const uint VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT = 1;
        private const uint VK_PIPELINE_STAGE_TRANSFER_BIT = 1024;
        private const uint VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT = 128;
        private const uint VK_ACCESS_TRANSFER_READ_BIT = 1024;
        private const uint VK_ACCESS_TRANSFER_WRITE_BIT = 2048;
        private const uint VK_ACCESS_SHADER_READ_BIT = 32;
        private const uint VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_EXT = 0x00000020;
        private const uint VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_FD_BIT = 0x00000002;
        private const int VK_FORMAT_B8G8R8A8_UNORM = 44;
        private const int VK_FORMAT_R8G8B8A8_UNORM = 37;
        private const ulong DRM_FORMAT_MOD_INVALID = 0x00ffffffffffffff;

        private LinuxVulkanTextureCopier() { }

        // ──────────────────────────────────────────────────────────
        //  TryCreate / Initialize
        // ──────────────────────────────────────────────────────────

        public static LinuxVulkanTextureCopier TryCreate()
        {
            var copier = new LinuxVulkanTextureCopier();
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
                GD.PrintErr($"{Tag} No RenderingDevice available");
                return false;
            }

            // 1. 从 Godot 获取 Vulkan 设备句柄
            _device = rd.GetDriverResource(
                RenderingDevice.DriverResource.LogicalDevice, new Rid(), 0);
            if (_device == 0)
            {
                GD.PrintErr($"{Tag} Failed to get VkDevice from Godot");
                return false;
            }

            _physicalDevice = rd.GetDriverResource(
                RenderingDevice.DriverResource.PhysicalDevice, new Rid(), 0);
            if (_physicalDevice == 0)
            {
                GD.PrintErr($"{Tag} Failed to get VkPhysicalDevice from Godot");
                return false;
            }

            GD.Print($"{Tag} Got Godot VkDevice=0x{_device:X}, VkPhysicalDevice=0x{_physicalDevice:X}");

            // 2. 加载 libvulkan.so.1
            IntPtr libHandle;
            try
            {
                NativeLibrary.TryLoad("libvulkan.so.1", out libHandle);
                if (libHandle == IntPtr.Zero)
                {
                    GD.PrintErr($"{Tag} Failed to load libvulkan.so.1");
                    return false;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"{Tag} Failed to load libvulkan.so.1: {ex.Message}");
                return false;
            }

            try
            {
                // 3. 获取 vkGetDeviceProcAddr
                var getDeviceProcAddr = Marshal.GetDelegateForFunctionPointer<PFN_vkGetDeviceProcAddr>(
                    NativeLibrary.GetExport(libHandle, "vkGetDeviceProcAddr"));
                if (getDeviceProcAddr == null)
                {
                    GD.PrintErr($"{Tag} Failed to get vkGetDeviceProcAddr");
                    return false;
                }

                // 4. 解析所有设备函数指针
                if (!LoadDeviceFunctions(getDeviceProcAddr, libHandle))
                {
                    return false;
                }

                // 5. 检查 vkGetMemoryFdPropertiesKHR — 需要 GDCefGlue Vulkan Layer
                if (_vkGetMemoryFdPropertiesKHR == null)
                {
                    GD.PrintErr(
                        $"{Tag} vkGetMemoryFdPropertiesKHR is not available.\n" +
                        $"{Tag} This means VK_KHR_external_memory_fd / VK_EXT_external_memory_dma_buf\n" +
                        $"{Tag} are not enabled. Please install the GDCefGlue Vulkan layer\n" +
                        $"{Tag} to enable GPU-accelerated OSR on Linux.\n" +
                        $"{Tag} Falling back to CPU rendering.");
                    return false;
                }

                // 6. 获取 Godot 图形队列
                _queueFamilyIndex = 0;
                _vkGetDeviceQueue(_device, _queueFamilyIndex, 0, out _queue);
                if (_queue == 0)
                {
                    GD.PrintErr($"{Tag} Failed to get Vulkan queue");
                    return false;
                }

                // 7. 创建 command pool
                var poolCreateInfo = new VkCommandPoolCreateInfo
                {
                    sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
                    flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
                    queueFamilyIndex = _queueFamilyIndex,
                };

                ulong pool = 0;
                var result = _vkCreateCommandPool(_device, &poolCreateInfo, null, &pool);
                if (result != VK_SUCCESS)
                {
                    GD.PrintErr($"{Tag} Failed to create command pool: {result}");
                    return false;
                }
                _commandPool = pool;

                // 8. 分配 command buffer
                var allocInfo = new VkCommandBufferAllocateInfo
                {
                    sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
                    commandPool = _commandPool,
                    level = VK_COMMAND_BUFFER_LEVEL_PRIMARY,
                    commandBufferCount = 1,
                };

                ulong cmdBuf = 0;
                result = _vkAllocateCommandBuffers(_device, &allocInfo, &cmdBuf);
                if (result != VK_SUCCESS)
                {
                    GD.PrintErr($"{Tag} Failed to allocate command buffer: {result}");
                    return false;
                }
                _commandBuffer = cmdBuf;

                // 9. 创建 fence（初始为 signaled 状态，第一次 reset 不失败）
                var fenceCreateInfo = new VkFenceCreateInfo
                {
                    sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO,
                    flags = VK_FENCE_CREATE_SIGNALED_BIT,
                };

                ulong fence = 0;
                result = _vkCreateFence(_device, &fenceCreateInfo, null, &fence);
                if (result != VK_SUCCESS)
                {
                    GD.PrintErr($"{Tag} Failed to create fence: {result}");
                    return false;
                }
                _fence = fence;

                GD.Print($"{Tag} Initialized successfully (device=0x{_device:X})");
                return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"{Tag} Initialization failed: {ex.Message}");
                return false;
            }
            finally
            {
                // libvulkan 保持加载（NativeLibrary 不 unload，生命周期与进程一致）
            }
        }

        /// <summary>
        /// 解析所有 Vulkan 设备函数指针。
        /// 返回 false 表示关键函数不可用。
        /// </summary>
        private bool LoadDeviceFunctions(PFN_vkGetDeviceProcAddr getProcAddr, IntPtr libHandle)
        {
            _vkGetDeviceQueue = GetDeviceFunc<PFN_vkGetDeviceQueue>(getProcAddr, "vkGetDeviceQueue");
            _vkCreateCommandPool = GetDeviceFunc<PFN_vkCreateCommandPool>(getProcAddr, "vkCreateCommandPool");
            _vkDestroyCommandPool = GetDeviceFunc<PFN_vkDestroyCommandPool>(getProcAddr, "vkDestroyCommandPool");
            _vkAllocateCommandBuffers = GetDeviceFunc<PFN_vkAllocateCommandBuffers>(getProcAddr, "vkAllocateCommandBuffers");
            _vkCreateFence = GetDeviceFunc<PFN_vkCreateFence>(getProcAddr, "vkCreateFence");
            _vkDestroyFence = GetDeviceFunc<PFN_vkDestroyFence>(getProcAddr, "vkDestroyFence");
            _vkBeginCommandBuffer = GetDeviceFunc<PFN_vkBeginCommandBuffer>(getProcAddr, "vkBeginCommandBuffer");
            _vkEndCommandBuffer = GetDeviceFunc<PFN_vkEndCommandBuffer>(getProcAddr, "vkEndCommandBuffer");
            _vkCmdPipelineBarrier = GetDeviceFunc<PFN_vkCmdPipelineBarrier>(getProcAddr, "vkCmdPipelineBarrier");
            _vkCmdCopyImage = GetDeviceFunc<PFN_vkCmdCopyImage>(getProcAddr, "vkCmdCopyImage");
            _vkQueueSubmit = GetDeviceFunc<PFN_vkQueueSubmit>(getProcAddr, "vkQueueSubmit");
            _vkWaitForFences = GetDeviceFunc<PFN_vkWaitForFences>(getProcAddr, "vkWaitForFences");
            _vkResetFences = GetDeviceFunc<PFN_vkResetFences>(getProcAddr, "vkResetFences");
            _vkResetCommandBuffer = GetDeviceFunc<PFN_vkResetCommandBuffer>(getProcAddr, "vkResetCommandBuffer");
            _vkCreateImage = GetDeviceFunc<PFN_vkCreateImage>(getProcAddr, "vkCreateImage");
            _vkDestroyImage = GetDeviceFunc<PFN_vkDestroyImage>(getProcAddr, "vkDestroyImage");
            _vkGetImageMemoryRequirements = GetDeviceFunc<PFN_vkGetImageMemoryRequirements>(getProcAddr, "vkGetImageMemoryRequirements");
            _vkAllocateMemory = GetDeviceFunc<PFN_vkAllocateMemory>(getProcAddr, "vkAllocateMemory");
            _vkFreeMemory = GetDeviceFunc<PFN_vkFreeMemory>(getProcAddr, "vkFreeMemory");
            _vkBindImageMemory = GetDeviceFunc<PFN_vkBindImageMemory>(getProcAddr, "vkBindImageMemory");

            // vkGetMemoryFdPropertiesKHR 是扩展函数，可能为 null → 表示 Layer 未安装
            // 注意：必须通过设备解析，因为 vkGetDeviceProcAddr 对扩展函数也有效
            _vkGetMemoryFdPropertiesKHR = GetDeviceFunc<PFN_vkGetMemoryFdPropertiesKHR>(
                getProcAddr, "vkGetMemoryFdPropertiesKHR");

            // 检查关键函数是否都解析成功
            if (_vkGetDeviceQueue == null || _vkCreateCommandPool == null ||
                _vkDestroyCommandPool == null || _vkAllocateCommandBuffers == null ||
                _vkCreateFence == null || _vkDestroyFence == null ||
                _vkBeginCommandBuffer == null || _vkEndCommandBuffer == null ||
                _vkCmdPipelineBarrier == null || _vkCmdCopyImage == null ||
                _vkQueueSubmit == null || _vkWaitForFences == null ||
                _vkResetFences == null || _vkResetCommandBuffer == null ||
                _vkCreateImage == null || _vkDestroyImage == null ||
                _vkGetImageMemoryRequirements == null || _vkAllocateMemory == null ||
                _vkFreeMemory == null || _vkBindImageMemory == null)
            {
                GD.PrintErr($"{Tag} Failed to resolve one or more core Vulkan device functions");
                return false;
            }

            return true;
        }

        private T GetDeviceFunc<T>(PFN_vkGetDeviceProcAddr getProcAddr, string name) where T : Delegate
        {
            var ptr = getProcAddr(_device, name);
            if (ptr == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        // ══════════════════════════════════════════════════════════
        //  ITextureCopier 实现
        // ══════════════════════════════════════════════════════════

        public bool IsValid => _device != 0 && !_disposed;

        /// <summary>
        /// 从 CEF 的 AcceleratedPaintInfo 中提取 DMA-BUF fd(s)，
        /// dup 延长生命周期，存入 pending 供下一帧 ProcessPendingCopy 处理。
        /// </summary>
        public CopyResult QueueCopy(IntPtr sharedTextureHandle, int width, int height)
        {
            if (sharedTextureHandle == IntPtr.Zero || width <= 0 || height <= 0)
                return CopyResult.Failed;

            // 从指针读取 CEF 加速渲染信息
            CefAcceleratedPaintInfo info;
            try
            {
                info = Marshal.PtrToStructure<CefAcceleratedPaintInfo>(sharedTextureHandle);
            }
            catch
            {
                GD.PrintErr($"{Tag} QueueCopy: Failed to read AcceleratedPaintInfo");
                return CopyResult.Failed;
            }

            if (info.plane_count <= 0 || info.plane_count > 4)
            {
                GD.PrintErr($"{Tag} QueueCopy: Invalid plane count: {info.plane_count}");
                return CopyResult.Failed;
            }

            // 读取 plane 数组
            int planeCount = info.plane_count;
            int planeStructSize = Marshal.SizeOf<CefAcceleratedPaintPlane>();
            IntPtr planesPtr = sharedTextureHandle + 24; // 偏移：plane_count(4) + format(4) + modifier(8) + coded_size(8) = 24

            // 获取第一个 plane 的 fd 用于 inode 查询
            var firstPlane = Marshal.PtrToStructure<CefAcceleratedPaintPlane>(planesPtr);
            if (firstPlane.fd < 0)
            {
                GD.PrintErr($"{Tag} QueueCopy: Invalid fd in first plane");
                return CopyResult.Failed;
            }

            // 获取 DMA-BUF inode
            Libc.fstat(firstPlane.fd, out var statBuf);
            ulong inode = statBuf.st_ino;

            // 检查是否需要重新导入（缓存中不存在或尺寸变化）
            bool needsImport = true;
            for (int i = 0; i < _cacheCount; i++)
            {
                if (_cache[i].inode == inode)
                {
                    if (_cache[i].width == width && _cache[i].height == height)
                    {
                        needsImport = false;
                    }
                    break;
                }
            }

            // 分配 pending 拷贝信息
            var pending = new PendingLinuxCopy
            {
                inode = inode,
                needsImport = needsImport,
                format = CefColorTypeToVkFormat(info.format),
                width = (uint)Math.Max(1, width),
                height = (uint)Math.Max(1, height),
                modifier = info.modifier,
            };

            pending.fds = new int[planeCount];
            pending.strides = new uint[planeCount];
            pending.offsets = new ulong[planeCount];

            for (int i = 0; i < planeCount; i++)
            {
                var plane = Marshal.PtrToStructure<CefAcceleratedPaintPlane>(
                    planesPtr + i * planeStructSize);

                if (plane.fd < 0)
                {
                    GD.PrintErr($"{Tag} QueueCopy: Invalid fd for plane {i}");
                    // 清理已 dup 的 fd
                    for (int j = 0; j < i; j++)
                    {
                        if (pending.fds[j] >= 0)
                            Libc.close(pending.fds[j]);
                    }
                    return CopyResult.Failed;
                }

                // dup fd 延长生命周期，即使 CEF 释放原 fd 也仍然有效
                int dupFd = Libc.dup(plane.fd);
                if (dupFd < 0)
                {
                    GD.PrintErr($"{Tag} QueueCopy: dup() failed for plane {i}");
                    for (int j = 0; j < i; j++)
                    {
                        if (pending.fds[j] >= 0)
                            Libc.close(pending.fds[j]);
                    }
                    return CopyResult.Failed;
                }

                pending.fds[i] = dupFd;
                pending.strides[i] = (uint)plane.stride;
                pending.offsets[i] = (ulong)plane.offset;
            }

            // 如果不需要重新导入，释放 dup 的 fd（缓存中的导入已持有内存引用）
            if (!needsImport)
            {
                for (int i = 0; i < pending.fds.Length; i++)
                {
                    if (pending.fds[i] >= 0)
                    {
                        Libc.close(pending.fds[i]);
                        pending.fds[i] = -1;
                    }
                }
                // 清空 fds 列表，ProcessPendingCopy 会使用缓存
                pending.fds = Array.Empty<int>();
            }

            // 线程安全换入新的 pending 拷贝
            lock (_srcLock)
            {
                // 释放上一帧的 retired
                if (_retiredCopy.fds != null)
                {
                    _retiredCopy.Dispose();
                    _retiredCopy = default;
                }

                // 当前 pending 成为 retired
                _retiredCopy = _pendingCopy;
                // 新 pending
                _pendingCopy = pending;
            }

            return CopyResult.Success;
        }

        /// <summary>
        /// 处理待处理的 DMA-BUF 拷贝。
        /// 导入 DMA-BUF → VkImage，然后 vkCmdCopyImage 到 Godot 的目标纹理。
        /// 非阻塞：如果 fence 未完成返回 RetryLater。
        /// </summary>
        public CopyResult ProcessPendingCopy(Rid dstRdRid)
        {
            // 线程安全地获取当前 pending 拷贝
            PendingLinuxCopy pending;
            lock (_srcLock)
            {
                pending = _pendingCopy;
            }

            if (pending.fds == null)
                return CopyResult.Success; // 没有待处理拷贝

            if (!dstRdRid.IsValid)
            {
                GD.PrintErr($"{Tag} Invalid destination RID");
                return CopyResult.Failed;
            }

            // 检查 fence：上一帧拷贝是否完成
            if (_copyInFlight)
            {
                var fenceResult = _vkWaitForFences(_device, 1, _fence, VK_FALSE, 0);
                if (fenceResult == VK_TIMEOUT)
                {
                    return CopyResult.RetryLater; // 还没完成，下一帧再试
                }
                _copyInFlight = false;
            }

            // 获取 Godot 的目标 Vulkan 图像
            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null) return CopyResult.Failed;

            ulong dstImage = rd.GetDriverResource(
                RenderingDevice.DriverResource.Texture, dstRdRid, 0);
            if (dstImage == 0)
            {
                GD.PrintErr($"{Tag} Failed to get destination Vulkan image");
                return CopyResult.Failed;
            }

            var width = pending.width;
            var height = pending.height;

            try
            {
                // 导入或查找缓存的 DMA-BUF 图像
                ulong srcImage = 0;
                bool needsImport = pending.needsImport;

                // 检查缓存
                int cacheIdx = -1;
                for (int i = 0; i < _cacheCount; i++)
                {
                    if (_cache[i].inode == pending.inode)
                    {
                        cacheIdx = i;
                        break;
                    }
                }

                if (cacheIdx >= 0 && !needsImport)
                {
                    // 缓存命中，且尺寸匹配
                    srcImage = _cache[cacheIdx].image;
                    _cache[cacheIdx].lastUsed = _frameCount;
                }
                else if (needsImport && pending.fds.Length > 0)
                {
                    // 需要导入：销毁旧缓存（如果有且尺寸变化）
                    if (cacheIdx >= 0)
                    {
                        DestroyImportedImage(_cache[cacheIdx]);
                        // 移除缓存项，压缩数组
                        for (int j = cacheIdx; j < _cacheCount - 1; j++)
                            _cache[j] = _cache[j + 1];
                        _cacheCount--;
                        cacheIdx = -1;
                    }

                    // 导入 DMA-BUF → VkImage
                    var imported = ImportDmaBufToImage(pending);
                    if (imported.image == 0)
                    {
                        GD.PrintErr($"{Tag} Failed to import DMA-BUF to Vulkan image");
                        return CopyResult.Failed;
                    }
                    srcImage = imported.image;

                    // 加入缓存
                    if (_cacheCount >= CacheMaxSize)
                    {
                        // 淘汰最旧的
                        int oldest = 0;
                        for (int i = 1; i < _cacheCount; i++)
                        {
                            if (_cache[i].lastUsed < _cache[oldest].lastUsed)
                                oldest = i;
                        }
                        DestroyImportedImage(_cache[oldest]);
                        _cache[oldest] = imported;
                    }
                    else
                    {
                        _cache[_cacheCount++] = imported;
                    }
                }
                else
                {
                    GD.PrintErr($"{Tag} No cached image and no fds to import");
                    return CopyResult.Failed;
                }

                if (srcImage == 0)
                {
                    GD.PrintErr($"{Tag} Source image is null");
                    return CopyResult.Failed;
                }

                // 提交异步 GPU 拷贝
                if (!SubmitCopyAsync(srcImage, dstImage, width, height))
                {
                    GD.PrintErr($"{Tag} SubmitCopyAsync failed");
                    return CopyResult.Failed;
                }

                _copyInFlight = true;
                _frameCount++;

                return CopyResult.Success;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"{Tag} ProcessPendingCopy failed: {ex.Message}");
                return CopyResult.Failed;
            }
        }

        /// <summary>
        /// 等待所有正在进行中的拷贝完成（阻塞）。
        /// </summary>
        public void WaitForCopy()
        {
            if (!_copyInFlight) return;

            _vkWaitForFences(_device, 1, _fence, VK_TRUE, ulong.MaxValue);
            _copyInFlight = false;
        }

        /// <summary>
        /// 创建 Godot RenderingDevice 纹理作为拷贝目标。
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
            format.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
                               RenderingDevice.TextureUsageBits.CanCopyToBit;

            var view = new RDTextureView();

            return rd.TextureCreate(format, view);
        }

        // ══════════════════════════════════════════════════════════
        //  DMA-BUF 导入
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 将 DMA-BUF fd 导入为 Vulkan 图像。
        /// 使用 VkImportMemoryFdInfoKHR 导入外部内存。
        /// </summary>
        private ImportedImage ImportDmaBufToImage(PendingLinuxCopy pending)
        {
            var result = new ImportedImage();
            ulong image = 0;
            ulong memory = 0;

            try
            {
                // 1. 准备外部内存图像创建信息
                var externalMemInfo = new VkExternalMemoryImageCreateInfo
                {
                    sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO,
                    handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_EXT,
                };

                // 2. 判断是否使用 DRM modifier
                bool useDrmModifier = pending.modifier != DRM_FORMAT_MOD_INVALID;
                int tiling = useDrmModifier
                    ? VK_IMAGE_TILING_DRM_FORMAT_MODIFIER_EXT
                    : VK_IMAGE_TILING_LINEAR;

                // 3. 创建图像
                var imageCreateInfo = new VkImageCreateInfo
                {
                    sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,
                    pNext = &externalMemInfo,
                    imageType = VK_IMAGE_TYPE_2D,
                    format = pending.format,
                    extent = new VkExtent3D
                    {
                        width = pending.width,
                        height = pending.height,
                        depth = 1,
                    },
                    mipLevels = 1,
                    arrayLayers = 1,
                    samples = VK_SAMPLE_COUNT_1_BIT,
                    tiling = (uint)tiling,
                    usage = VK_IMAGE_USAGE_TRANSFER_SRC_BIT,
                    sharingMode = VK_SHARING_MODE_EXCLUSIVE,
                    initialLayout = VK_IMAGE_LAYOUT_UNDEFINED,
                };

                var createResult = _vkCreateImage(_device, &imageCreateInfo, null, &image);
                if (createResult != VK_SUCCESS)
                {
                    GD.PrintErr($"{Tag} vkCreateImage failed: {createResult} " +
                                $"(format={pending.format}, tiling={tiling})");
                    return result;
                }

                // 4. 获取内存需求
                var memRequirements = new VkMemoryRequirements();
                _vkGetImageMemoryRequirements(_device, image, &memRequirements);

                // 5. 获取 DMA-BUF fd 的内存属性
                var fdProps = new VkMemoryFdPropertiesKHR();
                var fdResult = _vkGetMemoryFdPropertiesKHR(
                    _device,
                    VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_EXT,
                    pending.fds[0],
                    &fdProps);
                if (fdResult != VK_SUCCESS)
                {
                    GD.PrintErr($"{Tag} vkGetMemoryFdPropertiesKHR failed: {fdResult}");
                    _vkDestroyImage(_device, image, null);
                    return result;
                }

                // 6. 找到兼容的内存类型
                uint memoryTypeBits = fdProps.memoryTypeBits & memRequirements.memoryTypeBits;
                if (memoryTypeBits == 0)
                {
                    GD.PrintErr($"{Tag} No compatible memory type for DMA-BUF " +
                                $"(fd=0x{fdProps.memoryTypeBits:X}, img=0x{memRequirements.memoryTypeBits:X})");
                    _vkDestroyImage(_device, image, null);
                    return result;
                }
                uint memoryTypeIndex = FindMemoryTypeIndex(memoryTypeBits);

                // 7. 导入内存：VkImportMemoryFdInfoKHR
                var importFdInfo = new VkImportMemoryFdInfoKHR
                {
                    sType = VK_STRUCTURE_TYPE_IMPORT_MEMORY_FD_INFO_KHR,
                    handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_EXT,
                    fd = pending.fds[0],
                };
                pending.fds[0] = -1; // 所有权转移给 Vulkan

                var dedicatedInfo = new VkMemoryDedicatedAllocateInfo
                {
                    sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO,
                    image = image,
                };

                // 链式结构：dedicatedInfo → importFdInfo
                importFdInfo.pNext = &dedicatedInfo;

                var allocInfo = new VkMemoryAllocateInfo
                {
                    sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                    pNext = &importFdInfo,
                    allocationSize = memRequirements.size,
                    memoryTypeIndex = memoryTypeIndex,
                };

                var allocResult = _vkAllocateMemory(_device, &allocInfo, null, &memory);
                if (allocResult != VK_SUCCESS)
                {
                    GD.PrintErr($"{Tag} vkAllocateMemory (DMA-BUF import) failed: {allocResult} " +
                                $"(typeIdx={memoryTypeIndex}, size={memRequirements.size})");
                    _vkDestroyImage(_device, image, null);
                    return result;
                }

                // 8. 绑定图像内存
                var bindResult = _vkBindImageMemory(_device, image, memory, 0);
                if (bindResult != VK_SUCCESS)
                {
                    GD.PrintErr($"{Tag} vkBindImageMemory failed: {bindResult}");
                    _vkFreeMemory(_device, memory, null);
                    _vkDestroyImage(_device, image, null);
                    return result;
                }

                result.image = image;
                result.memory = memory;
                result.inode = pending.inode;
                result.width = pending.width;
                result.height = pending.height;
                result.lastUsed = _frameCount;

                return result;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"{Tag} ImportDmaBufToImage exception: {ex.Message}");
                if (image != 0) _vkDestroyImage(_device, image, null);
                if (memory != 0) _vkFreeMemory(_device, memory, null);
                return result;
            }
        }

        /// <summary>
        /// 提交异步 GPU 拷贝：记录 barrier + vkCmdCopyImage + barrier，提交到队列。
        /// </summary>
        private bool SubmitCopyAsync(ulong srcImage, ulong dstImage, uint width, uint height)
        {
            // 1. Reset fence 和 command buffer
            _vkResetFences(_device, 1, _fence);
            _vkResetCommandBuffer(_commandBuffer, 0);

            // 2. Begin command buffer
            var beginInfo = new VkCommandBufferBeginInfo
            {
                sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
                flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT,
            };
            _vkBeginCommandBuffer(_commandBuffer, &beginInfo);

            // 3. Pipeline barriers
            var subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = 1,
            };

            // Barrier 1: src UNDEFINED → TRANSFER_SRC_OPTIMAL
            var srcBarrier = new VkImageMemoryBarrier
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                oldLayout = VK_IMAGE_LAYOUT_UNDEFINED,
                newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                image = srcImage,
                subresourceRange = subresourceRange,
                srcAccessMask = 0,
                dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT,
            };

            // Barrier 2: dst UNDEFINED → TRANSFER_DST_OPTIMAL
            var dstBarrier = new VkImageMemoryBarrier
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                oldLayout = VK_IMAGE_LAYOUT_UNDEFINED,
                newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                image = dstImage,
                subresourceRange = subresourceRange,
                srcAccessMask = 0,
                dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT,
            };

            var barriers = stackalloc VkImageMemoryBarrier[2];
            barriers[0] = srcBarrier;
            barriers[1] = dstBarrier;

            _vkCmdPipelineBarrier(
                _commandBuffer,
                VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                VK_PIPELINE_STAGE_TRANSFER_BIT,
                0, 0, null, 0, null, 2, barriers);

            // 4. vkCmdCopyImage
            var subresourceLayers = new VkImageSubresourceLayers
            {
                aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                mipLevel = 0,
                baseArrayLayer = 0,
                layerCount = 1,
            };

            var copyRegion = new VkImageCopy
            {
                srcSubresource = subresourceLayers,
                srcOffset = new VkOffset3D { x = 0, y = 0, z = 0 },
                dstSubresource = subresourceLayers,
                dstOffset = new VkOffset3D { x = 0, y = 0, z = 0 },
                extent = new VkExtent3D { width = width, height = height, depth = 1 },
            };

            _vkCmdCopyImage(
                _commandBuffer,
                srcImage, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                dstImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                1, &copyRegion);

            // 5. Final barrier: dst TRANSFER_DST → SHADER_READ_ONLY
            var finalBarrier = new VkImageMemoryBarrier
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                image = dstImage,
                subresourceRange = subresourceRange,
                srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT,
                dstAccessMask = VK_ACCESS_SHADER_READ_BIT,
            };

            _vkCmdPipelineBarrier(
                _commandBuffer,
                VK_PIPELINE_STAGE_TRANSFER_BIT,
                VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
                0, 0, null, 0, null, 1, &finalBarrier);

            // 6. End command buffer
            _vkEndCommandBuffer(_commandBuffer);

            // 7. Submit（使用局部变量取地址，字段在堆上需要 fixed）
            var cmdBufLocal = _commandBuffer;
            var submitInfo = new VkSubmitInfo
            {
                sType = VK_STRUCTURE_TYPE_SUBMIT_INFO,
                commandBufferCount = 1,
                pCommandBuffers = &cmdBufLocal,
            };

            var submitResult = _vkQueueSubmit(_queue, 1, &submitInfo, _fence);
            if (submitResult != VK_SUCCESS)
            {
                GD.PrintErr($"{Tag} vkQueueSubmit failed: {submitResult}");
                return false;
            }

            return true;
        }

        // ══════════════════════════════════════════════════════════
        //  辅助方法
        // ══════════════════════════════════════════════════════════

        private static uint FindMemoryTypeIndex(uint typeBits)
        {
            if (typeBits == 0) return 0;
            // 找到最低位 1 的位置
            uint idx = 0;
            while ((typeBits & 1) == 0)
            {
                typeBits >>= 1;
                idx++;
            }
            return idx;
        }

        private void DestroyImportedImage(ImportedImage img)
        {
            if (img.image != 0)
                _vkDestroyImage(_device, img.image, null);
            if (img.memory != 0)
                _vkFreeMemory(_device, img.memory, null);
        }

        private static int CefColorTypeToVkFormat(int cefFormat)
        {
            // CEF_COLOR_TYPE_RGBA_8888 = 0, CEF_COLOR_TYPE_BGRA_8888 = 1
            // 内存顺序映射到 Vulkan 格式
            return cefFormat switch
            {
                0 => VK_FORMAT_R8G8B8A8_UNORM,   // CEF RGBA → Vulkan R8G8B8A8
                1 => VK_FORMAT_B8G8R8A8_UNORM,   // CEF BGRA → Vulkan B8G8R8A8
                _ => VK_FORMAT_B8G8R8A8_UNORM,   // 默认 BGRA
            };
        }

        // ══════════════════════════════════════════════════════════
        //  Dispose
        // ══════════════════════════════════════════════════════════

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                WaitForCopy();

                // 清理 pending 拷贝
                lock (_srcLock)
                {
                    _retiredCopy.Dispose();
                    _pendingCopy.Dispose();
                }

                // 清理导入图像缓存
                for (int i = 0; i < _cacheCount; i++)
                {
                    DestroyImportedImage(_cache[i]);
                }
                _cacheCount = 0;

                if (_fence != 0)
                {
                    _vkDestroyFence(_device, _fence, null);
                    _fence = 0;
                }

                if (_commandBuffer != 0)
                {
                    // command buffer 由 command pool 自动释放
                    _commandBuffer = 0;
                }

                if (_commandPool != 0)
                {
                    _vkDestroyCommandPool(_device, _commandPool, null);
                    _commandPool = 0;
                }

                // 注意：device / physicalDevice / queue 由 Godot 拥有，不销毁
                _device = 0;
                _physicalDevice = 0;
                _queue = 0;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"{Tag} Error during Dispose: {ex.Message}");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  CEF 数据结构 — Linux DMA-BUF
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// CEF AcceleratedPaintInfo（Linux DMA-BUF 版本）。
    /// 匹配 CEF C API 的 cef_accelerated_paint_info_t 布局。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CefAcceleratedPaintInfo
    {
        public int plane_count;
        public int format;       // CefColorType 枚举值
        public ulong modifier;   // DRM format modifier
        public int coded_width;
        public int coded_height;
        // 后面跟着 CefAcceleratedPaintPlane[plane_count]
    }

    /// <summary>
    /// CEF AcceleratedPaintPlane（Linux DMA-BUF）。
    /// 匹配 CEF C API 的 cef_accelerated_paint_plane_t 布局。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CefAcceleratedPaintPlane
    {
        public int fd;
        public int stride;
        public int offset;
        public int size;
    }

    // ══════════════════════════════════════════════════════════════
    //  内部数据结构
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 待处理的 DMA-BUF 拷贝信息。
    /// </summary>
    internal struct PendingLinuxCopy
    {
        public ulong inode;
        public bool needsImport;
        public int format;       // VkFormat
        public uint width;
        public uint height;
        public ulong modifier;
        public int[] fds;        // dup 后的 DMA-BUF fd(s)
        public uint[] strides;
        public ulong[] offsets;

        public void Dispose()
        {
            if (fds == null) return;
            foreach (var fd in fds)
            {
                if (fd >= 0)
                    Libc.close(fd);
            }
            fds = null;
        }
    }

    /// <summary>
    /// 已导入的 Vulkan 图像缓存条目。
    /// </summary>
    internal struct ImportedImage
    {
        public ulong image;   // VkImage
        public ulong memory;  // VkDeviceMemory
        public ulong inode;
        public uint width;
        public uint height;
        public ulong lastUsed;
    }

    // ══════════════════════════════════════════════════════════════
    //  Vulkan 结构体定义（最小化 — 仅实际使用的结构体）
    // ══════════════════════════════════════════════════════════════

    // ── Vulkan 结构体 ──

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkExtent3D
    {
        public uint width;
        public uint height;
        public uint depth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkOffset3D
    {
        public int x;
        public int y;
        public int z;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkImageSubresourceLayers
    {
        public uint aspectMask;
        public uint mipLevel;
        public uint baseArrayLayer;
        public uint layerCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkImageSubresourceRange
    {
        public uint aspectMask;
        public uint baseMipLevel;
        public uint levelCount;
        public uint baseArrayLayer;
        public uint layerCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkImageCopy
    {
        public VkImageSubresourceLayers srcSubresource;
        public VkOffset3D srcOffset;
        public VkImageSubresourceLayers dstSubresource;
        public VkOffset3D dstOffset;
        public VkExtent3D extent;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkImageMemoryBarrier
    {
        public uint sType;
        public void* pNext;
        public uint srcAccessMask;
        public uint dstAccessMask;
        public uint oldLayout;
        public uint newLayout;
        public uint srcQueueFamilyIndex;
        public uint dstQueueFamilyIndex;
        public ulong image;
        public VkImageSubresourceRange subresourceRange;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkSubmitInfo
    {
        public uint sType;
        public void* pNext;
        public uint waitSemaphoreCount;
        public ulong* pWaitSemaphores;
        public ulong* pWaitDstStageMask;
        public uint commandBufferCount;
        public ulong* pCommandBuffers;
        public uint signalSemaphoreCount;
        public ulong* pSignalSemaphores;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkCommandBufferBeginInfo
    {
        public uint sType;
        public void* pNext;
        public uint flags;
        public void* pInheritanceInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkCommandPoolCreateInfo
    {
        public uint sType;
        public void* pNext;
        public uint flags;
        public uint queueFamilyIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkCommandBufferAllocateInfo
    {
        public uint sType;
        public void* pNext;
        public ulong commandPool;
        public uint level;
        public uint commandBufferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkFenceCreateInfo
    {
        public uint sType;
        public void* pNext;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkImageCreateInfo
    {
        public uint sType;
        public void* pNext;
        public uint flags;
        public uint imageType;
        public int format;
        public VkExtent3D extent;
        public uint mipLevels;
        public uint arrayLayers;
        public uint samples;
        public uint tiling;
        public uint usage;
        public uint sharingMode;
        public uint queueFamilyIndexCount;
        public void* pQueueFamilyIndices;
        public uint initialLayout;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkMemoryRequirements
    {
        public ulong size;
        public ulong alignment;
        public uint memoryTypeBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkMemoryAllocateInfo
    {
        public uint sType;
        public void* pNext;
        public ulong allocationSize;
        public uint memoryTypeIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkExternalMemoryImageCreateInfo
    {
        public uint sType;
        public void* pNext;
        public uint handleTypes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkImportMemoryFdInfoKHR
    {
        public uint sType;
        public void* pNext;
        public uint handleType;
        public int fd;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkMemoryDedicatedAllocateInfo
    {
        public uint sType;
        public void* pNext;
        public ulong image;
        public ulong buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkMemoryFdPropertiesKHR
    {
        public uint sType;
        public void* pNext;
        public uint memoryTypeBits;
    }

    // ══════════════════════════════════════════════════════════════
    //  Vulkan 函数指针委托
    // ══════════════════════════════════════════════════════════════

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate IntPtr PFN_vkGetDeviceProcAddr(ulong device, [MarshalAs(UnmanagedType.LPStr)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PFN_vkGetDeviceQueue(ulong device, uint queueFamilyIndex, uint queueIndex, out ulong queue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate int PFN_vkCreateCommandPool(ulong device, VkCommandPoolCreateInfo* pCreateInfo, void* pAllocator, ulong* pCommandPool);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate void PFN_vkDestroyCommandPool(ulong device, ulong commandPool, void* pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate int PFN_vkAllocateCommandBuffers(ulong device, VkCommandBufferAllocateInfo* pAllocateInfo, ulong* pCommandBuffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate int PFN_vkCreateFence(ulong device, VkFenceCreateInfo* pCreateInfo, void* pAllocator, ulong* pFence);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate void PFN_vkDestroyFence(ulong device, ulong fence, void* pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate int PFN_vkBeginCommandBuffer(ulong commandBuffer, VkCommandBufferBeginInfo* pBeginInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int PFN_vkEndCommandBuffer(ulong commandBuffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate void PFN_vkCmdPipelineBarrier(
        ulong commandBuffer,
        uint srcStageMask,
        uint dstStageMask,
        uint dependencyFlags,
        uint memoryBarrierCount,
        void* pMemoryBarriers,
        uint bufferMemoryBarrierCount,
        void* pBufferMemoryBarriers,
        uint imageMemoryBarrierCount,
        VkImageMemoryBarrier* pImageMemoryBarriers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate void PFN_vkCmdCopyImage(
        ulong commandBuffer,
        ulong srcImage,
        uint srcImageLayout,
        ulong dstImage,
        uint dstImageLayout,
        uint regionCount,
        VkImageCopy* pRegions);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate int PFN_vkQueueSubmit(ulong queue, uint submitCount, VkSubmitInfo* pSubmits, ulong fence);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int PFN_vkWaitForFences(ulong device, uint fenceCount, ulong fence, ulong waitAll, ulong timeout);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int PFN_vkResetFences(ulong device, uint fenceCount, ulong fence);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int PFN_vkResetCommandBuffer(ulong commandBuffer, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate int PFN_vkCreateImage(ulong device, VkImageCreateInfo* pCreateInfo, void* pAllocator, ulong* pImage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate void PFN_vkDestroyImage(ulong device, ulong image, void* pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate void PFN_vkGetImageMemoryRequirements(ulong device, ulong image, VkMemoryRequirements* pMemoryRequirements);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate int PFN_vkAllocateMemory(ulong device, VkMemoryAllocateInfo* pAllocateInfo, void* pAllocator, ulong* pMemory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate void PFN_vkFreeMemory(ulong device, ulong memory, void* pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int PFN_vkBindImageMemory(ulong device, ulong image, ulong memory, ulong memoryOffset);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate int PFN_vkGetMemoryFdPropertiesKHR(
        ulong device,
        uint handleType,
        int fd,
        VkMemoryFdPropertiesKHR* pMemoryFdProperties);

    // ══════════════════════════════════════════════════════════════
    //  libc flat API — DMA-BUF fd 操作
    // ══════════════════════════════════════════════════════════════

    internal static unsafe class Libc
    {
        [DllImport("libc", SetLastError = true)]
        public static extern int dup(int fd);

        [DllImport("libc", SetLastError = true)]
        public static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        public static extern int fstat(int fd, out Stat buf);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Stat
    {
        // 只包含需要的字段
        public ulong st_dev;
        public ulong st_ino;
        public ulong st_nlink;
        public uint st_mode;
        public uint st_uid;
        public uint st_gid;
        public int st_pad0;
        public ulong st_rdev;
        public long st_size;
        public long st_blksize;
        public long st_blocks;
        // 忽略时间戳等后续字段
    }
}

#endif // GD_GPU_LINUX