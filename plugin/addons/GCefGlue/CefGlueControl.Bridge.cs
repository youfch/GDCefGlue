using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  JS ↔ C# 桥接：IPC、RegisterJavascriptObject、EvaluateJavaScript
    // ══════════════════════════════════════════════════════════════
    public partial class CefGlueControl
    {
        // ── RegisteredObject (inner class) ──
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
                foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (m.IsSpecialName) continue;
                    var jsName = char.ToLowerInvariant(m.Name[0]) + m.Name.Substring(1);
                    Methods[jsName] = m;
                    names.Add(jsName);
                }
                MethodNames = names.ToArray();
            }
        }

        // ── CefGlue 序列化 marker ──
        private const string CefGlueStringMarker = "S";
        private const string CefGlueDateTimeMarker = "D";
        private const string CefGlueBinaryMarker = "B";
        private const int CefGlueMarkerLength = 1;

        private static string StripCefGlueMarker(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= CefGlueMarkerLength) return value;
            var marker = value.Substring(0, CefGlueMarkerLength);
            if (marker == CefGlueStringMarker || marker == CefGlueDateTimeMarker || marker == CefGlueBinaryMarker)
                return value.Substring(CefGlueMarkerLength);
            return value;
        }

        public void ExecuteJavaScript(string code, string url = null, int line = 1)
        {
            _browser?.GetMainFrame()?.ExecuteJavaScript(code, url ?? "about:blank", line);
        }

        public Task<T> EvaluateJavaScript<T>(string code, string url = null, int line = 1, TimeSpan? timeout = null)
        {
            var frame = _browser?.GetMainFrame();
            if (frame == null) return Task.FromResult<T>(default);
            var taskId = Interlocked.Increment(ref _lastEvalTaskId);
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingEvals.TryAdd(taskId, tcs);
            var msg = CefProcessMessage.Create("JsEvaluationRequest");
            using (var args = msg.Arguments)
            {
                args.SetInt(0, taskId);
                args.SetString(1, $"return {code};");
                args.SetString(2, url ?? "about:blank");
                args.SetInt(3, line);
            }
            frame.SendProcessMessage(CefProcessId.Renderer, msg);
            var pending = tcs.Task;
            if (timeout.HasValue)
                return Task.WhenAny(pending, Task.Delay(timeout.Value))
                    .ContinueWith(t => t.Result != pending ? throw new TimeoutException($"JS eval timed out after {timeout.Value.TotalMilliseconds}ms") : DeserializeEvalResult<T>(pending.Result));
            return pending.ContinueWith(t => DeserializeEvalResult<T>(t.Result));
        }

        public void RegisterJavascriptObject(object target, string name)
        {
            if (_browser == null || _browser.GetMainFrame() == null)
            { GD.PrintErr("[CefGlueControl] Cannot register object: browser not initialized"); return; }
            var reg = new RegisteredObject(target);
            if (!_registeredObjects.TryAdd(name, reg)) { _registeredObjects[name] = reg; }
            var msg = CefProcessMessage.Create("NativeObjectRegistrationRequest");
            using (var args = msg.Arguments) { args.SetString(0, name); args.SetString(1, JsonSerializer.Serialize(reg.MethodNames)); }
            _browser.GetMainFrame().SendProcessMessage(CefProcessId.Renderer, msg);
            GD.Print($"[CefGlueControl] Registered object '{name}' with {reg.MethodNames.Length} methods");
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

        // ── IPC message dispatch ──
        internal void HandleProcessMessage(CefProcessMessage message)
        {
            var name = message.Name;
            GD.Print($"[CefGlueControl] IPC received: {name}");
            switch (name)
            {
                case "JsEvaluationResult": HandleJsEvaluationResult(message); break;
                case "NativeObjectCallRequest": HandleNativeObjectCallRequest(message); break;
                case "JsUncaughtException":
                    using (var args = message.Arguments)
                    {
                        var msg = args.GetString(0);
                        if (!string.IsNullOrEmpty(msg))
                            GD.Print($"[CefGlueControl] JS uncaught (init noise): {msg}");
                    }
                    break;
            }
        }

        private void HandleJsEvaluationResult(CefProcessMessage message)
        {
            int taskId; bool success; string resultJson, exception;
            using (var args = message.Arguments)
            { taskId = args.GetInt(0); success = args.GetBool(1); resultJson = args.GetString(2); exception = args.GetString(3); }
            if (_pendingEvals.TryRemove(taskId, out var tcs))
            {
                if (success) tcs.TrySetResult(resultJson);
                else tcs.TrySetException(new Exception(exception ?? "Unknown JS error"));
            }
        }

        private void HandleNativeObjectCallRequest(CefProcessMessage message)
        {
            int callId; string objectName, memberName, argsJson;
            using (var args = message.Arguments)
            { callId = args.GetInt(0); objectName = args.GetString(1); memberName = args.GetString(2); argsJson = args.GetString(3); }
            if (!_registeredObjects.TryGetValue(objectName, out var reg))
            { SendNativeObjectCallResult(callId, null, $"Object '{objectName}' not registered"); return; }
            if (!reg.Methods.TryGetValue(memberName, out var method))
            { SendNativeObjectCallResult(callId, null, $"Method '{memberName}' not found on '{objectName}'"); return; }
            object result = null; Exception ex = null;
            try
            {
                var parameters = method.GetParameters();
                var invokeArgs = DeserializeCallArgs(argsJson, parameters);
                result = method.Invoke(reg.Target, invokeArgs);
                if (result is Task task)
                {
                    task.ContinueWith(t =>
                    {
                        object taskResult = null; Exception taskEx = null;
                        try { var resultProp = t.GetType().GetProperty("Result"); if (resultProp != null) taskResult = resultProp.GetValue(t); }
                        catch (Exception e) { taskEx = e.InnerException ?? e; }
                        if (taskEx != null) SendNativeObjectCallResult(callId, null, taskEx.Message);
                        else SendNativeObjectCallResult(callId, taskResult, null);
                    });
                    return;
                }
            }
            catch (Exception e) { ex = e.InnerException ?? e; }
            SendNativeObjectCallResult(callId, result, ex?.Message);
        }

        private void SendNativeObjectCallResult(int callId, object result, string errorMessage)
        {
            var frame = _browser?.GetMainFrame();
            if (frame == null) return;
            var msg = CefProcessMessage.Create("NativeObjectCallResult");
            using (var args = msg.Arguments)
            {
                args.SetInt(0, callId);
                if (errorMessage != null) { args.SetBool(1, false); args.SetString(2, null); args.SetString(3, errorMessage); }
                else
                {
                    args.SetBool(1, true);
                    try { args.SetString(2, JsonSerializer.Serialize(result)); }
                    catch { args.SetString(2, result?.ToString()); }
                    args.SetString(3, null);
                }
            }
            frame.SendProcessMessage(CefProcessId.Renderer, msg);
        }

        private static T DeserializeEvalResult<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            try
            {
                var result = JsonSerializer.Deserialize<T>(json);
                if (result is string strResult) return (T)(object)StripCefGlueMarker(strResult);
                return result;
            }
            catch
            {
                if (typeof(T) == typeof(string))
                {
                    try { using var doc = JsonDocument.Parse(json); return (T)(object)doc.RootElement.GetRawText(); }
                    catch { }
                }
                return default;
            }
        }

        private static object[] DeserializeCallArgs(string argsJson, ParameterInfo[] parameters)
        {
            if (parameters.Length == 0 || string.IsNullOrEmpty(argsJson)) return Array.Empty<object>();
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                var val = JsonSerializer.Deserialize(argsJson, parameters[0].ParameterType);
                if (val is string strVal) val = StripCefGlueMarker(strVal);
                return new[] { val };
            }
            var elements = new JsonElement[root.GetArrayLength()];
            int i = 0;
            foreach (var el in root.EnumerateArray()) elements[i++] = el;
            var result = new object[Math.Min(elements.Length, parameters.Length)];
            for (int j = 0; j < result.Length; j++)
            {
                var val = JsonSerializer.Deserialize(elements[j].GetRawText(), parameters[j].ParameterType);
                if (val is string strVal) val = StripCefGlueMarker(strVal);
                result[j] = val;
            }
            return result;
        }

        public void SendToJs(string jsonMessage)
        {
            var escaped = jsonMessage.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
            _browser.GetMainFrame()?.ExecuteJavaScript($"window._godotBridge && window._godotBridge._onMessage('{escaped}');", "godot://response", 1);
        }

        public void SendResponse(string cbId, string jsonResponse)
        {
            var escaped = jsonResponse.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
            _browser.GetMainFrame()?.ExecuteJavaScript($"window._godotBridge && window._godotBridge._onResponse('{cbId}',\"{escaped}\");", "godot://response", 1);
        }

        internal void OnBridgeRequest(string url)
        {
            try
            {
                var uri = new System.Uri(url);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                string type = query.Get("type") ?? "";
                string cbId = query.Get("cb");
                string payloadStr = query.Get("payload") ?? "";
                if (_renderMode == RenderMode.EmbeddedWindow && ForwardInputEvents && type == "event_forward")
                { HandleForwardedEvent(payloadStr); return; }
                GD.Print($"[CefGlueControl] Bridge request: type={type}, cb={cbId ?? "none"}, payloadLen={payloadStr.Length}");
                BridgeRequest?.Invoke(type, payloadStr, cbId);
            }
            catch (Exception ex) { GD.PrintErr($"[CefGlueControl] Failed to parse bridge URL '{url}': {ex.Message}"); }
        }
    }
}