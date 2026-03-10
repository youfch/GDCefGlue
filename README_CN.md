# GDCefGlue

基于 CefGlue 的 Godot 4.x CEF 浏览器控件。

[English](README.md)

## 功能特性

- GPU 硬件加速支持
- 中文/日文/韩文输入法支持
- 弹窗处理
- 完整的键盘和鼠标支持
- 易于集成到 Godot 4.x

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
└── CefGlue/          # 克隆的仓库
```

更新 `.csproj` 引用项目：
```xml
<ItemGroup>
  <ProjectReference Include="..\CefGlue\CefGlue\CefGlue.csproj" />
  <ProjectReference Include="..\CefGlue\CefGlue.Common\CefGlue.Common.csproj" />
  <ProjectReference Include="..\CefGlue\CefGlue.Common.Shared\CefGlue.Common.Shared.csproj" />
</ItemGroup>
```

### CefGlue 来源

| 来源 | CEF 版本 | 状态 | NuGet | GitHub |
|------|----------|------|-------|--------|
| youfch/CefGlue | 134 | 非官方（推荐） | `CefGlue.Common 134.6998.178` | [GitHub](https://github.com/youfch/CefGlue) |
| OutSystems/CefGlue | 120 | 官方 | `CefGlue.Common 120.6099.0` | [GitHub](https://github.com/OutSystems/CefGlue) |

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

使用源码依赖时，导出后需要手动复制文件。详见 [CEF_EXPORT_GUIDE.md](CEF_EXPORT_GUIDE.md)。

## CefGlueControl 属性

| 属性 | 类型 | 默认值 | 描述 |
|------|------|--------|------|
| `InitialUrl` | string | "about:blank" | 浏览器创建时加载的 URL |
| `OpenPopupInCurrentBrowser` | bool | true | 如果为 true，弹窗在当前浏览器中导航 |
| `GpuAcceleration` | bool | true | 如果为 true，启用 GPU 硬件加速 |

## CefGlueControl 方法

| 方法 | 描述 |
|------|------|
| `GoBack()` | 后退 |
| `GoForward()` | 前进 |
| `NavigateToUrl(string url)` | 导航到指定 URL |
| `Refresh()` | 刷新当前页面 |

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
