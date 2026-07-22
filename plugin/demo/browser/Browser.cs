using Godot;
using System;
using System.Collections.Generic;
using GDCefGlue;
using Xilium.CefGlue;

/// <summary>
/// Multi-tab browser demo.
/// Each tab is an independent CefGlueControl (= independent CEF render process).
/// Memory usage: ~150MB base + ~100MB per tab.
/// </summary>
public partial class Browser : Control
{
    private TabContainer _tabContainer;
    private LineEdit _urlInput;
    private Button _goButton;
    private Button _backButton;
    private Button _forwardButton;
    private Button _reloadButton;
    private Button _addTabButton;
    private Button _closeTabButton;
    private Button _openDevButton;
    private Label _statusLabel;
    private int _tabCounter = 2;

    private CefGlueControl CurrentBrowser
    {
        get
        {
            var tab = _tabContainer?.GetCurrentTabControl();
            return tab as CefGlueControl;
        }
    }

    public override void _Ready()
    {
        _tabContainer = GetNode<TabContainer>("TabContainer");
        _urlInput = GetNode<LineEdit>("Toolbar/UrlInput");
        _goButton = GetNode<Button>("Toolbar/GoButton");
        _backButton = GetNode<Button>("Toolbar/BackButton");
        _forwardButton = GetNode<Button>("Toolbar/ForwardButton");
        _reloadButton = GetNode<Button>("Toolbar/ReloadButton");
        _addTabButton = GetNode<Button>("Toolbar/AddTabButton");
        _closeTabButton = GetNode<Button>("Toolbar/CloseTabButton");
        _openDevButton = GetNode<Button>("Toolbar/OpenDevButton");
        _statusLabel = GetNode<Label>("StatusBar/StatusLabel");

        _goButton.Pressed += OnGoPressed;
        _backButton.Pressed += OnBackPressed;
        _forwardButton.Pressed += OnForwardPressed;
        _reloadButton.Pressed += OnReloadPressed;
        _addTabButton.Pressed += OnAddTabPressed;
        _closeTabButton.Pressed += OnCloseTabPressed;
        _openDevButton.Pressed += OnOpenDevPressed;
        _urlInput.TextSubmitted += OnUrlSubmitted;

        _tabContainer.TabChanged += OnTabChanged;

        // Connect existing tabs
        foreach (var child in _tabContainer.GetChildren())
        {
            if (child is CefGlueControl cef)
                ConnectTab(cef);
        }

        // Sync URL bar to first tab
        UpdateUrlBar();
    }

    private void ConnectTab(CefGlueControl cef)
    {
        cef.BrowserInitialized += OnBrowserInitialized;
        cef.AddressChanged += OnAddressChanged;
        cef.TitleChanged += OnTitleChanged;
        cef.LoadStart += OnLoadStart;
        cef.LoadEnd += OnLoadEnd;
        cef.LoadError += OnLoadError;
        cef.NewWindowRequested += OnNewWindowRequested;
        // 右键菜单：不订阅 BeforeContextMenu → 显示 CEF 默认菜单（后退/复制/粘贴等）
        // 仅订阅 ContextMenuCommand 处理自定义命令（DevTools）
        cef.ContextMenuCommand += OnContextMenuCommand;
    }

    private bool OnContextMenuCommand(int commandId, CefGlueControl.ContextMenuParams parameters, CefEventFlags eventFlags)
    {
        switch (commandId)
        {
            case CmdOpenDevTools:
                CurrentBrowser?.ShowDeveloperTools();
                return true;

            case CmdCopyLinkUrl:
                DisplayServer.ClipboardSet(parameters.LinkUrl);
                return true;

            case CmdOpenLinkNewTab:
                if (!string.IsNullOrEmpty(parameters.LinkUrl))
                    CallDeferred(nameof(AddTabWithUrl), parameters.LinkUrl);
                return true;

            // 内置 ID (Back/Forward/Reload/Copy/Paste/...) 由 CEF 自动处理
            default:
                GD.Print($"[Browser] ContextMenuCommand: id={commandId} (built-in, CEF handles)");
                return false;
        }
    }

    // 自定义命令 ID — 必须在 CefMenuId.UserFirst..UserLast 范围内
    private const int CmdCopyLinkUrl = (int)CefMenuId.UserFirst + 1;
    private const int CmdOpenLinkNewTab = (int)CefMenuId.UserFirst + 2;
    private const int CmdOpenDevTools = (int)CefMenuId.UserFirst + 3;

