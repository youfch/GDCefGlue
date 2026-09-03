using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

#if GD_GPU_WINDOWS
namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  Windows Vulkan GPU 纹理拷贝器 — 通过 VK_KHR_external_memory_win32
    //  条件编译：仅 GD_GPU_WINDOWS 有效（csproj: $([MSBuild]::IsOSPlatform('Windows'))）
    //
    //  使用原生 Vulkan P/Invoke 直接调用 vulkan-1.dll，不依赖任何绑定库
    //  （NativeAOT 安全）。流程（对齐 godot-cef 的 Rust 实现）：
    //
    //  1. 从 Godot 的 RenderingDevice 拿到 VkDevice / VkPhysicalDevice（u64 句柄）
    //  2. 通过 vkGetDeviceProcAddr 按需解析设备级函数指针
    //  3. 找一个独立队列（或回退到队列 0）做拷贝，避免与 Godot 主队列同步冲突
    //  4. QueueCopy（CEF 线程）：DuplicateHandle 复制 CEF 的 D3D11 共享句柄以延长生命周期
    //  5. ProcessPendingCopy（Godot 主线程）：
    //     - vkGetMemoryWin32HandlePropertiesKHR 查询可用的内存类型
    //     - VkImportMemoryWin32HandleInfoKHR(handle_type=D3D11_TEXTURE) 导入内存
    //     - VkExternalMemoryImageCreateInfo 创建外部内存图像
    //     - vkCmdCopyImage 拷贝到 Godot 目标纹理（含 pipeline barrier）
    //     - 独立 command buffer + fence，非阻塞轮询（timeout=0），超时返回 RetryLater
    //  6. 双缓冲：2 个 command buffer + 2 个 fence，避免阻塞主线程
    //
    //  注意：Godot 默认不会启用 VK_KHR_external_memory_win32 扩展，需要由独立的
    //  Vulkan Layer（C 项目，非本文件职责）注入。若 vkGetMemoryWin32HandlePropertiesKHR
    //  无法解析，说明 Layer 未安装，TryCreate 返回 null，走 CPU 回退。
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Windows Vulkan 外部内存 GPU 纹理拷贝器。
    /// 将 CEF（Chromium）的 D3D11 共享纹理跨 API 导入到 Godot 的 Vulkan 后端。
    /// </summary>
    internal unsafe class WindowsVulkanTextureCopier : ITextureCopier
    {
        // ── Vulkan 常量 ──
        private const int VK_SUCCESS = 0;
        private const int VK_TIMEOUT = 2;
        private const uint VK_TRUE = 1;
        private const uint VK_QUEUE_FAMILY_IGNORED = 0xFFFFFFFFu;

        // VkStructureType
        private const int VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO = 0;
        private const int VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO = 23;
        private const int VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO = 36;
        private const int VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO = 40;
        private const int VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO = 44;
        private const int VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER = 46;
        private const int VK_STRUCTURE_TYPE_FENCE_CREATE_INFO = 52;
        private const int VK_STRUCTURE_TYPE_SUBMIT_INFO = 4;
        private const int VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO = 1000067003;
        private const int VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO = 1000072004;
        private const int VK_STRUCTURE_TYPE_IMPORT_MEMORY_WIN32_HANDLE_INFO_KHR = 1000073000;
        private const int VK_STRUCTURE_TYPE_MEMORY_WIN32_HANDLE_PROPERTIES_KHR = 1000073001;

        // VkImageType / VkFormat / VkTiling / VkSharingMode / VkSampleCount
        private const int VK_IMAGE_TYPE_2D = 1;
        private const int VK_FORMAT_B8G8R8A8_SRGB = 50;
        private const int VK_IMAGE_TILING_OPTIMAL = 0;
        private const int VK_SHARING_MODE_EXCLUSIVE = 0;
        private const uint VK_SAMPLE_COUNT_1_BIT = 1;

        // VkImageLayout
        private const int VK_IMAGE_LAYOUT_UNDEFINED = 0;
        private const int VK_IMAGE_LAYOUT_GENERAL = 1;
        private const int VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL = 5;
        private const int VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL = 6;

        // VkImageUsageFlags / VkAccessFlags / VkPipelineStageFlags
        private const uint VK_IMAGE_USAGE_TRANSFER_SRC_BIT = 0x00000001u;
        private const uint VK_ACCESS_TRANSFER_READ_BIT = 0x00000800u;
        private const uint VK_ACCESS_TRANSFER_WRITE_BIT = 0x00001000u;
        private const uint VK_ACCESS_SHADER_READ_BIT = 0x00000020u;
        private const uint VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT = 0x00000001u;
        private const uint VK_PIPELINE_STAGE_TRANSFER_BIT = 0x00000400u;
        private const uint VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT = 0x00000080u;
        private const uint VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT = 0x00000002u;

        // VkImageAspectFlags / VkCommandBufferUsage / VkCommandBufferLevel
        private const uint VK_IMAGE_ASPECT_COLOR_BIT = 1u;
        private const uint VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT = 1u;
        private const uint VK_COMMAND_BUFFER_LEVEL_PRIMARY = 0u;

        // VkCommandPoolCreateFlags / VkFenceCreateFlags
        private const uint VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT = 2u;
        private const uint VK_FENCE_CREATE_SIGNALED_BIT = 1u;

        // VkQueueFlags
        private const uint VK_QUEUE_GRAPHICS_BIT = 1u;
        private const uint VK_QUEUE_COMPUTE_BIT = 2u;
        private const uint VK_QUEUE_TRANSFER_BIT = 4u;

        // VkExternalMemoryHandleTypeFlags
        private const uint VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT = 0x00000080u;

        // D3D11on12 目标纹理的固定布局（对齐 godot-cef 的 SHADER_READ_ONLY_OPTIMAL 终态）
        private const int DST_LAYOUT_FINAL = 3; // VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL

        // ── Godot 提供的设备句柄（不销毁，由 Godot 管理） ──
        private readonly ulong _device;
        private readonly ulong _physicalDevice;

        // ── 我们创建并拥有的资源 ──
        private readonly ulong _commandPool;
        private readonly ulong[] _commandBuffers = new ulong[2];
        private readonly ulong[] _fences = new ulong[2];
        private readonly ulong _queue;
        private readonly uint _queueFamilyIndex;
        private readonly bool _usesSeparateQueue;
        private readonly bool[] _framesInFlight = new bool[2];
        private int _currentFrame;
        private ulong _frameCount;

        // ── 已加载的 Vulkan 设备函数指针 ──
        private readonly VulkanFunctions _fns;

        // ── 缓存：句柄值 → 已导入的 Vulkan 图像（含双份句柄） ──
        private readonly Dictionary<long, ImportedVulkanImage> _cache = new();
        private uint? _cachedMemoryTypeIndex;

        // ── 待处理拷贝（线程安全：QueueCopy 在 CEF 线程，ProcessPendingCopy 在 Godot 主线程） ──
        private readonly object _pendingLock = new();
        private PendingVulkanCopy _pendingCopy;

        private bool _disposed;

        private WindowsVulkanTextureCopier(
            ulong device,
            ulong physicalDevice,
            ulong commandPool,
            ulong[] commandBuffers,
            ulong[] fences,
            ulong queue,
            uint queueFamilyIndex,
            bool usesSeparateQueue,
            VulkanFunctions fns)
        {
            _device = device;
            _physicalDevice = physicalDevice;
            _commandPool = commandPool;
            _commandBuffers = commandBuffers;
            _fences = fences;
            _queue = queue;
            _queueFamilyIndex = queueFamilyIndex;
            _usesSeparateQueue = usesSeparateQueue;
            _fns = fns;
        }

        public static WindowsVulkanTextureCopier TryCreate()
        {
            try
            {
                var rd = RenderingServer.Singleton.GetRenderingDevice();
                if (rd == null)
                {
                    GD.PrintErr("[Vulkan/Win] Failed to get RenderingDevice");
                    return null;
                }

                // 从 Godot 拿到 Vulkan 设备（仅在 Vulkan 渲染后端下有效）。
                var devicePtr = rd.GetDriverResource(
                    RenderingDevice.DriverResource.LogicalDevice, new Rid(), 0);
                if (devicePtr == 0)
                {
                    GD.PrintErr("[Vulkan/Win] Failed to get Vulkan device from Godot");
                    return null;
                }
                var device = devicePtr;

                var physicalDevicePtr = rd.GetDriverResource(
                    RenderingDevice.DriverResource.PhysicalDevice, new Rid(), 0);
                var physicalDevice = physicalDevicePtr;

                // 加载 vulkan-1.dll 并解析设备级函数指针。
                var fns = VulkanFunctions.Load(device);
                if (fns == null)
                {
                    // 关键路径：vkGetMemoryWin32HandlePropertiesKHR 无法解析
                    // → Vulkan Layer 未安装，返回 null 走 CPU 回退。
                    GD.PrintErr(
                        "[Vulkan/Win] vkGetMemoryWin32HandlePropertiesKHR unavailable. " +
                        "The GDCefGlue Vulkan layer that enables VK_KHR_external_memory_win32 " +
                        "is not installed. Falling back to CPU copy.");
                    return null;
                }

                // 寻找独立拷贝队列（避免与 Godot 主队列同步冲突）。
                var (queueFamilyIndex, queueIndex, usesSeparateQueue) =
                    FindCopyQueue(physicalDevice);

                var queue = fns.GetDeviceQueue(device, queueFamilyIndex, queueIndex);
                if (queue == 0)
                {
                    GD.Print("[Vulkan/Win] Preferred queue unavailable, falling back to queue 0");
                    queue = fns.GetDeviceQueue(device, 0, 0);
                    queueFamilyIndex = 0;
                    queueIndex = 0;
                    usesSeparateQueue = false;
                }

                if (queue == 0)
                {
                    GD.PrintErr("[Vulkan/Win] Failed to get any Vulkan queue");
                    return null;
                }

                // 为拷贝队列创建 command pool（RESET_COMMAND_BUFFER 标志）。
                var poolCreateInfo = new VkCommandPoolCreateInfo
                {
                    sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
                    flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
                    queueFamilyIndex = queueFamilyIndex,
                };
                var commandPool = fns.CreateCommandPool(device, ref poolCreateInfo);
                if (commandPool == 0)
                {
                    GD.PrintErr("[Vulkan/Win] Failed to create command pool");
                    return null;
                }

                // 分配 2 个 command buffer（双缓冲）。
                var allocInfo = new VkCommandBufferAllocateInfo
                {
                    sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
                    commandPool = commandPool,
                    level = VK_COMMAND_BUFFER_LEVEL_PRIMARY,
                    commandBufferCount = 2,
                };
                var commandBuffers = new ulong[2];
                if (!fns.AllocateCommandBuffers(device, ref allocInfo, commandBuffers))
                {
                    GD.PrintErr("[Vulkan/Win] Failed to allocate command buffers");
                    fns.DestroyCommandPool(device, commandPool);
                    return null;
                }

                // 创建 2 个 fence（初始为 SIGNALED，保证首次 reset 不失败）。
                var fenceCreateInfo = new VkFenceCreateInfo
                {
                    sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO,
                    flags = VK_FENCE_CREATE_SIGNALED_BIT,
                };
                var fences = new ulong[2];
                for (int i = 0; i < 2; i++)
                {
                    fences[i] = fns.CreateFence(device, ref fenceCreateInfo);
                    if (fences[i] == 0)
                    {
                        GD.PrintErr($"[Vulkan/Win] Failed to create fence {i}");
                        for (int j = 0; j < i; j++)
                            fns.DestroyFence(device, fences[j]);
                        fns.DestroyCommandPool(device, commandPool);
                        return null;
                    }
                }

                if (usesSeparateQueue)
                {
                    GD.Print(
                        $"[Vulkan/Win] Using separate queue (family={queueFamilyIndex}, index={queueIndex}) for texture copies");
                }
                else
                {
                    GD.Print("[Vulkan/Win] Using shared graphics queue - may have sync issues under load");
                }

                return new WindowsVulkanTextureCopier(
                    device, physicalDevice, commandPool, commandBuffers, fences,
                    queue, queueFamilyIndex, usesSeparateQueue, fns);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Vulkan/Win] Initialization failed: {ex.Message}");
                return null;
            }
        }

        public bool IsValid => !_disposed && _device != 0;

        // ──────────────────────────────────────────────────────────────
        //  ITextureCopier 接口实现
        // ──────────────────────────────────────────────────────────────

        public CopyResult QueueCopy(IntPtr sharedTextureHandle, int width, int height)
        {
            if (sharedTextureHandle == IntPtr.Zero || width <= 0 || height <= 0)
                return CopyResult.Failed;

            var handleVal = sharedTextureHandle.ToInt64();

            IntPtr duplicatedHandle = IntPtr.Zero;
            bool needsImport = true;

            // 若已缓存且尺寸一致，无需再次导入（也就不需要复制句柄）。
            if (_cache.TryGetValue(handleVal, out var cached))
            {
                needsImport = cached.Width != width || cached.Height != height;
            }

            if (needsImport)
            {
                // DuplicateHandle 以延长 CEF 共享句柄的生命周期。非阻塞、开销极小。
                if (!Kernel32.DuplicateHandle(
                        Kernel32.GetCurrentProcess(),
                        sharedTextureHandle,
                        Kernel32.GetCurrentProcess(),
                        out duplicatedHandle,
                        0,
                        false,
                        Kernel32.DUPLICATE_SAME_ACCESS))
                {
                    GD.PrintErr($"[Vulkan/Win] DuplicateHandle failed: 0x{Marshal.GetLastWin32Error():X8}");
                    return CopyResult.Failed;
                }
            }

            lock (_pendingLock)
            {
                // 替换旧的 pending（Drop 旧对象会关闭其持有的句柄）。
                _pendingCopy = new PendingVulkanCopy(handleVal, duplicatedHandle, width, height);
            }

            return CopyResult.Success;
        }

        public CopyResult ProcessPendingCopy(Rid dstRdRid)
        {
            PendingVulkanCopy pending;
            lock (_pendingLock)
            {
                pending = _pendingCopy;
                _pendingCopy = null;
            }

            if (pending == null)
                return CopyResult.Success; // 没有待处理拷贝

            if (!dstRdRid.IsValid)
            {
                GD.PrintErr("[Vulkan/Win] Invalid destination RID");
                pending.Dispose();
                return CopyResult.Failed;
            }

            // 非阻塞等待当前帧的 fence（timeout=0）。若仍在运行则把 pending 放回去下一帧再试。
            if (_framesInFlight[_currentFrame])
            {
                int result = _fns.WaitForFences(_device, 1, ref _fences[_currentFrame], VK_TRUE, 0);
                if (result == VK_TIMEOUT)
                {
                    // 上一帧仍在飞行，跳过本帧避免阻塞主线程。
                    lock (_pendingLock)
                    {
                        _pendingCopy = pending;
                    }
                    return CopyResult.RetryLater;
                }
                if (result != VK_SUCCESS)
                {
                    GD.PrintErr($"[Vulkan/Win] Failed to wait for fence: {result}");
                    pending.Dispose();
                    return CopyResult.Failed;
                }
                _framesInFlight[_currentFrame] = false;
            }

            // 若尺寸变化，使缓存中的旧图像失效。
            if (_cache.TryGetValue(pending.SourceHandle, out var cachedEntry)
                && (cachedEntry.Width != pending.Width || cachedEntry.Height != pending.Height))
            {
                _cache.Remove(pending.SourceHandle);
                DestroyImportedImage(cachedEntry);
            }

            // 若不在缓存，执行导入。
            if (!_cache.ContainsKey(pending.SourceHandle))
            {
                if (!pending.HasDuplicatedHandle)
                {
                    GD.PrintErr("[Vulkan/Win] Missing duplicated handle for new import");
                    pending.Dispose();
                    return CopyResult.Failed;
                }

                var imported = ImportHandleToImageFromDuplicated(
                    pending.DuplicatedHandle, pending.Width, pending.Height);
                if (imported == null)
                {
                    pending.Dispose();
                    return CopyResult.Failed;
                }
                _cache[pending.SourceHandle] = imported;
            }

            var src = _cache[pending.SourceHandle];
            src.LastUsed = _frameCount;
            ulong srcImage = src.Image;

            // 获取 Godot 目标 Vulkan 图像。
            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null)
            {
                pending.Dispose();
                return CopyResult.Failed;
            }
            ulong dstImage = rd.GetDriverResource(
                RenderingDevice.DriverResource.Texture, dstRdRid, 0);
            if (dstImage == 0)
            {
                GD.PrintErr("[Vulkan/Win] Failed to get destination Vulkan image");
                pending.Dispose();
                return CopyResult.Failed;
            }

            // 提交 GPU 拷贝（非阻塞）。
            if (!SubmitCopyAsync(srcImage, dstImage, pending.Width, pending.Height))
            {
                pending.Dispose();
                return CopyResult.Failed;
            }
            _framesInFlight[_currentFrame] = true;

            // 推进到下一个帧槽。
            _currentFrame = (_currentFrame + 1) % 2;
            _frameCount++;

            // 简单 LRU 淘汰：缓存超过 10 个时移除最旧的。
            if (_cache.Count > 10)
            {
                long oldestKey = 0;
                ulong oldestTime = ulong.MaxValue;
                foreach (var kv in _cache)
                {
                    if (kv.Value.LastUsed < oldestTime)
                    {
                        oldestTime = kv.Value.LastUsed;
                        oldestKey = kv.Key;
                    }
                }
                if (_cache.Remove(oldestKey, out var removed))
                    DestroyImportedImage(removed);
            }

            return CopyResult.Success;
        }

        public void WaitForCopy()
        {
            for (int i = 0; i < 2; i++)
            {
                if (_framesInFlight[i])
                {
                    int result = _fns.WaitForFences(_device, 1, ref _fences[i], VK_TRUE, ulong.MaxValue);
                    if (result != VK_SUCCESS)
                        GD.PrintErr($"[Vulkan/Win] Failed to wait for fence {i}: {result}");
                    _framesInFlight[i] = false;
                }
            }
        }

        public Rid CreateDestinationTexture(int width, int height)
        {
            var rd = RenderingServer.Singleton.GetRenderingDevice();
            if (rd == null) return new Rid();

            // 创建标准 GPU 纹理（对齐 GpuTextureCopier.Windows.cs）。
            // 必须 AddShareableFormat 标记纹理为可共享，否则 Vulkan 后端不分配可外部访问资源。
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                WaitForCopy();

                lock (_pendingLock)
                {
                    _pendingCopy?.Dispose();
                    _pendingCopy = null;
                }

                // 清理导入缓存。
                foreach (var kv in _cache)
                    DestroyImportedImage(kv.Value);
                _cache.Clear();

                for (int i = 0; i < 2; i++)
                {
                    if (_fences[i] != 0)
                    {
                        _fns.DestroyFence(_device, _fences[i]);
                        _fences[i] = 0;
                    }
                }
                if (_commandPool != 0)
                {
                    _fns.DestroyCommandPool(_device, _commandPool);
                }
                // 注意：device/queue 归 Godot 所有，不销毁。
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Vulkan/Win] Error during Dispose: {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  内部实现
        // ──────────────────────────────────────────────────────────────

        private ImportedVulkanImage ImportHandleToImageFromDuplicated(
            IntPtr duplicatedHandle, int width, int height)
        {
            // 创建带外部内存标志的图像。
            var externalMemoryInfo = new VkExternalMemoryImageCreateInfo
            {
                sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO,
                handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT,
            };

            var imageInfo = new VkImageCreateInfo
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,
                pNext = &externalMemoryInfo, // 链式传递外部内存信息
                imageType = VK_IMAGE_TYPE_2D,
                format = VK_FORMAT_B8G8R8A8_SRGB,
                extent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
                mipLevels = 1,
                arrayLayers = 1,
                samples = VK_SAMPLE_COUNT_1_BIT,
                tiling = VK_IMAGE_TILING_OPTIMAL,
                usage = VK_IMAGE_USAGE_TRANSFER_SRC_BIT,
                sharingMode = VK_SHARING_MODE_EXCLUSIVE,
                initialLayout = VK_IMAGE_LAYOUT_UNDEFINED,
            };

            ulong image;
            if (_fns.CreateImage(_device, ref imageInfo, out image) == 0)
            {
                GD.PrintErr("[Vulkan/Win] Failed to create image");
                return null;
            }

            ulong memory;
            if (!ImportMemoryForImage(duplicatedHandle, image, width, height, out memory))
            {
                _fns.DestroyImage(_device, image);
                return null;
            }

            return new ImportedVulkanImage(duplicatedHandle, image, memory, width, height, _frameCount);
        }

        private bool ImportMemoryForImage(
            IntPtr handle, ulong image, int width, int height, out ulong memory)
        {
            memory = 0;

            // 获取/缓存内存类型索引（同一句柄类型可复用）。
            uint memoryTypeIndex;
            if (_cachedMemoryTypeIndex.HasValue)
            {
                memoryTypeIndex = _cachedMemoryTypeIndex.Value;
            }
            else
            {
                // 查询该 Win32 句柄支持的内存类型位。
                var handleProps = new VkMemoryWin32HandlePropertiesKHR
                {
                    sType = VK_STRUCTURE_TYPE_MEMORY_WIN32_HANDLE_PROPERTIES_KHR,
                };
                int result = _fns.GetMemoryWin32HandlePropertiesKHR(
                    _device, VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT, handle, ref handleProps);
                if (result != VK_SUCCESS)
                {
                    GD.PrintErr($"[Vulkan/Win] vkGetMemoryWin32HandlePropertiesKHR failed: {result}");
                    return false;
                }

                uint typeFilter = handleProps.memoryTypeBits;
                if (typeFilter == 0)
                {
                    GD.PrintErr("[Vulkan/Win] No usable memory type bits for handle");
                    return false;
                }
                // memoryTypeBits 是位掩码，取最低有效位即为第一个可用内存类型索引。
                memoryTypeIndex = CountTrailingZeros(typeFilter);
                _cachedMemoryTypeIndex = memoryTypeIndex;
            }

            // 使用带有 Win32 句柄的导入信息。
            var importInfo = new VkImportMemoryWin32HandleInfoKHR
            {
                sType = VK_STRUCTURE_TYPE_IMPORT_MEMORY_WIN32_HANDLE_INFO_KHR,
                handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT,
                handle = handle,
                name = IntPtr.Zero,
            };

            var dedicatedInfo = new VkMemoryDedicatedAllocateInfo
            {
                sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO,
                image = image,
                pNext = &importInfo, // 链式传递导入信息
            };

            ulong allocationSize = (ulong)width * (ulong)height * 4;

            var allocInfo = new VkMemoryAllocateInfo
            {
                sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                pNext = &dedicatedInfo, // 链式传递专用分配信息
                allocationSize = allocationSize,
                memoryTypeIndex = memoryTypeIndex,
            };

            if (!_fns.AllocateMemory(_device, ref allocInfo, out ulong importedMemory))
            {
                GD.PrintErr("[Vulkan/Win] Failed to allocate/import memory");
                return false;
            }

            // 将图像绑定到导入的内存。
            if (!_fns.BindImageMemory(_device, image, importedMemory, 0))
            {
                _fns.FreeMemory(_device, importedMemory);
                GD.PrintErr("[Vulkan/Win] Failed to bind image memory");
                return false;
            }

            memory = importedMemory;
            return true;
        }

        private bool SubmitCopyAsync(ulong src, ulong dst, int width, int height)
        {
            ulong cmdBuffer = _commandBuffers[_currentFrame];
            ulong fence = _fences[_currentFrame];

            // 重置 fence 与 command buffer。
            _fns.ResetFences(_device, 1, ref fence);
            _fns.ResetCommandBuffer(cmdBuffer, 0);

            var beginInfo = new VkCommandBufferBeginInfo
            {
                sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
                flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT,
            };
            _fns.BeginCommandBuffer(cmdBuffer, ref beginInfo);

            var subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = 1,
            };

            // 源图像：若使用独立外部队列则从 GENERAL 转换，否则从 UNDEFINED（无所有权转移）。
            // 本实现 src_external_queue_family 恒为 IGNORED（对齐非外部队列场景）。
            var srcBarrier = new VkImageMemoryBarrier
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                oldLayout = VK_IMAGE_LAYOUT_UNDEFINED,
                newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                image = src,
                subresourceRange = subresourceRange,
                srcAccessMask = 0,
                dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT,
            };

            var dstBarrier = new VkImageMemoryBarrier
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                oldLayout = VK_IMAGE_LAYOUT_UNDEFINED,
                newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                image = dst,
                subresourceRange = subresourceRange,
                srcAccessMask = 0,
                dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT,
            };

            var barriers = stackalloc VkImageMemoryBarrier[2];
            barriers[0] = srcBarrier;
            barriers[1] = dstBarrier;

            _fns.CmdPipelineBarrier(
                cmdBuffer,
                VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                VK_PIPELINE_STAGE_TRANSFER_BIT,
                0,
                0, IntPtr.Zero,
                0, IntPtr.Zero,
                2, barriers);

            var subresourceLayers = new VkImageSubresourceLayers
            {
                aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                mipLevel = 0,
                baseArrayLayer = 0,
                layerCount = 1,
            };

            var region = new VkImageCopy
            {
                srcSubresource = subresourceLayers,
                srcOffset = new VkOffset3D { x = 0, y = 0, z = 0 },
                dstSubresource = subresourceLayers,
                dstOffset = new VkOffset3D { x = 0, y = 0, z = 0 },
                extent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
            };

            _fns.CmdCopyImage(
                cmdBuffer, src, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                dst, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                1, &region);

            // 目标终态转换：TransferDst → ShaderReadOnly。
            // 若使用独立队列且队列族不同，需做队列族所有权转移。
            uint srcFamily = VK_QUEUE_FAMILY_IGNORED;
            uint dstFamily = VK_QUEUE_FAMILY_IGNORED;
            if (_usesSeparateQueue && _queueFamilyIndex != 0)
            {
                srcFamily = _queueFamilyIndex;
                dstFamily = 0;
            }

            var finalBarrier = new VkImageMemoryBarrier
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                newLayout = DST_LAYOUT_FINAL,
                srcQueueFamilyIndex = srcFamily,
                dstQueueFamilyIndex = dstFamily,
                image = dst,
                subresourceRange = subresourceRange,
                srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT,
                dstAccessMask = VK_ACCESS_SHADER_READ_BIT,
            };

            _fns.CmdPipelineBarrier(
                cmdBuffer,
                VK_PIPELINE_STAGE_TRANSFER_BIT,
                VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
                0,
                0, IntPtr.Zero,
                0, IntPtr.Zero,
                1, &finalBarrier);

            _fns.EndCommandBuffer(cmdBuffer);

            // 提交拷贝命令。
            var submitInfo = new VkSubmitInfo
            {
                sType = VK_STRUCTURE_TYPE_SUBMIT_INFO,
                commandBufferCount = 1,
                pCommandBuffers = &cmdBuffer,
            };

            int result = _fns.QueueSubmit(_queue, 1, ref submitInfo, fence);
            if (result != VK_SUCCESS)
            {
                GD.PrintErr($"[Vulkan/Win] Failed to submit copy command: {result}");
                return false;
            }
            return true;
        }

        private void DestroyImportedImage(ImportedVulkanImage img)
        {
            _fns.DestroyImage(_device, img.Image);
            _fns.FreeMemory(_device, img.Memory);
            if (img.DuplicatedHandle != IntPtr.Zero)
                Kernel32.CloseHandle(img.DuplicatedHandle);
        }

        private static (uint Family, uint Index, bool Separate) FindCopyQueue(ulong physicalDevice)
        {
            if (physicalDevice == 0)
                return (0, 0, false);

            // 获取物理设备队列族属性（该函数从 vulkan-1.dll 直接导出）。
            uint familyCount = 0;
            VulkanFunctions.EnumerateQueueFamilyProperties(physicalDevice, ref familyCount, null);
            if (familyCount == 0)
                return (0, 0, false);

            var props = new VkQueueFamilyProperties[familyCount];
            VulkanFunctions.EnumerateQueueFamilyProperties(physicalDevice, ref familyCount, props);

            // 图形队列族有多条队列时，优先使用同族索引 1。
            if (props.Length > 0 && props[0].queueCount > 1)
            {
                GD.Print($"[Vulkan/Win] Graphics family has {props[0].queueCount} queues, trying queue index 1");
                return (0, 1, true);
            }

            // 寻找仅传输的专用队列族。
            for (int i = 0; i < props.Length; i++)
            {
                uint flags = props[i].queueFlags;
                bool hasTransfer = (flags & VK_QUEUE_TRANSFER_BIT) != 0;
                bool hasGraphics = (flags & VK_QUEUE_GRAPHICS_BIT) != 0;
                bool hasCompute = (flags & VK_QUEUE_COMPUTE_BIT) != 0;
                if (hasTransfer && !hasGraphics && props[i].queueCount > 0)
                {
                    GD.Print($"[Vulkan/Win] Found dedicated transfer queue family {i} (compute={hasCompute})");
                    return ((uint)i, 0, true);
                }
            }

            GD.Print("[Vulkan/Win] No separate queue available, using shared graphics queue");
            return (0, 0, false);
        }

        private static uint CountTrailingZeros(uint value)
        {
            uint count = 0;
            while ((value & 1) == 0)
            {
                value >>= 1;
                count++;
            }
            return count;
        }

        // ──────────────────────────────────────────────────────────────
        //  数据结构
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 一次待处理的拷贝（QueueCopy 产生，ProcessPendingCopy 消费）。
        /// </summary>
        private sealed class PendingVulkanCopy
        {
            public long SourceHandle;
            public IntPtr DuplicatedHandle;
            public int Width;
            public int Height;

            public PendingVulkanCopy(long sourceHandle, IntPtr duplicatedHandle, int width, int height)
            {
                SourceHandle = sourceHandle;
                DuplicatedHandle = duplicatedHandle;
                Width = width;
                Height = height;
            }

            public bool HasDuplicatedHandle => DuplicatedHandle != IntPtr.Zero;

            public void Dispose()
            {
                if (DuplicatedHandle != IntPtr.Zero)
                {
                    Kernel32.CloseHandle(DuplicatedHandle);
                    DuplicatedHandle = IntPtr.Zero;
                }
            }
        }

        /// <summary>
        /// 已导入 Vulkan 的外部内存图像（含需自行关闭的双份句柄）。
        /// </summary>
        private sealed class ImportedVulkanImage
        {
            public IntPtr DuplicatedHandle;
            public ulong Image;
            public ulong Memory;
            public int Width;
            public int Height;
            public ulong LastUsed;

            public ImportedVulkanImage(IntPtr duplicatedHandle, ulong image, ulong memory, int width, int height, ulong lastUsed)
            {
                DuplicatedHandle = duplicatedHandle;
                Image = image;
                Memory = memory;
                Width = width;
                Height = height;
                LastUsed = lastUsed;
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Vulkan 结构体（原始布局，NativeAOT 安全）
        // ──────────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct VkExtent3D
        {
            public uint width;
            public uint height;
            public uint depth;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkOffset3D
        {
            public int x;
            public int y;
            public int z;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkImageSubresourceRange
        {
            public uint aspectMask;
            public uint baseMipLevel;
            public uint levelCount;
            public uint baseArrayLayer;
            public uint layerCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkImageSubresourceLayers
        {
            public uint aspectMask;
            public uint mipLevel;
            public uint baseArrayLayer;
            public uint layerCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct VkImageCreateInfo
        {
            public int sType;
            public VkExternalMemoryImageCreateInfo* pNext;
            public uint flags;
            public int imageType;
            public int format;
            public VkExtent3D extent;
            public uint mipLevels;
            public uint arrayLayers;
            public uint samples;
            public int tiling;
            public uint usage;
            public int sharingMode;
            public uint queueFamilyIndexCount;
            public uint* pQueueFamilyIndices;
            public int initialLayout;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkExternalMemoryImageCreateInfo
        {
            public int sType;
            public IntPtr pNext; // 无需链更深，置 null
            public uint handleTypes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkMemoryAllocateInfo
        {
            public int sType;
            public unsafe VkMemoryDedicatedAllocateInfo* pNext;
            public ulong allocationSize;
            public uint memoryTypeIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct VkMemoryDedicatedAllocateInfo
        {
            public int sType;
            public VkImportMemoryWin32HandleInfoKHR* pNext;
            public ulong image;
            public ulong buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkImportMemoryWin32HandleInfoKHR
        {
            public int sType;
            public IntPtr pNext;
            public uint handleType;
            public IntPtr handle;
            public IntPtr name;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkMemoryWin32HandlePropertiesKHR
        {
            public int sType;
            public IntPtr pNext;
            public uint memoryTypeBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkCommandPoolCreateInfo
        {
            public int sType;
            public IntPtr pNext;
            public uint flags;
            public uint queueFamilyIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkCommandBufferAllocateInfo
        {
            public int sType;
            public IntPtr pNext;
            public ulong commandPool;
            public uint level;
            public uint commandBufferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkCommandBufferBeginInfo
        {
            public int sType;
            public IntPtr pNext;
            public uint flags;
            public IntPtr pInheritanceInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkFenceCreateInfo
        {
            public int sType;
            public IntPtr pNext;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct VkSubmitInfo
        {
            public int sType;
            public IntPtr pNext;
            public uint waitSemaphoreCount;
            public IntPtr pWaitSemaphores;
            public IntPtr pWaitDstStageMask;
            public uint commandBufferCount;
            public ulong* pCommandBuffers;
            public uint signalSemaphoreCount;
            public IntPtr pSignalSemaphores;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkImageMemoryBarrier
        {
            public int sType;
            public IntPtr pNext;
            public uint srcAccessMask;
            public uint dstAccessMask;
            public int oldLayout;
            public int newLayout;
            public uint srcQueueFamilyIndex;
            public uint dstQueueFamilyIndex;
            public ulong image;
            public VkImageSubresourceRange subresourceRange;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkImageCopy
        {
            public VkImageSubresourceLayers srcSubresource;
            public VkOffset3D srcOffset;
            public VkImageSubresourceLayers dstSubresource;
            public VkOffset3D dstOffset;
            public VkExtent3D extent;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VkQueueFamilyProperties
        {
            public uint queueFlags;
            public uint queueCount;
            public uint timestampValidBits;
            public VkExtent3D minImageTransferGranularity;
        }

        // ──────────────────────────────────────────────────────────────
        //  Vulkan 设备函数指针加载器
        // ──────────────────────────────────────────────────────────────

        private sealed class VulkanFunctions
        {
            // 设备级函数
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkCreateImage(ulong device, ref VkImageCreateInfo pCreateInfo, IntPtr pAllocator, out ulong pImage);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void PFN_vkDestroyImage(ulong device, ulong image, IntPtr pAllocator);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkAllocateMemory(ulong device, ref VkMemoryAllocateInfo pAllocateInfo, IntPtr pAllocator, out ulong pMemory);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void PFN_vkFreeMemory(ulong device, ulong memory, IntPtr pAllocator);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkBindImageMemory(ulong device, ulong image, ulong memory, ulong memoryOffset);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkCreateCommandPool(ulong device, ref VkCommandPoolCreateInfo pCreateInfo, IntPtr pAllocator, out ulong pCommandPool);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void PFN_vkDestroyCommandPool(ulong device, ulong commandPool, IntPtr pAllocator);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkAllocateCommandBuffers(ulong device, ref VkCommandBufferAllocateInfo pAllocateInfo, ulong[] pCommandBuffers);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkCreateFence(ulong device, ref VkFenceCreateInfo pCreateInfo, IntPtr pAllocator, out ulong pFence);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void PFN_vkDestroyFence(ulong device, ulong fence, IntPtr pAllocator);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkBeginCommandBuffer(ulong commandBuffer, ref VkCommandBufferBeginInfo pBeginInfo);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkEndCommandBuffer(ulong commandBuffer);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void PFN_vkCmdPipelineBarrier(ulong commandBuffer, uint srcStageMask, uint dstStageMask, uint dependencyFlags,
                uint memoryBarrierCount, IntPtr pMemoryBarriers, uint bufferMemoryBarrierCount, IntPtr pBufferMemoryBarriers,
                uint imageMemoryBarrierCount, VkImageMemoryBarrier* pImageMemoryBarriers);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void PFN_vkCmdCopyImage(ulong commandBuffer, ulong srcImage, int srcLayout, ulong dstImage, int dstLayout,
                uint regionCount, VkImageCopy* pRegions);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkQueueSubmit(ulong queue, uint submitCount, ref VkSubmitInfo pSubmits, ulong fence);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkWaitForFences(ulong device, uint fenceCount, ref ulong pFences, uint waitAll, ulong timeout);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkResetFences(ulong device, uint fenceCount, ref ulong pFences);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkResetCommandBuffer(ulong commandBuffer, uint flags);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void PFN_vkGetDeviceQueue(ulong device, uint queueFamilyIndex, uint queueIndex, out ulong pQueue);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int PFN_vkGetMemoryWin32HandlePropertiesKHR(ulong device, uint handleType, IntPtr handle, ref VkMemoryWin32HandlePropertiesKHR pMemoryWin32HandleProperties);

            // 函数指针字段
            private readonly PFN_vkCreateImage _createImage;
            private readonly PFN_vkDestroyImage _destroyImage;
            private readonly PFN_vkAllocateMemory _allocateMemory;
            private readonly PFN_vkFreeMemory _freeMemory;
            private readonly PFN_vkBindImageMemory _bindImageMemory;
            private readonly PFN_vkCreateCommandPool _createCommandPool;
            private readonly PFN_vkDestroyCommandPool _destroyCommandPool;
            private readonly PFN_vkAllocateCommandBuffers _allocateCommandBuffers;
            private readonly PFN_vkCreateFence _createFence;
            private readonly PFN_vkDestroyFence _destroyFence;
            private readonly PFN_vkBeginCommandBuffer _beginCommandBuffer;
            private readonly PFN_vkEndCommandBuffer _endCommandBuffer;
            private readonly PFN_vkCmdPipelineBarrier _cmdPipelineBarrier;
            private readonly PFN_vkCmdCopyImage _cmdCopyImage;
            private readonly PFN_vkQueueSubmit _queueSubmit;
            private readonly PFN_vkWaitForFences _waitForFences;
            private readonly PFN_vkResetFences _resetFences;
            private readonly PFN_vkResetCommandBuffer _resetCommandBuffer;
            private readonly PFN_vkGetDeviceQueue _getDeviceQueue;
            private readonly PFN_vkGetMemoryWin32HandlePropertiesKHR _getMemoryWin32HandleProperties;

            private VulkanFunctions(
                PFN_vkCreateImage createImage, PFN_vkDestroyImage destroyImage,
                PFN_vkAllocateMemory allocateMemory, PFN_vkFreeMemory freeMemory,
                PFN_vkBindImageMemory bindImageMemory, PFN_vkCreateCommandPool createCommandPool,
                PFN_vkDestroyCommandPool destroyCommandPool, PFN_vkAllocateCommandBuffers allocateCommandBuffers,
                PFN_vkCreateFence createFence, PFN_vkDestroyFence destroyFence,
                PFN_vkBeginCommandBuffer beginCommandBuffer, PFN_vkEndCommandBuffer endCommandBuffer,
                PFN_vkCmdPipelineBarrier cmdPipelineBarrier, PFN_vkCmdCopyImage cmdCopyImage,
                PFN_vkQueueSubmit queueSubmit, PFN_vkWaitForFences waitForFences,
                PFN_vkResetFences resetFences, PFN_vkResetCommandBuffer resetCommandBuffer,
                PFN_vkGetDeviceQueue getDeviceQueue, PFN_vkGetMemoryWin32HandlePropertiesKHR getMemoryWin32HandleProperties)
            {
                _createImage = createImage;
                _destroyImage = destroyImage;
                _allocateMemory = allocateMemory;
                _freeMemory = freeMemory;
                _bindImageMemory = bindImageMemory;
                _createCommandPool = createCommandPool;
                _destroyCommandPool = destroyCommandPool;
                _allocateCommandBuffers = allocateCommandBuffers;
                _createFence = createFence;
                _destroyFence = destroyFence;
                _beginCommandBuffer = beginCommandBuffer;
                _endCommandBuffer = endCommandBuffer;
                _cmdPipelineBarrier = cmdPipelineBarrier;
                _cmdCopyImage = cmdCopyImage;
                _queueSubmit = queueSubmit;
                _waitForFences = waitForFences;
                _resetFences = resetFences;
                _resetCommandBuffer = resetCommandBuffer;
                _getDeviceQueue = getDeviceQueue;
                _getMemoryWin32HandleProperties = getMemoryWin32HandleProperties;
            }

            /// <summary>
            /// 从 vulkan-1.dll 加载设备级函数。若 vkGetMemoryWin32HandlePropertiesKHR
            /// 不可用（即 VK_KHR_external_memory_win32 被禁用/Layer 未安装）返回 null。
            /// </summary>
            public static VulkanFunctions Load(ulong device)
            {
                try
                {
                    // 直接通过 DllImport 调用 vkGetDeviceProcAddr 解析函数指针。
                    // 注意：不要先通过 GetDeviceProcAddr 拿到地址再 Marshal.GetDelegateForFunctionPointer，
                    // 因为 vkGetDeviceProcAddr(NULL, "vkGetDeviceProcAddr") 可能返回 NULL。
                    // 直接用 DllImport 调用是最可靠的方式。
                    IntPtr Resolve(string name)
                    {
                        return GetDeviceProcAddrInternal(device, name);
                    }

                    var createImage = Marshal.GetDelegateForFunctionPointer<PFN_vkCreateImage>(Resolve("vkCreateImage"));
                    var destroyImage = Marshal.GetDelegateForFunctionPointer<PFN_vkDestroyImage>(Resolve("vkDestroyImage"));
                    var allocateMemory = Marshal.GetDelegateForFunctionPointer<PFN_vkAllocateMemory>(Resolve("vkAllocateMemory"));
                    var freeMemory = Marshal.GetDelegateForFunctionPointer<PFN_vkFreeMemory>(Resolve("vkFreeMemory"));
                    var bindImageMemory = Marshal.GetDelegateForFunctionPointer<PFN_vkBindImageMemory>(Resolve("vkBindImageMemory"));
                    var createCommandPool = Marshal.GetDelegateForFunctionPointer<PFN_vkCreateCommandPool>(Resolve("vkCreateCommandPool"));
                    var destroyCommandPool = Marshal.GetDelegateForFunctionPointer<PFN_vkDestroyCommandPool>(Resolve("vkDestroyCommandPool"));
                    var allocateCommandBuffers = Marshal.GetDelegateForFunctionPointer<PFN_vkAllocateCommandBuffers>(Resolve("vkAllocateCommandBuffers"));
                    var createFence = Marshal.GetDelegateForFunctionPointer<PFN_vkCreateFence>(Resolve("vkCreateFence"));
                    var destroyFence = Marshal.GetDelegateForFunctionPointer<PFN_vkDestroyFence>(Resolve("vkDestroyFence"));
                    var beginCommandBuffer = Marshal.GetDelegateForFunctionPointer<PFN_vkBeginCommandBuffer>(Resolve("vkBeginCommandBuffer"));
                    var endCommandBuffer = Marshal.GetDelegateForFunctionPointer<PFN_vkEndCommandBuffer>(Resolve("vkEndCommandBuffer"));
                    var cmdPipelineBarrier = Marshal.GetDelegateForFunctionPointer<PFN_vkCmdPipelineBarrier>(Resolve("vkCmdPipelineBarrier"));
                    var cmdCopyImage = Marshal.GetDelegateForFunctionPointer<PFN_vkCmdCopyImage>(Resolve("vkCmdCopyImage"));
                    var queueSubmit = Marshal.GetDelegateForFunctionPointer<PFN_vkQueueSubmit>(Resolve("vkQueueSubmit"));
                    var waitForFences = Marshal.GetDelegateForFunctionPointer<PFN_vkWaitForFences>(Resolve("vkWaitForFences"));
                    var resetFences = Marshal.GetDelegateForFunctionPointer<PFN_vkResetFences>(Resolve("vkResetFences"));
                    var resetCommandBuffer = Marshal.GetDelegateForFunctionPointer<PFN_vkResetCommandBuffer>(Resolve("vkResetCommandBuffer"));
                    var getDeviceQueue = Marshal.GetDelegateForFunctionPointer<PFN_vkGetDeviceQueue>(Resolve("vkGetDeviceQueue"));

                    // 关键函数：若该扩展不被启用（Layer 未安装），返回 null。
                    IntPtr memWin32Fp = Resolve("vkGetMemoryWin32HandlePropertiesKHR");
                    if (memWin32Fp == IntPtr.Zero)
                        return null;
                    var getMemoryWin32HandleProperties = Marshal.GetDelegateForFunctionPointer<PFN_vkGetMemoryWin32HandlePropertiesKHR>(memWin32Fp);

                    return new VulkanFunctions(
                        createImage, destroyImage, allocateMemory, freeMemory, bindImageMemory,
                        createCommandPool, destroyCommandPool, allocateCommandBuffers,
                        createFence, destroyFence, beginCommandBuffer, endCommandBuffer,
                        cmdPipelineBarrier, cmdCopyImage, queueSubmit, waitForFences,
                        resetFences, resetCommandBuffer, getDeviceQueue, getMemoryWin32HandleProperties);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[Vulkan/Win] Failed to load Vulkan device functions: {ex.Message}");
                    return null;
                }
            }

            // 物理设备级函数（无需实例），直接从 DLL 导出符号解析。
            [DllImport("vulkan-1.dll", EntryPoint = "vkGetPhysicalDeviceQueueFamilyProperties", CallingConvention = CallingConvention.StdCall)]
            private static extern void GetPhysicalDeviceQueueFamilyPropertiesNative(ulong physicalDevice, ref uint pCount, IntPtr pProperties);

            public static void EnumerateQueueFamilyProperties(ulong physicalDevice, ref uint count, VkQueueFamilyProperties[] props)
            {
                IntPtr ptr = IntPtr.Zero;
                if (props != null && props.Length > 0)
                {
                    fixed (VkQueueFamilyProperties* p = props)
                        ptr = (IntPtr)p;
                }
                GetPhysicalDeviceQueueFamilyPropertiesNative(physicalDevice, ref count, ptr);
            }

            [DllImport("vulkan-1.dll", EntryPoint = "vkGetDeviceProcAddr", CallingConvention = CallingConvention.StdCall)]
            private static extern IntPtr GetDeviceProcAddrInternal(ulong device, [MarshalAs(UnmanagedType.LPStr)] string pName);

            // ── 设备函数调用封装 ──
            public ulong CreateImage(ulong device, ref VkImageCreateInfo info, out ulong image)
            {
                if (_createImage(device, ref info, IntPtr.Zero, out image) == VK_SUCCESS)
                    return image;
                image = 0;
                return 0;
            }
            public void DestroyImage(ulong device, ulong image) => _destroyImage(device, image, IntPtr.Zero);
            public bool AllocateMemory(ulong device, ref VkMemoryAllocateInfo info, out ulong memory)
                => _allocateMemory(device, ref info, IntPtr.Zero, out memory) == VK_SUCCESS;
            public void FreeMemory(ulong device, ulong memory) => _freeMemory(device, memory, IntPtr.Zero);
            public bool BindImageMemory(ulong device, ulong image, ulong memory, ulong offset)
                => _bindImageMemory(device, image, memory, offset) == VK_SUCCESS;
            public ulong CreateCommandPool(ulong device, ref VkCommandPoolCreateInfo info)
            {
                if (_createCommandPool(device, ref info, IntPtr.Zero, out ulong pool) == VK_SUCCESS)
                    return pool;
                return 0;
            }
            public void DestroyCommandPool(ulong device, ulong pool) => _destroyCommandPool(device, pool, IntPtr.Zero);
            public bool AllocateCommandBuffers(ulong device, ref VkCommandBufferAllocateInfo info, ulong[] bufs)
                => _allocateCommandBuffers(device, ref info, bufs) == VK_SUCCESS;
            public ulong CreateFence(ulong device, ref VkFenceCreateInfo info)
            {
                if (_createFence(device, ref info, IntPtr.Zero, out ulong fence) == VK_SUCCESS)
                    return fence;
                return 0;
            }
            public void DestroyFence(ulong device, ulong fence) => _destroyFence(device, fence, IntPtr.Zero);
            public void BeginCommandBuffer(ulong cmd, ref VkCommandBufferBeginInfo info) => _beginCommandBuffer(cmd, ref info);
            public void EndCommandBuffer(ulong cmd) => _endCommandBuffer(cmd);
            public void CmdPipelineBarrier(ulong cmd, uint srcStage, uint dstStage, uint deps,
                uint memBarrierCount, IntPtr memBarriers, uint bufBarrierCount, IntPtr bufBarriers,
                uint imgBarrierCount, VkImageMemoryBarrier* imgBarriers)
                => _cmdPipelineBarrier(cmd, srcStage, dstStage, deps, memBarrierCount, memBarriers, bufBarrierCount, bufBarriers, imgBarrierCount, imgBarriers);
            public void CmdCopyImage(ulong cmd, ulong src, int srcLayout, ulong dst, int dstLayout, uint count, VkImageCopy* regions)
                => _cmdCopyImage(cmd, src, srcLayout, dst, dstLayout, count, regions);
            public int QueueSubmit(ulong queue, uint count, ref VkSubmitInfo submits, ulong fence)
                => _queueSubmit(queue, count, ref submits, fence);
            public int WaitForFences(ulong device, uint count, ref ulong fences, uint waitAll, ulong timeout)
                => _waitForFences(device, count, ref fences, waitAll, timeout);
            public void ResetFences(ulong device, uint count, ref ulong fences) => _resetFences(device, count, ref fences);
            public void ResetCommandBuffer(ulong cmd, uint flags) => _resetCommandBuffer(cmd, flags);
            public ulong GetDeviceQueue(ulong device, uint family, uint index)
            {
                _getDeviceQueue(device, family, index, out ulong queue);
                return queue;
            }
            public int GetMemoryWin32HandlePropertiesKHR(ulong device, uint handleType, IntPtr handle, ref VkMemoryWin32HandlePropertiesKHR props)
                => _getMemoryWin32HandleProperties(device, handleType, handle, ref props);
        }
    }
}
#endif