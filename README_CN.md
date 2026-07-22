# GDCefGlue

基于 CefGlue 的 Godot 4.x CEF 浏览器控件。

[English](README_EN.md) | [English](README.md)

## 功能特性

- **双渲染模式**：OSR（离屏渲染，支持透明）和 EmbeddedWindow（嵌入窗口，高性能）
- **Inspector 属性分组**：Browser Settings / Feature Toggles / Embedded Mode
- **动态属性显隐**：选 OSR 时自动隐藏嵌入模式属性，选 EmbeddedWindow 时自动隐藏 OSR 属性
- **跨平台嵌入窗口**：Windows (Win32)、Linux (X11)、macOS (Cocoa)
- **键盘事件穿透**：嵌入模式下将浏览器内键盘事件转发到 Godot
- **GPU 硬件加速**
- **中文/日文/韩文输入法支持**：JS 焦点监视器自动检测输入框焦点，驱动 IME 激活/关闭
- **右键上下文菜单**：OSR 模式下通过 Godot PopupMenu 显示 CEF 默认菜单项，支持自定义
- **弹窗处理**
- **完整的键盘和鼠标支持**
- **页面内查找**：`Find()` / `StopFinding()` 方法 + `FindResult` 事件
- **JS ↔ C# 桥接**：通过 RegisterJavascriptObject（CEF IPC，无需 iframe）
- **GDScript 桥接**：通过 RegisterJsHandler（Callable 方式，用于 GDExtension）
- **引擎无关的 JS API**：window.__hostBridge / window.__hostEvents — 一次编写，Godot、Unreal 或任何 CEF 宿主通用

## 快速开始

### C#（插件模式）

`csharp
var browser = new CefGlueControl();
browser.InitialUrl = "https://godotengine.org";
browser.Mode = RenderMode.OSR;        // OSR（支持透明）或 EmbeddedWindow
browser.Transparent = true;           // 仅 OSR 模式生效
AddChild(browser);
`

### GDScript（GDExtension 模式）

`gdscript
var browser = CefGlueControl.new()
browser.InitialUrl = "https://godotengine.org"
browser.FrameRate = 120
browser.Mode = 0  # 0=OSR, 1=EmbeddedWindow

# 连接信号（GDExtension 使用 snake_case 命名）
browser.browser_initialized.connect(_on_ready)
browser.address_changed.connect(_on_address_changed)
browser.load_start.connect(_on_loading)
browser.load_end.connect(_on_done)
browser.load_error.connect(_on_error)
`

## 环境要求

- **Godot Engine**: 4.6.0 或更高版本（需要 .NET/Mono 支持）
- **.NET SDK**: 8.0 或更高版本
- **Windows/Linux/macOS**: x64 架构（ARM64 也支持）

## 构建流程

### Plugin（C# 插件，Godot.NET.Sdk）

```bash
# 普通构建（编译检查）
dotnet build plugin/GDCefGlue.csproj

# 发布
dotnet publish plugin/GDCefGlue.csproj -c Release
```

Plugin 使用 `Godot.NET.Sdk`，CEF 文件在构建时自动复制，无需手动操作。

### Extension（GDExtension，NativeAOT）

```bash
# 普通构建（仅编译检查，产物不可用于 GDExtension）
dotnet build extension/GDCefGlueExtension.csproj

# AOT 发布（实际用于 GDExtension 的构建）
dotnet publish extension/GDCefGlueExtension.csproj -c Release -r win-x64
```

**AOT 构建产物路径：**
- 原生 DLL: `extension/bin/Release/net10.0/win-x64/native/GDCefGlueExtension.dll`
- 发布目录: `extension/bin/Release/net10.0/win-x64/publish/`

> **注意：** Extension 必须使用 `dotnet publish -r <RID>` 进行 AOT 编译，`dotnet build` 仅生成托管程序集，不可被 GDExtension 加载。支持 RID: `win-x64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`。

## NuGet 包

