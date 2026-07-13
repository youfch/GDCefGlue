using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Events;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    public void GoBack() => _browser?.GoBack();
    public void GoForward() => _browser?.GoForward();
    public void NavigateToUrl(string url) { if (!string.IsNullOrEmpty(url)) _browser?.GetMainFrame()?.LoadUrl(url); }
    public void Reload(bool ignoreCache = false) => _browser?.Reload();
    public void ShowDeveloperTools() { var w = CefWindowInfo.Create(); w.RuntimeStyle = CefRuntimeStyle.Chrome; _browserHost?.ShowDevTools(w, _client, new CefBrowserSettings(), new CefPoint()); }
    public void CloseDeveloperTools() => _browserHost?.CloseDevTools();

    public void EvalJs(string code) => _ = EvalJsAsync(code);
    private async Task EvalJsAsync(string code)
    { string result = null, error = null; try { result = await InternalEvalRaw($"return {code};"); } catch (Exception ex) { error = ex.Message; } CallDeferred(nameof(OnEvalDone), result ?? "", error ?? ""); }
    private Task<string> InternalEvalRaw(string code)
    { var frame = _browser?.GetMainFrame(); if (frame == null) return Task.FromResult<string>(null); var id = Interlocked.Increment(ref _lastEvalTaskId); var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); _pendingEvals.TryAdd(id, tcs); var m = CefProcessMessage.Create("JsEvaluationRequest"); using (var a = m.Arguments) { a.SetInt(0, id); a.SetString(1, code); a.SetString(2, "about:blank"); a.SetInt(3, 1); } frame.SendProcessMessage(CefProcessId.Renderer, m); return tcs.Task; }
    private void OnEvalDone(string result, string error) => EmitSignal(new StringName("eval_completed"), result, error);

    // ── CEF 事件回调 ──
    internal void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
    { if (frame.IsMain) CallDeferred("_notify_address_changed", url); }
    private void _notify_address_changed(string url) => AddressChanged?.Invoke(this, url);

    internal void OnTitleChange(CefBrowser browser, string title)
    { Title = title; CallDeferred("_notify_title_changed", title); }
    private void _notify_title_changed(string title) => TitleChanged?.Invoke(this, title);

    internal void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
    => CallDeferred("_notify_load_start");
    private void _notify_load_start() { LoadStart?.Invoke(this, new LoadStartEventArgs(null)); EmitSignal(new StringName(nameof(LoadStart))); }

    internal void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
    {
        // 每次页面加载后重新注册 JS handler（BrowserProcess的V8上下文重建不可靠）
        if (frame.IsMain)
        {
            foreach (var kv in _jsHandlerMethods)
            {
                var msg = CefProcessMessage.Create("NativeObjectRegistrationRequest");
                using (var a = msg.Arguments) { a.SetString(0, kv.Key); a.SetString(1, kv.Value); }
                frame.SendProcessMessage(CefProcessId.Renderer, msg);
            }
        }
        CallDeferred("_notify_load_end");
    }
    private void _notify_load_end() { LoadEnd?.Invoke(this, new LoadEndEventArgs(null, 0)); EmitSignal(new StringName(nameof(LoadEnd))); }

    internal void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
    { CallDeferred("_notify_load_error", errorText, failedUrl); }
    private void _notify_load_error(string errorText, string failedUrl)
    { LoadError?.Invoke(this, new LoadErrorEventArgs(null, CefErrorCode.None, errorText, failedUrl)); EmitSignal(new StringName(nameof(LoadError)), errorText, failedUrl); }
}