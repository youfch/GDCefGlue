using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    public void ExecuteJavaScript(string code, string url = null, int line = 1) => _browser?.GetMainFrame()?.ExecuteJavaScript(code, url ?? "about:blank", line);

    /// <summary>
    /// 非泛型版 EvaluateJavaScript，AOT 安全。返回原始 JSON 字符串。
    /// </summary>
    public Task<string> EvaluateJavaScriptRaw(string code, string url = null, int line = 1, TimeSpan? timeout = null)
    {
        var frame = _browser?.GetMainFrame(); if (frame == null) return Task.FromResult<string>(null);
        var id = Interlocked.Increment(ref _lastEvalTaskId); var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingEvals.TryAdd(id, tcs);
        var msg = CefProcessMessage.Create("JsEvaluationRequest");
        using (var a = msg.Arguments) { a.SetInt(0, id); a.SetString(1, $"return {code};"); a.SetString(2, url ?? "about:blank"); a.SetInt(3, line); }
        frame.SendProcessMessage(CefProcessId.Renderer, msg);
        var p = tcs.Task;
        if (timeout.HasValue) return Task.WhenAny(p, Task.Delay(timeout.Value)).ContinueWith(t => t.Result != p ? throw new TimeoutException($"JS eval timed out after {timeout.Value.TotalMilliseconds}ms") : p.Result);
        return p;
    }

    // 保留泛型版用于 GDScript，AOT 下仅用于 string 类型
    public Task<T> EvaluateJavaScript<T>(string code, string url = null, int line = 1, TimeSpan? timeout = null)
    {
        var raw = EvaluateJavaScriptRaw(code, url, line, timeout);
        if (typeof(T) == typeof(string))
            return raw.ContinueWith(t => (T)(object)ParseJsonString(t.Result));
        return raw.ContinueWith(t => JsonSerializer.Deserialize<T>(t.Result));
    }

    /// <summary>
    /// AOT 安全的 JSON 字符串解析：从 "value" 中提取 value
    /// </summary>
    private static string ParseJsonString(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length < 2) return json;
        if (json[0] == '"' && json[^1] == '"') return json[1..^1];
        return json; // 非字符串 JSON（数字等），原样返回
    }

    public void RegisterJsHandler(string name, Callable callable, string methodsJson = "[\"hello\",\"echo\",\"add\",\"getVersion\",\"eval\"]")
    {
        if (_browser?.GetMainFrame() == null) return;
        _jsHandlers[name] = callable;
        _jsHandlerMethods[name] = methodsJson;
        var msg = CefProcessMessage.Create("NativeObjectRegistrationRequest");
        using (var a = msg.Arguments) { a.SetString(0, name); a.SetString(1, methodsJson); }
        _browser.GetMainFrame().SendProcessMessage(CefProcessId.Renderer, msg);
    }

    public void UnregisterJsHandler(string name) { _jsHandlers.Remove(name); _jsHandlerMethods.Remove(name); if (_browser?.GetMainFrame() == null) return; var m = CefProcessMessage.Create("NativeObjectUnregistrationRequest"); using (var a = m.Arguments) a.SetString(0, name); _browser.GetMainFrame().SendProcessMessage(CefProcessId.Renderer, m); }

    internal void HandleProcessMessage(CefProcessMessage message)
    {
        var n = message.Name;
        switch (n)
        {
            case "JsEvaluationResult": HandleJsEvaluationResult(message); break;
            case "NativeObjectCallRequest": HandleNativeObjectCallRequest(message); break;
            case "JsUncaughtException": break;
        }
    }

    private void HandleJsEvaluationResult(CefProcessMessage m)
    { int id; bool ok; string r, e; using (var a = m.Arguments) { id = a.GetInt(0); ok = a.GetBool(1); r = a.GetString(2); e = a.GetString(3); } if (_pendingEvals.TryRemove(id, out var t)) { if (ok) t.TrySetResult(r); else t.TrySetException(new Exception(e ?? "Unknown JS error")); } }

    // ── 合并版 HandleNativeObjectCallRequest ──
    // 支持: dotnetBridge.eval, GDScript Callable, RegisteredObject (C# bridge)
    private void HandleNativeObjectCallRequest(CefProcessMessage m)
    {
        int cid; string on, mn, aj;
        using (var a = m.Arguments) { cid = a.GetInt(0); on = a.GetString(1); mn = a.GetString(2); aj = a.GetString(3); }

        // dotnetBridge.eval → 走 async EvaluateJavaScript，结果通过 Promise 返回
        if (on == "dotnetBridge" && mn == "eval")
        {
            HandleEvalAsync(cid, aj);
            return;
        }

        object r = null; Exception ex = null;
        try
        {
            // 剥离 CefGlue marker（S 前缀）后再传给 GDScript
            var cleanArgs = StripCefGlueMarkersFromJson(aj);
            if (_jsHandlers.TryGetValue(on, out var cb))
            {
                r = cb.Call(mn, cleanArgs);
                NativeCall?.Invoke(on, mn, cleanArgs, rr => SendNativeObjectCallResult(cid, rr, null));
                if (r != null) { SendNativeObjectCallResult(cid, r, null); return; }
            }
            else if (_registeredObjects.TryGetValue(on, out var reg))
            {
                // RegisteredObject (Reflection) 方式
                if (reg.Methods.TryGetValue(mn, out var method))
                {
                    var parameters = method.GetParameters();
                    var invokeArgs = DeserializeCallArgs(aj, parameters);
                    r = method.Invoke(reg.Target, invokeArgs);
                    if (r is Task task)
                    {
                        task.ContinueWith(t =>
                        {
                            object taskResult = null; Exception taskEx = null;
                            try { var resultProp = t.GetType().GetProperty("Result"); if (resultProp != null) taskResult = resultProp.GetValue(t); }
                            catch (Exception e) { taskEx = e.InnerException ?? e; }
                            if (taskEx != null) SendNativeObjectCallResult(cid, null, taskEx.Message);
                            else SendNativeObjectCallResult(cid, taskResult, null);
                        });
                        return;
                    }
                }
                else { ex = new Exception($"Method '{mn}' not found on '{on}'"); }
            }
            else
            {
                NativeCall?.Invoke(on, mn, cleanArgs, null);
                ex = new Exception($"Handler '{on}' not registered");
            }
        }
        catch (Exception e) { ex = e.InnerException ?? e; }
        SendNativeObjectCallResult(cid, r, ex?.Message);
    }

    private void HandleEvalAsync(int callId, string argsJson)
    {
        string code = null;
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                code = StripCefGlueMarker(root[0].GetString());
        }
        catch { }

        if (string.IsNullOrEmpty(code))
        {
            SendNativeObjectCallResult(callId, null, "No code provided");
            return;
        }

        try
        {
            var task = EvaluateJavaScriptRaw(code, timeout: TimeSpan.FromSeconds(5));
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    SendNativeObjectCallResult(callId, null, t.Exception?.InnerException?.Message ?? "Eval failed");
                else if (t.IsCanceled)
                    SendNativeObjectCallResult(callId, null, "Eval canceled");
                else
                    SendNativeObjectCallResult(callId, StripResultMarker(t.Result), null);
            });
        }
        catch (Exception ex)
        {
            SendNativeObjectCallResult(callId, null, ex.Message);
        }
    }

    private static string StripCefGlueMarker(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 1) return value;
        char marker = value[0];
        if (marker == 'S' || marker == 'D' || marker == 'B')
            return value.Substring(1);
        return value;
    }

    /// <summary>
    /// 从 JSON 字符串中剥离 CefGlue 类型 marker（S/D/B 前缀）。
    /// 处理 JSON array 中每个字符串元素，以及顶层 JSON string。
    /// </summary>
    private static string StripCefGlueMarkersFromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                var sb = new System.Text.StringBuilder("[");
                bool first = true;
                foreach (var el in root.EnumerateArray())
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (el.ValueKind == JsonValueKind.String)
                        sb.Append('"').Append(StripCefGlueMarker(el.GetString())).Append('"');
                    else if (el.ValueKind == JsonValueKind.Null)
                        sb.Append("null");
                    else
                        sb.Append(el.GetRawText());
                }
                sb.Append(']');
                return sb.ToString();
            }
            if (root.ValueKind == JsonValueKind.String)
                return "\"" + StripCefGlueMarker(root.GetString()) + "\"";
            return json;
        }
        catch
        {
            return json;
        }
    }

    private void SendNativeObjectCallResult(int cid, object r, string err)
    {
        var f = _browser?.GetMainFrame(); if (f == null) return;
        var m = CefProcessMessage.Create("NativeObjectCallResult");
        using (var a = m.Arguments)
        {
            a.SetInt(0, cid);
            if (err != null) { a.SetBool(1, false); a.SetString(2, null); a.SetString(3, err); }
            else { a.SetBool(1, true); a.SetString(2, ToJsonString(r?.ToString())); a.SetString(3, null); }
        }
        f.SendProcessMessage(CefProcessId.Renderer, m);
    }

    /// <summary>
    /// AOT 安全的字符串→JSON 编码。不做 System.Text.Json 序列化。
    /// </summary>
    private static string ToJsonString(string value)
    {
        if (value == null) return "null";
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>
    /// 剥离 CefGlue 结果中的 marker 前缀。结果为 JSON 字符串时剥掉 'S' 前缀。
    /// </summary>
    private static string StripResultMarker(string jsonResult)
    {
        if (string.IsNullOrEmpty(jsonResult) || jsonResult.Length < 3) return jsonResult;
        // JSON 字符串格式: "Svalue" → 去掉 S
        if (jsonResult[0] == '"' && jsonResult[1] == 'S')
            return "\"" + jsonResult.Substring(2);
        return jsonResult;
    }

    public void SendToJs(string json)
    {
        var e = json.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        _browser?.GetMainFrame()?.ExecuteJavaScript($"window.__hostBridge && window.__hostBridge._onMessage('{e}');", "godot://response", 1);
    }

    public void SendResponse(string cbId, string json)
    {
        var e = json.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        _browser?.GetMainFrame()?.ExecuteJavaScript($"window.__hostBridge && window.__hostBridge._onResponse('{cbId}',\"{e}\");", "godot://response", 1);
    }

    internal void OnBridgeRequest(string url)
    {
        try { var u = new System.Uri(url); var q = System.Web.HttpUtility.ParseQueryString(u.Query); string t = q.Get("type") ?? "", c = q.Get("cb"), p = q.Get("payload") ?? ""; BridgeRequest?.Invoke(t, p, c); }
        catch (Exception ex) { GD.PrintErr($"[CefGlueControl] Failed to parse bridge URL '{url}': {ex.Message}"); }
    }

    // ── RegisterJavascriptObject (CEF IPC) ──

    private sealed class RegisteredObject
    {
        public object Target { get; }
        public Dictionary<string, MethodInfo> Methods { get; }
        public string[] MethodNames { get; }

        public RegisteredObject(object target)
        {
            Target = target;
            Methods = new Dictionary<string, MethodInfo>();
            var names = new List<string>();
            var type = target.GetType();
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Methods[m.Name] = m;
                names.Add(m.Name);
            }
            MethodNames = names.ToArray();
        }
    }

    public void RegisterJavascriptObject(object target, string name)
    {
        if (_browser == null || _browser.GetMainFrame() == null)
        { GD.PrintErr("[CefGlueControl] Cannot register object: browser not initialized"); return; }
        var reg = new RegisteredObject(target);
        _registeredObjects[name] = reg;
        var msg = CefProcessMessage.Create("NativeObjectRegistrationRequest");
        using (var args = msg.Arguments)
        {
            args.SetString(0, name);
            // 手动构建 JSON 数组，避免 NativeAOT 下 JsonSerializer 反射失败
            var methodNamesJson = "[" + string.Join(",", Array.ConvertAll(reg.MethodNames, n => "\"" + n + "\"")) + "]";
            args.SetString(1, methodNamesJson);
        }
        _browser.GetMainFrame().SendProcessMessage(CefProcessId.Renderer, msg);
    }

    public void UnregisterJavascriptObject(string name)
    {
        _registeredObjects.TryRemove(name, out _);
        if (_browser?.GetMainFrame() != null)
        {
            var msg = CefProcessMessage.Create("NativeObjectUnregistrationRequest");
            using (var args = msg.Arguments) args.SetString(0, name);
            _browser.GetMainFrame().SendProcessMessage(CefProcessId.Renderer, msg);
        }
    }

    private static object[] DeserializeCallArgs(string argsJson, ParameterInfo[] parameters)
    {
        if (string.IsNullOrEmpty(argsJson) || parameters.Length == 0)
            return Array.Empty<object>();
        try
        {
            var elements = JsonSerializer.Deserialize<JsonElement[]>(argsJson);
            var result = new object[parameters.Length];
            for (int i = 0; i < parameters.Length && i < elements.Length; i++)
            {
                var el = elements[i];
                var paramType = parameters[i].ParameterType;
                if (paramType == typeof(string)) result[i] = StripCefGlueMarker(el.GetString());
                else if (paramType == typeof(int)) result[i] = el.GetInt32();
                else if (paramType == typeof(long)) result[i] = el.GetInt64();
                else if (paramType == typeof(double)) result[i] = el.GetDouble();
                else if (paramType == typeof(float)) result[i] = el.GetSingle();
                else if (paramType == typeof(bool)) result[i] = el.GetBoolean();
                else result[i] = JsonSerializer.Deserialize(el.GetRawText(), paramType);
            }
            return result;
        }
        catch { return Array.Empty<object>(); }
    }
}