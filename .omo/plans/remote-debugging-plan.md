# 远程调试 (Chrome DevTools Protocol) 方案

> 版本: 1.0
> 日期: 2026-09-16
> 状态: 已定稿，待执行
> 目标版本: CEF 149 (CefGlue 149.7827.156) / Godot 4.6+
> 上游线索: `E:\Work\Hub\FlightSimulationPart2\src\g\addons\GCefGlue\CefInitializer.cs`（CEF 120 下游分支，含环境变量版实现）

---

## 1. 目标

为 GDCefGlue 增加 **Chrome DevTools Protocol 远程调试**能力：开启后，外部工具（`chrome://inspect`、Puppeteer、Playwright、Selenium、`curl`）可通过 `http://127.0.0.1:<port>` 附加到进程内的 CEF 实例，**按选择器 / DOM 驱动页面**，而不必对离屏渲染（OSR）的画面做坐标点击。

**验收标准**：

- 默认（未开启）行为与现状**逐字节一致** —— 不监听任何端口
- 开启后 `curl http://127.0.0.1:<port>/json/version` 返回 JSON，`chrome://inspect` 可见目标
- **多个 `CefGlueControl` 实例共享同一个端点**，在 `/json/list` 中表现为多条 target
- 端口仅绑定 loopback，**不允许**任何形式的对外暴露

---

## 2. 现状

### 已具备

| 项 | 位置 | 说明 |
|---|---|---|
| `RemoteDebuggingPort` 设置项 | `plugin/addons/GCefGlue/CefInitializer.cs:68`<br>`extension/CefInitializer.cs:69` | **已预留但硬编码 `0`**，即关闭 |
| `CefInitializer.Initialize()` 幂等守卫 | `if (_initialized) return;` | 保证「一个进程只初始化一次 CEF」 |
| `OnBeforeCommandLineProcessing` 钩子 | `plugin/addons/GCefGlue/Handlers/GodotCefApp.cs:23`<br>`extension/GodotCefApp.cs:15` | 已有 browser 进程分支（`string.IsNullOrEmpty(processType)`） |
| 官方 Godot 绑定（扩展侧） | `extension/Dll/Godot.Bindings.dll` | `Godot Engine contributors` / `4.6.0-dev`，已暴露 `AddEditorPluginByType`、`EditorPlugin`、`ProjectSettings.AddPropertyInfo` 等 |

### 缺失

- 无任何开关、无注册、无文档
- `CacheDirectory` 的「per-node `[Export]` → static → 初始化」模式存在缺陷（见 3.3），**不复制**

### 上游分支（FlightSimulationPart2）的取舍

该目录是 CEF 120 的下游分支，其远程调试实现为环境变量 `GODOT_CEF_DEBUG_PORT`：

| 做法 | 本项目决定 |
|---|---|
| `ResolveRemoteDebuggingPort()` 读环境变量 | ❌ **否决**（见 3.2） |
| `port > 0 && port < 65536` 校验 | ⚠️ **收紧**为 `1024–65535`（见 3.4） |
| 未追加 `remote-allow-origins` | ⚠️ **补上**（见 3.5） |
| 其余 7 个文件（`GodotRenderHandler.cs` 等） | ⛔ **严禁同步** —— 那份是 CEF 120 旧线，会退化为空实现的 `OnAcceleratedPaint(..., IntPtr)`、复活已退役的 `godot://bridge` iframe 桥、回退 IME 修复 |

**只取用该分支的 `CefInitializer.cs` 中约 15 行的思路，其余全部保持本项目现状。**

---

## 3. 核心架构决策

### 3.1 端口必须是**进程级**，不可做成 per-node

CEF 每个进程只有一个 CDP 端点，源码级证据（见附录 10.1）：

| 层 | 事实 |
|---|---|
| CEF 设置 | `cef_settings_t.remote_debugging_port` 是**单个 int**；`CefBrowserSettings`（每浏览器那份）**没有端口字段** |
| CEF 公开 API | `CefBrowserHost` 只有 `ShowDevTools` / `SendDevToolsMessage` / `ExecuteDevToolsMethod`，**没有任何监听 socket 的 API** |
| Chromium | `DevToolsAgentHost::StartRemoteDebuggingServer()` → `SetDevToolsHttpHandler(...)`，**每进程一个 handler**（静态单例） |
| 端点 | `/json/version` 只返回**一个** browser 端点 |

