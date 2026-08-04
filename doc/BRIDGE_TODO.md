# GDCefGlue TODO

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

## 已实现功能

### 渲染模式

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
  ├── GpuCompositing
  ├── OpenPopupInCurrentBrowser
  ├── EnableMediaStream
  ├── SyncCursor          ← 仅 OSR 显示
  └── ContextMenuEnabled  ← 仅 OSR 显示

▸ Embedded Mode        ← 仅 Mode=EmbeddedWindow 时显示
  └── ForwardInputEvents
`

### 已实现的 Handler

| Handler | 文件 | 说明 |
|---------|------|------|
| RenderHandler | `GodotRenderHandler.cs` | OSR 离屏渲染；窗口模式返回 null |
| LifeSpanHandler | `GodotLifeSpanHandler.cs` | 弹窗拦截、NewWindowRequested 事件 |
| DisplayHandler | `GodotDisplayHandler.cs` | 地址/标题/光标变化 |
| LoadHandler | `GodotLoadHandler.cs` | 加载状态回调 |
| RequestHandler | `GodotRequestHandler.cs` | 自定义 godot:// 协议桥接 |
| PermissionHandler | `GodotPermissionHandler.cs` | 媒体流权限（EnableMediaStream） |
| ContextMenuHandler | `GodotContextMenuHandler.cs` | OSR 右键菜单 + Godot PopupMenu |
| FocusHandler | `GodotFocusHandler.cs` | CEF 焦点变化同步（IME 驱动） |
| FindHandler | `GodotFindHandler.cs` | 页面内查找结果回调 |

### 已实现的 CefGlueControl 文件

| Partial 文件 | 职责 |
|--------------|------|
| `CefGlueControl.cs` | 核心字段、枚举、构造函数 |
| `CefGlueControl.Properties.cs` | Export 属性、静态属性、只读属性、事件声明 |
| `CefGlueControl.Initialization.cs` | CEF 初始化、_Ready、_ExitTree、浏览器创建 |
| `CefGlueControl.Rendering.cs` | OSR 渲染（OnPaint → _Process → _Draw）、光标 |
| `CefGlueControl.Input.cs` | Godot → CEF 输入转发（仅 OSR 模式） |
| `CefGlueControl.Bridge.cs` | JS ↔ C# 桥接、IPC、EvaluateJavaScript |
| `CefGlueControl.Navigation.cs` | 导航方法、CEF 事件回调（三层线程安全模式） |
| `CefGlueControl.Inspector.cs` | _ValidateProperty 属性可见性控制 |
| `CefGlueControl.Events.cs` | ForwardInputEvents 事件转发（JS → Godot） |
| `CefGlueControl.Embedded.cs` | EmbeddedWindow 模式管理 |
| `CefGlueControl.Cookies.cs` | Cookie 管理（Task-based + event-based API） |
| `CefGlueControl.ContextMenu.cs` | OSR 右键菜单 PopupMenu 构建 + 回调 |

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
- 无法使用 SchemeHandler + fetch（file:// → ipc:// 跨域 CORS 死胡同）

## CEF 原生 6 种 JS↔Native 机制

| # | 机制 | 方向 | Payload 限制 | 线程 |
|---|------|------|-------------|------|
| 1 | CefMessageRouterBrowserSide（window.cefQuery） | JS→C++ 双向 | 默认字符串；145+ 支持 shared memory | UI 线程 |
| 2 | CefRegisterExtension + CefV8Handler | JS→C++ 同步 | 无限制 | TID_RENDERER |
| 3 | OnContextCreated + CefV8Value::CreateFunction | JS→C++ 同步 | 无限制 | TID_RENDERER |
| 4 | CefFrame::ExecuteJavaScript | C++→JS 单向 | 无返回值 | 任意线程 |
| 5 | SendProcessMessage + OnProcessMessageReceived | 跨进程双向 | ~128KB；shared memory 无限 | UI 线程 |
| 6 | CefResourceRequestHandler / CefSchemeHandlerFactory | 双向（HTTP） | 无限（流式） | TID_IO |

## 计划功能

| 功能 | 说明 | 优先级 | 参考实现 |
|------|------|--------|---------|
| **GPU 加速 OSR** (`EnableGpuAcceleration`) | ⚠️ 实验性功能，暂不可用。通过 `OnAcceleratedPaint` + SharedTexture 实现 GPU 加速渲染，后续完善。 | 🔬 实验性 | `CefGlueControl.AcceleratedPaint.cs` 已有框架 |
| **下载处理** | CefDownloadHandler 实现，拦截 OnBeforeDownload / OnDownloadUpdated，提供下载进度信号。Godot FileDialog 选择保存路径，进度条 UI | ⭐⭐⭐ | CefGlue.Common + CefRunContextMenuCallback 模式（同右键菜单） |
| **页面查找** | CefBrowserHost.Find() / StopFinding()，页面内查找功能 | ⭐⭐ | CefBrowserHost API |
| **缩放控制** | CefBrowserHost.SetZoomLevel()，页面缩放 | ⭐⭐ | CefBrowserHost API |
| **全屏处理** | 响应 CEF 全屏事件，自动切换 Godot 窗口全屏状态 | ⭐ | CefDisplayHandler.OnFullscreenModeChange |
| **打印支持** | CEF 的打印功能 (CefBrowserHost.Print()) | ⭐ | CefBrowserHost API |
| **JS 控制台日志** | CefDisplayHandler.OnConsoleMessage 将 JS console.log 转发到 Godot 输出 | ⭐ | GodotDisplayHandler 扩展 |
| **键盘事件（嵌入模式）** | HandleForwardedEvent 补充 InputEventKey 处理（keydown/keyup） | ⭐⭐ | CefGlueControl.Events.cs 现有框架 |
| **Linux 嵌入式模式** | EmbeddedWindow 在 Linux 上目前仅支持窗口模式，完整嵌入式支持（容器内）后续实现 | 🔬 后续 | X11 子窗口嵌入完善 |

## 参考实现

- **godot_wry**: https://github.com/doceazedo/godot_wry
  - lib.rs: JS 事件监听 + IPC handler + push_input
  - godot_window.rs: DisplayServer.WindowGetNativeHandle → HWND
  - 关键技巧：移除 WS_CLIPCHILDREN、坐标换算、CURRENT_BUTTON_MASK 状态机
- wrymium（Tauri+CEF 混合）: https://github.com/gxcsoccer/wrymium
- Chromely: https://github.com/chromelyapps/CefSharp
