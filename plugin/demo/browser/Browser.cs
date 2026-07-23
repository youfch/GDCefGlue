using Godot;
using System;
using System.Collections.Generic;
using GDCefGlue;
using Xilium.CefGlue;

/// <summary>
/// Multi-tab browser demo.
/// Each tab is an independent CefGlueControl (= independent CEF render process).
/// Supports both EmbeddedWindow (default) and OSR (transparent) modes.
/// Features: Ctrl+F find-in-page, right-click context menu, DevTools.
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
    private Button _addOsrTabButton;
    private Button _closeTabButton;
    private Button _openDevButton;
    private Button _gcButton;
    private Label _statusLabel;
    private int _tabCounter = 2;

    // ── OSR mode toggle ──
    private bool _osrMode; // false=EmbeddedWindow, true=OSR

    // ── Find-in-page ──
    private Panel _searchBar;
    private LineEdit _searchInput;
    private Button _searchPrev;
    private Button _searchNext;
    private Button _searchClose;
    private Label _searchMatchCount;
    private bool _searchVisible;

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

        // 工具栏按钮现在在 Toolbar/ToolbarHBox/ 下（HBoxContainer 布局）
        _urlInput = GetNode<LineEdit>("Toolbar/ToolbarHBox/UrlInput");
        _goButton = GetNode<Button>("Toolbar/ToolbarHBox/GoButton");
        _backButton = GetNode<Button>("Toolbar/ToolbarHBox/BackButton");
        _forwardButton = GetNode<Button>("Toolbar/ToolbarHBox/ForwardButton");
        _reloadButton = GetNode<Button>("Toolbar/ToolbarHBox/ReloadButton");
        _addTabButton = GetNode<Button>("Toolbar/ToolbarHBox/AddTabButton");
        _addOsrTabButton = GetNode<Button>("Toolbar/ToolbarHBox/AddOsrTabButton");
        _closeTabButton = GetNode<Button>("Toolbar/ToolbarHBox/CloseTabButton");
        _openDevButton = GetNode<Button>("Toolbar/ToolbarHBox/OpenDevButton");
        _gcButton = GetNode<Button>("Toolbar/ToolbarHBox/GcButton");
        _statusLabel = GetNode<Label>("StatusBar/StatusLabel");

        // ── Search bar（在 SearchBar/SearchHBox/ 下）──
        _searchBar = GetNode<Panel>("SearchBar");
        _searchInput = GetNode<LineEdit>("SearchBar/SearchHBox/SearchInput");
        _searchPrev = GetNode<Button>("SearchBar/SearchHBox/SearchPrev");
        _searchNext = GetNode<Button>("SearchBar/SearchHBox/SearchNext");
        _searchClose = GetNode<Button>("SearchBar/SearchHBox/SearchClose");
        _searchMatchCount = GetNode<Label>("SearchBar/SearchHBox/SearchMatchCount");

        _goButton.Pressed += OnGoPressed;
        _backButton.Pressed += OnBackPressed;
        _forwardButton.Pressed += OnForwardPressed;
        _reloadButton.Pressed += OnReloadPressed;
        _addTabButton.Pressed += OnAddTabPressed;
        _addOsrTabButton.Pressed += OnOsrTogglePressed;
        _closeTabButton.Pressed += OnCloseTabPressed;
        _openDevButton.Pressed += OnOpenDevPressed;
        _gcButton.Pressed += OnGcPressed;
        _urlInput.TextSubmitted += OnUrlSubmitted;

        _tabContainer.TabChanged += OnTabChanged;

        _searchInput.TextChanged += OnSearchTextChanged;
        _searchInput.TextSubmitted += OnSearchSubmitted;
        _searchPrev.Pressed += OnSearchPrev;
        _searchNext.Pressed += OnSearchNext;
        _searchClose.Pressed += OnSearchClose;

        // Connect existing tabs
        foreach (var child in _tabContainer.GetChildren())
        {
            if (child is CefGlueControl cef)
                ConnectTab(cef);
        }

        // Sync URL bar to first tab
        UpdateUrlBar();

        // Apply dark theme
        CallDeferred(nameof(ApplyTheme));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // Ctrl+F → toggle search bar
            if (key.Keycode == Key.F && key.CtrlPressed)
            {
                GetViewport()?.SetInputAsHandled();
                ToggleSearchBar();
            }
            // Escape → close search bar
            if (key.Keycode == Key.Escape && _searchVisible)
            {
                GetViewport()?.SetInputAsHandled();
                HideSearchBar();
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Tab management
    // ══════════════════════════════════════════════════════════════

    private void ConnectTab(CefGlueControl cef)
    {
        cef.BrowserInitialized += OnBrowserInitialized;
        cef.AddressChanged += OnAddressChanged;
        cef.TitleChanged += OnTitleChanged;
        cef.LoadStart += OnLoadStart;
        cef.LoadEnd += OnLoadEnd;
        cef.LoadError += OnLoadError;
        cef.NewWindowRequested += OnNewWindowRequested;
        cef.FindResult += OnFindResult;
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
        CallDeferred(nameof(AddTabWithUrl), url);
    }

    private void AddTabWithUrl(string url)
    {
        _tabCounter++;
        var isOsr = _osrMode;
        var tab = new CefGlueControl
        {
            Name = $"Tab{_tabCounter}",
            FrameRate = 120,
            InitialUrl = url,
            Mode = isOsr ? RenderMode.OSR : RenderMode.EmbeddedWindow,
            Transparent = isOsr,
            ContextMenuEnabled = isOsr,
            OpenPopupInCurrentBrowser = false,
            SyncCursor = true,
        };
        ConnectTab(tab);
        _tabContainer.AddChild(tab);
        _tabContainer.CurrentTab = _tabContainer.GetTabCount() - 1;
        _statusLabel.Text = isOsr
            ? $"Tab {_tabCounter}: OSR mode (transparent)"
            : $"Tab {_tabCounter}: EmbeddedWindow mode";
    }

    private void OnTabChanged(long tabIndex)
    {
        UpdateUrlBar();
        _statusLabel.Text = CurrentBrowser?.IsLoading == true ? "Loading..." : "Ready";
        // 切换标签时关闭搜索栏
        if (_searchVisible)
            HideSearchBar();
    }

    private void OnTabClosePressed(long tabIndex)
    {
        var tab = _tabContainer.GetChild<Control>((int)tabIndex);
        if (tab == null || _tabContainer.GetTabCount() <= 1) return;
        tab.QueueFree();
    }

    private void UpdateUrlBar()
    {
        var cef = CurrentBrowser;
        if (cef != null && _urlInput != null)
            _urlInput.Text = cef.Address;
    }

    // ══════════════════════════════════════════════════════════════
    //  Toolbar handlers
    // ══════════════════════════════════════════════════════════════

    private void OnAddTabPressed()
    {
        AddTabWithUrl("https://www.bing.com");
    }

    private void OnOsrTogglePressed()
    {
        _osrMode = !_osrMode;
        UpdateOsrButtonState();
        _statusLabel.Text = _osrMode ? "OSR mode ON — new tabs will use OSR" : "OSR mode OFF — new tabs will use EmbeddedWindow";
    }

    private void UpdateOsrButtonState()
    {
        if (_osrMode)
        {
            _addOsrTabButton.Text = "OSR";
            _addOsrTabButton.AddThemeColorOverride("font_color", new Color(0.25f, 0.50f, 1.0f));
            _addOsrTabButton.AddThemeColorOverride("font_hover_color", new Color(0.35f, 0.58f, 1.0f));
        }
        else
        {
            _addOsrTabButton.Text = "OSR";
            _addOsrTabButton.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f));
            _addOsrTabButton.AddThemeColorOverride("font_hover_color", new Color(0.8f, 0.8f, 0.9f));
        }
    }

    private void OnCloseTabPressed()
    {
        var tab = _tabContainer.GetCurrentTabControl();
        if (tab == null) return;
        tab.QueueFree();
        if (_tabContainer.GetTabCount() == 0)
            GetTree().Quit();
    }

    // ══════════════════════════════════════════════════════════════
    //  Find-in-page
    // ══════════════════════════════════════════════════════════════

    private void ToggleSearchBar()
    {
        if (_searchVisible)
            HideSearchBar();
        else
            ShowSearchBar();
    }

    private void ShowSearchBar()
    {
        _searchVisible = true;
        _searchBar.Visible = true;
        _searchBar.OffsetTop = -52;
        _searchBar.OffsetBottom = -24;
        _searchInput.GrabFocus();
        _searchInput.SelectAll();
    }

    private void HideSearchBar()
    {
        _searchVisible = false;
        _searchBar.Visible = false;
        _searchBar.OffsetTop = -24;
        _searchBar.OffsetBottom = -24;
        // 停止当前搜索
        CurrentBrowser?.StopFinding(true);
        _searchMatchCount.Text = "0/0";
    }

    private void OnSearchTextChanged(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            CurrentBrowser?.StopFinding(true);
            _searchMatchCount.Text = "0/0";
            return;
        }
        // 新搜索会话
        CurrentBrowser?.Find(text, forward: true, matchCase: false, findNext: false);
    }

    private void OnSearchSubmitted(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // Enter → 查找下一个
        CurrentBrowser?.Find(text, forward: true, matchCase: false, findNext: true);
    }

    private void OnSearchNext()
    {
        var text = _searchInput.Text;
        if (string.IsNullOrEmpty(text)) return;
        CurrentBrowser?.Find(text, forward: true, matchCase: false, findNext: true);
    }

    private void OnSearchPrev()
    {
        var text = _searchInput.Text;
        if (string.IsNullOrEmpty(text)) return;
        CurrentBrowser?.Find(text, forward: false, matchCase: false, findNext: true);
    }

    private void OnSearchClose()
    {
        HideSearchBar();
    }

    private void OnFindResult(int identifier, int count, int activeMatchOrdinal, bool finalUpdate)
    {
        // CEF 可能多次回调（intermediate + final），只在 finalUpdate=true 时更新 UI
        if (!finalUpdate) return;

        if (count > 0)
            _searchMatchCount.Text = $"{activeMatchOrdinal}/{count}";
        else
            _searchMatchCount.Text = "0/0";
    }

    // ══════════════════════════════════════════════════════════════
    //  CEF callbacks
    // ══════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════
    //  Toolbar actions
    // ══════════════════════════════════════════════════════════════

    private void OnBackPressed() => CurrentBrowser?.GoBack();
    private void OnForwardPressed() => CurrentBrowser?.GoForward();
    private void OnReloadPressed() => CurrentBrowser?.Reload();
    private void OnOpenDevPressed() => CurrentBrowser?.ShowDeveloperTools();

    private void OnGcPressed()
    {
        var before = GC.GetTotalMemory(forceFullCollection: false);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        var after = GC.GetTotalMemory(forceFullCollection: false);
        var freed = before - after;
        _statusLabel.Text = $"GC: {(freed / 1024.0 / 1024.0):F1} MB freed (now {after / 1024.0 / 1024.0:F1} MB)";
    }

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

    // ══════════════════════════════════════════════════════════════
    //  Dark theme
    // ══════════════════════════════════════════════════════════════

    private void ApplyTheme()
    {
        // ── Colors ──
        var bgDark = new Color(0.12f, 0.12f, 0.13f);
        var bgMedium = new Color(0.16f, 0.16f, 0.17f);
        var bgLight = new Color(0.20f, 0.20f, 0.22f);
        var accent = new Color(0.25f, 0.50f, 1.0f);
        var accentHover = new Color(0.35f, 0.58f, 1.0f);
        var textPrimary = new Color(0.92f, 0.92f, 0.95f);
        var textSecondary = new Color(0.60f, 0.60f, 0.65f);
        var borderColor = new Color(0.25f, 0.25f, 0.28f);

        // ── Toolbar ──
        var toolbarPanel = GetNode<Panel>("Toolbar");
        var toolbarBg = new StyleBoxFlat();
        toolbarBg.BgColor = bgDark;
        toolbarBg.ContentMarginLeft = 6;
        toolbarBg.ContentMarginRight = 6;
        toolbarPanel.AddThemeStyleboxOverride("panel", toolbarBg);

        // ── Status bar ──
        var statusPanel = GetNode<Panel>("StatusBar");
        var statusBg = new StyleBoxFlat();
        statusBg.BgColor = bgDark;
        statusPanel.AddThemeStyleboxOverride("panel", statusBg);

        _statusLabel.AddThemeColorOverride("font_color", textSecondary);
        _statusLabel.AddThemeFontSizeOverride("font_size", 12);

        // ── Search bar ──
        var searchPanel = GetNode<Panel>("SearchBar");
        var searchBg = new StyleBoxFlat();
        searchBg.BgColor = new Color(0.14f, 0.14f, 0.15f); // 略深于 TabContainer，视觉区分
        searchBg.BorderWidthTop = 1;
        searchBg.BorderWidthBottom = 1;
        searchBg.BorderColor = borderColor;
        searchPanel.AddThemeStyleboxOverride("panel", searchBg);

        //搜索栏 "Find:" 标签
        var searchLabel = GetNode<Label>("SearchBar/SearchHBox/SearchLabel");
        searchLabel.AddThemeColorOverride("font_color", textSecondary);
        searchLabel.AddThemeFontSizeOverride("font_size", 12);

        _searchMatchCount.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.3f)); // 黄色高亮匹配数
        _searchMatchCount.AddThemeFontSizeOverride("font_size", 12);

        // 搜索栏按钮使用更紧凑的边距（搜索栏高度较矮）
        var searchBtnNormal = new StyleBoxFlat();
        searchBtnNormal.BgColor = new Color(0, 0, 0, 0);
        searchBtnNormal.ContentMarginLeft = 4;
        searchBtnNormal.ContentMarginRight = 4;
        searchBtnNormal.ContentMarginTop = 2;
        searchBtnNormal.ContentMarginBottom = 2;

        var searchBtnHover = new StyleBoxFlat();
        searchBtnHover.BgColor = bgLight;
        searchBtnHover.CornerRadiusTopLeft = 3;
        searchBtnHover.CornerRadiusTopRight = 3;
        searchBtnHover.CornerRadiusBottomLeft = 3;
        searchBtnHover.CornerRadiusBottomRight = 3;
        searchBtnHover.ContentMarginLeft = 4;
        searchBtnHover.ContentMarginRight = 4;
        searchBtnHover.ContentMarginTop = 2;
        searchBtnHover.ContentMarginBottom = 2;

        var searchBtnPressed = new StyleBoxFlat();
        searchBtnPressed.BgColor = new Color(0.28f, 0.28f, 0.30f);
        searchBtnPressed.CornerRadiusTopLeft = 3;
        searchBtnPressed.CornerRadiusTopRight = 3;
        searchBtnPressed.CornerRadiusBottomLeft = 3;
        searchBtnPressed.CornerRadiusBottomRight = 3;
        searchBtnPressed.ContentMarginLeft = 4;
        searchBtnPressed.ContentMarginRight = 4;
        searchBtnPressed.ContentMarginTop = 2;
        searchBtnPressed.ContentMarginBottom = 2;

        var searchButtons = new[] { _searchPrev, _searchNext, _searchClose };
        foreach (var btn in searchButtons)
        {
            if (btn == null) continue;
            btn.AddThemeStyleboxOverride("normal", searchBtnNormal);
            btn.AddThemeStyleboxOverride("hover", searchBtnHover);
            btn.AddThemeStyleboxOverride("pressed", searchBtnPressed);
            btn.AddThemeColorOverride("font_color", textPrimary);
            btn.AddThemeColorOverride("font_hover_color", textPrimary);
            btn.AddThemeColorOverride("font_pressed_color", textPrimary);
            btn.AddThemeFontSizeOverride("font_size", 12);
        }

        // ── TabContainer ──
        var tabContainerBg = new StyleBoxFlat();
        tabContainerBg.BgColor = bgMedium;
        _tabContainer.AddThemeStyleboxOverride("panel", tabContainerBg);

        _tabContainer.AddThemeColorOverride("font_color", textSecondary);
        _tabContainer.AddThemeColorOverride("font_selected_color", textPrimary);
        _tabContainer.AddThemeColorOverride("font_hovered_color", new Color(0.8f, 0.8f, 0.9f));

        // ── Button theme ──
        var btnNormal = new StyleBoxFlat();
        btnNormal.BgColor = new Color(0, 0, 0, 0);
        btnNormal.BorderWidthBottom = 0;
        btnNormal.ContentMarginLeft = 6;
        btnNormal.ContentMarginRight = 6;
        btnNormal.ContentMarginTop = 4;
        btnNormal.ContentMarginBottom = 4;

        var btnHover = new StyleBoxFlat();
        btnHover.BgColor = bgLight;
        btnHover.CornerRadiusTopLeft = 4;
        btnHover.CornerRadiusTopRight = 4;
        btnHover.CornerRadiusBottomLeft = 4;
        btnHover.CornerRadiusBottomRight = 4;
        btnHover.ContentMarginLeft = 6;
        btnHover.ContentMarginRight = 6;
        btnHover.ContentMarginTop = 4;
        btnHover.ContentMarginBottom = 4;

        var btnPressed = new StyleBoxFlat();
        btnPressed.BgColor = new Color(0.28f, 0.28f, 0.30f);
        btnPressed.CornerRadiusTopLeft = 4;
        btnPressed.CornerRadiusTopRight = 4;
        btnPressed.CornerRadiusBottomLeft = 4;
        btnPressed.CornerRadiusBottomRight = 4;
        btnPressed.ContentMarginLeft = 6;
        btnPressed.ContentMarginRight = 6;
        btnPressed.ContentMarginTop = 4;
        btnPressed.ContentMarginBottom = 4;

        var allButtons = new[] {
            _backButton, _forwardButton, _reloadButton,
            _addTabButton, _addOsrTabButton, _closeTabButton, _gcButton,
            _goButton, _openDevButton,
            _searchPrev, _searchNext, _searchClose
        };

        foreach (var btn in allButtons)
        {
            if (btn == null) continue;
            btn.AddThemeStyleboxOverride("normal", btnNormal);
            btn.AddThemeStyleboxOverride("hover", btnHover);
            btn.AddThemeStyleboxOverride("pressed", btnPressed);
            btn.AddThemeColorOverride("font_color", textPrimary);
            btn.AddThemeColorOverride("font_hover_color", textPrimary);
            btn.AddThemeColorOverride("font_pressed_color", textPrimary);
            btn.AddThemeFontSizeOverride("font_size", 12);
        }

        // OSR 按钮初始状态（默认不激活）
        UpdateOsrButtonState();

        // ── LineEdit (URL bar + search) ──
        var urlBg = new StyleBoxFlat();
        urlBg.BgColor = new Color(0.08f, 0.08f, 0.09f);
        urlBg.CornerRadiusTopLeft = 4;
        urlBg.CornerRadiusTopRight = 4;
        urlBg.CornerRadiusBottomLeft = 4;
        urlBg.CornerRadiusBottomRight = 4;
        urlBg.ContentMarginLeft = 10;
        urlBg.ContentMarginRight = 10;
        urlBg.ContentMarginTop = 4;
        urlBg.ContentMarginBottom = 4;

        var urlFocused = new StyleBoxFlat();
        urlFocused.BgColor = new Color(0.08f, 0.08f, 0.09f);
        urlFocused.BorderWidthBottom = 1;
        urlFocused.BorderColor = accent;
        urlFocused.CornerRadiusTopLeft = 4;
        urlFocused.CornerRadiusTopRight = 4;
        urlFocused.CornerRadiusBottomLeft = 4;
        urlFocused.CornerRadiusBottomRight = 4;
        urlFocused.ContentMarginLeft = 10;
        urlFocused.ContentMarginRight = 10;
        urlFocused.ContentMarginTop = 4;
        urlFocused.ContentMarginBottom = 4;

        var urlInputs = new[] { _urlInput, _searchInput };
        foreach (var input in urlInputs)
        {
            if (input == null) continue;
            input.AddThemeStyleboxOverride("normal", urlBg);
            input.AddThemeStyleboxOverride("focus", urlFocused);
            input.AddThemeColorOverride("font_color", textPrimary);
            input.AddThemeColorOverride("placeholder_color", textSecondary);
            input.AddThemeColorOverride("caret_color", accent);
            input.AddThemeFontSizeOverride("font_size", 13);
        }

        // ── HSeparator ──
        var sepColor = new Color(0.22f, 0.22f, 0.24f);
        foreach (var sep in GetNode("Toolbar/ToolbarHBox").GetChildren())
        {
            if (sep is HSeparator hs)
            {
                var sepStyle = new StyleBoxLine();
                sepStyle.Color = sepColor;
                hs.AddThemeStyleboxOverride("separator", sepStyle);
            }
        }
    }
}