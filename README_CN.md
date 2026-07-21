# GDCefGlue

基于 CefGlue 的 Godot 4.x CEF 浏览器控件。

[English](README_EN.md) | [English](README.md)

## 功能特性

- **双渲染模式**：OSR（离屏渲染，支持透明）和 EmbeddedWindow（嵌入窗口，高性能）
- **Inspector 属性分组**：Browser Settings / Feature Toggles / Embedded Mode
- **动态属性显隐**：选 OSR 时自动隐藏嵌入模式属性，选 EmbeddedWindow 时自动隐藏 OSR 属性
- **跨平台嵌入窗口**：Windows (Win32)、Linux (X11)、macOS (Cocoa)
- **键盘事件穿透**：嵌入模式下将浏览器内键盘事件转发到 Godot
- GPU 硬件加速
- 中文/日文/韩文输入法支持
- 弹窗处理
- 完整的键盘和鼠标支持
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
var browser: CefGlueControl = 
browser.InitialUrl = "https://godotengine.org"
browser.FrameRate = 120
browser.Mode = 0  # 0=OSR, 1=EmbeddedWindow

# 连接信号
browser.BrowserInitialized.connect(_on_ready)
browser.AddressChanged.connect(_on_address_changed)
browser.LoadStart.connect(_on_loading)
browser.LoadEnd.connect(_on_done)
browser.LoadError.connect(_on_error)
`

## 环境要求

- **Godot Engine**: 4.6.0 或更高版本（需要 .NET/Mono 支持）
- **.NET SDK**: 8.0 或更高版本
- **Windows/Linux/macOS**: x64 架构（ARM64 也支持）

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
| GpuAcceleration | bool | true | Feature Toggles | 启用 GPU 硬件加速 |
| OpenPopupInCurrentBrowser | bool | true | Feature Toggles | 弹窗在当前浏览器中导航 |
| SyncCursor | bool | false | Feature Toggles | 鼠标光标跟随网页内容（仅 OSR 模式） |
| ForwardInputEvents | bool | false | Embedded Mode | 嵌入模式事件穿透（仅 EmbeddedWindow 模式） |

### 动态属性显隐

| Mode | 显示 | 隐藏 |
|------|------|------|
| OSR | SyncCursor、Transparent | ForwardInputEvents、"Embedded Mode" 分组 |
| EmbeddedWindow | ForwardInputEvents、"Embedded Mode" 分组 | SyncCursor |

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

### 只读属性

| 属性 | 类型 | 描述 |
|------|------|------|
| Address | string | 当前页面 URL |
| IsBrowserInitialized | bool | 浏览器是否已初始化 |
| IsLoading | bool | 页面是否正在加载 |
| Title | string | 当前页面标题 |

## CefGlueControl 方法

| 方法 | 描述 |
|------|------|
| GoBack() | 后退 |
| GoForward() | 前进 |
| NavigateToUrl(string url) | 导航到指定 URL |
| Reload(bool ignoreCache = false) | 刷新当前页面，可选忽略缓存 |
| ExecuteJavaScript(string code, ...) | 执行 JavaScript 代码 |
| EvaluateJavaScript<T>(string code, ...) | 执行 JavaScript 并返回结果 |
| EvalJs(string code) | 异步 JS eval，结果通过 eval_completed 信号（GDScript） |
| ShowDeveloperTools() | 打开开发者工具 |
| CloseDeveloperTools() | 关闭开发者工具 |

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

__hostBridge / __hostEvents 命名与引擎无关 — 同一份 HTML 页面在 Godot、Unreal 或任何 CEF 宿主中均可使用，无需修改。

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

## CefGlueControl 信号

| 信号 | 参数 | 描述 |
|------|------|------|
| BrowserInitialized | — | 浏览器初始化完成 |
| AddressChanged | url: string | 当前页面 URL 变化 |
| TitleChanged | 	itle: string | 页面标题变化 |
| LoadStart | — | 页面开始加载 |
| LoadEnd | — | 页面加载完成 |
| LoadError | errorText: string, failedUrl: string | 页面加载失败 |
| eval_completed | esult, error | EvalJs 结果（GDExtension） |
| ridge_request | 	ype, payload, cbId | 桥接请求（GDExtension） |

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

1. **右键菜单**：暂不支持
2. **网络通知**：WSALookupServiceBegin failed with: 10108 是正常警告
3. **嵌入窗口焦点**：点击 Godot 输入框后 CEF 可能未释放键盘焦点，已通过 Win32 SetFocus / X11 XSetInputFocus / macOS makeFirstResponder 自动处理
4. **JS Bridge S 前缀**：CefGlue 序列化协议会给字符串加 marker 前缀（'S'），已自动剥离

## 许可证

- [CefGlue](https://github.com/youfch/CefGlue) - BSD-3-Clause
- [CEF](https://bitbucket.org/chromiumembedded/cef) - BSD-3-Clause
- [Godot Engine](https://godotengine.org) - MIT
- [godot-dotnet](https://github.com/raulsntos/godot-dotnet) - MIT
