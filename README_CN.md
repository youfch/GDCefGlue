# GDCefGlue

基于 CefGlue 的 Godot 4.x CEF 浏览器控件。

[English](README.md)

## 功能特性

- GPU 硬件加速支持
- 中文/日文/韩文输入法支持
- 弹窗处理
- 完整的键盘和鼠标支持
- 易于集成到 Godot 4.x

## 性能演示

### WebGL 水族馆

支持 WebGL 渲染，20000 条鱼稳定 120fps：

!\[WebGL水族馆]\(img/WebGL水族馆.png null)

## 快速开始

### C#（插件模式）

在场景中添加 `CefGlueControl` 节点：

```csharp
var browser = new CefGlueControl();
browser.InitialUrl = "https://godotengine.org";
AddChild(browser);
```

### GDScript（GDExtension 模式）

```gdscript
var browser: CefGlueControl = $CefGlueControl
browser.InitialUrl = "https://godotengine.org"
browser.FrameRate = 120

# 连接信号
browser.BrowserInitialized.connect(_on_ready)
browser.AddressChanged.connect(_on_address_changed)
browser.LoadStart.connect(_on_loading)
browser.LoadEnd.connect(_on_done)
browser.LoadError.connect(_on_error)
```

## 环境要求

- **Godot Engine**: 4.6.0 或更高版本（需要 .NET/Mono 支持）
- **.NET SDK**: 8.0 或更高版本
- **Windows**: x64 架构

## 依赖方式

### 方式一：NuGet 包（推荐）

最简单的使用方式。构建时自动复制所有必要文件。

**CEF 149（推荐）：**

```xml
<ItemGroup>
  <PackageReference Include="CefGlue.Common" Version="149.7827.156" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="149.0.4" />
</ItemGroup>
```

**CEF 120（官方，不推荐——太老旧）：**

```xml
<ItemGroup>
  <PackageReference Include="CefGlue.Common" Version="120.6099.0" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="120.2.3" />
</ItemGroup>
```

### 方式二：从源码构建

如果需要最新版本或自定义 CefGlue：

```bash
git clone https://github.com/youfch/CefGlue.git
```

将克隆的仓库放置到项目目录：

```
GDCefGlue/                        ← 本仓库
├── plugin/                       ← Godot .NET 项目（插件源码 + 演示）
│   └── addons/GCefGlue/         ←    CefGlueControl C# 脚本
├── extension/                    ← GDExtension C# 项目（AOT 原生库）
│   └── Dll/                      ←    godot-dotnet 绑定文件
├── test/GDExtensionGame/         ← GDExtension 测试用的 Godot 项目
├── Nuget/                        ← 本地 NuGet 包（LFS）
├── img/
└── README*.md
```

## 项目类型

### GDExtension 项目

位于 `extension/` 目录。适用于 Godot 4.6+。

**特点：**

- 使用 Godot 的 GDExtension 系统
- 编译为原生 AOT 库
- 性能更好，文件体积更小
- 导出时需要手动复制 CEF 文件
- 入口点：`gdcefglue_library_init`

**结构：**

