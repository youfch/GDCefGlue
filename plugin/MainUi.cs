using Godot;
using System;
using GDCefGlue;

public partial class MainUi : Control
{
    private CefGlueControl _browser;
    private LineEdit _urlInput;
    private Button _goButton;
    private Button _backButton;
    private Button _forwardButton;
    private Button _reloadButton;
    private Button _openDevButton;
    private Label _statusLabel;

    public override void _Ready()
    {
        _browser = GetNode<CefGlueControl>("Browser");
        _urlInput = GetNode<LineEdit>("Toolbar/UrlInput");
        _goButton = GetNode<Button>("Toolbar/GoButton");
        _backButton = GetNode<Button>("Toolbar/BackButton");
        _forwardButton = GetNode<Button>("Toolbar/ForwardButton");
        _reloadButton = GetNode<Button>("Toolbar/ReloadButton");
        _openDevButton = GetNode<Button>("Toolbar/OpenDevButton");
        _statusLabel = GetNode<Label>("StatusBar/StatusLabel");

        _browser.InitialUrl = "https://www.bing.com";
        _browser.BrowserInitialized += OnBrowserInitialized;
        _browser.AddressChanged += OnAddressChanged;
        _browser.LoadStart += OnLoadStart;
        _browser.LoadEnd += OnLoadEnd;
        _browser.LoadError += OnLoadError;

        _goButton.Pressed += OnGoPressed;
        _backButton.Pressed += OnBackPressed;
        _forwardButton.Pressed += OnForwardPressed;
        _reloadButton.Pressed += OnReloadPressed;
        _openDevButton.Pressed += OnOpenDevPressed;
        _urlInput.TextSubmitted += OnUrlSubmitted;
    }

    private void OnBrowserInitialized()
    {
        CallDeferred(nameof(UpdateStatusLabel), "Ready");
    }

    private void OnAddressChanged(object sender, string url)
    {
        CallDeferred(nameof(UpdateUrlInput), url);
    }

    private void OnLoadStart(object sender, Xilium.CefGlue.Common.Events.LoadStartEventArgs e)
    {
        CallDeferred(nameof(UpdateStatusLabel), "Loading...");
    }

    private void OnLoadEnd(object sender, Xilium.CefGlue.Common.Events.LoadEndEventArgs e)
    {
        CallDeferred(nameof(UpdateStatusLabel), "Done");
    }

    private void OnLoadError(object sender, Xilium.CefGlue.Common.Events.LoadErrorEventArgs e)
    {
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

    private void OnBackPressed()
    {
        _browser?.GoBack();
    }

    private void OnForwardPressed()
    {
        _browser?.GoForward();
    }

    private void OnReloadPressed()
    {
        _browser?.Reload();
    }

    private void OnOpenDevPressed()
    {
        _browser?.ShowDeveloperTools();
    }

    private void OnGoPressed()
    {
        NavigateToUrl();
    }

    private void OnUrlSubmitted(string text)
    {
        NavigateToUrl();
    }

    private void NavigateToUrl()
    {
        var url = _urlInput?.Text?.Trim();
        if (string.IsNullOrEmpty(url))
            return;

        if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("about:") && !url.StartsWith("file://"))
        {
            url = "https://" + url;
        }

        if (_browser != null)
        {
            _browser.Address = url;
        }
    }
}
