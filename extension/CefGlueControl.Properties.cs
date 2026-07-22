using System;
using Godot;
using Xilium.CefGlue.Common.Events;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    // ── Inspector 属性（通过 BindMembers + _ValidateProperty 注册）──
    public string InitialUrl { get; set; } = "about:blank";

    private RenderMode _mode = RenderMode.OSR;
    public RenderMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            _renderMode = value;
            NotifyPropertyListChanged();
        }
    }

    public int FrameRate { get; set; } = 60;

    private bool _transparent;
    public bool Transparent
    {
        get => _transparent;
        set => _transparent = value;
    }

    private bool _gpuAcceleration = true;
    public bool GpuAcceleration
    {
        get => _gpuAcceleration;
        set => _gpuAcceleration = value;
    }

    private bool _openPopupInCurrentBrowser = false;
    public bool OpenPopupInCurrentBrowser
    {
        get => _openPopupInCurrentBrowser;
        set => _openPopupInCurrentBrowser = value;
    }

    private bool _syncCursor;
    public bool SyncCursor
    {
        get => _syncCursor;
        set => _syncCursor = value;
    }

    private bool _enableMediaStream;
    public bool EnableMediaStream
    {
        get => _enableMediaStream;
        set => _enableMediaStream = value;
    }

    // ── Embedded Mode ──
    private bool _forwardInputEvents;
    public bool ForwardInputEvents
    {
        get => _forwardInputEvents;
        set => _forwardInputEvents = value;
    }

    private static bool _useGpuAcceleration = true;
    private static bool _useTransparent = false;
    public static bool UseGpuAcceleration { get => _useGpuAcceleration; set => _useGpuAcceleration = value; }
    public static bool UseTransparent { get => _useTransparent; set => _useTransparent = value; }

    public string Address
    {
        get
        {
            using var frame = _browser?.GetMainFrame();
            return frame?.Url ?? InitialUrl;
        }
        set
        {
            using var frame = _browser?.GetMainFrame();
            if (frame != null) frame.LoadUrl(value);
            else InitialUrl = value;
        }
    }
    public bool IsBrowserInitialized => _browser != null;
    public bool IsLoading => _browser?.IsLoading ?? false;
    public string Title { get; private set; }

    public event Action BrowserInitialized;
    public event AddressChangedEventHandler AddressChanged;
    public event TitleChangedEventHandler TitleChanged;
    public event LoadStartEventHandler LoadStart;
    public event LoadEndEventHandler LoadEnd;
    public event LoadErrorEventHandler LoadError;
    public event Action<string, string, string> BridgeRequest;
    public event Action<string, string, string, Action<string>> NativeCall;
    public event Action<string, bool> NewWindowRequested;
    internal void RaiseNewWindowRequested(string url, bool isNewWindow) => NewWindowRequested?.Invoke(url, isNewWindow);
    internal bool HasNewWindowSubscribers => NewWindowRequested != null;

    // ── 页面内查找事件 ──
    public event Action<int, int, int, bool> FindResult;
    internal void RaiseFindResult(int identifier, int count, int activeMatchOrdinal, bool finalUpdate)
        => FindResult?.Invoke(identifier, count, activeMatchOrdinal, finalUpdate);
}