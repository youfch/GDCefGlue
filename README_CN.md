# GDCefGlue

基于 CefGlue 的 Godot 4.x CEF 浏览器控件。

[English](README.md)

---

## 功能特性

- **双渲染模式**：OSR（离屏渲染，支持透明）和 EmbeddedWindow（嵌入窗口，高性能）
- **IME 输入法**：JS 焦点监视器自动检测输入框焦点，驱动 IME 激活/关闭（中日韩输入）
- **右键上下文菜单**：OSR 模式下通过 Godot PopupMenu 显示 CEF 默认菜单项，支持自定义
- **页面内查找**：`Find()` / `StopFinding()` 方法 + `FindResult` 事件
- **JS ↔ C#/GDScript 桥接**：V8 IPC（无需 iframe），双向通信
- **引擎无关的 JS API**：`window.__hostBridge` / `window.__hostEvents` / `window.__hostFocus`
- **跨平台**：Windows (Win32)、Linux (X11)、macOS (Cocoa)
- **GPU 加速**（实验性，暂不可用，后续实现）、弹窗处理、键盘鼠标支持

---

## 快速开始

### C#（插件模式）

```csharp
var browser = new CefGlueControl();
browser.InitialUrl = "https://godotengine.org";
browser.Mode = RenderMode.OSR;   // OSR 或 EmbeddedWindow
browser.Transparent = true;      // 仅 OSR 模式生效
AddChild(browser);
```

### GDScript（GDExtension 模式）

```gdscript
var browser = CefGlueControl.new()
browser.InitialUrl = "https://godotengine.org"
browser.Mode = 0  # 0=OSR, 1=EmbeddedWindow
browser.browser_initialized.connect(_on_ready)
add_child(browser)
```

> 完整示例：`plugin/demo/`（C#）| `test/GDExtensionGame/demo/`（GDScript）

---

## 环境要求

- Godot 4.6+（需要 .NET/Mono 支持）
- .NET SDK 8.0+
- Windows/Linux/macOS（x64 / ARM64）

---

## 安装

### 方式 A：Plugin（C#）

1. 从 [GitHub Releases](https://github.com/youfch/GDCefGlue/releases) 下载最新发布包
2. 解压 `addons/GCefGlue/` 到项目的 `addons/`
3. 添加 NuGet 包 — 见下方 [NuGet 配置](#nuget-配置)

### 方式 B：GDExtension（GDScript）

1. 从 [GitHub Releases](https://github.com/youfch/GDCefGlue/releases) 下载 GDExtension 发布包
2. 解压 `addons/gdcefglue/` 到项目的 `addons/`
3. 完成 — `.gdextension` 已自动配置

### NuGet 配置（仅 Plugin）

CefGlue 包发布在 GitHub Releases 上（不在 NuGet.org）。

**下载地址：**
- [CefGlue NuGet 包](https://www.nuget.org/packages/CefGlue.Common/120.6099.211)
- [chromiumembeddedframework.runtime](https://github.com/youfch/cef.redist.win/releases)

创建 `nuget.config`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local-cefglue" value="./nuget-feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

在 `.csproj` 中添加引用：

```xml
<ItemGroup>
<PackageReference Include="CefGlue.Common" Version="120.6099.211" />
<PackageReference Include="chromiumembeddedframework.runtime" Version="120.1.8" />
<PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="120.1.8" />
</ItemGroup>
```

---

## 构建流程

### Plugin

```bash
dotnet build plugin/GDCefGlue.csproj
dotnet publish plugin/GDCefGlue.csproj -c Release
```

### Extension（NativeAOT）

```bash
dotnet build extension/GDCefGlueExtension.csproj      # 仅编译检查
dotnet publish extension/GDCefGlueExtension.csproj -c Release -r win-x64
```

AOT 产物：`extension/bin/Release/net10.0/win-x64/native/GDCefGlueExtension.dll`

> 支持 RID：`win-x64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`

---

## 截图

> 截图待补充，欢迎贡献！

| 浏览器演示 | IPC 桥接 | 右键菜单 |
|:---:|:---:|:---:|
| ![浏览器](img/screenshot-browser.png?raw=true) | ![桥接](img/screenshot-bridge.png?raw=true) | ![右键菜单](img/screenshot-context-menu.png?raw=true) |
| Godot 中的多标签浏览器 | JS ↔ C# 桥接演示 | OSR 右键上下文菜单 |

---

## 文档

| 文档 | 说明 |
|------|------|
| [用户指南](doc/USER_GUIDE_CN.md) | 完整使用说明、桥接 API、事件、故障排除 |
| [User Guide](doc/USER_GUIDE.md) | Full usage, bridge API, events, troubleshooting |
| [Bridge TODO](doc/BRIDGE_TODO.md) | 桥接实现状态与计划 |

---

## 已知问题

1. `WSALookupServiceBegin failed with: 10108` — 正常 Windows 警告，忽略
2. CefGlue 序列化协议给字符串加 'S' 前缀 — 已自动剥离
3. **GPU 加速**（`EnableGpuAcceleration`）为**实验性功能**，暂不可用，后续实现
4. **Linux 嵌入式模式不完整** — 仅窗口模式可用，Linux 上暂时推荐 OSR 模式。完整嵌入式支持为后续实现

---

## 许可证

MIT。第三方依赖：[CefGlue](https://github.com/youfch/CefGlue) (BSD-3)、[CEF](https://bitbucket.org/chromiumembedded/cef) (BSD-3)、[Godot](https://godotengine.org) (MIT)