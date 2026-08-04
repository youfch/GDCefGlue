using System;
using System.Collections.Generic;
using Godot;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Events;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    // ── Inspector 属性（通过 BindMembers + _GetPropertyList 注册）──
    public string InitialUrl { get; set; } = "about:blank";

    public string CacheDirectory { get; set; } = "user://cef_cache";

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

    private bool _gpuCompositing = true;
    public bool GpuCompositing
    {
        get => _gpuCompositing;
        set => _gpuCompositing = value;
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

    private bool _contextMenuEnabled = true;
    public bool ContextMenuEnabled
    {
        get => _contextMenuEnabled;
        set => _contextMenuEnabled = value;
    }

    // ── Embedded Mode ──
    private bool _forwardInputEvents;
    public bool ForwardInputEvents
    {
        get => _forwardInputEvents;
        set => _forwardInputEvents = value;
    }

    private static bool _useGpuCompositing = true;
    private static bool _useTransparent = false;
    private static RenderMode _activeRenderMode = RenderMode.OSR;
    public static bool UseGpuCompositing { get => _useGpuCompositing; set => _useGpuCompositing = value; }
    public static bool UseTransparent { get => _useTransparent; set => _useTransparent = value; }
    public static RenderMode ActiveRenderMode
    {
        get => _activeRenderMode;
        set => _activeRenderMode = value;
    }

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

    // ── 右键菜单事件（OSR 模式） ──

    /// <summary>
    /// 右键菜单即将显示时触发。可在事件处理中修改 <paramref name="model"/>
    /// （清空、添加、移除项）来定制菜单内容。
    /// </summary>
    public event Action<ContextMenuModel, ContextMenuParams> BeforeContextMenu;

    /// <summary>
    /// 右键菜单命令被选中时触发。参数: (commandId, parameters, eventFlags)。
    /// 返回 true 表示已处理；返回 false 让 CEF 应用默认行为（对内置 ID 有效）。
    /// </summary>
    public event Func<int, ContextMenuParams, CefEventFlags, bool> ContextMenuCommand;

    internal void RaiseBeforeContextMenu(ContextMenuModel model, ContextMenuParams parameters)
        => BeforeContextMenu?.Invoke(model, parameters);

    internal bool RaiseContextMenuCommand(int commandId, ContextMenuParams parameters, CefEventFlags eventFlags)
    {
        var handler = ContextMenuCommand;
        return handler != null && handler(commandId, parameters, eventFlags);
    }

    internal bool HasBeforeContextMenuSubscribers => BeforeContextMenu != null;
    internal bool HasContextMenuCommandSubscribers => ContextMenuCommand != null;

    // ── 页面内查找事件 ──
    public event Action<int, int, int, bool> FindResult;
    internal void RaiseFindResult(int identifier, int count, int activeMatchOrdinal, bool finalUpdate)
        => FindResult?.Invoke(identifier, count, activeMatchOrdinal, finalUpdate);
}