using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Godot;
using Godot.Collections;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    internal void OnPaint(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects)
    {
        if (width <= 0 || height <= 0) return;
        int bufferSize = width * height * 4;
        lock (_bufferLock)
        {
            _width = width; _height = height;
            if (_pixelBuffer == null || _pixelBufferSize != bufferSize)
            {
                if (_pixelBuffer != null && _pixelBufferSize > 0) ArrayPool<byte>.Shared.Return(_pixelBuffer);
                _pixelBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                _pixelBufferSize = bufferSize;
                _packedBuffer = new PackedByteArray(); _packedBuffer.Resize(bufferSize);
            }
Marshal.Copy(buffer, _pixelBuffer, 0, bufferSize);
        ConvertBgraToRgba(_pixelBuffer, width * height);
        _isDirty = true;
        }
    }

    protected override void _Process(double delta)
    {
        if (Godot.Engine.Singleton.IsEditorHint()) return;

        // 在非 Windows 平台上，CEF 使用外部消息循环模式，
        // 需要在主线程定期调用 DoMessageLoopWork() 驱动 CEF 消息循环。
        if (CefInitializer.UseExternalMessageLoop && CefRuntime.IsInitialized)
        {
            try { CefRuntime.DoMessageLoopWork(); }
            catch { /* CEF 尚未完成初始化或已关闭，忽略 */ }
        }

        _cachedGlobalPosition = GlobalPosition;
        _cachedContentScale = DisplayServer.Singleton.ScreenGetScale();

        if (_renderMode == RenderMode.EmbeddedWindow) { ProcessEmbeddedMode(delta); return; }

        // OSR: skip texture update when hidden (browser/audio/JS still runs in background)
        if (!Visible) return;

        if (_browserHost != null && Size.X > 0 && Size.Y > 0)
        {
            int w = (int)Size.X, h = (int)Size.Y;
            if (w != _controlWidth || h != _controlHeight) { _controlWidth = w; _controlHeight = h; _pendingWidth = w; _pendingHeight = h; _resizeStableCount = 0; QueueRedraw(); }
            else if (_pendingWidth > 0 && _pendingHeight > 0 && ++_resizeStableCount >= ResizeStableThreshold) { _browserHost.WasResized(); _pendingWidth = 0; _pendingHeight = 0; }
        }
        if (_isDirty && _pixelBuffer != null && _width > 0 && _height > 0)
        {
            int expected = _width * _height * 4;
            if (_pixelBufferSize >= expected)
            {
                lock (_bufferLock)
                {
                    var pba = new PackedByteArray(_pixelBuffer.AsSpan(0, expected));
                    if (_texture.GetSize().X != _width || _texture.GetSize().Y != _height)
                    {
                        // 尺寸变化时释放旧纹理 GPU RID 再创建新纹理
                        RenderingServer.Singleton.FreeRid(_texture.GetRid());
                        _texture.Dispose();
                        _image.SetData(_width, _height, false, Image.Format.Rgba8, pba);
                        _texture = ImageTexture.CreateFromImage(_image);
                    }
                    else
                    {
                        _image.SetData(_width, _height, false, Image.Format.Rgba8, pba);
                        _texture.Update(_image);
                    }
                }
                QueueRedraw();
            }
            _isDirty = false;
        }
        if (!_browserCreated && Size.X > 0 && Size.Y > 0) CreateBrowserDeferred();
    }

    protected override void _Draw()
    {
        if (Godot.Engine.Singleton.IsEditorHint()) return;
        if (_renderMode == RenderMode.EmbeddedWindow) return;
        if (_texture != null && _controlWidth > 0 && _controlHeight > 0)
        {
            if (Transparent) DrawTextureRect(_texture, new Rect2(Vector2.Zero, _controlWidth, _controlHeight), false, new Color(1, 1, 1, 1), false);
            else if (_width == _controlWidth && _height == _controlHeight) DrawTexture(_texture, Vector2.Zero);
            else DrawTextureRect(_texture, new Rect2(Vector2.Zero, _controlWidth, _controlHeight), false);
        }
    }

    internal void OnCursorChanged(CefCursorType type) { if (_renderMode == RenderMode.EmbeddedWindow) return; if (!SyncCursor) return; CallDeferred(nameof(UpdateCursorShape), (int)type); }

    private void UpdateCursorShape(int cefCursorType)
    {
        MouseDefaultCursorShape = (Control.CursorShape)(cefCursorType switch
        {
            (int)CefCursorType.IBeam => Control.CursorShape.Ibeam,
            (int)CefCursorType.Hand => Control.CursorShape.PointingHand,
            (int)CefCursorType.Cross => Control.CursorShape.Cross,
            (int)CefCursorType.Wait or (int)CefCursorType.Progress => Control.CursorShape.Wait,
            (int)CefCursorType.Help => Control.CursorShape.Help,
            (int)CefCursorType.NotAllowed => Control.CursorShape.Forbidden,
            (int)CefCursorType.NorthSouthResize or (int)CefCursorType.NorthResize or (int)CefCursorType.SouthResize or (int)CefCursorType.RowResize => Control.CursorShape.Vsize,
            (int)CefCursorType.EastWestResize or (int)CefCursorType.EastResize or (int)CefCursorType.WestResize or (int)CefCursorType.ColumnResize => Control.CursorShape.Hsize,
            (int)CefCursorType.Move => Control.CursorShape.Move,
            _ => Control.CursorShape.Arrow,
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConvertBgraToRgba(byte[] buffer, int pixelCount)
    {
        if (Avx2.IsSupported)
        {
            int vecSize = 32, vecCount = pixelCount / 8;
            var m = Vector256.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15, (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
            fixed (byte* p = buffer) { for (int i = 0; i < vecCount; i++) { var d = Avx.LoadVector256(p + i * vecSize); Avx.Store(p + i * vecSize, Avx2.Shuffle(d, m)); } for (int i = vecCount * 8; i < pixelCount; i++) { int o = i * 4; byte b = p[o]; p[o] = p[o + 2]; p[o + 2] = b; } }
        }
        else if (Ssse3.IsSupported)
        {
            int vecSize = 16, vecCount = pixelCount / 4;
            var m = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
            fixed (byte* p = buffer) { for (int i = 0; i < vecCount; i++) { var d = Sse2.LoadVector128(p + i * vecSize); Sse2.Store(p + i * vecSize, Ssse3.Shuffle(d, m)); } for (int i = vecCount * 4; i < pixelCount; i++) { int o = i * 4; byte b = p[o]; p[o] = p[o + 2]; p[o + 2] = b; } }
        }
        else
        {
            for (int i = 0; i < pixelCount; i++) { int o = i * 4; byte b = buffer[o]; buffer[o] = buffer[o + 2]; buffer[o + 2] = b; }
        }
    }
}