- `extension/` - GDExtension 的 C# 源代码
- `extension/Dll/` - Godot .NET 绑定（来自 [godot-dotnet](https://github.com/raulsntos/godot-dotnet)）
- `test/GDExtensionGame/` - 用于测试的 Godot 项目

**构建说明：**

1. **获取 Godot .NET 绑定：**

   不同 Godot 版本需要对应版本的 godot-dotnet：
   | Godot 版本 | godot-dotnet 分支 |
   | -------- | --------------- |
   | 4.6.x    | master 或对应标签    |
   | 4.5.x    | 检查对应 release 标签 |
   | 4.4.x    | 检查对应 release 标签 |
   > **注意：** godot-dotnet 没有发布 release 包，需要查看历史提交，下载对应 Godot 版本的源码后手动编译。
   ```bash
   git clone https://github.com/raulsntos/godot-dotnet.git
   cd godot-dotnet
   # 切换到对应 Godot 版本的分支/标签（如需要）
   # git checkout <godot-version-tag>
   dotnet build -p:GenerateGodotBindings=true
   ```
   将生成的 `Godot.Bindings.dll` 及相关文件复制到 `extension/Dll/`。
2. **CEF 依赖（跨平台）：**
   - **Windows：** 通过 NuGet 包自动获取 `chromiumembeddedframework.runtime.win-x64`
   - **Linux：** 需要添加 [cef.redist.linux](https://github.com/OutSystems/cef.redist.linux) 依赖
   - **macOS：** 需要添加 [cef.redist.osx](https://github.com/OutSystems/cef.redist.osx) 依赖
   查看 [CefGlue 仓库](https://github.com/youfch/CefGlue) 了解如何添加跨平台依赖。
3. **构建 GDExtension：**

    进入 `extension` 目录执行：

   **Windows x64：**
   ```bash
   # Debug 版本
   dotnet publish -c Debug -r win-x64 --self-contained true

   # Release 版本
   dotnet publish -c Release -r win-x64 --self-contained true
   ```
   **Linux x64：**
   ```bash
   dotnet publish -c Release -r linux-x64 --self-contained true
   ```
   **macOS x64/ARM64：**
   ```bash
   # Intel Mac
   dotnet publish -c Release -r osx-x64 --self-contained true

   # Apple Silicon Mac
   dotnet publish -c Release -r osx-arm64 --self-contained true
   ```
4. **部署：**

   编译输出位于 `bin\Release(Debug)\net9.0\win-x64\publish\`（Windows）或对应平台目录。

将 publish 目录中的所有文件复制到 `test/GDExtensionGame/lib/` 目录。
5. **运行：**
    使用 Godot 4.6 打开 `test/GDExtensionGame/` 项目并运行。

**不同 CEF 版本支持：**

如需使用不同版本的 CEF，有两种方式：

1. **NuGet 包方式（推荐）：** 修改 `.csproj` 中的 NuGet 包版本
   ```xml
   <PackageReference Include="CefGlue.Common" Version="xxx.xxxx.x" />
   <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="xxx.x.x" />
   ```
2. **手动编译：** 从 [CefGlue 仓库](https://github.com/youfch/CefGlue) 下载对应 CEF 版本的源码，手动编译 NuGet 包后引用。

### 普通 C# 项目

位于 `plugin/` 目录。传统的 Godot C# 项目方式。

**特点：**

- 标准 Godot .NET SDK 项目
- 使用 `Godot.NET.Sdk`
- 使用 NuGet 包时 CEF 文件自动复制
- 导出后可能需要手动复制资源（NuGet 包除外）
- 源代码位于 `addons/GCefGlue/`

**适用场景：**

- 如果你偏好传统的 Godot C# 开发方式
- 如果 GDExtension 不能满足你的需求

更新 `.csproj` 引用项目：

```xml
<ItemGroup>
  <ProjectReference Include="..\CefGlue\CefGlue\CefGlue.csproj" />
  <ProjectReference Include="..\CefGlue\CefGlue.Common\CefGlue.Common.csproj" />
  <ProjectReference Include="..\CefGlue\CefGlue.Common.Shared\CefGlue.Common.Shared.csproj" />
</ItemGroup>
```

### CefGlue 来源

| 来源                 | CEF 版本 | 状态      | NuGet                         | GitHub                                          |
| ------------------ | ------ | ------- | ----------------------------- | ----------------------------------------------- |
| youfch/CefGlue     | 149    | 维护中（推荐） | `CefGlue.Common 149.7827.156` | [GitHub](https://github.com/youfch/CefGlue)     |
| youfch/cef.redist.linux | 149 | Linux 运行时 | `cef.redist.linux64 149.0.4` | [GitHub](https://github.com/youfch/cef.redist.linux) |
| youfch/cef.redist.osx | 149 | macOS 运行时 | `cef.redist.osx64 149.0.4` | [GitHub](https://github.com/youfch/cef.redist.osx) |
| OutSystems/CefGlue | 120    | 官方（旧版）      | `CefGlue.Common 120.6099.0`   | [GitHub](https://github.com/OutSystems/CefGlue) |

## 构建说明

### 使用 NuGet 包

```powershell
# 还原包
dotnet restore

# 构建
dotnet build
```

### 使用源码

```powershell
# 克隆 CefGlue
git clone https://github.com/youfch/CefGlue.git

# 构建
dotnet restore
dotnet build
```

## 构建输出

成功构建后，将生成以下文件：

**核心文件：**

- `GDCefGlue.dll` - 主插件程序集
- `Xilium.CefGlue.dll` - CefGlue 包装器
- `Xilium.CefGlue.Common.dll` - 通用功能

**CEF 原生文件：**

- `libcef.dll` - Chromium 核心库
- `chrome_*.pak` - UI 资源
- `resources.pak` - 应用程序资源
- `locales\*.pak` - 语言包

**BrowserProcess 文件：**

- `CefGlueBrowserProcess\` - 浏览器子进程文件

## 导出分发

### 使用 NuGet 包

使用 NuGet 包时，所有必要文件在构建时自动复制。正常导出 Godot 项目即可。

### 使用源码

使用源码依赖时，导出后需要手动复制文件。详见 [CEF\_EXPORT\_GUIDE.md](CEF_EXPORT_GUIDE.md)。

## CefGlueControl 属性

| 属性                          | 类型     | 默认值           | 描述                   |
| --------------------------- | ------ | ------------- | -------------------- |
| `InitialUrl`                | string | "about:blank" | 浏览器创建时加载的 URL        |
| `OpenPopupInCurrentBrowser` | bool   | true          | 如果为 true，弹窗在当前浏览器中导航 |
| `GpuAcceleration`           | bool   | true          | 如果为 true，启用 GPU 硬件加速 |
| `FrameRate`                 | int    | 60            | 浏览器帧率，范围 1-360       |
| `Transparent`               | bool   | false         | 如果为 true，启用透明背景支持    |

### 静态属性

| 属性                | 类型   | 描述                          |
| ----------------- | ---- | --------------------------- |
| `UseGpuAcceleration` | bool | 全局 GPU 加速设置，需在 CEF 初始化前设置    |
| `UseTransparent`     | bool | 全局透明背景设置，需在 CEF 初始化前设置       |

### 只读属性

| 属性                    | 类型     | 描述             |
| --------------------- | ------ | -------------- |
| `Address`             | string | 当前页面 URL       |
| `IsBrowserInitialized` | bool   | 浏览器是否已初始化      |
| `IsLoading`           | bool   | 页面是否正在加载       |
| `Title`               | string | 当前页面标题         |

## CefGlueControl 方法

| 方法                                      | 描述                  |
| --------------------------------------- | ------------------- |
| `GoBack()`                              | 后退                  |
| `GoForward()`                           | 前进                  |
| `NavigateToUrl(string url)`             | 导航到指定 URL           |
| `Reload(bool ignoreCache = false)`      | 刷新当前页面，可选忽略缓存       |
| `ExecuteJavaScript(string code, ...)`   | 执行 JavaScript 代码    |
| `EvaluateJavaScript<T>(string code, ...)` | 执行 JavaScript 并返回结果 |
| `ShowDeveloperTools()`                  | 打开开发者工具             |
| `CloseDeveloperTools()`                 | 关闭开发者工具             |

## JS ↔ C# 桥接

GDCefGlue 通过 `godot://` 协议实现 JavaScript 与 C# 的双向通信。

### JS → C#

JS 通过导航到 `godot://bridge` URL 发送消息：

```javascript
// 使用 iframe（无需 _godotBridge）
var i = document.createElement('iframe');
i.src = 'godot://bridge?type=ping&cb=myCallbackId&payload=' + encodeURIComponent(JSON.stringify({}));
document.body.appendChild(i);
```

或使用 `_godotBridge` 辅助对象（在你的 HTML 中注入）：

```javascript
window._godotBridge = {
    _onResponse: function(cbId, msg) { /* 根据 cbId 找到对应的 Promise resolve */ },
    _onMessage: function(msg) { /* 处理 C# 推送的消息 */ },
    sendToGodot: function(data) {
        var i = document.createElement('iframe');
        i.src = 'godot://bridge?type=' + data.type
              + '&cb=' + data.cbId
              + '&payload=' + encodeURIComponent(JSON.stringify(data.payload));
        document.body.appendChild(i);
    }
};
```

### C# → JS

```csharp
// 推送消息到 JS（调用 window._godotBridge._onMessage）
browser.SendToJs("{\"type\":\"update\",\"payload\":{\"count\":42}}");

// 回复特定 JS 请求（调用 window._godotBridge._onResponse）
browser.SendResponse("myCallbackId", "{\"status\":\"ok\"}");
```

### C# 事件

```csharp
browser.BridgeRequest += (type, payload, cbId) => {
    switch (type) {
        case "ping":
            browser.SendResponse(cbId, "{\"status\":\"pong\"}");
            break;
    }
};
```

| API | 方向 | 描述 |
|-----|------|------|
| `BridgeRequest` 事件 | JS → C# | JS 调用 `godot://bridge` 时触发 |
| `SendToJs(json)` | C# → JS | 推送消息到 `window._godotBridge._onMessage` |
| `SendResponse(cbId, json)` | C# → JS | 回复特定 JS 回调 `window._godotBridge._onResponse` |

## CefGlueControl 信号

| 信号 | 参数 | 描述 |
|------|------|------|
| `BrowserInitialized` | — | 浏览器初始化完成 |
| `AddressChanged` | `url: string` | 当前页面 URL 变化 |
| `TitleChanged` | `title: string` | 页面标题变化 |
| `LoadStart` | — | 页面开始加载 |
| `LoadEnd` | — | 页面加载完成 |
| `LoadError` | `errorText: string, failedUrl: string` | 页面加载失败 |

## GPU 配置

```csharp
// 启用 GPU 加速（默认）- 在检查器中设置
// GpuAcceleration = true;

// 禁用 GPU 加速（软件渲染）- 在检查器中设置
// GpuAcceleration = false;
```

**注意：** 此属性已暴露给 Godot 检查器，在控件初始化前设置。

## 故障排除

### GPU 进程崩溃

1. 确保所有 CEF 文件正确复制
2. 尝试在检查器中禁用 GPU 加速

### 缺少 DLL

1. 运行 `dotnet restore` 还原 NuGet 包
2. 清理并重新构建解决方案

### 空白页面

1. 检查 `locales` 目录是否存在
2. 确保 `resources.pak` 存在

## 已知问题

1. **右键菜单**：暂不支持
2. **网络通知**：`WSALookupServiceBegin failed with: 10108` 是正常警告

## 许可证

- [CefGlue](https://github.com/youfch/CefGlue) - BSD-3-Clause
- [CEF](https://bitbucket.org/chromiumembedded/cef) - BSD-3-Clause
- [Godot Engine](https://godotengine.org) - MIT
- [godot-dotnet](https://github.com/raulsntos/godot-dotnet) - MIT

