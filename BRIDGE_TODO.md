# GDCefGlue JS↔C# Bridge & Render Mode TODO

## Render Mode (已实现)

```
RenderMode 枚举:
  OSR = 0           ← [默认] 离屏渲染，支持真 alpha 透明
  EmbeddedWindow = 1 ← 嵌入 HWND 子窗口，性能更优，不支持透明
```

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Mode` | RenderMode | OSR | Inspector 中切换，OSR 时隐藏"Embedded Mode"分组 |
| `ForwardInputEvents` | bool | false | 嵌入模式下 JS→Godot 事件穿透，已迁移到 RegisterJavascriptObject IPC |
| `Transparent` | bool | false | 仅 OSR 模式生效 |

### Inspector 分组

```
▸ Browser Settings
  ├── InitialUrl
  ├── Mode
  ├── FrameRate
  └── Transparent

▸ Feature Toggles
  ├── GpuAcceleration
  ├── OpenPopupInCurrentBrowser
  └── SyncCursor

▸ Embedded Mode        ← 仅 Mode=EmbeddedWindow 时显示
  └── ForwardInputEvents
```

## 当前 Bridge 实现

### 通道 1: RegisterJavascriptObject (CEF IPC) ✅ 主通道

C# 端注册对象 → BrowserProcess 创建 V8 绑定 → JS 直接调方法 → IPC 往返

| 方向 | 机制 | 状态 |
|------|------|------|
| JS → C# | `window.dotnetBridge.method(args).then(cb)` | ✅ 已验证 |
| C# → JS | `EvaluateJavaScript<T>` (SendProcessMessage IPC) | ✅ 已验证 |
| C# → JS 推送 | `ExecuteJavaScript` 回灌字符串 | ✅ 已验证 |

**注意：** CefGlue 的 `objectsStringifier` 会给字符串加类型 marker（`S`/`D`/`B`），C# 端通过 `StripCefGlueMarker` 去掉。

### 通道 2: godot://bridge (iframe → OnBeforeBrowse) 🔸 已弃用

```
JS → iframe.src = "godot://bridge?type=X&cb=ID&payload=JSON"
  → OnBeforeBrowse 拦截 → BridgeRequest 事件
```

保留作 fallback，不推荐使用。限制：URL query 长度 ~2-64KB。

### 已验证

- [x] ping/status/navigate 测试用例通过（DemoScript.cs / test.html）
- [x] 方式 B: `RegisterJavascriptObject` — hello/echo/add/getVersion 全部 IPC 往返
- [x] 方式 C: `DotnetBridge.eval()` — JS→C# 触发 EvaluateJavaScript 求值
- [x] `EvaluateJavaScript<T>` 异步求值 + 超时 + 数值/字符串结果
- [x] C#→JS 推送消息（`SendToJs` → `ExecuteJavaScript`）
- [x] CefGlue marker 前缀（"S"）自动剥离
- [x] 大 payload（~10KB）URL 编码传输
- [x] DevTool 按钮

### 已知限制

- `OnBeforeBrowse` 无法读 POST body，payload 走 URL query（长度上限 ~2-64KB）
- `BridgeRequest.Invoke` 在 CEF UI 线程同步执行
- CefGlue BrowserProcess 的 V8 绑定重建有初始化噪音（`JsUncaughtException`），不影响功能

## CEF 原生 6 种 JS↔Native 机制

| # | 机制 | 方向 | Payload 限制 | 线程 |
|---|------|------|-------------|------|
| 1 | `CefMessageRouterBrowserSide`（`window.cefQuery`） | JS→C++ 双向 | 默认字符串；145+ 支持 shared memory | UI 线程 |
| 2 | `CefRegisterExtension` + `CefV8Handler` | JS→C++ 同步 | 无限制 | TID_RENDERER |
| 3 | `OnContextCreated` + `CefV8Value::CreateFunction` | JS→C++ 同步 | 无限制 | TID_RENDERER |
| 4 | `CefFrame::ExecuteJavaScript` | C++→JS 单向 | 无返回值 | 任意线程 |
| 5 | `SendProcessMessage` + `OnProcessMessageReceived` | 跨进程双向 | ~128KB；shared memory 无限 | UI 线程 |
| 6 | `CefResourceRequestHandler` / `CefSchemeHandlerFactory` | 双向（HTTP） | 无限（流式） | TID_IO |

### 当前限制

- `CefRuntime.RegisterExtension` 注册 V8 扩展失败（NuGet 版不支持从 browser 进程注入）
- 无法使用 `CefMessageRouter`（需修改 BrowserProcess，不满足 addon 分发）
- 无法使用 `SchemeHandler + fetch`（`file://` → `ipc://` 跨域 CORS 死胡同）

## TODO

### Bridge 优化

- [ ] 键盘事件（keydown/keyup）C# 端 `HandleForwardedEvent` 补充 InputEventKey 处理

### 嵌入模式

- [x] `ForwardInputEvents` 完全实现：JS DOM 事件 → IPC → C# InputEvent → `viewport.PushInput`
  - 鼠标事件（move/down/up/wheel）— 已完成
  - 键盘事件（keydown/keyup）— JS 已捕获，C# 端暂未处理，待补全
  - 坐标换算（CEF 物理像素 → Godot 虚拟像素）— 已完成
  - 通讯方式已从 iframe → OnBeforeBrowse 迁移到 RegisterJavascriptObject IPC

### 已修复/已完成

- [x] iframe 复用：已由 RegisterJavascriptObject IPC 替代，不再需要 iframe
- [x] ForwardInputEvents 通讯迁移到 RegisterJavascriptObject IPC
- [x] CefGlue 序列化 marker 前缀（"S"）自动剥离（`StripCefGlueMarker`）
- [x] BrowserProcess V8 绑定重建失败后页面重新注册（`OnLoadEnd` 中重注册）
- [x] test.html 方式 B 从 callback 风格改为 Promise 风格（匹配 CefGlue 的 Promise 返回）
- [x] test.html 方式 C 按钮改为实际触发 `EvaluateJavaScript`
- [x] `SendToJs` / `SendResponse` 改用 `JsonSerializer.Serialize` 安全序列化，移除手写 `Replace` 转义
- [x] `DeserializeEvalResult<string>` 支持非字符串 JSON 值（数字/bool）
- [x] DotnetBridge 持有 CefGlueControl 引用，支持异步 eval 方法
- [x] JsUncaughtException 诊断日志（BrowserProcess 初始化噪音，降级为 info）
- [x] DelayedEvalTests 防重复执行

## 参考实现

- **godot_wry**: https://github.com/doceazedo/godot_wry
  - `lib.rs`: JS 事件监听 + IPC handler + `push_input`
  - `godot_window.rs`: `DisplayServer.WindowGetNativeHandle` → HWND
  - 关键技巧：移除 `WS_CLIPCHILDREN`、坐标换算、`CURRENT_BUTTON_MASK` 状态机
- wrymium（Tauri+CEF 混合）: https://github.com/gxcsoccer/wrymium
- Chromely: https://github.com/chromelyapps/CefSharp