**结论**：`[Export] int RemoteDebuggingPort` 写在 `CefGlueControl` 上是**语义错误** —— 只有第一个 `_Ready` 的节点会生效，其余静默忽略。**多个 `CefBrowser` 在同一个端口下表现为多条 target**，客户端靠 `id` / `title` / `url` 区分（见附录 10.2）。

### 3.2 配置源：Godot **项目设置**，不是环境变量、不是 Inspector 属性

| 候选 | 默认关闭 | 不进发布包 | 可版本控制 | 语义匹配（全局唯一） | 结论 |
|---|:---:|:---:|:---:|:---:|---|
| 环境变量 | ✅ | ✅ | ❌ | ✅ | ❌ 不可发现、须在启动编辑器前设置 |
| Inspector `[Export]` | ✅ | ❌ 会写进 `.tscn` | ✅ | ❌ per-node 错配 | ❌ 且会误导用户 |
| **Godot 项目设置** | ✅ | ✅ | ✅ | ✅ | ✅ **采用** |
| `CefInitializer` 静态属性 | ✅ | ✅ | ❌ | ✅ | 可作为附加代码入口，非本次范围 |

**设置键**：`gdcefglue/remote_debugging_port`（`int`，`0` = 关闭）

### 3.3 读取点：`CefInitializer.Initialize()`，**不在任何节点里**

- 项目设置在引擎启动时已加载，**不依赖节点 `_Ready` 顺序**
- 与 `CacheDirectory` 的缺陷形成对比：后者是 per-node `[Export]` 在 `_Ready` 里推给 static，**第二个节点改了无效且无警告**。本方案不复制该模式

### 3.4 端口范围硬约束 `1024–65535`

CEF 仅在 `1024 ≤ port ≤ 65535` 时把该值翻译成 `--remote-debugging-port`（`chrome_main_delegate_cef.cc` 的 `BasicStartupComplete`）。越界值**会被静默忽略 —— 服务根本不启动**。因此必须：

- `0` → 明确表示关闭，直接返回
- 非 `0` 但越界 → **`GD.PushWarning` 告警**并关闭（不能静默）

> 备注：走设置项时 `0` 不产生临时端口；如需临时端口须显式传 `--remote-debugging-port=0` 并读 `<cache-dir>/DevToolsActivePort`。本方案不涉及。

### 3.5 必须追加 `remote-allow-origins`

Chromium M111+ 起，DevTools HTTP handler 会**拒绝携带 `Origin` 头的 WebSocket 升级**（返回 403），而 CEF **不会**自动放行（`chromiumembedded/cef#3740` 已 WontFix）。

- 不带 `Origin` 的客户端（Puppeteer / Playwright / `curl` / `/json/list`）**本就不受影响**
- 带 `Origin` 的客户端（Web 版 DevTools 前端、Selenium 旧版 Netty 客户端）**会吃 403**

**决定**：仅当端口生效时，在 **browser 进程分支**内追加 `commandLine.AppendSwitch("remote-allow-origins", "*")`。理由是调试端点仅 loopback、默认关闭，暴露面增量为零；且发布构建完全不受影响。

### 3.6 面板注册按构建分叉

| 构建 | 编辑器加载钩子 | 决定 |
|---|---|---|
| **GDExtension**（`addons/gdcefglue/`） | ✅ `.dll` 也被编辑器进程加载 | **做注册** —— 扩展 init 时若 `OS.HasFeature("editor")` 则 `SetSetting` → `AddPropertyInfo` → `SetInitialValue` → `SetRestartIfChanged` |
| **纯 C# addon**（`addons/GCefGlue/`，无 `plugin.cfg`） | ❌ 无受支持的钩子（`[Tool]` 需实例、static ctor 不触发、`[ModuleInitializer]` 无文档且可能在引擎就绪前跑） | **不注册**，仅文档化 key（在 Project Settings 里搜索或开 Advanced 可见，类型猜为 int、无范围提示）。可选：Phase 4 提供一个**可单独启用**的小插件包 |

