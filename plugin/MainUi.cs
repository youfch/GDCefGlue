using Godot;
using System;
using GDCefGlue;
using Xilium.CefGlue;

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
        _browser.BridgeRequest += OnBridgeRequest;

        _goButton.Pressed += OnGoPressed;
        _backButton.Pressed += OnBackPressed;
        _forwardButton.Pressed += OnForwardPressed;
        _reloadButton.Pressed += OnReloadPressed;
        _openDevButton.Pressed += OnOpenDevPressed;
        _urlInput.TextSubmitted += OnUrlSubmitted;
    }

    /// <summary>
    /// JS → C# bridge handler.
    /// Test in browser console:
    ///   fetch('godot://bridge?type=ping&cb=test1&payload={}').catch(()=>{})
    ///   var i=document.createElement('iframe'); i.src='godot://bridge?type=ping&cb=test1&payload=%7B%7D'; document.body.appendChild(i);
    /// Or if __hostBridge is injected:
    ///   window.__hostBridge.send({type:'ping', payload:{}})
    /// </summary>
    private void OnBridgeRequest(string type, string payload, string cbId)
    {
        GD.Print($"[Bridge] type={type}, cb={cbId ?? "none"}, payload={payload}");

        switch (type)
        {
            case "ping":
                _browser.SendResponse(cbId, "{\"status\":\"pong\"}");
                CallDeferred(nameof(UpdateStatusLabel), "Bridge: ping → pong");
                break;

            case "status":
                var status = $"{{\"initialized\":{_browser.IsBrowserInitialized.ToString().ToLower()},\"loading\":{_browser.IsLoading.ToString().ToLower()},\"title\":\"{_browser.Title}\"}}";
                _browser.SendResponse(cbId, status);
                break;

            case "navigate":
                var json = Json.ParseString(payload);
                if (json.VariantType == Variant.Type.Dictionary)
                {
                    var dict = json.AsGodotDictionary();
                    string url = dict.ContainsKey("url") ? dict["url"].AsString() : "";
                    if (!string.IsNullOrEmpty(url))
                    {
                        CallDeferred(nameof(NavigateFromBridge), url);
                        _browser.SendResponse(cbId, "{\"status\":\"navigating\"}");
                    }
                    else
                    {
                        _browser.SendResponse(cbId, "{\"error\":\"url is empty\"}");
                    }
                }
                break;

            default:
                GD.PrintErr($"[Bridge] Unknown type: {type}");
                _browser.SendResponse(cbId, "{\"error\":\"unknown type\"}");
                break;
        }
    }

    private void NavigateFromBridge(string url)
    {
        if (_browser != null)
        {
            _browser.Address = url;
            UpdateStatusLabel($"Bridge navigate: {url}");
        }
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
