# GDCefGlue JS-C# Bridge 演进 TODO

## 现状

当前 bridge 实现：`godot://bridge?type=X&cb=ID&payload=JSON` URL 拦截（`OnBeforeBrowse`）

- 方向：JS→C# 单向；C#→JS 靠 `ExecuteJavaScript` 回灌字符串
- 限制：
  - `OnBeforeBrowse` 无法读 POST body，payload 走 URL query（长度上限 ~2-64KB）
  - 每次 `sendToGodot` 新建 iframe，无复用，高频调用产生孤儿 DOM 节点
  - C#→JS 用字符串拼接 + 手写转义（`Replace("\\","\\\\")` x 5），有注入风险且每次触发 V8 parser
  - `BridgeRequest.Invoke` 在 CEF UI 线程同步执行，订阅者重活会卡渲染

## CEF 原生 6 种 JS↔Native 机制（官方 API 实证）

所有桥方案最终都建立在 CEF 底层 API 之上。CefGlue 完整暴露了以下 6 种机制的 C# 绑定。

| # | 机制 | 方向 | 同步/异步 | Payload 限制 | 线程 | 官方定位 |
|---|---|---|---|---|---|---|
| 1 | `CefMessageRouterBrowserSide`（`window.cefQuery`） | JS→C++ 双向 | 异步（支持 `persistent` 推送） | 默认字符串；CEF 145+ 支持 binary + `CefSharedMemoryRegion`（可绕开 ~128KB IPC 限制） | UI 线程 | **官方推荐首选** |
| 2 | `CefRegisterExtension` + `CefV8Handler` | JS→C++（可同步返回） | 同步返回 `retval`，但运行在 Render 进程 | 无限制（CefV8Value 直接传） | TID_RENDERER | 静态全局 API |
| 3 | 窗口绑定 `OnContextCreated` + `CefV8Value::CreateFunction` | JS→C++ 同步 | 同步 | 无限制 | TID_RENDERER | 每帧可变、有 DOM |
| 4 | `CefFrame::ExecuteJavaScript` | C++→JS | 异步无返回 | 无返回值 | Browser 任意线程 / Renderer 主线程 | 一句话注入 |
| 5 | `CefFrame::SendProcessMessage` + `OnProcessMessageReceived` | 跨进程双向 | 异步 | `CefListValue` ~128KB；`CefSharedMemoryRegion` 无限 | UI 线程 | 底层 IPC 基石 |
| 6 | `CefResourceRequestHandler` / `CefSchemeHandlerFactory` | 双向（HTTP 语义） | 异步 | 无限制（流式） | TID_IO | 大 payload、二进制 |

### 对你方案的映射

| 你当前的做法 | 对应 CEF 机制 | 问题 |
|---|---|---|
| `godot://` URL 拦截（`OnBeforeBrowse`） | 机制 5 的变体 | `OnBeforeBrowse` 无法读 POST body（机制 6 的 `OnBeforeResourceLoad` 才可以） |
| `iframe.src` 触发导航 | 机制 5（用 `SendProcessMessage` 语义） | 本质是"模拟导航发 IPC"，但多了 DOM 创建开销 |
| `ExecuteJavaScript` 回灌字符串 | 机制 4 的单向版 | 无返回值、每次 V8 parse、有注入风险 |
| 手搓 callback ID（`cb=ID`） | 机制 1 的 `cefQuery` 内建功能 | 官方 MessageRouter 已提供 callback ID 管理和 cancellation |

**结论**：你的方案在 CEF 原生坐标系里，把"应该用机制 1（MessageRouter）或机制 6（SchemeHandler）"的事情，用机制 5 的变体 + 机制 4 手搓实现了。这就是为什么主流库（CefSharp/CefGlue.Common/NanUI）都不走这条路。

## 跨生态定位

| 维度 | 当前方案 | CefSharp 推荐 | Tauri | Electron |
|---|---|---|---|---|
| JS->Native transport | URL 拦截 | V8Handler + IPC | 自定义 scheme + HTTP POST | ipcMain/ipcRenderer |
| Native->JS transport | ExecuteJavaScript 字符串注入 | EvaluateScriptAsync | HTTP response | webContents.send |
| 双向 round trip | 需两条通道 | IPC 单通道 | HTTP 单通道 | IPC 单通道 |
| Payload 限制 | URL 长度 | ~128MB IPC | 无限（流式） | Structured Clone |
| 类型化契约 | 无 | 反射 + Binder | #[command] 宏 | 无（手搓） |