**两侧都不调用 `SetAsBasic`** —— 调试后门藏在 "Advanced Settings" 之后是**特性而非缺陷**，这样两种构建的用户体验也趋于一致。

> ⚠️ 注册顺序**不可颠倒**：`_add_property_info_bind` 内部有 `ERR_FAIL_COND(!props.has(pinfo.name))`，**设置必须先存在**，否则 hint 被静默丢弃。

---

## 4. 能力覆盖矩阵

| 能力 | GDExtension | 纯 C# addon |
|---|:---:|:---:|
| 读取 `gdcefglue/remote_debugging_port` | ✅ Phase 1 | ✅ Phase 1 |
| 端口越界告警 + 默认关闭 | ✅ Phase 1 | ✅ Phase 1 |
| `remote-allow-origins`（端口生效时） | ✅ Phase 2 | ✅ Phase 2 |
| Project Settings 面板带范围提示 | ✅ Phase 3 | ⚠️ Phase 4（需另启小插件） |
| 无面板时的 key 可见性 | ✅ 搜索/Advanced | ✅ 搜索/Advanced |
| 多实例 → 单一端点、多 target | ✅ | ✅ |
| 仅 loopback / 默认关闭 | ✅ | ✅ |

---

## 5. 分阶段路线图

### Phase 1（P0 — Small）: 设置读取 + 端口校验

**目标**：把已预留的 `RemoteDebuggingPort = 0` 接上项目设置，含严格校验与告警。

**操作**：

1. 两个 `CefInitializer.cs` 各新增静态只读属性（沿用文件内已有的 `UseExternalMessageLoop { get; private set; }` 模式）：

   ```csharp
   /// <summary>
   /// 已生效的 CEF 远程调试端口（Chrome DevTools Protocol）。0 = 关闭。
   /// 必须是进程级单一值：CEF 每进程只有一个 CDP 端点，多个 CefBrowser
   /// 在该端点下表现为多个 target，无法各自占用端口。
   /// 修改此项需重启（CEF 仅初始化一次）。
   /// </summary>
   public static int RemoteDebuggingPort { get; private set; }
   ```

2. `ResolveRemoteDebuggingPort()`：读 `ProjectSettings`，`0` → 返回 0；`int.TryParse` 失败或越界 → `GD.PushWarning` + 返回 0；成功 → `GD.Print` 并返回。

3. `Initialize()` 中在**构造 `settings` 之前**赋值（`OnBeforeCommandLineProcessing` 是在 `CefRuntime.Initialize()` 内部被回调的，顺序不能反），并把 `RemoteDebuggingPort = 0` 改为 `RemoteDebuggingPort = RemoteDebuggingPort`。

**注意（两侧 API 差异）**：

| 构建 | 访问方式 |
|---|---|
| plugin | `ProjectSettings.GetSetting(...)`（静态，同 `CefInitializer.cs:48` 的 `ProjectSettings.GlobalizePath`） |
| extension | `ProjectSettings.Singleton.GetSetting(...)`（实例，同 `extension/CefInitializer.cs:37`） |

**验证**：不设 key 时 `netstat -ano | findstr 9222` 为空；设 `9222` 后 `curl /json/version` 返回 JSON；设 `80` 打出告警且端口未开。

**工作量**：≈ 0.5 天

---

### Phase 2（P0 — Small）: `remote-allow-origins`

**目标**：让带 `Origin` 的 DevTools 客户端也能连上。

**操作**：两个 `GodotCefApp.cs` 的 `if (string.IsNullOrEmpty(processType))` 分支内（plugin `:58` / extension `:50`）：

```csharp
if (CefInitializer.RemoteDebuggingPort > 0)
{
    // Chromium M111+ 拒绝携带 Origin 头的 DevTools WebSocket 连接，
    // CEF 不会自动放行（chromiumembedded/cef#3740 已 WontFix）。
    // 不带 Origin 的 Puppeteer/Playwright/curl 本就不受影响；
    // 此举为放行 Web 版 DevTools 前端等客户端。
    // 仅调试端口开启时追加 —— 发布构建不受影响。
    commandLine.AppendSwitch("remote-allow-origins", "*");
}
```

