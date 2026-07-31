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
        public static ITextureCopier Create()
        {
            // Try Windows D3D12 first
            var windowsCopier = D3D11on12TextureCopier.TryCreate();
            if (windowsCopier != null)
            {
                GD.Print("[TextureCopier] Using D3D11on12 texture copier (Windows D3D12)");
                return windowsCopier;
            }

            // macOS Metal would go here in Phase 2
            // Linux Vulkan would go here in Phase 4

            GD.Print("[TextureCopier] No GPU-accelerated copier available for this platform");
            return null;
        }
    }
}