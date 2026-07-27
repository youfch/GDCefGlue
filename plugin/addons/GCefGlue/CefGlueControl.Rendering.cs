using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  OSR 渲染：OnPaint、_Process、_Draw、光标
    // ══════════════════════════════════════════════════════════════
    public partial class CefGlueControl
    {
        internal void OnPaint(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects)
        {
            if (width <= 0 || height <= 0) return;
            int bufferSize = width * height * 4;
            bool lockTaken = false;
            try
            {
                _spinLock.Enter(ref lockTaken);
                _width = width;
                _height = height;
                if (_pixelBuffer == null || _pixelBufferSize != bufferSize)
                {
                    if (_pixelBuffer != null && _pixelBufferSize > 0)
                        ArrayPool<byte>.Shared.Return(_pixelBuffer);
                    _pixelBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                    _pixelBufferSize = bufferSize;
                }
                Marshal.Copy(buffer, _pixelBuffer, 0, bufferSize);
                ConvertBgraToRgba(_pixelBuffer, width * height);
                _isDirty = true;
            }
            finally { if (lockTaken) _spinLock.Exit(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void ConvertBgraToRgba(byte[] buffer, int pixelCount)
        {
            if (Avx2.IsSupported)
            {
                int vectorSize = 32, vectorCount = pixelCount / 8;
                var shuffleMask = Vector256.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15, (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
                fixed (byte* ptr = buffer)
                {
                    for (int i = 0; i < vectorCount; i++)
                    { var data = Avx.LoadVector256(ptr + i * vectorSize); Avx.Store(ptr + i * vectorSize, Avx2.Shuffle(data, shuffleMask)); }
                    for (int i = vectorCount * 8; i < pixelCount; i++) { int o = i * 4; byte b = ptr[o]; ptr[o] = ptr[o + 2]; ptr[o + 2] = b; }
                }
            }
            else if (Ssse3.IsSupported)
            {
                int vectorSize = 16, vectorCount = pixelCount / 4;
                var shuffleMask = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
                fixed (byte* ptr = buffer)
                {
                    for (int i = 0; i < vectorCount; i++)
                    { var data = Sse2.LoadVector128(ptr + i * vectorSize); Sse2.Store(ptr + i * vectorSize, Ssse3.Shuffle(data, shuffleMask)); }
                    for (int i = vectorCount * 4; i < pixelCount; i++) { int o = i * 4; byte b = ptr[o]; ptr[o] = ptr[o + 2]; ptr[o + 2] = b; }
                }
            }
            else
            {
                for (int i = 0; i < pixelCount; i++) { int o = i * 4; byte b = buffer[o]; buffer[o] = buffer[o + 2]; buffer[o + 2] = b; }
            }
        }

        public override void _Process(double delta)
        {
            if (Engine.IsEditorHint()) return;

            // 在非 Windows 平台上，CEF 使用外部消息循环模式，
            // 需要在主线程定期调用 DoMessageLoopWork() 驱动 CEF 消息循环。
            if (CefInitializer.UseExternalMessageLoop && CefRuntime.IsInitialized)
            {
                try { CefRuntime.DoMessageLoopWork(); }
                catch { /* CEF 尚未完成初始化或已关闭，忽略 */ }
            }

            _cachedGlobalPosition = GlobalPosition;
            _cachedContentScale = DisplayServer.ScreenGetScale();

            if (_renderMode == RenderMode.EmbeddedWindow) { ProcessEmbeddedMode(delta); return; }

            // OSR: skip texture update when hidden (browser/audio/JS still runs in background)
            if (!Visible) return;

                if (_browserHost != null && Size.X > 0 && Size.Y > 0)
                {
                    int newWidth = (int)Size.X, newHeight = (int)Size.Y;
                    if (newWidth != _controlWidth || newHeight != _controlHeight)
                    {
                        _controlWidth = newWidth; _controlHeight = newHeight;
                        _pendingWidth = newWidth; _pendingHeight = newHeight;
                        _resizeStableCount = 0; QueueRedraw();
                    }
                    else if (_pendingWidth > 0 && _pendingHeight > 0)
                    {
                        _resizeStableCount++;
                        if (_resizeStableCount >= ResizeStableThreshold)
                        {
                            // WasResized 内部会在 resize 完成后自动触发 OnPaint,
                            // 不需要额外的 Invalidate, 否则同一帧画两次
                            _browserHost.WasResized();
                            _pendingWidth = 0; _pendingHeight = 0;
                        }
                    }
                }

            if (_isDirty && _pixelBuffer != null && _width > 0 && _height > 0)
            {
                int expectedSize = _width * _height * 4;
                if (_pixelBufferSize >= expectedSize)
                {
                    if (_renderBuffer == null || _renderBufferSize != expectedSize)
                    { _renderBuffer = new byte[expectedSize]; _renderBufferSize = expectedSize; }
                    bool lockTaken = false;
                    try { _spinLock.Enter(ref lockTaken); Buffer.BlockCopy(_pixelBuffer, 0, _renderBuffer, 0, expectedSize); }
                    finally { if (lockTaken) _spinLock.Exit(); }
                    if (_texture.GetSize().X != _width || _texture.GetSize().Y != _height)
                    {
                        // 尺寸变化时必须创建新纹理, 但先释放旧纹理的 GPU RID
                        // Godot C# 绑定不会自动释放 RID (godotengine/godot#29006)
                        if (_texture != null)
                        {
                            RenderingServer.FreeRid(_texture.GetRid());
                            _texture.Dispose();
                        }
                        _image.SetData(_width, _height, false, Image.Format.Rgba8, _renderBuffer);
                        _texture = ImageTexture.CreateFromImage(_image);
                    }
                    else { _image.SetData(_width, _height, false, Image.Format.Rgba8, _renderBuffer); _texture.Update(_image); }
                    QueueRedraw();
                }
                _isDirty = false;
            }

            if (!_browserCreated && Size.X > 0 && Size.Y > 0) CreateBrowserDeferred();
        }

        public override void _Draw()
        {
            if (Engine.IsEditorHint()) return;
            if (_renderMode == RenderMode.EmbeddedWindow) return;
            if (_texture != null && _controlWidth > 0 && _controlHeight > 0)
            {
                if (Transparent) DrawTextureRect(_texture, new Rect2(Vector2.Zero, _controlWidth, _controlHeight), false, Colors.White, false);
                else if (_width == _controlWidth && _height == _controlHeight) DrawTexture(_texture, Vector2.Zero);
                else DrawTextureRect(_texture, new Rect2(Vector2.Zero, _controlWidth, _controlHeight), false);
            }
        }

        internal void OnCursorChanged(CefCursorType type)
        {
            // 嵌入窗口模式下 CEF 原生窗口自行处理光标，忽略 SyncCursor 设置
            if (_renderMode == RenderMode.EmbeddedWindow) return;
            if (!SyncCursor) return;
            CallDeferred(nameof(UpdateCursorShape), (int)type);
        }

        private void UpdateCursorShape(int cefCursorType)
        {
            var shape = cefCursorType switch
            {
                (int)CefCursorType.IBeam => CursorShape.Ibeam,
                (int)CefCursorType.Hand => CursorShape.PointingHand,
                (int)CefCursorType.Cross => CursorShape.Cross,
                (int)CefCursorType.Wait or (int)CefCursorType.Progress => CursorShape.Wait,
                (int)CefCursorType.Help => CursorShape.Help,
                (int)CefCursorType.NotAllowed => CursorShape.Forbidden,
                (int)CefCursorType.NorthSouthResize or (int)CefCursorType.NorthResize or (int)CefCursorType.SouthResize or (int)CefCursorType.RowResize => CursorShape.Vsize,
                (int)CefCursorType.EastWestResize or (int)CefCursorType.EastResize or (int)CefCursorType.WestResize or (int)CefCursorType.ColumnResize => CursorShape.Hsize,
                (int)CefCursorType.Move => CursorShape.Move,
                _ => Control.CursorShape.Arrow,
            };
            MouseDefaultCursorShape = (Control.CursorShape)shape;
        }
    }
}