    private void OnNewWindowRequested(string url, bool isNewWindow)
    {
        if (string.IsNullOrEmpty(url)) return;
        // 新窗口 → 创建新标签页（当前 demo 以标签代替独立窗口）
        // 新标签页 → 创建新标签页
        CallDeferred(nameof(AddTabWithUrl), url);
    }

    private void AddTabWithUrl(string url)
    {
        _tabCounter++;
        var tab = new CefGlueControl
        {
            Name = $"Tab{_tabCounter}",
            FrameRate = 120,
            InitialUrl = url,
            Mode = RenderMode.EmbeddedWindow,
            OpenPopupInCurrentBrowser = false,
            SyncCursor = true,
        };
        ConnectTab(tab);
        _tabContainer.AddChild(tab);
        _tabContainer.CurrentTab = _tabContainer.GetTabCount() - 1;
    }

    private void OnTabChanged(long tabIndex)
    {
        UpdateUrlBar();
        _statusLabel.Text = CurrentBrowser?.IsLoading == true ? "Loading..." : "Ready";
    }

    private void OnTabClosePressed(long tabIndex)
    {
        var tab = _tabContainer.GetChild<Control>((int)tabIndex);
        if (tab == null || _tabContainer.GetTabCount() <= 1) return; // keep at least 1 tab
        tab.QueueFree();
    }

    private void UpdateUrlBar()
    {
        var cef = CurrentBrowser;
        if (cef != null && _urlInput != null)
            _urlInput.Text = cef.Address;
    }

    private void OnAddTabPressed()
    {
        _tabCounter++;
        var tab = new CefGlueControl
        {
            Name = $"Tab{_tabCounter}",
            FrameRate = 120,
            InitialUrl = "https://www.bing.com",
            Mode = RenderMode.EmbeddedWindow,
            OpenPopupInCurrentBrowser = false,
            SyncCursor = true,
        };
        ConnectTab(tab);
        _tabContainer.AddChild(tab);
        _tabContainer.CurrentTab = _tabContainer.GetTabCount() - 1;
        _statusLabel.Text = $"Tab {_tabCounter}: new tab";
    }

    private void OnCloseTabPressed()
    {
        var tab = _tabContainer.GetCurrentTabControl();
        if (tab == null) return;
        tab.QueueFree();
        if (_tabContainer.GetTabCount() == 0)
            GetTree().Quit(); // close last tab → quit app
    }

    // ── CEF callbacks ──

    private void OnBrowserInitialized()
    {
        CallDeferred(nameof(UpdateStatusLabel), "Ready");
    }

    private void OnAddressChanged(object sender, string url)
    {
        if (sender == CurrentBrowser)
            CallDeferred(nameof(UpdateUrlInput), url);
    }

    private void OnTitleChanged(object sender, string title)
    {
        if (sender is CefGlueControl cef && !string.IsNullOrEmpty(title))
            cef.Name = title.Length > 20 ? title[..20] + "…" : title;
    }

    private void OnLoadStart(object sender, Xilium.CefGlue.Common.Events.LoadStartEventArgs e)
    {
        if (sender == CurrentBrowser)
            CallDeferred(nameof(UpdateStatusLabel), "Loading...");
    }

    private void OnLoadEnd(object sender, Xilium.CefGlue.Common.Events.LoadEndEventArgs e)
    {
        if (sender == CurrentBrowser)
            CallDeferred(nameof(UpdateStatusLabel), "Done");
    }

    private void OnLoadError(object sender, Xilium.CefGlue.Common.Events.LoadErrorEventArgs e)
    {
        if (sender == CurrentBrowser)
            CallDeferred(nameof(UpdateStatusLabel), $"Error: {e.ErrorText}");
    }

    private void UpdateStatusLabel(string text)
    {
        if (_statusLabel != null)
            _statusLabel.Text = text;
    }

    private void UpdateUrlInput(string url)
    {
        if (_urlInput != null)
            _urlInput.Text = url;
    }

    // ── Toolbar actions ──

    private void OnBackPressed() => CurrentBrowser?.GoBack();
    private void OnForwardPressed() => CurrentBrowser?.GoForward();
    private void OnReloadPressed() => CurrentBrowser?.Reload();
    private void OnOpenDevPressed() => CurrentBrowser?.ShowDeveloperTools();

    private void OnGoPressed() => NavigateToUrl();
    private void OnUrlSubmitted(string text) => NavigateToUrl();

    private void NavigateToUrl()
    {
        var url = _urlInput?.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;
        if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("about:") && !url.StartsWith("file://"))
            url = "https://" + url;
        if (CurrentBrowser != null)
            CurrentBrowser.Address = url;
    }
}