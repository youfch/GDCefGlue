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
        // CefGlue.Common 的 JSON 序列化（StringJsonConverter / BinaryJsonConverter /
        // DateTimeJsonConverter）在序列化值时，会在值前追加一个单字符类型标记：
        //   'S' = string / 'B' = byte[] / 'D' = DateTime
        // 该标记始终在值的最前端（如 "hello!" → S"hello!"），且始终存在。
        // 因此无条件剥离第一个标记字符即可还原原始值。
        // 对 base64 二进制数据同样安全：SGVsbG8= → SSGVsbG8=（标记 + base64），
        // 剥离一个 S 后得到正确的 SGVsbG8=。
        private const int CefGlueMarkerLength = 1;

        private static string StripCefGlueMarker(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= CefGlueMarkerLength) return value;
            char marker = value[0];
            if (marker == 'S' || marker == 'D' || marker == 'B')
                return value.Substring(CefGlueMarkerLength);
            return value;
        }

        public void ExecuteJavaScript(string code, string url = null, int line = 1)
        {
            using var frame = _browser?.GetMainFrame();
            frame?.ExecuteJavaScript(code, url ?? "about:blank", line);
        }

        public Task<T> EvaluateJavaScript<T>(string code, string url = null, int line = 1, TimeSpan? timeout = null)
        {
            using var frame = _browser?.GetMainFrame();
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
            using var frame = _browser?.GetMainFrame();
            if (frame == null)
            { GD.PrintErr("[CefGlueControl] Cannot register object: browser not initialized"); return; }
            var reg = new RegisteredObject(target);
            if (!_registeredObjects.TryAdd(name, reg)) { _registeredObjects[name] = reg; }
            var msg = CefProcessMessage.Create("NativeObjectRegistrationRequest");
            using (var args = msg.Arguments) { args.SetString(0, name); args.SetString(1, JsonSerializer.Serialize(reg.MethodNames)); }
            frame.SendProcessMessage(CefProcessId.Renderer, msg);
        }

        public void UnregisterJavascriptObject(string name)
        {
            _registeredObjects.TryRemove(name, out _);
            using var frame = _browser?.GetMainFrame();
            if (frame != null)
            {
                var msg = CefProcessMessage.Create("NativeObjectUnregistrationRequest");
                using (var args = msg.Arguments) args.SetString(0, name);
                frame.SendProcessMessage(CefProcessId.Renderer, msg);
            }
        }

        // ── IPC message dispatch ──
        internal void HandleProcessMessage(CefProcessMessage message)
        {
            var name = message.Name;
            switch (name)
            {
                case "JsEvaluationResult": HandleJsEvaluationResult(message); break;
                case "NativeObjectCallRequest": HandleNativeObjectCallRequest(message); break;
                case "JsUncaughtException":
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
            byte[][] binaryArgs = null;
            using (var args = message.Arguments)
            {
                callId = args.GetInt(0);
                objectName = args.GetString(1);
                memberName = args.GetString(2);
                argsJson = args.GetString(3);

                // 读取二进制参数（新路径：原生 SetBinary 传输）
                if (args.Count > 4)
                {
                    var binaryCount = args.GetInt(4);
                    binaryArgs = new byte[binaryCount][];
                    for (int i = 0; i < binaryCount; i++)
                    {
                        using (var binary = args.GetBinary(5 + i))
                        {
                            binaryArgs[i] = binary.ToArray();
                        }
                    }
                }
            }

            // 二进制参数直接传到 DeserializeCallArgs，无需经过 base64
            if (!_registeredObjects.TryGetValue(objectName, out var reg))
            { SendNativeObjectCallResult(callId, null, $"Object '{objectName}' not registered"); return; }
            if (!reg.Methods.TryGetValue(memberName, out var method))
            { SendNativeObjectCallResult(callId, null, $"Method '{memberName}' not found on '{objectName}'"); return; }
            object result = null; Exception ex = null;
            try
            {
                var parameters = method.GetParameters();
                var invokeArgs = DeserializeCallArgs(argsJson, parameters, binaryArgs);
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
            using var frame = _browser?.GetMainFrame();
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

        private static object[] DeserializeCallArgs(string argsJson, ParameterInfo[] parameters, byte[][] binaryArgs = null)
        {
            if (parameters.Length == 0 || string.IsNullOrEmpty(argsJson)) return Array.Empty<object>();
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return new[] { DeserializeSingleArg(root, parameters[0].ParameterType, binaryArgs) };
            }
            var elements = new JsonElement[root.GetArrayLength()];
            int i = 0;
            foreach (var el in root.EnumerateArray()) elements[i++] = el;
            var result = new object[Math.Min(elements.Length, parameters.Length)];
            for (int j = 0; j < result.Length; j++)
            {
                result[j] = DeserializeSingleArg(elements[j], parameters[j].ParameterType, binaryArgs);
            }
            return result;
        }

        /// <summary>
        /// 反序列化单个 JSON 参数，处理 CefGlue 的 S/D/B 类型标记。
        /// 对 byte[] 目标类型：优先从 binaryArgs 解析 __BINARY_N__ 占位符（零 base64），
        /// 否则回退到 B marker + base64 解码。
        /// 对 string 目标类型：识别 S/D/B marker → 剥除 → 返回明文。
        /// </summary>
        private static object DeserializeSingleArg(JsonElement element, Type targetType, byte[][] binaryArgs = null)
        {
            // 优先处理 __BINARY_N__ 占位符 → 直接从 binaryArgs 取 byte[]（零 base64）
            if (targetType == typeof(byte[]) && element.ValueKind == JsonValueKind.String)
            {
                var raw = element.GetString();
                if (raw != null && raw.StartsWith("__BINARY_") && raw.EndsWith("__") && binaryArgs != null)
                {
                    // 解析索引: "__BINARY_0__" → 0
                    var indexStr = raw.Substring(9, raw.Length - 11);
                    if (int.TryParse(indexStr, out var index) && index >= 0 && index < binaryArgs.Length)
                    {
                        return binaryArgs[index];
                    }
                }

                // 回退：CefGlue 的 BinaryMarker 内联 base64 字符串 → byte[]
                if (!string.IsNullOrEmpty(raw) && raw.Length > 1)
                {
                    if (raw[0] == 'B' || raw[0] == 'S')
                        raw = raw.Substring(1);
                    try { return Convert.FromBase64String(raw); }
                    catch (FormatException) { }
                }
                return null;
            }

            var val = JsonSerializer.Deserialize(element.GetRawText(), targetType);
            if (val is string strVal) val = StripCefGlueMarker(strVal);
            return val;
        }

        /// <summary>
        /// 向 JS 推送 JSON 消息。JS 端监听 window.__hostBridge._onMessage(msg) 接收。
        /// 从 Godot 主线程调用即可（内部使用 ExecuteJavaScript 在 CEF 主帧执行）。
        /// </summary>
        public void SendToJs(string jsonMessage)
        {
            var escaped = jsonMessage.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
            using var frame = _browser.GetMainFrame();
            frame?.ExecuteJavaScript($"window.__hostBridge && window.__hostBridge._onMessage('{escaped}');", "godot://response", 1);
        }

        // ── 二进制通道 ──
        // CefGlue.Common 的 RegisterJavascriptObject 支持 Uint8Array ↔ byte[] 传输：
        //   JS 传 Uint8Array → CefGlue interceptor 序列化为 "B" + btoa(data)（B marker + base64）
        //   C# DeserializeCallArgs 识别 B marker → Convert.FromBase64String → byte[]
        // 因此插件无需再做额外 base64 编解码（仅框架固有的一层）。
        //
        // 如需零 base64 膨胀的原生 ArrayBuffer 传输，需实现自定义 CefRenderProcessHandler
        // 并注入自定义 CefProcessMessage 路由（见方案 A）。

        /// <summary>
        /// 向 JS 推送二进制数据。内部编码为 base64 后通过 ExecuteJavaScript 注入。
        /// JS 端监听 window.__hostBridge._onBinaryMessage(base64) 接收。
        /// </summary>
        public void SendBinaryToJs(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            var b64 = Convert.ToBase64String(data);
            // 使用简单的字符转义，仅处理 \ 和 '（JS 字符串字面量安全）
            var escaped = b64.Replace("\\", "\\\\").Replace("'", "\\'");
            using var frame = _browser.GetMainFrame();
            frame?.ExecuteJavaScript(
                $"window.__hostBridge && window.__hostBridge._onBinaryMessage('{escaped}');",
                "godot://binary", 0);
        }

        /// <summary>
        /// JS → C# 二进制数据到达事件。参数为解码后的原始字节。
        /// 由 JS 调用 window.dotnetBridge.sendBinary(Uint8Array) 触发。
        /// </summary>
        public event Action<byte[]> BridgeBinary;

        /// <summary>
        /// 供注册的桥接对象调用，将 JS 侧传来的字节数据触发 <see cref="BridgeBinary"/>。
        /// 注意：可能从 CEF IPC 线程进入，需用 CallDeferred 调度到 Godot 主线程触发事件，
        /// 确保订阅者可在事件处理器中安全访问 Godot 节点。
        /// </summary>
        internal void RaiseBridgeBinary(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            var captured = data; // capture for closure
            Callable.From(() =>
            {
                if (_disposed) return;
                BridgeBinary?.Invoke(captured);
            }).CallDeferred();
        }
    }
}