**验证**：`chrome://inspect` 能附加；Puppeteer `connect({ browserURL })` 成功。

**工作量**：≈ 0.5 天

---

### Phase 3（P0 — Spike）: 扩展侧面板注册原型 ⚠️ 唯一的实质不确定性

**目标**：确认 GDExtension 能否让设置在 Project Settings 面板里带范围滑块出现。

**背景**：`extension/Dll/Godot.Bindings.dll` 已确认存在 `AddEditorPluginByType`、`EditorPlugin`、`EditorInterface`、`GDExtensionInterfaceEditorAddPlugin`、`ProjectSettings.AddPropertyInfo`。但 **godot-dotnet 的 NativeAOT flavor 对 editor 类别（`EditorPlugin`）的支持是新领域**，需先验证。

**最简路径（优先尝试，无需 EditorPlugin）**：在现有 `extension/Main.cs` 的初始化流程中，若 `OS.HasFeature("editor")` 则直接注册 —— 因为扩展 `.dll` 也被编辑器进程加载，调用会落到编辑器的 `ProjectSettings` 单例上。

```csharp
const string Key = "gdcefglue/remote_debugging_port";
if (!ProjectSettings.Singleton.HasSetting(Key))
    ProjectSettings.Singleton.SetSetting(Key, 0);          // 必须先建，否则下一步 ERR_FAIL
ProjectSettings.Singleton.AddPropertyInfo(new Godot.Collections.Dictionary
{
    { "name",        Key },
    { "type",        (int)VariantType.Int },               // 不要传 "usage"，4.5+ 会告警
    { "hint",        (int)PropertyHint.Range },
    { "hint_string", "0,65535,1" },
});
ProjectSettings.Singleton.SetInitialValue(Key, 0);
ProjectSettings.Singleton.SetRestartIfChanged(Key, true);
```

**兜底路径**：若注册时机晚于对话框构建导致不显示，改用 `AddEditorPluginByType<T>()`（等价于 C++ 的 `EditorPlugins::add_by_type`）+ 一个 `EditorPlugin` 子类，在其 `_EnterTree` 中注册。此时需给 `Main.cs` 增加 `InitializationLevel.Editor` 分支（当前只有 `SetMinimumLibraryInitializationLevel(InitializationLevel.Scene)` + `if (level != Scene) return;`）。

**验证**：编辑器中打开 Project Settings → 搜索 `gdcefglue` → 出现带范围滑块的 int 项。

**工作量**：≈ 0.5 天（spike）

---

### Phase 4（P1 — Small）: 扩展侧注册正式化

**操作**：按 Phase 3 的结论固化实现；必要时把注册逻辑独立成文件（如 `extension/EditorSettingsRegistration.cs`），避免污染 `Main.cs`。

**工作量**：≈ 0.5 天

---

### Phase 5（P2 — Optional）: 纯 C# addon 的可选编辑器插件包

**目标**：让想让 C# 构建也获得面板效果的用户，**不改变核心分发形态**。

**做法**：单独发行一个可独立启用的插件目录（如 `addons/GCefGlueEditorSettings/`，含自己的 `plugin.cfg` + 数行 `[Tool] EditorPlugin`），在其 `_EnterTree` 中执行与 Phase 4 相同的注册序列。启用与否完全由用户决定；核心 `GCefGlue` 节点包**保持零 `plugin.cfg`**。

**先决条件**：仅在用户明确要求面板效果时再做（YAGNI）。

**工作量**：≈ 0.5 天

---

### Phase 6（P1 — Small）: 文档

**操作**：

1. `doc/USER_GUIDE.md` + `doc/USER_GUIDE_CN.md` 新增 "Remote Debugging" 小节，必须包含以下 4 条（见附录 10.2 / 10.3）
2. `doc/BRIDGE_TODO.md` 的「计划功能」表保持单行索引，指向本文件
3. `README{,_CN}.md` 的 Features 列表可加一行

