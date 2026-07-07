# GDCefGlue JS↔C# Bridge & Render Mode TODO

## Render Mode (已实现)

```
RenderMode 枚举:
  OSR = 0           ← [默认] 离屏渲染，支持真 alpha 透明
  EmbeddedWindow = 1 ← 嵌入 HWND 子窗口，性能更优，不支持透明
```

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Mode` | RenderMode | OSR | Inspector 中切换，OSR 时隐藏"Embedded Mode"分组 |
| `ForwardInputEvents` | bool | false | 嵌入模式下 JS→Godot 事件穿透（TODO，未完全实现） |
| `Transparent` | bool | false | 仅 OSR 模式生效 |

### Inspector 分组

```
▸ Browser Settings
  ├── InitialUrl
  ├── Mode
  ├── FrameRate
  └── Transparent

▸ Feature Toggles
  ├── GpuAcceleration
  ├── OpenPopupInCurrentBrowser
  └── SyncCursor

▸ Embedded Mode        ← 仅 Mode=EmbeddedWindow 时显示
  └── ForwardInputEvents
```

## 当前 Bridge 实现

`godot://bridge?type=X&cb=ID&payload=JSON` URL 拦截（`OnBeforeBrowse`）

- 方向：JS→C# 单向；C#→JS 靠 `ExecuteJavaScript` 回灌字符串
- 已验证：
  - [x] ping/status/navigate 测试用例通过（MainUi.cs / IpcDemo.tscn）
  - [x] 大 payload（~10KB）URL 编码传输
  - [x] C# 对象注册（`RegisterJavascriptObject`）暴露方法给 JS
  - [x] `EvaluateJavaScript<T>` 异步求值 + 超时
  - [x] DevTool 按钮

### 已知限制

- `OnBeforeBrowse` 无法读 POST body，payload 走 URL query（长度上限 ~2-64KB）
- 每次 `sendToGodot` 新建 iframe，无复用
- C#→JS 用字符串拼接 + 手写转义，有注入风险
- `BridgeRequest.Invoke` 在 CEF UI 线程同步执行

## CEF 原生 6 种 JS↔Native 机制

| # | 机制 | 方向 | Payload 限制 | 线程 |
|---|------|------|-------------|------|
| 1 | `CefMessageRouterBrowserSide`（`window.cefQuery`） | JS→C++ 双向 | 默认字符串；145+ 支持 shared memory | UI 线程 |
| 2 | `CefRegisterExtension` + `CefV8Handler` | JS→C++ 同步 | 无限制 | TID_RENDERER |
| 3 | `OnContextCreated` + `CefV8Value::CreateFunction` | JS→C++ 同步 | 无限制 | TID_RENDERER |
| 4 | `CefFrame::ExecuteJavaScript` | C++→JS 单向 | 无返回值 | 任意线程 |
| 5 | `SendProcessMessage` + `OnProcessMessageReceived` | 跨进程双向 | ~128KB；shared memory 无限 | UI 线程 |
| 6 | `CefResourceRequestHandler` / `CefSchemeHandlerFactory` | 双向（HTTP） | 无限（流式） | TID_IO |

### 当前限制

- `CefRuntime.RegisterExtension` 注册 V8 扩展失败（NuGet 版不支持从 browser 进程注入）
- 无法使用 `CefMessageRouter`（需修改 BrowserProcess，不满足 addon 分发）
- 无法使用 `SchemeHandler + fetch`（`file://` → `ipc://` 跨域 CORS 死胡同）
- **唯一可行：iframe → OnBeforeBrowse 拦截**

## TODO

### Bridge 优化

- [ ] iframe 复用：改用单个隐藏 iframe 反复设置 src，代替每次 createElement
- [ ] 考虑 `SchemeHandler + fetch` 替代 iframe（需解决 CORS，如用 `ipc://` 自加载页面）
- [ ] C#→JS 推送改用安全序列化（替换手写 `Replace("\\","\\\\")` 转义）

### 嵌入模式

- [ ] `ForwardInputEvents` 完全实现：JS DOM 事件 → IPC → C# InputEvent → `viewport.PushInput`
  - 鼠标事件（move/down/up/wheel）
  - 键盘事件（keydown/keyup）
  - 坐标换算（CEF 物理像素 → Godot 虚拟像素）

## 参考实现

- **godot_wry**: https://github.com/doceazedo/godot_wry
  - `lib.rs`: JS 事件监听 + IPC handler + `push_input`
  - `godot_window.rs`: `DisplayServer.WindowGetNativeHandle` → HWND
  - 关键技巧：移除 `WS_CLIPCHILDREN`、坐标换算、`CURRENT_BUTTON_MASK` 状态机
- wrymium（Tauri+CEF 混合）: https://github.com/gxcsoccer/wrymium
- Chromely: https://github.com/chromelyapps/CefSharp