## 演进路径对比

### 路径 A：Tauri 方案（SchemeHandler + POST）【推荐】

替换 `OnBeforeBrowse` 为 `OnBeforeResourceLoad` + `CefResourceHandler`，JS 侧改用 `fetch`。

收益：
- 一次性拿到双向通道（HTTP request/response）
- payload 走 POST body，无 URL 长度限制
- 支持二进制（`application/octet-stream`）
- JS 侧 `fetch` 返回 Promise，符合现代 JS 习惯
- wrymium 项目已实证可行（0.48ms round trip）

代价：
- `OnBeforeResourceLoad` 在 CEF IO 线程，不能阻塞，需改异步派发
- 需注册自定义 scheme（`CefRuntime.RegisterSchemeHandlerFactory`）

### 路径 B：CefMessageRouter（官方方案）

引入 CefGlue 自带的 `Wrapper/MessageRouter/`，JS 侧改用 `window.cefQuery`。

收益：
- 官方稳定，cancellation / persistent push / shared memory 大 payload 全部现成
- persistent query 支持 C# 主动推 JS

代价：
- 需转发 5 个生命周期回调（OnBeforeClose / OnBeforeBrowse / OnProcessMessageReceived / OnRenderProcessTerminated + renderer 侧 OnContextCreated/Released）
- payload 默认字符串，大 payload 要配置 message_size_threshold 启用 shared memory

### 路径 C：CefGlue.Common 对象绑定

引入 `CefGlue.BrowserProcess` + `CefGlue.Common` NuGet，用 `NativeObjectRegistry.Register`。

收益：
- 零 transport 代码，直接反射调用 C# 方法
- 等价于 CefSharp `BindObjectAsync`

代价：
- 增加 NuGet 依赖体积
- 只支持 JSON 字符串单参数（CefGlue.Common 限制）
- GDExtension AOT 场景需评估 trim 影响

### 不推荐：Electron 方案

等于手搓一遍 CefMessageRouter，且 CEF 无 contextBridge 隔离世界（Electron 核心卖点不可复现）。

## 架构总览

```
可用方案：iframe → godot:// OnBeforeBrowse（当前，唯一可行方案）
待解决：CefRuntime.RegisterExtension = False（NuGet 包不支持浏览器进程注册 V8）
```

### 当前限制

- `CefRuntime.RegisterExtension` 注册 V8 扩展失败（NuGet 版 CEF 不支持从浏览器进程注入）
- 无法使用 `CefMessageRouter`（需修改 BrowserProcess，独立项目不满足 addons 分发）
- 无法使用 `SchemeHandler + fetch`（file:// → ipc:// 跨域 CORS 死胡同）
- **唯一可行：iframe → OnBeforeBrowse 拦截**

### 关键关注点：iframe 垃圾回收

每次 `cefQuery` 调用会创建 iframe（`display:none`），当前方案 100ms 后自动移除。
高频调用场景下（如 mousemove 60fps）需确保：

- [ ] iframe 创建后及时从 DOM 移除（已在 BridgeScript 中 100ms setTimeout 清理）
- [ ] 页面跳转/卸载时 iframe 不残留（CEF 自动清理页面 DOM，安全）
- [ ] 高频调用时 iframe 不堆积（当前 100ms 窗口期内不会堆积超过 ~6 个 iframe）
- [ ] 考虑复用单个隐藏 iframe 代替每次创建（后续优化点）

### 渲染模式：双模式可选

```
CefGlueControl 新增属性 UseEmbeddedWindow : bool
  ├── false [默认] → OSR 模式（当前实现不变）
  └── true → 嵌入窗口模式
```

**OSR 模式（默认）**：
- `SetAsWindowless(IntPtr.Zero, Transparent)`
- `GodotRenderHandler.OnPaint` → Godot 纹理
- 事件由 Godot 原生处理，无需转发

**嵌入窗口模式（可选）**：
- `SetAsChild(hwnd)` → CEF 直接渲染到子 HWND
- 每帧 `GetGlobalRect()` → `SetWindowPos(cefHwnd, ...)` 位置同步
- 事件需 JS 监听转发（godot_wry 方案）

### 事件透传方案（仅嵌入模式需要）

参考 godot_wry 的 JS 事件转发模式：

```
CEF 区域内鼠标/键盘操作
  → 注入的 JS 监听 DOM 事件（mousedown/mousemove/keydown...）
  → 通过 IPC 通道发送到 C#（_mouse_down, _mouse_up, _key_down...）
  → C# 构造 Godot InputEventXXX
  → viewport.push_input(event) 推回 Godot 事件系统
```

