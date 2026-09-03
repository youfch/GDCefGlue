using System;
using Godot;

namespace GDCefGlue
{
    /// <summary>
    /// Result of a GPU texture copy operation.
    /// </summary>
    internal enum CopyResult
    {
        Success,
        RetryLater,   // Previous copy still in flight
        Failed,       // Unrecoverable error
        NotSupported, // Platform/backend not supported
    }

    /// <summary>
    /// Detected Godot rendering backend for GPU-accelerated OSR.
    /// </summary>
    internal enum GpuBackend
    {
        Unknown,
        D3D12,
        Vulkan,
        Metal,
        OpenGL,
    }

    /// <summary>
    /// Interface for GPU-accelerated texture copy from CEF shared texture to Godot RenderingDevice texture.
    /// Each platform/backend implements this interface.
    /// </summary>
    internal interface ITextureCopier : IDisposable
    {
        /// <summary>
        /// Whether the copier was successfully initialized for this platform.
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// Queue a copy from CEF's shared texture handle.
        /// Called from CEF UI thread — must be non-blocking.
        /// </summary>
        CopyResult QueueCopy(IntPtr sharedTextureHandle, int width, int height);

        /// <summary>
        /// Process the pending copy into the Godot RenderingDevice texture identified by <paramref name="dstRdRid"/>.
        /// Called from Godot main thread in _Process.
        /// </summary>
        CopyResult ProcessPendingCopy(Rid dstRdRid);

        /// <summary>
        /// Wait for all in-flight copies to complete.
        /// </summary>
        void WaitForCopy();

        /// <summary>
        /// Create a RenderingDevice texture of the given size.
        /// Returns the RID of the created texture.
        /// </summary>
        Rid CreateDestinationTexture(int width, int height);
    }

    /// <summary>
    /// Factory for creating platform-appropriate ITextureCopier instances.
    /// </summary>
    internal static class TextureCopierFactory
    {
        /// <summary>
        /// Detect the current Godot rendering backend at runtime.
        /// </summary>
        public static GpuBackend DetectBackend()
        {
            try
            {
                var rd = RenderingServer.Singleton.GetRenderingDevice();
                if (rd == null) return GpuBackend.Unknown;

                var driverName = RenderingServer.Singleton.GetCurrentRenderingDriverName().ToLowerInvariant();
                if (driverName.Contains("d3d12")) return GpuBackend.D3D12;
                if (driverName.Contains("vulkan")) return GpuBackend.Vulkan;
                if (driverName.Contains("metal")) return GpuBackend.Metal;
                if (driverName.Contains("opengl") || driverName.Contains("gl_")) return GpuBackend.OpenGL;
                return GpuBackend.Unknown;
            }
            catch
            {
                return GpuBackend.Unknown;
            }
        }

        public static ITextureCopier Create()
        {
            var backend = DetectBackend();
            GD.Print($"[TextureCopier] Detected render backend: {backend}");

#if GD_GPU_WINDOWS
            // Windows: try D3D12 first (D3D11on12 bridge via Vortice)
            if (backend == GpuBackend.D3D12)
            {
                var copier = D3D11on12TextureCopier.TryCreate();
                if (copier != null)
                {
                    GD.Print("[TextureCopier] Using D3D11on12 texture copier (Windows D3D12)");
                    return copier;
                }
                GD.Print("[TextureCopier] D3D11on12 copier failed, trying fallback...");
            }

            // Windows Vulkan: try Vulkan external memory copier (via VK_KHR_external_memory_win32)
            if (backend == GpuBackend.Vulkan)
            {
                var copier = WindowsVulkanTextureCopier.TryCreate();
                if (copier != null)
                {
                    GD.Print("[TextureCopier] Using Windows Vulkan external memory copier");
                    return copier;
                }
                GD.Print("[TextureCopier] Windows Vulkan copier failed, using CPU fallback");
            }
#endif

#if GD_GPU_MACOS
            // macOS: Metal IOSurface import
            if (backend == GpuBackend.Metal)
            {
                var copier = MetalTextureCopier.TryCreate();
                if (copier != null)
                {
                    GD.Print("[TextureCopier] Using Metal IOSurface texture copier (macOS)");
                    return copier;
                }
                GD.Print("[TextureCopier] Metal copier failed, using CPU fallback");
            }
#endif

#if GD_GPU_LINUX
            // Linux: Vulkan external memory / DMA-BUF
            if (backend == GpuBackend.Vulkan)
            {
                var copier = LinuxVulkanTextureCopier.TryCreate();
                if (copier != null)
                {
                    GD.Print("[TextureCopier] Using Linux Vulkan DMA-BUF texture copier");
                    return copier;
                }
                GD.Print("[TextureCopier] Linux Vulkan copier failed, using CPU fallback");
            }
#endif

            GD.Print($"[TextureCopier] No GPU-accelerated copier available for backend {backend}, using CPU fallback");
            return null;
        }
    }
}