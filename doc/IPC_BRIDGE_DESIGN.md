# GDCefGlue JS↔C# Bridge 方案设计

## 概述

本文档描述 GDCefGlue 中 JS 与 C# 双向通信的桥接方案。
提供 `window.gdCefGlue.ping()` / `.send(type, payload)` 的 Promise API，
底层使用 iframe + `godot://bridge` 自定义协议传输。

## 架构

```
┌─────────────────────────────────────────────────────┐
│                   JS 侧 (网页)                       │
│                                                     │
│  window.gdCefGlue.ping().then(r => ...)             │
│  window.gdCefGlue.send("echo", payload).then(...)   │
│          │                                           │
│          │ iframe.src = "godot://bridge?type=X&..."   │
│          ▼                                           │
│  CEF: GodotRequestHandler.OnBeforeBrowse             │
│          │                                           │
│          │ CefGlueControl.OnBridgeRequest(url)       │
│          ▼                                           │
├─────────────────────────────────────────────────────┤
│                   C# 侧 (Godot)                      │
│                                                     │
│  BridgeRequest?.Invoke(type, payload, cbId)          │
│          │                                           │
│          │ 用户处理（CallDeferred 到主线程）           │
│          ▼                                           │
│  SendResponse(cbId, json)                            │
│          │                                           │
│          │ ExecuteJavaScript → _onResponse(cbId,msg) │
│          ▼                                           │
│  JS: Promise.resolve(msg)                            │
└─────────────────────────────────────────────────────┘
```

## 关键组件

### BridgeRegistry（反射验证层）

位于 `addons/GCefGlue/BridgeRegistry.cs`。

通过反射访问 `Xilium.CefGlue.Common.ObjectBinding.NativeObjectRegistry`，
验证反射通路是否正常。Register() 返回 True 确认 CefGlue 内部 API 可用。

由于 CEF 149 的 V8 上下文绑定（`frame?.V8Context` 返回 null）不可用，
`NativeObjectRegistry` 的 V8 对象创建不生效，实际通信走 InjectJsFallback。

```csharp
BridgeRegistry.TryInit(browser);   // 反射创建 NativeObjectRegistry
BridgeRegistry.Register(handler, "gdCefGlue");  // 验证反射通路（V8 绑定不生效）
BridgeRegistry.InjectJsFallback(frame, "gdCefGlue"); // 注入 JS 桥接（生效）
```

### BridgeHandler（C# 处理对象）

位于 `addons/GCefGlue/BridgeHandler.cs`。

- `Ping()` —— 健康检查
- `Send(type, payload)` —— 异步消息，返回 `Task<string>`
- `TryResolve(callId, result)` —— 回复 pending 调用
- `DispatchRequest` 回调 —— 由 CefGlueControl 通过 CallDeferred 调度到 Godot 主线程

### CefGlueControl 集成

在 `OnBrowserCreated` 中：

```csharp
_bridgeHandler = new BridgeHandler();
_bridgeHandler.DispatchRequest = (type, payload, callId) =>
    CallDeferred(nameof(NotifyBridgeRequest), type, payload, callId);

_useNativeIpc = BridgeRegistry.TryInit(browser);
if (_useNativeIpc)
    BridgeRegistry.Register(_bridgeHandler, "gdCefGlue");
```

在 `OnLoadEnd` 中：

```csharp
if (frame.IsMain)
    BridgeRegistry.InjectJsFallback(frame, "gdCefGlue");
```

## JS API

### window.gdCefGlue.ping()

健康检查，返回 `"pong"`。

```javascript
window.gdCefGlue.ping().then(r => console.log(r))
// → "pong"
```

### window.gdCefGlue.send(type, payload)

发送消息到 C# 侧并等待异步回复。

```javascript
window.gdCefGlue.send("echo", JSON.stringify({msg:"hello"})).then(r => {
    console.log(r);
    // → {"status":"echoed","received":{"msg":"hello"}}
});
```

## C# API

### BridgeRequest 事件

```csharp
browser.BridgeRequest += (type, payload, cbId) =>
{
    switch (type)
    {
        case "ping":
            browser.SendResponse(cbId, "{\"status\":\"pong\"}");
            break;
        case "myType":
            // 处理业务逻辑
            browser.SendResponse(cbId, "{\"result\":\"ok\"}");
            break;
    }
};
```

### SendResponse(cbId, json)

回复 JS 请求。优先走原生 IPC 路径（BridgeHandler.TryResolve），
不可用时回退到 ExecuteJavaScript。

```csharp
browser.SendResponse("callbackId123", "{\"status\":\"ok\"}");
```

### SendToJs(json)

主动推送消息到 JS（无回调）。

```csharp
browser.SendToJs("{\"type\":\"update\",\"payload\":{\"count\":42}}");
```

## 传输层说明

当前 JS↔C# 通信使用 iframe + `godot://bridge` 协议：

1. JS 创建隐藏 iframe，设置 `src` 为 `godot://bridge?type=X&cb=Y&payload=Z`
2. CEF 拦截 `OnBeforeBrowse` 事件，提取参数
3. 解析后触发 `BridgeRequest` 事件
4. 用户代码处理并调用 `SendResponse`
5. `SendResponse` 通过 `ExecuteJavaScript` 调用 `window._godotBridge._onResponse`
6. 注入的 JS 代理解析响应，完成 Promise

### 关于原生 IPC（NativeObjectRegistry）

CefGlue.Common 内部提供了 `NativeObjectRegistry` 用于注册 C# 对象到 JS V8 上下文。
理论上这可以零 iframe、零 ExecuteJavaScript 实现 JS↔C# 通信。

但在 CEF 149 上，渲染器子进程的 `frame?.V8Context` 返回 null，
导致 `NativeObjectRegistrationRequest` 消息处理后无法创建 V8 对象。
CEF 日志显示 `browser_info_manager` 帧路由超时（`browser_info_manager.cc:858`）。

该反射通路保留在 `BridgeRegistry.Register()` 中用于验证，
实际通信由 `InjectJsFallback` 注入的 JS 代理完成，API 完全一致。

## 依赖

- `Xilium.CefGlue` （本地源码，路径 `../CefGlue/`）
- `chromiumembeddedframework.runtime.win-x64` （NuGet，仅 libcef.dll 等原生文件）