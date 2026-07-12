using System;
using System.Buffers;
using System.Runtime.InteropServices;
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
            _isDirty = true;
        }
    }

    protected override void _Process(double delta)
    {
        if (Godot.Engine.Singleton.IsEditorHint()) return;
        _cachedGlobalPosition = GlobalPosition;
        if (_browserHost != null && Size.X > 0 && Size.Y > 0)
        {
            int w = (int)Size.X, h = (int)Size.Y;
            if (w != _controlWidth || h != _controlHeight) { _controlWidth = w; _controlHeight = h; _pendingWidth = w; _pendingHeight = h; _resizeStableCount = 0; QueueRedraw(); }
            else if (_pendingWidth > 0 && _pendingHeight > 0 && ++_resizeStableCount >= ResizeStableThreshold) { _browserHost.WasResized(); _browserHost.Invalidate(CefPaintElementType.View); _pendingWidth = 0; _pendingHeight = 0; }
            else if (_width != _controlWidth || _height != _controlHeight) _browserHost.Invalidate(CefPaintElementType.View);
        }
        if (_isDirty && _pixelBuffer != null && _width > 0 && _height > 0)
        {
            int expected = _width * _height * 4;
            if (_pixelBufferSize >= expected)
            {
                lock (_bufferLock)
                {
                    var pba = new PackedByteArray(_pixelBuffer.AsSpan(0, expected));
                    _image.SetData(_width, _height, false, Image.Format.Rgba8, pba);
                    _texture = ImageTexture.CreateFromImage(_image);
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
        if (_texture != null && _controlWidth > 0 && _controlHeight > 0)
        {
            if (_width == _controlWidth && _height == _controlHeight) DrawTexture(_texture, Vector2.Zero);
            else DrawTextureRect(_texture, new Rect2(Vector2.Zero, _controlWidth, _controlHeight), false);
        }
    }

    internal void OnCursorChanged(CefCursorType type) { if (!SyncCursor) return; CallDeferred(nameof(UpdateCursorShape), (int)type); }

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
}