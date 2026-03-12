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

## 环境要求

- **Godot Engine**: 4.6.0 或更高版本（需要 .NET/Mono 支持）
- **.NET SDK**: 8.0 或更高版本
- **Windows**: x64 架构

## 依赖方式

### 方式一：NuGet 包（推荐）

最简单的使用方式。构建时自动复制所有必要文件。

**CEF 134（推荐）：**

```xml
<ItemGroup>
  <PackageReference Include="CefGlue.Common" Version="134.6998.178" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="134.3.9" />
</ItemGroup>
```

**CEF 120（官方）：**

```xml
<ItemGroup>
  <PackageReference Include="CefGlue.Common" Version="120.6099.0" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="120.2.3" />
</ItemGroup>
```

### 方式二：从源码构建

如果需要最新版本或自定义 CefGlue：

**CEF 134（推荐）：**

```bash
git clone https://github.com/youfch/CefGlue.git
```

**CEF 120（官方）：**

```bash
git clone https://github.com/OutSystems/CefGlue.git
```

将克隆的仓库放置到项目目录：

```
你的项目/
├── GDCefGlue/
│   ├── GDExtension/
│   │   ├── Extension/     # C# GDExtension 源代码
│   │   └── Project/       # Godot 测试项目
│   ├── NormalProject/     # 普通 C# 项目示例
│   ├── img/
│   └── README*.md
└── CefGlue/               # 克隆的仓库
```

## 项目类型

### GDExtension 项目

位于 `GDExtension/` 目录。适用于 Godot 4.6+。

**特点：**

- 使用 Godot 的 GDExtension 系统
- 编译为原生 AOT 库
- 性能更好，文件体积更小
- 导出时需要手动复制 CEF 文件
- 入口点：`gdcefglue_library_init`

**结构：**

- `GDExtension/Extension/` - GDExtension 的 C# 源代码
- `GDExtension/Extension/Dll/` - Godot .NET 绑定（来自 [godot-dotnet](https://github.com/raulsntos/godot-dotnet)）
- `GDExtension/Project/` - 用于测试的 Godot 项目

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
   将生成的 `Godot.Bindings.dll` 及相关文件复制到 `GDExtension/Extension/Dll/`。
2. **CEF 依赖（跨平台）：**
   - **Windows：** 通过 NuGet 包自动获取 `chromiumembeddedframework.runtime.win-x64`
   - **Linux：** 需要添加 [cef.redist.linux](https://github.com/OutSystems/cef.redist.linux) 依赖
   - **macOS：** 需要添加 [cef.redist.osx](https://github.com/OutSystems/cef.redist.osx) 依赖
   查看 [CefGlue 仓库](https://github.com/youfch/CefGlue) 了解如何添加跨平台依赖。
3. **构建 GDExtension：**

   进入 `GDExtension/Extension` 目录执行：

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

   将 publish 目录中的所有文件复制到 `GDExtension/Game/lib/` 目录。
5. **运行：**
   使用 Godot 4.6 打开 `GDExtension/Game/` 项目并运行。

**不同 CEF 版本支持：**

如需使用不同版本的 CEF，有两种方式：

1. **NuGet 包方式（推荐）：** 修改 `.csproj` 中的 NuGet 包版本
   ```xml
   <PackageReference Include="CefGlue.Common" Version="xxx.xxxx.x" />
   <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="xxx.x.x" />
   ```
2. **手动编译：** 从 [CefGlue 仓库](https://github.com/youfch/CefGlue) 下载对应 CEF 版本的源码，手动编译 NuGet 包后引用。

### 普通 C# 项目

位于 `NormalProject/` 目录。传统的 Godot C# 项目方式。

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
| youfch/CefGlue     | 134    | 非官方（推荐） | `CefGlue.Common 134.6998.178` | [GitHub](https://github.com/youfch/CefGlue)     |
| OutSystems/CefGlue | 120    | 官方      | `CefGlue.Common 120.6099.0`   | [GitHub](https://github.com/OutSystems/CefGlue) |

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
# 克隆 CefGlue（选择一个）
git clone https://github.com/youfch/CefGlue.git   # CEF 134
# 或
git clone https://github.com/OutSystems/CefGlue.git  # CEF 120

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

## CefGlueControl 方法

| 方法                          | 描述        |
| --------------------------- | --------- |
| `GoBack()`                  | 后退        |
| `GoForward()`               | 前进        |
| `NavigateToUrl(string url)` | 导航到指定 URL |
| `Refresh()`                 | 刷新当前页面    |

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

