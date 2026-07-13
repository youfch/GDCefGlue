using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Events;

namespace GDCefGlueExtension;

public enum RenderMode
{
    OSR = 0,
    EmbeddedWindow = 1
}

public partial class CefGlueControl : Control
{
    private CefBrowser _browser;
    private CefBrowserHost _browserHost;
    private CefClient _client;
    private Image _image;
    private ImageTexture _texture;
    private byte[] _pixelBuffer;
    private PackedByteArray _packedBuffer;
    private readonly object _bufferLock = new();
    private int _pixelBufferSize;
    private int _renderBufferSize;
    internal int _width;
    internal int _height;
    internal int _controlWidth;
    internal int _controlHeight;
    internal Vector2 _cachedGlobalPosition;
    private bool _isFocused;
    private bool _browserCreated;
    private bool _isDirty;
    private CefMouseButtonType _pressedButton = (CefMouseButtonType)(-1);
    private bool _isMousePressed;
    private double _lastClickTime;
    private int _clickCount;
    private const double DoubleClickInterval = 0.5;
    private int _pendingWidth;
    private int _pendingHeight;
    private int _resizeStableCount;
    private const int ResizeStableThreshold = 2;
    private int _lastEvalTaskId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pendingEvals = new();
    private readonly Dictionary<string, Callable> _jsHandlers = new();

    public CefGlueControl()
    {
        GD.Print("CefGlueControl: Constructor called");
    }
}