CefGlue 的 NuGet 包发布在 [GitHub Releases](https://github.com/youfch/CefGlue/releases)（不在 NuGet.org 上）。需要下载 `.nupkg` 文件并配置本地源。

### 配置步骤

1. **下载全部** `.nupkg` 文件从 [GitHub Releases](https://github.com/youfch/CefGlue/releases/tag/v149.7827.156) — `CefGlue.BrowserProcess.runtime.jit` 是元包，其依赖项需要在本地解析。

2. **放置** 到本地文件夹，例如 `./nuget-feed/`。

3. **创建** `nuget.config` 在项目根目录：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local-cefglue" value="./nuget-feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

4. **添加** 包引用到 `.csproj`：

```xml
<ItemGroup>
  <PackageReference Include="CefGlue.Common" Version="149.7827.156" />
  <PackageReference Include="CefGlue.BrowserProcess.runtime.jit" Version="149.7827.156" />
  <PackageReference Include="chromiumembeddedframework.runtime" Version="149.0.4" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="149.0.4" />
</ItemGroup>
```

所有 CEF 文件在构建时自动复制，无需手动操作。

> **注意：** 如需跨平台构建（Linux/macOS），请添加对应的 `cef.redist.*` 包。

## 文件结构

`
addons/GCefGlue/                    ← 插件源码
├── CefGlueControl.cs               骨架（枚举、字段、构造函数）
├── CefGlueControl.Properties.cs    导出属性、静态属性、事件
├── CefGlueControl.Initialization.cs CEF 初始化、生命周期
├── CefGlueControl.Rendering.cs     OSR 渲染、_Process、_Draw、光标
├── CefGlueControl.Input.cs         输入转发、IME、_Notification
├── CefGlueControl.Bridge.cs        JS 桥接、IPC、反序列化
├── CefGlueControl.Navigation.cs    导航、DevTools、CEF 回调
├── CefGlueControl.Inspector.cs     Inspector 属性可见性控制
├── CefGlueControl.Events.cs        ForwardInputEvents 事件转发
├── CefGlueControl.Embedded.cs      嵌入窗口模式
├── CefInitializer.cs               CEF 初始化
├── Handlers/                       CEF 处理器
│   ├── GodotCefApp.cs
│   ├── GodotCefClient.cs
│   ├── GodotDisplayHandler.cs
│   ├── GodotLifeSpanHandler.cs
│   ├── GodotLoadHandler.cs
│   ├── GodotRenderHandler.cs
│   ├── GodotRequestHandler.cs
│   └── GodotBrowserProcessHandler.cs
└── Platform/                       跨平台原生 API
    ├── NativeWindowMethods.cs      平台抽象层
    ├── X11Methods.cs               Linux X11 P/Invoke
    └── MacMethods.cs               macOS Cocoa P/Invoke
`

## CefGlueControl 属性

| 属性 | 类型 | 默认值 | 分组 | 描述 |
|------|------|--------|------|------|
| InitialUrl | string | "about:blank" | Browser Settings | 浏览器创建时加载的 URL |
| Mode | RenderMode | OSR | Browser Settings | 渲染模式：OSR / EmbeddedWindow |
| FrameRate | int | 60 | Browser Settings | 浏览器帧率，范围 1-360 |
| Transparent | bool | false | Browser Settings | 启用透明背景（仅 OSR 模式） |
| CacheDirectory | string | "user://cef_cache" | Browser Settings | CEF 缓存目录 |
| GpuAcceleration | bool | true | Feature Toggles | 启用 GPU 硬件加速 |
| OpenPopupInCurrentBrowser | bool | false | Feature Toggles | 弹窗在当前浏览器中导航 |
| EnableMediaStream | bool | false | Feature Toggles | 启用媒体流访问（麦克风/摄像头） |
| SyncCursor | bool | false | Feature Toggles | 鼠标光标跟随网页内容（仅 OSR 模式） |
| ContextMenuEnabled | bool | true | Feature Toggles | 启用右键上下文菜单（仅 OSR 模式） |
| ForwardInputEvents | bool | false | Embedded Mode | 嵌入模式事件穿透（仅 EmbeddedWindow 模式） |

### 动态属性显隐

| Mode | 显示 | 隐藏 |
|------|------|------|
| OSR | SyncCursor、Transparent、ContextMenuEnabled | ForwardInputEvents、"Embedded Mode" 分组 |
| EmbeddedWindow | ForwardInputEvents、"Embedded Mode" 分组 | SyncCursor、ContextMenuEnabled |

### RenderMode 枚举

| 值 | 说明 |
|----|------|
| OSR (0) | 离屏渲染，CEF 渲染到内存 → Godot 纹理。**支持透明背景**。跨平台（Windows/Linux/macOS） |
| EmbeddedWindow (1) | 嵌入原生子窗口，CEF 直接渲染到系统窗口。**性能更好**（视频/WebGL），**不支持透明**。跨平台：Windows (Win32)、Linux (X11)、macOS (Cocoa) |

### ForwardInputEvents（嵌入模式事件穿透）

`
JS 事件 → window.__hostEvents.forward(payload) → CEF IPC → C# → viewport.PushInput()
`

### 静态属性

| 属性 | 类型 | 描述 |
|------|------|------|
| UseGpuAcceleration | bool | 全局 GPU 加速设置，需在 CEF 初始化前设置 |
| UseTransparent | bool | 全局透明背景设置，需在 CEF 初始化前设置 |
| ActiveRenderMode | RenderMode | 全局渲染模式，需在 CEF 初始化前设置 |

### 只读属性

| 属性 | 类型 | 描述 |
|------|------|------|
| Address | string | 当前页面 URL |
| IsBrowserInitialized | bool | 浏览器是否已初始化 |
| IsLoading | bool | 页面是否正在加载 |
| Title | string | 当前页面标题 |

## CefGlueControl 方法

### C#（Plugin）

| 方法 | 返回 | 描述 |
|------|------|------|
| `GoBack()` | `void` | 后退 |
| `GoForward()` | `void` | 前进 |
| `NavigateToUrl(string url)` | `void` | 导航到指定 URL |
| `Reload(bool ignoreCache = false)` | `void` | 刷新当前页面，可选忽略缓存 |
| `ExecuteJavaScript(string code, ...)` | `void` | 执行 JavaScript 代码 |
| `EvaluateJavaScript<T>(string code, ...)` | `Task<T>` | 执行 JS 并返回结果（异步） |
| `EvalJs(string code)` | `void` | 异步 JS eval，结果通过 `eval_completed` 信号 |
| `ShowDeveloperTools()` | `void` | 打开开发者工具 |
| `CloseDeveloperTools()` | `void` | 关闭开发者工具 |
| `Find(string text, bool forward, bool matchCase, bool findNext)` | `void` | 页面内搜索 |
| `StopFinding(bool clearSelection)` | `void` | 停止搜索 |
| `RegisterJavascriptObject(object target, string name)` | `void` | 注册 C# 对象，JS 可调其方法 |
| `UnregisterJavascriptObject(string name)` | `void` | 取消注册 |
| `SendToJs(string json)` | `void` | 推送消息到 JS |
| `SendResponse(string cbId, string json)` | `void` | 回复桥接请求 |

### GDScript（GDExtension）

| 方法 | 返回 | 描述 |
|------|------|------|
| `go_back()` | `void` | 后退 |
| `go_forward()` | `void` | 前进 |
| `navigate_to_url(url: String)` | `void` | 导航到指定 URL |
| `reload(ignore_cache: bool = false)` | `void` | 刷新当前页面 |
| `execute_javascript(code: String, url: String = "about:blank", line: int = 1)` | `void` | 执行 JavaScript 代码 |
| `eval_js(code: String)` | `void` | 异步 JS eval，结果通过 `eval_completed` 信号 |
| `show_developer_tools()` | `void` | 打开开发者工具 |
| `close_developer_tools()` | `void` | 关闭开发者工具 |
| `find(search_text: String, forward: bool = true, match_case: bool = false, find_next: bool = false)` | `void` | 页面内搜索 |
| `stop_finding(clear_selection: bool = true)` | `void` | 停止搜索 |
| `register_js_handler(name: String, handler: Callable, methods: String = "[\"hello\",\"echo\",\"add\",\"getVersion\",\"eval\"]")` | `void` | 注册 GDScript 处理器，JS 可调 |
| `unregister_js_handler(name: String)` | `void` | 取消注册 |
| `send_to_js(json: String)` | `void` | 推送消息到 JS |
| `send_response(cb_id: String, json: String)` | `void` | 回复桥接请求 |

## JS ↔ C# 桥接

### RegisterJavascriptObject（推荐）✅

C# 端注册对象，JS 通过 V8 IPC 直接调用 C# 方法。

`csharp
browser.RegisterJavascriptObject(new MyBridge(), "myBridge");
`

**JS 端调用（返回 Promise）：**
`javascript
window.myBridge.hello().then(function(result) {
    console.log(result);
}).catch(function(err) {
    console.error(err);
});
`

**C# → JS：求值并取回返回值：**
`csharp
var title = await browser.EvaluateJavaScript<string>("document.title");
`

**C# → JS：主动推送消息（JS 通过 window.__hostBridge._onMessage 接收）：**
`csharp
browser.SendToJs("{\"type\":\"update\",\"payload\":{\"count\":42}}");
`

### JS API 参考

| 对象 | 方法 | 描述 |
|------|------|------|
| window.__hostBridge | ._onMessage(msg) | 接收宿主（C#/GDScript）推送的消息 |
| window.__hostBridge | ._onResponse(cbId, json) | 接收宿主的响应 |
| window.__hostEvents | .forward(payload) | 转发输入事件到宿主（EmbeddedWindow 模式） |
| window.__hostFocus | .onInputFocusChanged(bool) | 通知宿主输入框焦点变化（驱动 IME 激活/关闭） |

__hostBridge / __hostEvents / __hostFocus 命名与引擎无关 — 同一份 HTML 页面在 Godot、Unreal 或任何 CEF 宿主中均可使用，无需修改。

### IME 输入法焦点监视

页面加载时自动注入 `__hostFocus` V8 对象 + JS 焦点监视脚本。通过 `focusin`/`focusout` 事件检测页面中是否有可编辑元素（`<input>`、`<textarea>`、`contentEditable`）聚焦，自动激活/关闭 Godot IME：

```javascript
// 页面焦点变化时自动调用（由注入脚本驱动）
window.__hostFocus.onInputFocusChanged(true);   // 输入框聚焦 → 激活 IME
window.__hostFocus.onInputFocusChanged(false);  // 输入框失焦 → 关闭 IME
```

IME 激活/关闭由 JS 焦点变化驱动，无需手动干预。适用于 OSR 和 EmbeddedWindow 模式。

### API 速览

| API | 平台 | 方向 | 说明 |
|-----|------|------|------|
| RegisterJavascriptObject(target, name) | Plugin | C# → JS | 注册对象，JS 可调其方法 |
| RegisterJsHandler(name, Callable, methods) | GDExtension | GDScript → JS | 注册 GDScript 处理器 |
| EvaluateJavaScript<T>(code, ...) | Plugin | C# → JS | 执行 JS 并返回结果 |
| EvalJs(code) | Both | GDScript/C# → JS | 异步 JS eval，通过信号 |
| SendToJs(json) | Both | C#/GDScript → JS | 推送消息到 JS |
| SendResponse(cbId, json) | Both | C#/GDScript → JS | 回复桥接请求 |
| BridgeRequest 事件 | Both | JS → C# | 桥接请求入口 |
| ridge_request 信号 | GDExtension | JS → GDScript | 桥接请求信号 |

## 事件 / 信号

### C# 事件（Plugin）

C# 事件使用 **PascalCase** 命名，通过 `+=` 订阅：

| 事件 | 参数 | 描述 |
|------|------|------|
| `BrowserInitialized` | `Action` | 浏览器初始化完成 |
| `AddressChanged` | `AddressChangedEventHandler` | 当前页面 URL 变化 |
| `TitleChanged` | `TitleChangedEventHandler` | 页面标题变化 |
| `LoadStart` | `LoadStartEventHandler` | 页面开始加载 |
| `LoadEnd` | `LoadEndEventHandler` | 页面加载完成 |
| `LoadError` | `LoadErrorEventHandler` | 页面加载失败 |
| `BridgeRequest` | `Action<string, string, string>` | JS → C# 桥接请求（type, payload, cbId） |
| `NewWindowRequested` | `Action<string, bool>` | 新窗口/新标签请求 |
| `FindResult` | `Action<int, int, int, bool>` | 页面内查找结果（identifier, count, activeMatchOrdinal, finalUpdate） |
| `BeforeContextMenu` | `Action<ContextMenuModel, ContextMenuParams>` | 右键菜单即将显示（可修改菜单项） |
| `ContextMenuCommand` | `Func<int, ContextMenuParams, CefEventFlags, bool>` | 右键菜单项被选中 |
| `CookiesVisited` | `Action<List<CookieInfo>>` | Cookie 遍历完成 |
| `SetCookieCompleted` | `Action<bool>` | SetCookie 完成 |
| `DeleteCookiesCompleted` | `Action<int>` | DeleteCookies 完成 |

### GDScript 信号（GDExtension）

GDScript 信号使用 **snake_case** 命名，通过 `.connect()` 订阅：

| 信号 | 参数 | 描述 |
|------|------|------|
| `browser_initialized` | — | 浏览器初始化完成 |
| `address_changed` | `url: String` | 当前页面 URL 变化 |
| `title_changed` | `title: String` | 页面标题变化 |
| `load_start` | — | 页面开始加载 |
| `load_end` | — | 页面加载完成 |
| `load_error` | `errorText: String, failedUrl: String` | 页面加载失败 |
| `eval_completed` | `result: String, error: String` | EvalJs 结果 |
| `bridge_request` | `type: String, payload: String, cbId: String` | JS → GDScript 桥接请求 |
| `new_window_requested` | `url: String, isNewWindow: bool` | 新窗口/新标签请求 |
| `find_result` | `identifier: int, count: int, activeMatchOrdinal: int, finalUpdate: bool` | 页面内查找结果 |  

## Demo 演示

### C# 插件（Godot .NET 项目）

用 Godot 4.6+ 打开 `plugin/` 目录：

| 演示 | 场景路径 | 说明 |
|------|---------|------|
| 多标签浏览器 | `plugin/demo/browser/Browser.tscn` | 完整浏览器 UI，支持多标签页、导航、DevTools |
| IPC 桥接 | `plugin/demo/ipc/IpcDemo.tscn` | JS ↔ C# 桥接通信演示 |

### GDExtension（原生 AOT 编译）

用 Godot 4.6+ 打开 `test/GDExtensionGame/` 目录：

| 演示 | 场景路径 | 说明 |
|------|---------|------|
| 多标签浏览器 | `test/GDExtensionGame/demo/browser/Browser.tscn` | GDScript 版的浏览器演示 |
| IPC 桥接 | `test/GDExtensionGame/demo/ipc/IpcDemo.tscn` | JS ↔ GDScript 桥接演示 |

## 故障排除

### GPU 进程崩溃
1. 确保所有 CEF 文件正确复制
2. 尝试在检查器中禁用 GPU 加速

### 缺少 DLL
1. 运行 dotnet restore 还原 NuGet 包
2. 清理并重新构建解决方案

### 空白页面
1. 检查 locales 目录是否存在
2. 确保 esources.pak 存在

## 已知问题

1. **右键菜单**：OSR 模式下默认显示 CEF 菜单项，可通过 `ContextMenuEnabled` 关闭
2. **网络通知**：`WSALookupServiceBegin failed with: 10108` 是正常警告，不影响功能
3. **JS Bridge S 前缀**：CefGlue 序列化协议会给字符串加 marker 前缀（'S'），已自动剥离

## 许可证

- [CefGlue](https://github.com/youfch/CefGlue) - BSD-3-Clause
- [CEF](https://bitbucket.org/chromiumembedded/cef) - BSD-3-Clause
- [Godot Engine](https://godotengine.org) - MIT
- [godot-dotnet](https://github.com/raulsntos/godot-dotnet) - MIT
