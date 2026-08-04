using System;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  GPU 加速 OSR: OnAcceleratedPaint 分派 + 纹理管理
    //
    //  当 EnableGpuAcceleration=true 且平台支持时，CEF 调用
    //  OnAcceleratedPaint 而非 OnPaint。此模块负责：
    //  1. 创建/管理 ITextureCopier 实例
    //  2. 管理 GPU 纹理 (RenderingDevice RID) 的生命周期
    //  3. 在 _Process 中处理待完成的 GPU 拷贝
    // ══════════════════════════════════════════════════════════════
    public partial class CefGlueControl
    {
        // ── GPU 加速状态 ──
        private ITextureCopier _gpuCopier;
        private Rid _gpuTextureRdRid;
        private bool _gpuAccelerationActive;
        private bool _gpuCopyPending;
        private int _gpuPendingWidth;
        private int _gpuPendingHeight;
        private bool _gpuTextureNeedsResize;

        /// <summary>
        /// Initialize GPU accelerated rendering. Called during browser creation
        /// when EnableGpuAcceleration is true.
        /// </summary>
        private void InitializeGpuAcceleration()
        {
            if (_gpuCopier != null) return;

            _gpuCopier = TextureCopierFactory.Create();
            _gpuAccelerationActive = _gpuCopier != null && _gpuCopier.IsValid;

            if (_gpuAccelerationActive)
            {
                GD.Print("[CefGlueControl] GPU acceleration initialized");
            }
            else
            {
                GD.Print("[CefGlueControl] GPU acceleration not available, using CPU fallback");
            }
        }

        /// <summary>
        /// Called from GodotRenderHandler.OnAcceleratedPaint on CEF UI thread.
        /// Queues the GPU copy — non-blocking.
        /// </summary>
        internal void OnAcceleratedPaint(IntPtr sharedTextureHandle, int width, int height)
        {
            if (_gpuCopier == null || !_gpuAccelerationActive) return;

            // 尺寸变化标记
            if (width != _gpuPendingWidth || height != _gpuPendingHeight)
            {
                _gpuTextureNeedsResize = true;
            }

            _gpuPendingWidth = width;
            _gpuPendingHeight = height;

            var result = _gpuCopier.QueueCopy(sharedTextureHandle, width, height);
            if (result == CopyResult.Success)
            {
                _gpuCopyPending = true;
            }
        }

        /// <summary>
        /// Process pending GPU copy in _Process (Godot main thread).
        /// </summary>
        private void ProcessGpuAcceleratedPaint()
        {
            if (!_gpuCopyPending || _gpuCopier == null) return;

            // 如果尺寸变化，重新创建纹理
            if (_gpuTextureNeedsResize)
            {
                FreeGpuTexture();
                _gpuTextureRdRid = _gpuCopier.CreateDestinationTexture(
                    _gpuPendingWidth, _gpuPendingHeight);
                _gpuTextureNeedsResize = false;
            }

            // 确保纹理存在
            if (!_gpuTextureRdRid.IsValid)
            {
                _gpuTextureRdRid = _gpuCopier.CreateDestinationTexture(
                    _gpuPendingWidth, _gpuPendingHeight);
                if (!_gpuTextureRdRid.IsValid)
                {
                    return;
                }
            }

            // 处理待完成的拷贝
            var result = _gpuCopier.ProcessPendingCopy(_gpuTextureRdRid);
            if (result == CopyResult.Success)
            {
                _gpuCopyPending = false;
                _gpuTextureDirty = true;
                QueueRedraw();
            }
            // RetryLater 时不重置 _gpuCopyPending，下一帧继续
        }

        /// <summary>
        /// Free GPU texture resources.
        /// </summary>
        private void FreeGpuTexture()
        {
            if (_gpuTextureRdRid.IsValid)
            {
                var rd = RenderingServer.Singleton.GetRenderingDevice();
                if (rd != null) rd.FreeRid(_gpuTextureRdRid);
                _gpuTextureRdRid = new Rid();
            }
        }

        /// <summary>
        /// Cleanup GPU acceleration resources.
        /// </summary>
        private void CleanupGpuAcceleration()
        {
            FreeGpuTexture();
            if (_gpuCopier != null)
            {
                _gpuCopier.Dispose();
                _gpuCopier = null;
            }
            _gpuAccelerationActive = false;
            _gpuCopyPending = false;
        }

        // ── 标记纹理已更新，在 _Draw 中使用 ──
        internal bool _gpuTextureDirty;
    }
}