事件转发 IPC 通道选型：

| 方案 | 延迟 | 适用场景 |
|---|---|---|
| 同一个 SchemeHandler + fetch | ~0.5ms | 低频事件（click, keydown） |
| `CefRegisterExtension` + V8Handler | ~0.01ms | 高频事件（mousemove 60fps） |
| `CefMessageRouter` | ~0.1ms | 中频事件 |

**推荐**：事件转发走 SchemeHandler 同一通道（实现简单，减少学习成本），如果实测 mousemove 延迟过高再优化到 V8Handler。

## 推荐路径：iframe bridge + cefQuery API TODO

### 当前方案已验证

- [x] `window.cefQuery(request, callback)` API 在 JS 端可用
- [x] iframe → OnBeforeBrowse 拦截 → BridgeRequest 事件
- [x] C# → JS 响应通过 ExecuteJavaScript（`__cefQueryResponse`）
- [x] 测试全部通过（ping/echo/100KB payload）
- [x] 每页自动注入桥脚本（LoadEnd 事件注入）
- [x] 向后兼容旧 `_godotBridge` 回调
- [x] DevTool 按钮

### 待优化

- [ ] iframe 复用：改用单个隐藏 iframe 反复设置 src，代替每次 createElement（减少 DOM 操作和 GC 压力）
- [ ] `CefRuntime.RegisterExtension = False` 持续关注 CefGlue 版本更新，未来可能支持
- [ ] 考虑 `SchemeHandler + fetch` 替代 iframe（需解决 file:// CORS，如用 `ipc://` 自加载页面）

## 验收标准

- [ ] 现有 ping/status/navigate 测试用例通过（`MainUi.cs`）
- [ ] 大 payload（>10KB JSON）不再报 URL 长度错误
- [ ] round trip 延迟 < 5ms（实测）
- [ ] iframe 不再被创建（改用 `fetch`）
- [ ] C#->JS 推送仍可用（`SendToJs` 保留或升级）
- [ ] Godot 控件覆盖在 CEF 区域上时，鼠标事件仍可穿透到 Godot 控件（事件转发验证）
- [ ] 鼠标在 CEF 区域外点击 → CEF 失去焦点
- [ ] LSP 诊断无新增 error
- [ ] Godot 编辑器运行无报错

## 参考实现

- **godot_wry**：https://github.com/doceazedo/godot_wry —— **核心参考**，嵌入窗口 + JS 事件转发 + IPC 的完整实现
  - `src/lib.rs`：JS 注入事件监听 + IPC handler + `push_input`
  - `src/godot_window.rs`：`DisplayServer.WindowGetNativeHandle` → HWND
  - 关键技巧：移除 `WS_CLIPCHILDREN`、坐标换算、`CURRENT_BUTTON_MASK` 状态机
- wrymium（Tauri+CEF 混合）：https://github.com/gxcsoccer/wrymium —— 用 `CefSchemeHandlerFactory` 实现 Tauri `invoke()`
- Chromely：https://github.com/chromelyapps/CefSharp —— 整个桥建立在 `CefSchemeHandlerFactory` 上
- CefSharp `ExampleResourceRequestHandler`：POST body 读取示例
- CefGlue 自带：`E:\Work\Hub\CefGlue\CefGlue\Classes.Handlers\CefResourceHandler.cs`

## 关键 API 索引

| 用途 | CefGlue 类 | 文件 |
|---|---|---|
| 注册 scheme | `CefRuntime.RegisterSchemeHandlerFactory` | `CefGlue\CefRuntime.cs` |
| 注册 scheme（App 钩子） | `CefApp.OnRegisterCustomSchemes` | `CefGlue\Classes.Handlers\CefApp.cs` |
| Factory 接口 | `CefSchemeHandlerFactory` | `CefGlue\Classes.Handlers\CefSchemeHandlerFactory.cs` |
| Response handler | `CefResourceHandler` | `CefGlue\Classes.Handlers\CefResourceHandler.cs` |
| Request 拦截 | `CefResourceRequestHandler.OnBeforeResourceLoad` | `CefGlue\Classes.Handlers\CefResourceRequestHandler.cs` |
| MessageRouter（备选） | `CefMessageRouterBrowserSide` | `CefGlue\Wrapper\MessageRouter\` |
| 对象绑定（备选） | `NativeObjectRegistry` | `CefGlue.Common\ObjectBinding\` |
