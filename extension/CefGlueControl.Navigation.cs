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
    public void NavigateToUrl(string url) { if (!string.IsNullOrEmpty(url)) { using var frame = _browser?.GetMainFrame(); frame?.LoadUrl(url); } }
    public void Reload(bool ignoreCache = false) => _browser?.Reload();
    public void ShowDeveloperTools() { var w = CefWindowInfo.Create(); w.RuntimeStyle = CefRuntimeStyle.Chrome; _browserHost?.ShowDevTools(w, _client, new CefBrowserSettings(), new CefPoint()); }
    public void CloseDeveloperTools() => _browserHost?.CloseDevTools();

    // ── 页面内查找 ──
    public void Find(string searchText, bool forward = true, bool matchCase = false, bool findNext = false)
        => _browserHost?.Find(searchText, forward, matchCase, findNext);
    public void StopFinding(bool clearSelection = true)
        => _browserHost?.StopFinding(clearSelection);

    public void EvalJs(string code) => _ = EvalJsAsync(code);
    private async Task EvalJsAsync(string code)
    { string result = null, error = null; try { result = await InternalEvalRaw($"return {code};"); } catch (Exception ex) { error = ex.Message; } CallDeferred(nameof(OnEvalDone), result ?? "", error ?? ""); }
    private Task<string> InternalEvalRaw(string code)
    { var frame = _browser?.GetMainFrame(); if (frame == null) return Task.FromResult<string>(null); var id = Interlocked.Increment(ref _lastEvalTaskId); var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); _pendingEvals.TryAdd(id, tcs); var m = CefProcessMessage.Create("JsEvaluationRequest"); using (var a = m.Arguments) { a.SetInt(0, id); a.SetString(1, code); a.SetString(2, "about:blank"); a.SetInt(3, 1); } frame.SendProcessMessage(CefProcessId.Renderer, m); return tcs.Task; }
    private void OnEvalDone(string result, string error) => EmitSignal(new StringName("eval_completed"), result, error);

    // ── CEF 事件回调 ──
    internal void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
    { if (!_disposed && frame.IsMain) CallDeferred("_notify_address_changed", url); }
    private void _notify_address_changed(string url) => AddressChanged?.Invoke(this, url);

    internal void OnTitleChange(CefBrowser browser, string title)
    { if (!_disposed) { Title = title; CallDeferred("_notify_title_changed", title); } }
    private void _notify_title_changed(string title) => TitleChanged?.Invoke(this, title);

    internal void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
    { if (!_disposed) CallDeferred("_notify_load_start"); }
    private void _notify_load_start() { LoadStart?.Invoke(this, new LoadStartEventArgs(null)); EmitSignal(new StringName("load_start")); }

    internal void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
    {
        if (_disposed) return;
        // 每次页面加载后重新注册 JS handler（BrowserProcess的V8上下文重建不可靠）
        if (frame.IsMain)
        {
            foreach (var kv in _jsHandlerMethods)
            {
                var msg = CefProcessMessage.Create("NativeObjectRegistrationRequest");
                using (var a = msg.Arguments) { a.SetString(0, kv.Key); a.SetString(1, kv.Value); }
                frame.SendProcessMessage(CefProcessId.Renderer, msg);
            }
            // 注入焦点监视 JS（驱动 IME 激活/关闭）
            InjectFocusWatcherIfNeeded();
            // 嵌入模式下注入事件转发 JS
            InjectEventForwardingScriptIfNeeded();
        }
        CallDeferred("_notify_load_end");
    }
    private void _notify_load_end() { LoadEnd?.Invoke(this, new LoadEndEventArgs(null, 0)); EmitSignal(new StringName("load_end")); }

    internal void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
    { if (!_disposed) CallDeferred("_notify_load_error", errorText, failedUrl); }
    private void _notify_load_error(string errorText, string failedUrl)
    { LoadError?.Invoke(this, new LoadErrorEventArgs(null, CefErrorCode.None, errorText, failedUrl)); EmitSignal(new StringName("load_error"), errorText, failedUrl); }

    // ── 页面内查找回调 ──

    internal void OnFindResult(CefBrowser browser, int identifier, int count,
        CefRectangle selectionRect, int activeMatchOrdinal, bool finalUpdate)
    {
        if (_disposed) return;
        CallDeferred("_notify_find_result", identifier, count, activeMatchOrdinal, finalUpdate);
    }

    private void _notify_find_result(int identifier, int count, int activeMatchOrdinal, bool finalUpdate)
        => RaiseFindResult(identifier, count, activeMatchOrdinal, finalUpdate);
}