**工作量**：≈ 0.5 天

---

## 6. 回退策略

```
读取 gdcefglue/remote_debugging_port
  ├── 缺失 / 0           → 端口关闭，行为与现状逐字节一致（默认路径）
  ├── 非整数 / 越界       → GD.PushWarning，端口关闭
  └── 1024..65535        → 启用
        ├── remote-allow-origins="*" 追加到 browser 进程命令行
        └── 端口被占用      → CEF 无端口回退，服务静默不启动（见风险清单）
```

**关键原则**：任何异常路径都必须**安全地退化为「关闭」**，绝不出现「以为关了但实际在监听」。

---

## 7. 风险清单

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| **OSR 的 target `type` 可能是 `other` 而非 `page`** | Selenium / ChromeDriver 只选 `page` 目标，会选不中（历史上 M125 OSR 出现过 `other`） | CEF 有 alloy patch 使其报 `page`；**Phase 1 实测记录实际值**，并在文档写明；对外承诺前必须验证 |
| **`/json/new` 产生孤儿浏览器** | CEF 走 Chrome 的 tab 路径（`NEW_FOREGROUND_TAB`），**不经过 `GodotLifeSpanHandler`**，收不到 `OnAfterCreated`、拿不到可管理的 `CefBrowser`；且 `PUT /json/new?javascript:...` 是已知免鉴权脚本执行面 | 文档标记**不支持**；客户端应附加到已有浏览器，新标签由本项目自己的 API 创建 |
| **端口占用无回退** | `CreateLocalHostServerSocket` 依次试 `127.0.0.1` / `::1` 后返回 `nullptr`，服务静默不启动 | 文档提示；可选在启用时打印启动后 `netstat` 自检提示 |
| **修改设置需重启** | CEF 仅 `Initialize()` 一次，改 key 当次不生效 | `SetRestartIfChanged(true)` + 文档说明 |
| **`AddEditorPluginByType` 在 NativeAOT 下可能不可用** | Phase 3/4 面板注册失败 | Phase 3 先做 spike，失败则降级为「与纯 C# 一致：仅文档化 key」 |
| **纯 C# addon 无面板注册** | 用户体验不一致 | 文档说明可手工加 key；Phase 5 提供可选小插件 |
| **CDP 无鉴权** | 能连上端口 = 完全控制浏览器 | 仅 loopback（Chromium 硬编码）；默认关闭；文档强调勿用于生产 |
| **误信 `--remote-debugging-address` 能对外开放** | 用户以为能远程访问而实际不行 | 文档明确：CEF 路径不读该开关，需走 SSH 隧道 / 反向代理 |
| **同步上游 7 个文件导致退化** | `OnAcceleratedPaint` 空实现废掉 D3D12 GPU 通路；复活 iframe 桥 | 计划中显式列为「严禁」；实施时只改 4 个文件 |

---

## 8. 工作量汇总

| Phase | 内容 | 优先级 | 工作量 | 风险 |
|:---:|---|:---:|:---:|:---:|
| 1 | 设置读取 + 端口校验 | P0 | 0.5 天 | 低 |
| 2 | `remote-allow-origins` | P0 | 0.5 天 | 低 |
| 3 | 扩展侧注册 spike | P0 | 0.5 天 | **中**（NativeAOT editor 支持未知） |
| 4 | 扩展侧注册正式化 | P1 | 0.5 天 | 低 |
| 5 | 纯 C# addon 可选插件包 | P2 | 0.5 天 | 低 |
| 6 | 文档 | P1 | 0.5 天 | 低 |
| | **核心（1+2+6）** | | **≈ 1.5 天** | |
| | **合计** | | **≈ 3 天** | |

---

## 9. 文件变更清单

### Phase 1（设置读取）

| 文件 | 位置 | 操作 |
|------|------|:----:|
| `CefInitializer.cs` | plugin + extension | 修改（新增静态属性 + `ResolveRemoteDebuggingPort()`，`RemoteDebuggingPort = 0` → 赋值） |

### Phase 2（allow-origins）

