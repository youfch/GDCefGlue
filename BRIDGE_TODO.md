# GDCefGlue JS↔C# Bridge & Render Mode TODO

## API 状态速览

| API | 状态 | 说明 |
|-----|------|------|
| RegisterJavascriptObject | ✅ 核心 | JS→C# IPC 主通道 |
| EvaluateJavaScript<T> | ✅ 核心 | C#→JS 求值 + 返回值 |
| SendToJs(json) | ✅ 活跃 | C#→JS 推送 |
| DotnetBridge.eval() | 💡 演示 | IPC 往返演示，JS 直接 eval 即可 |
| SendResponse(cbId, json) | 🔴 淘汰 | [Obsolete] 仅旧版 iframe 使用 |
| BridgeRequest 事件 | 🔴 淘汰 | 仅旧版 iframe 使用 |
| GodotRequestHandler (OnBeforeBrowse) | 🔴 淘汰 | 仅旧版 iframe 使用 |

## Render Mode (已实现)

`
RenderMode 枚举:
  OSR = 0           ← [默认] 离屏渲染，支持真 alpha 透明
  EmbeddedWindow = 1 ← 嵌入 HWND 子窗口，性能更优，不支持透明
`

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| Mode | RenderMode | OSR | Inspector 中切换，OSR 时隐藏"Embedded Mode"分组 |
| ForwardInputEvents | bool | false | 嵌入模式下 JS→Godot 事件穿透 |
| Transparent | bool | false | 仅 OSR 模式生效 |

### Inspector 分组

`
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
`

## 当前 Bridge 实现

### 主通道: RegisterJavascriptObject (CEF IPC) ✅

C# 端注册对象 → BrowserProcess 创建 V8 绑定 → JS 直接调方法 → IPC 往返

| 方向 | 机制 | 状态 |
|------|------|------|
| JS → C# | window.dotnetBridge.method(args).then(cb) | ✅ 已验证 |
| C# → JS | EvaluateJavaScript<T> (SendProcessMessage IPC) | ✅ 已验证 |
| C# → JS 推送 | ExecuteJavaScript 回灌字符串 | ✅ 已验证 |

**注意：** CefGlue 的 objectsStringifier 会给字符串加类型 marker（S/D/B），C# 端通过 StripCefGlueMarker 去掉。

### 已知限制

- CefGlue BrowserProcess 的 V8 绑定重建有初始化噪音（JsUncaughtException），不影响功能
- CefRuntime.RegisterExtension 注册 V8 扩展失败（NuGet 版不支持从 browser 进程注入）
- 无法使用 CefMessageRouter（需修改 BrowserProcess，不满足 addon 分发）
- 无法使用 SchemeHandler + fetch（ile:// → ipc:// 跨域 CORS 死胡同）

## CEF 原生 6 种 JS↔Native 机制

| # | 机制 | 方向 | Payload 限制 | 线程 |
|---|------|------|-------------|------|
| 1 | CefMessageRouterBrowserSide（window.cefQuery） | JS→C++ 双向 | 默认字符串；145+ 支持 shared memory | UI 线程 |
| 2 | CefRegisterExtension + CefV8Handler | JS→C++ 同步 | 无限制 | TID_RENDERER |
| 3 | OnContextCreated + CefV8Value::CreateFunction | JS→C++ 同步 | 无限制 | TID_RENDERER |
| 4 | CefFrame::ExecuteJavaScript | C++→JS 单向 | 无返回值 | 任意线程 |
| 5 | SendProcessMessage + OnProcessMessageReceived | 跨进程双向 | ~128KB；shared memory 无限 | UI 线程 |
| 6 | CefResourceRequestHandler / CefSchemeHandlerFactory | 双向（HTTP） | 无限（流式） | TID_IO |

## TODO

### Bridge 优化

- [ ] 键盘事件（keydown/keyup）C# 端 HandleForwardedEvent 补充 InputEventKey 处理

### 计划功能

| 功能 | 说明 | 优先级 |
|------|------|--------|
| **下载处理** | 实现 CefDownloadHandler，拦截 OnBeforeDownload / OnDownloadUpdated，提供下载进度信号 | ⭐⭐⭐ |
| **页面查找** | CefBrowserHost.Find() / StopFinding()，页面内查找功能 | ⭐⭐ |
| **缩放控制** | CefBrowserHost.SetZoomLevel()，页面缩放 | ⭐⭐ |
| **Cookie 管理** | CefCookieManager，获取/设置/删除 Cookie | ⭐⭐ |
| **右键菜单** | 实现 CefContextMenuHandler，提供基本右键菜单（复制/粘贴/在新标签打开） | ⭐⭐ |
| **全屏处理** | 响应 CEF 全屏事件，自动切换 Godot 窗口全屏状态 | ⭐ |
| **打印支持** | CEF 的打印功能 (CefBrowserHost.Print()) | ⭐ |
| **JS 控制台日志** | CefDisplayHandler.OnConsoleMessage 将 JS console.log 转发到 Godot 输出 | ⭐ |

## 参考实现

- **godot_wry**: https://github.com/doceazedo/godot_wry
  - lib.rs: JS 事件监听 + IPC handler + push_input
  - godot_window.rs: DisplayServer.WindowGetNativeHandle → HWND
  - 关键技巧：移除 WS_CLIPCHILDREN、坐标换算、CURRENT_BUTTON_MASK 状态机
- wrymium（Tauri+CEF 混合）: https://github.com/gxcsoccer/wrymium
- Chromely: https://github.com/chromelyapps/CefSharp
