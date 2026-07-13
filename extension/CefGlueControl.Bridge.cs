using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    public void ExecuteJavaScript(string code, string url = null, int line = 1) => _browser?.GetMainFrame()?.ExecuteJavaScript(code, url ?? "about:blank", line);

    public Task<T> EvaluateJavaScript<T>(string code, string url = null, int line = 1, TimeSpan? timeout = null)
    {
        var frame = _browser?.GetMainFrame(); if (frame == null) return Task.FromResult<T>(default);
        var id = Interlocked.Increment(ref _lastEvalTaskId); var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingEvals.TryAdd(id, tcs);
        var msg = CefProcessMessage.Create("JsEvaluationRequest");
        using (var a = msg.Arguments) { a.SetInt(0, id); a.SetString(1, $"return {code};"); a.SetString(2, url ?? "about:blank"); a.SetInt(3, line); }
        frame.SendProcessMessage(CefProcessId.Renderer, msg);
        var p = tcs.Task;
        if (timeout.HasValue) return Task.WhenAny(p, Task.Delay(timeout.Value)).ContinueWith(t => t.Result != p ? throw new TimeoutException($"JS eval timed out after {timeout.Value.TotalMilliseconds}ms") : JsonSerializer.Deserialize<T>(p.Result));
        return p.ContinueWith(t => JsonSerializer.Deserialize<T>(t.Result));
    }

    public void RegisterJsHandler(string name, Callable callable)
    {
        if (_browser?.GetMainFrame() == null) return;
        _jsHandlers[name] = callable;
        var methodsJson = "[\"hello\",\"echo\",\"add\",\"getVersion\",\"eval\"]";
        _jsHandlerMethods[name] = methodsJson;
        var msg = CefProcessMessage.Create("NativeObjectRegistrationRequest");
        using (var a = msg.Arguments) { a.SetString(0, name); a.SetString(1, methodsJson); }
        _browser.GetMainFrame().SendProcessMessage(CefProcessId.Renderer, msg);
        GD.Print($"[CefGlueControl] Registered JS handler '{name}'");
    }

    public void UnregisterJsHandler(string name) { _jsHandlers.Remove(name); _jsHandlerMethods.Remove(name); if (_browser?.GetMainFrame() == null) return; var m = CefProcessMessage.Create("NativeObjectUnregistrationRequest"); using (var a = m.Arguments) a.SetString(0, name); _browser.GetMainFrame().SendProcessMessage(CefProcessId.Renderer, m); }

    internal void HandleProcessMessage(CefProcessMessage message)
    {
        var n = message.Name; GD.Print($"[CefGlueControl] IPC received: {n}");
        switch (n)
        {
            case "JsEvaluationResult": HandleJsEvaluationResult(message); break;
            case "NativeObjectCallRequest": HandleNativeObjectCallRequest(message); break;
            case "JsUncaughtException": using (var a = message.Arguments) { var m = a.GetString(0); if (!string.IsNullOrEmpty(m)) GD.Print($"[CefGlueControl] JS uncaught: {m}"); } break;
        }
    }

    private void HandleJsEvaluationResult(CefProcessMessage m)
    { int id; bool ok; string r, e; using (var a = m.Arguments) { id = a.GetInt(0); ok = a.GetBool(1); r = a.GetString(2); e = a.GetString(3); } if (_pendingEvals.TryRemove(id, out var t)) { if (ok) t.TrySetResult(r); else t.TrySetException(new Exception(e ?? "Unknown JS error")); } }

    private void HandleNativeObjectCallRequest(CefProcessMessage m)
    { int cid; string on, mn, aj; using (var a = m.Arguments) { cid = a.GetInt(0); on = a.GetString(1); mn = a.GetString(2); aj = a.GetString(3); } object r = null; Exception ex = null; try { if (_jsHandlers.TryGetValue(on, out var cb)) { r = cb.Call(mn, aj); NativeCall?.Invoke(on, mn, aj, rr => SendNativeObjectCallResult(cid, rr, null)); } else { NativeCall?.Invoke(on, mn, aj, null); ex = new Exception($"Handler '{on}' not registered"); } } catch (Exception e) { ex = e.InnerException ?? e; } SendNativeObjectCallResult(cid, r, ex?.Message); }

    private void SendNativeObjectCallResult(int cid, object r, string err)
    {
        var f = _browser?.GetMainFrame(); if (f == null) return;
        var m = CefProcessMessage.Create("NativeObjectCallResult");
        using (var a = m.Arguments) { a.SetInt(0, cid); if (err != null) { a.SetBool(1, false); a.SetString(2, null); a.SetString(3, err); } else { a.SetBool(1, true); try { var json = System.Text.Json.JsonSerializer.Serialize(r?.ToString()); a.SetString(2, json); } catch { a.SetString(2, "\"" + r?.ToString() + "\""); } a.SetString(3, null); } }
        f.SendProcessMessage(CefProcessId.Renderer, m);
    }

    public void SendToJs(string json)
    {
        var e = json.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        _browser?.GetMainFrame()?.ExecuteJavaScript($"window._godotBridge && window._godotBridge._onMessage('{e}');", "godot://response", 1);
    }

    public void SendResponse(string cbId, string json)
    {
        var e = json.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        _browser?.GetMainFrame()?.ExecuteJavaScript($"window._godotBridge && window._godotBridge._onResponse('{cbId}',\"{e}\");", "godot://response", 1);
    }

    internal void OnBridgeRequest(string url)
    {
        try { var u = new System.Uri(url); var q = System.Web.HttpUtility.ParseQueryString(u.Query); string t = q.Get("type") ?? "", c = q.Get("cb"), p = q.Get("payload") ?? ""; BridgeRequest?.Invoke(t, p, c); }
        catch (Exception ex) { GD.PrintErr($"[CefGlueControl] Failed to parse bridge URL '{url}': {ex.Message}"); }
    }
}