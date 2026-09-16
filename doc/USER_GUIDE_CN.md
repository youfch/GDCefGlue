# GDCefGlue 用户指南

[English](USER_GUIDE.md)

## 概述

GDCefGlue 将完整的 Chromium 浏览器（CEF）嵌入 Godot 4.x 的 `Control` 节点中。支持两种渲染模式和完整的 JS ↔ C#/GDScript 桥接。

---

## 安装

### 方式 A：Plugin（C#，Godot.NET.Sdk）

1. **创建 Godot .NET 项目**（Godot 4.6+）。
2. **下载最新发布包** 从 [GitHub Releases](https://github.com/youfch/GDCefGlue/releases)。
3. **解压** `addons/GCefGlue/` 到项目的 `addons/` 目录。
4. **添加 NuGet 包** — 见下方 [NuGet 配置](#nuget-配置)。

### 方式 B：GDExtension（NativeAOT，GDScript）

1. **下载 GDExtension 发布包** 从 [GitHub Releases](https://github.com/youfch/GDCefGlue/releases)。
2. **解压** `addons/gdcefglue/` 到项目的 `addons/` 目录。
3. `.gdextension` 文件已自动配置 — 无需额外设置。

### NuGet 配置（仅 Plugin）

CefGlue 的 NuGet 包发布在 GitHub Releases 上（不在 NuGet.org），需要手动下载并配置本地源。

**下载地址：**
- [CefGlue NuGet 包](https://github.com/youfch/CefGlue/releases/tag/v149.7827.156) — 下载全部 `.nupkg` 文件
- [chromiumembeddedframework.runtime](https://github.com/youfch/cef.redist.win/releases) — CEF 运行时包

**配置步骤：**

1. 下载全部 `.nupkg` 文件，放入 `./nuget-feed/` 目录
2. 在项目根目录创建 `nuget.config`：

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
  <PackageReference Include="CefGlue.Common" Version="149.7827.156" />
  <PackageReference Include="CefGlue.BrowserProcess.runtime.jit" Version="149.7827.156" />
  <PackageReference Include="chromiumembeddedframework.runtime" Version="149.0.4" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="149.0.4" />
</ItemGroup>
```

---

## 基本用法

### 添加浏览器到场景

**C#：**
```csharp
var browser = new CefGlueControl();
browser.InitialUrl = "https://godotengine.org";
browser.Mode = RenderMode.OSR;   // OSR（支持透明）或 EmbeddedWindow
AddChild(browser);
```

> 完整 C# 示例见 `plugin/demo/` 目录。

**GDScript：**
```gdscript
var browser = CefGlueControl.new()
browser.InitialUrl = "https://godotengine.org"
browser.Mode = 0  # 0=OSR, 1=EmbeddedWindow
add_child(browser)
```

> 完整 GDScript 示例见 `test/GDExtensionGame/demo/` 目录。

### Inspector 设置

在 Godot 编辑器中添加 `CefGlueControl` 节点。关键 Inspector 属性：

| 属性 | 默认值 | 说明 |
|----------|---------|------|
| InitialUrl | `about:blank` | 启动时加载的 URL |
| Mode | `OSR` | `OSR`（透明，跨平台）或 `EmbeddedWindow`（原生窗口，视频/WebGL 性能更好） |
| FrameRate | `60` | 浏览器帧率（1-360，仅 OSR） |
| Transparent | `false` | 启用透明背景（仅 OSR） |
| ContextMenuEnabled | `true` | 显示右键菜单（仅 OSR） |
| EnableGpuAcceleration | `false` | ⚠️ **实验性** — 暂不可用，默认保持禁用 |

---

## 渲染模式

### OSR（离屏渲染）— 默认

> **注意：** GPU 加速（`EnableGpuAcceleration`）为实验性功能，暂不可用，后续实现。请勿在生产环境启用。

CEF 渲染到内存 → Godot 作为纹理绘制。

**优点：**
- ✅ 支持 Alpha 透明
- ✅ 跨平台（Windows/Linux/macOS）
- ✅ 可在任何 Godot 容器中使用（ScrollContainer 等）
- ✅ IME 输入法支持

**缺点：**
- ❌ 视频/WebGL 性能较低
- ❌ CPU 占用较高（软件渲染路径）

### EmbeddedWindow

> ⚠️ **Linux**：嵌入式模式在 Linux 上尚不完整，仅窗口模式可用。Linux 上暂时推荐 OSR 模式。完整嵌入式支持为后续实现。

CEF 创建原生子窗口嵌入到 Godot 窗口中。

**优点：**
- ✅ GPU 硬件加速
- ✅ 流畅的视频/WebGL 播放
- ✅ CPU 占用较低

**缺点：**
- ❌ 不支持透明
- ❌ 各平台行为不同（焦点、Z 顺序）
- ❌ 某些 Godot 容器中可能无法正常工作

---

## 连接与事件

### C# 事件

```csharp
browser.BrowserInitialized += () => GD.Print("浏览器已就绪");
browser.LoadEnd += (sender, args) => GD.Print("页面加载完成");
browser.AddressChanged += (sender, url) => GD.Print("URL: " + url);
browser.TitleChanged += (sender, title) => GD.Print("标题: " + title);
browser.LoadError += (sender, args) => GD.PrintErr("加载失败: " + args.ErrorText);
```

### GDScript 信号

```gdscript
browser.browser_initialized.connect(_on_ready)
browser.load_end.connect(_on_done)
browser.address_changed.connect(_on_address_changed)

func _on_ready():
    print("浏览器已就绪")
    browser.eval_js("console.log('Hello from Godot!')")
```

---

## JS ↔ C# 桥接

### 注册 C# 对象（Plugin）

注册任意 C# 对象，JavaScript 可直接调用其方法：

```csharp
public class MyBridge
{
    public string Hello(string name) => $"Hello, {name}!";
    public int Add(int a, int b) => a + b;
}

// 注册
browser.RegisterJavascriptObject(new MyBridge(), "myBridge");
```

**JavaScript 调用：**
```javascript
window.myBridge.hello("World").then(r => console.log(r)); // "Hello, World!"
window.myBridge.add(2, 3).then(r => console.log(r));      // 5
```

### 注册 GDScript 处理器（GDExtension）

```gdscript
browser.register_js_handler("dotnetBridge", Callable(self, "_on_js_call"))

func _on_js_call(method_name: String, args_json: String) -> Variant:
    match method_name:
        "hello":
            return "Hello from GDScript!"
        "add":
            var arr = JSON.parse_string(args_json) as Array
            return int(arr[0]) + int(arr[1])
```

### 宿主推送消息到 JS

```csharp
// C#
browser.SendToJs("{\"type\":\"update\",\"payload\":{\"count\":42}}");
```

```gdscript
# GDScript
browser.send_to_js('{"type":"update","payload":{"count":42}}')
```

**JS 端接收：**
```javascript
window.__hostBridge._onMessage = function(msg) {
    console.log("收到宿主消息:", msg);
};
```

### 执行 JS 并获取返回值

```csharp
// C#（异步，返回 Task<T>）
var title = await browser.EvaluateJavaScript<string>("document.title");
var count = await browser.EvaluateJavaScript<int>("document.querySelectorAll('a').length");
```

```gdscript
# GDScript（异步，通过信号获取结果）
browser.eval_js("document.title")
# 结果在信号中接收：
func _on_eval_done(result: String, error: String):
    if error.is_empty():
        print("JS 结果: ", result)
```

---

## IME 输入法（中文/日文/韩文）

IME 由 JS 焦点监视器自动管理：

- **点击输入框** → JS 检测到 `focusin` → 自动激活 IME
- **点击输入框外部** → JS 检测到 `focusout` → 自动关闭 IME
- **无需手动管理 IME**

OSR 和 EmbeddedWindow 两种模式均支持。

---

## 右键菜单

OSR 模式下，右键点击会显示 Godot `PopupMenu`，包含 CEF 默认菜单项（后退、前进、刷新、复制、粘贴、检查等）。

- **启用/关闭**：在 Inspector 中设置 `ContextMenuEnabled`
- **自定义**：订阅 `BeforeContextMenu` 事件修改菜单项

```csharp
browser.BeforeContextMenu += (model, params) => {
    model.Clear();
    model.AddItem(26500, "自定义功能");  // UserFirst = 26500
};
```

---

## 页面内查找

```csharp
// 开始搜索
browser.Find("搜索文本", forward: true, matchCase: false, findNext: false);

// 停止搜索
browser.StopFinding(clearSelection: true);

// 处理结果
browser.FindResult += (identifier, count, activeMatchOrdinal, finalUpdate) => {
    GD.Print($"找到 {count} 个匹配，当前: {activeMatchOrdinal}");
};
```

---

## 导航与开发者工具

```csharp
browser.GoBack();                            // 后退
browser.GoForward();                         // 前进
browser.NavigateToUrl("https://example.com"); // 导航到 URL
browser.Reload();                            // 刷新
browser.ShowDeveloperTools();                // 打开 DevTools
browser.CloseDeveloperTools();               // 关闭 DevTools
```

---

## 远程调试（Chrome DevTools Protocol）

CEF 可以开放一个 CDP 端点，让外部工具连接到内嵌浏览器。该功能**默认关闭**，由**唯一一个** Godot 项目设置控制。

### 开启

把项目设置 `gdcefglue/remote_debugging_port` 设成 **1024–65535** 之间的端口（例如 `9222`）。`0` 表示关闭。

| 构建形态 | 如何让该设置出现在「项目设置」面板里 |
|---------|--------------------------------------|
| **Plugin（C#）** | 插件包内附一个可选的编辑器插件。在 *项目设置 ▸ 插件* 里启用 **GDCefGlue Project Settings**，即可在项目设置中找到它。**必须先以 Debug 构建**，否则插件会报 `Unable to load addon script`。 |
| **GDExtension** | 由扩展自动注册，无需启用任何插件。 |

这是一项调试专用设置，因此请打开「项目设置」右上角的**高级设置**开关，或直接在搜索框里输入 `gdcefglue`。启用该插件是**严格增量**的 —— 不启用时 `CefGlueControl` 的行为与之前完全一致。

也可以直接编辑 `project.godot`：

```ini
[gdcefglue]

remote_debugging_port=9222
```

修改后**需要重启**：CEF 每个进程只初始化一次。

### 连接

```bash
# 连通性自检 —— 两条都应返回 JSON
curl http://127.0.0.1:9222/json/version
curl http://127.0.0.1:9222/json/list
```

- Google Chrome 的 **chrome://inspect**
- **Puppeteer** —— `puppeteer.connect({ browserURL: 'http://127.0.0.1:9222' })`，然后 `await browser.pages()`
- **Playwright** —— `chromium.connectOverCDP('http://127.0.0.1:9222')`，然后 `browser.contexts()[0].pages()`

### 必须知道的限制

1. **`http://127.0.0.1:9222/` 是空白页。** vanilla CEF 构建没有把 DevTools 发现页资源编进去。端点本身是正常的 —— 请改用 `chrome://inspect` 或 `/json/list`。
2. **一个端口对应多个浏览器。** CEF 每进程**只有一个** CDP 端点，所以场景里的每个 `CefGlueControl` 都表现为 `/json/list` 里一个独立的 *target*。**无法给每个浏览器分配各自的端口**；请按 `title` / `url` 选择目标。
3. **无法给 target 打标签。** CEF 没有让宿主设置 target 名称的 API（`window_name` 不会暴露，`description` 为空）。要区分多个标签页，请设置有辨识度的 `document.title`，或在 URL 里加标记（如 `#gc-tab=3`）再按它匹配。
4. **`/json/new` 不支持。** 它走的是 Chrome 的标签页基础设施而不是 `CefBrowserHost::CreateBrowser`，因此不会经过你的 `CefLifeSpanHandler`，会留下不受管理的浏览器。它同时还是一个免鉴权的脚本执行面（`/json/new?javascript:...`）。请用自己的代码创建浏览器，客户端会把它们识别为新的 target。
5. **OSR 的 target `type`。** 当前 CEF 构建下离屏浏览器上报 `type: "page"`，但历史上出现过变化。如果 Selenium/ChromeDriver 找不到你的浏览器，请在 `/json/list` 里确认 `type` 字段的实际值。
6. **仅本机可访问、无鉴权。** 端点绑定在 `127.0.0.1` / `::1`，CEF **不读取** `--remote-debugging-address`。需要远程访问请用 SSH 隧道或反向代理。任何能连上该端口的人都能完全控制浏览器 —— **绝不要在发布构建里开启**。

---

## 平台注意事项

### Windows

- **EmbeddedWindow**：使用 Win32 子 HWND。`WS_EX_NOACTIVATE` 防止抢焦点。
- **CEF 文件**：构建时自动复制所有 DLL。
- **Locales**：确保 `locales/` 目录在可执行文件旁边。

### Linux

- **EmbeddedWindow**：⚠️ Linux 上嵌入式模式不完整。仅窗口模式可用，嵌入模式在容器内无法正常工作。**Linux 上暂时推荐 OSR 模式**。完整嵌入式支持为后续实现。
- **依赖**：安装 `libxkbcommon-x11-dev` 以支持键盘。
- **AOT 构建**：需要 `clang` 和 `zlib1g-dev`。

### macOS

- **EmbeddedWindow**：使用 Cocoa NSView。
- **AOT 构建**：需要 Xcode Command Line Tools。
- **公证**：分发的 CEF 二进制文件可能需要签名。

---

## 故障排除

| 问题 | 解决方案 |
|------|---------|
| **空白页面** | 检查 `locales/` 目录和 `resources.pak` 是否存在 |
| **GPU 崩溃** | `EnableGpuAcceleration` 为实验性功能，暂不可用，后续实现。默认保持禁用。 |
| **缺少 DLL** | 运行 `dotnet restore` 并重新构建 |
| **IME 无法切换** | 确认 `__hostFocus` V8 对象已注册（检查调试输出） |
| **右键无反应** | 在 Inspector 中设置 `ContextMenuEnabled = true` |
| **WSALookupServiceBegin 错误** | 正常 Windows 警告，忽略 |

---

## 项目结构

```
addons/GCefGlue/          ← 插件（C#）
├── CefGlueControl.cs     ← 浏览器控件节点
├── CefInitializer.cs     ← CEF 启动
└── Handlers/             ← CEF 事件处理器

addons/gdcefglue/         ← GDExtension（NativeAOT）
├── gdcefglue.gdextension ← 扩展配置
├── windows-x64/          ← Windows 二进制
├── linux-x64/            ← Linux 二进制
└── macos-arm64/          ← macOS 二进制
```

---

## 从源码构建

详见主 README 的[构建流程](README_CN.md#构建流程)章节。