| 文件 | 位置 | 操作 |
|------|------|:----:|
| `GodotCefApp.cs` | plugin + extension | 修改（browser 进程分支内条件追加） |

### Phase 3 / 4（扩展侧注册）

| 文件 | 位置 | 操作 |
|------|------|:----:|
| `Main.cs` | extension | 修改（注册调用；兜底路径需加 `InitializationLevel.Editor` 分支） |
| `EditorSettingsRegistration.cs` | extension | 新增（可选，注册逻辑独立） |

### Phase 5（可选插件包）

| 文件 | 位置 | 操作 |
|------|------|:----:|
| `addons/GCefGlueEditorSettings/plugin.cfg` | 新增目录 | 新增 |
| `addons/GCefGlueEditorSettings/GCefGlueSettingsPlugin.cs` | 新增目录 | 新增 |

### Phase 6（文档）

| 文件 | 位置 | 操作 |
|------|------|:----:|
| `USER_GUIDE.md` / `USER_GUIDE_CN.md` | doc | 修改（新增 Remote Debugging 小节） |
| `BRIDGE_TODO.md` | doc | 修改（计划功能表加一行索引） |
| `README.md` / `README_CN.md` | 根 | 修改（Features 加一行） |

**严禁改动**：`GodotRenderHandler.cs`、`GodotRequestHandler.cs`、`GodotLifeSpanHandler.cs`、`CefGlueControl.Properties.cs`、`CefGlueControl.Input.cs`、`CefGlueControl.Navigation.cs`、`CefGlueControl.Bridge.cs`（上游分支为 CEF 120 旧线，同步会导致退化）

---

## 10. 附录

### 10.1 CEF 进程级端口的源码证据

| 层 | 证据 |
|---|---|
| 设置 | `cef_settings_t.remote_debugging_port` 为单个 `int`；`_cef_browser_settings_t` 无端口字段 |
| 公开 API | `CefBrowserHost`：`ShowDevTools` / `SendDevToolsMessage` / `ExecuteDevToolsMethod` / `AddDevToolsMessageObserver`，均非监听 |
| Chromium | `DevToolsAgentHost::StartRemoteDebuggingServer()` → `SetDevToolsHttpHandler(make_unique<DevToolsHttpHandler>(...))`；`StopRemoteDebuggingServer()` → `SetDevToolsHttpHandler(nullptr)` |
| 端口翻译 | `chrome_main_delegate_cef.cc` 的 `BasicStartupComplete`：仅 `1024 ≤ port ≤ 65535` 时 `AppendSwitchASCII("remote-debugging-port", ...)` |
| 绑定 | `CreateLocalHostServerSocket` 依次试 `127.0.0.1` → `::1`，失败返回 `nullptr`（默认模式无端口回退） |
| 发现页 | Chrome bootstrap 下 `http://127.0.0.1:<port>/` 为**空白页**（未编译发现页资源，已知 WontFix）；`/json/list` 与 `chrome://inspect` 正常 |

### 10.2 CDP 端点与多实例选择

`GET /json/list` 每条 target 的字段（`SerializeDescriptor`）：

```
id, parentId?, type, title, description, url,
faviconUrl?, webSocketDebuggerUrl (ws://host/devtools/page/<id>), devtoolsFrontendUrl
```

- **列表按最后活动时间排序 —— 不得按数组下标选择**
- `type`：`page` / `other` / `tab` / `browser`…
- `title` = 页面 `<title>`（文档标题），**不是** CEF `SetAsChild` / `SetAsPopup` 的 `window_name`
- `description`：CEF / Chrome 对普通页面返回**空串**
- **没有 `CefBrowser` ↔ target id 的映射 API**，id 是懒生成的不透明 GUID

**客户端选择方式**：

```js
// Puppeteer
const browser = await puppeteer.connect({ browserURL: 'http://127.0.0.1:9222' });
const pages = await browser.pages();
const tab = pages.find(p => p.url().includes('tab=B'));

// Playwright
const browser = await chromium.connectOverCDP('http://localhost:9222');
const pages = browser.contexts()[0].pages();

// 裸 CDP（浏览器端点 + flat session）
// Target.setDiscoverTargets {discover:true} → Target.getTargets
// → Target.attachToTarget {targetId, flatten:true} → {sessionId}
```

