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
        set { _mode = value; NotifyPropertyListChanged(); }
    }

    public int FrameRate { get; set; } = 60;
    public bool Transparent { get; set; } = false;
    public bool GpuAcceleration { get; set; } = true;
    public bool OpenPopupInCurrentBrowser { get; set; } = true;
    public bool SyncCursor { get; set; } = false;

    // ── Embedded Mode ──
    public bool ForwardInputEvents { get; set; } = false;

    private static bool _useGpuAcceleration = true;
    private static bool _useTransparent = false;
    public static bool UseGpuAcceleration { get => _useGpuAcceleration; set => _useGpuAcceleration = value; }
    public static bool UseTransparent { get => _useTransparent; set => _useTransparent = value; }

    public string Address
    {
        get => _browser?.GetMainFrame()?.Url ?? InitialUrl;
        set { if (_browser?.GetMainFrame() != null) _browser.GetMainFrame().LoadUrl(value); else InitialUrl = value; }
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
}