**多标签识别建议**：无公开 API 可设置 target 标题，因此约定靠页面自标识 —— 在 `document.title` 注入前缀，或初始 URL 带 `#label=`，客户端按 `title` / `url` 匹配。

### 10.3 官方注册 API 与实际先例

**API**：`ProjectSettings.add_property_info`（`core/config/project_settings.cpp` 中绑定到脚本层，C# 形式为 `ProjectSettings.AddPropertyInfo(...)`）。

**顺序约束**：`_add_property_info_bind` 与 `set_custom_property_info` 均有 `ERR_FAIL_COND(!props.has(prop_name))` —— **必须先 `SetSetting` 建出该项，再 `AddPropertyInfo`**。

**`usage` key 自 Godot 4.5 起不再支持**（会 `WARN_PRINT`），改用 `SetAsBasic` / `SetRestartIfChanged` / `SetAsInternal`。

**视图控制**：

| 调用 | 效果 |
|---|---|
| （不调） | 藏在 **Advanced Settings** 后面 —— **本项目采用** |
| `SetAsBasic(name, true)` | 始终显示 |
| `SetAsInternal(name, true)` | 完全不在对话框中显示 |

**真实先例**（引用这些，而非 `Delsin-Yu/CSharp-Wrapper-Generator-for-GDExtension`）：

- `Atlinx/Godot-Mono-CustomResourceRegistry` → `Settings.cs` 中 `ProjectSettings.AddPropertyInfo(info)`（纯 C# addon + `plugin.cfg`）
- `NetCodersX/EasyInject.Godot` → `CoreSystemEditorPlugin.cs` 的 `_EnterTree()` → `AddProjectSettings()` → `AddPropertyInfo` → `ProjectSettings.Save()`
- Godot 官方 `ProjectSettings.xml` 文档自带 `EditorPlugin._enter_tree` 注册示例
- 原生侧：`editor_add_plugin` GDExtension 接口函数（PR #77010，`@since 4.1`）；**注意 PR #65592 的 `.gdextension` `editor_plugins` 数组至今未合并**，不要据此设计

**已排除的候选**：

- `Delsin-Yu/CSharp-Wrapper-Generator-for-GDExtension` —— 经查它注册的是 **`EditorSettings`**（Editor ▸ Editor Settings，per-user 且跨项目），全仓库唯一的 `ProjectSettings` 引用是 `GlobalizePath` 路径工具；且它不是 GDExtension（无 `.gdextension`、纯 Mono），对本项目两种构建都**不适用**
- 「像 Godot Jolt 那样注册」—— Jolt 4.5 是**内置模块**，用 C++ `GLOBAL_DEF` 宏（GDExtension / C# **均不可达**）；只有早期的 *godot-jolt GDExtension* 用过 `add_property_info`

### 10.4 参考链接

- CDP Target 域：<https://chromedevtools.github.io/devtools-protocol/tot/Target/>
- CDP HTTP 端点（`/json/*`）：<https://chromedevtools.github.io/devtools-protocol/>
- CEF 远程调试（无 `remote-allow-origins` 特殊处理）：<https://github.com/chromiumembedded/cef/issues/3740>
- CEF 多浏览器 → 多 target 论坛实证：<https://magpcss.org/ceforum/viewtopic.php?t=14279>
- CEF OSR `type: other`（旧版）：<https://www.magpcss.org/ceforum/viewtopic.php?f=6&t=19787>
- Godot `ProjectSettings.add_property_info` 文档与示例：<https://docs.godotengine.org/en/stable/classes/class_projectsettings.html>
- `editor_add_plugin` 接口（PR #77010）：<https://github.com/godotengine/godot/pull/77010>
- Puppeteer `ConnectOptions.browserURL`：<https://pptr.dev/api/puppeteer.connectoptions>
- Playwright `connectOverCDP`：<https://playwright.dev/docs/api/class-browsertype#browser-type-connect-over-cdp>
