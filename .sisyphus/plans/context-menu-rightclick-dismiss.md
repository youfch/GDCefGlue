# 右键菜单弹出状态下再次右键 → 关闭旧菜单 + 新位置唤出新菜单

## 问题描述

OSR 模式下，右键弹出 PopupMenu 后，用户在页面其他区域再次右键：
- 期望：关闭旧菜单，在新鼠标位置唤起新菜单（标准浏览器行为）
- 实际：旧菜单不关闭，或关闭后右键被 Godot 模态弹窗吃掉，新菜单不出现

## 方案：非模态 PopupMenu + _GuiInput 拦截右键

### 原理

Godot 的 `PopupMenu.Popup()` 默认设置 `Exclusive = true`，创建模态弹窗。模态弹窗在点击外部时：
1. 关闭弹窗（模态 dismiss）
2. **右键事件被消费掉**，不传到 `_GuiInput` → CEF
3. 用户需要右键两次才能唤起新菜单

方案改用 `Exclusive = false`，让点击穿透到 `_GuiInput`，在其中手动关闭旧菜单 + 转发右键到 CEF。

### 实现步骤

#### 1. `CefGlueControl.ContextMenu.cs` — `NotifyRunContextMenu()`

将 `_contextMenuPopup.Popup()` 改为：

```csharp
_contextMenuPopup.Position = screenPos;
_contextMenuPopup.Popup();           // 先 Popup (确保正确尺寸和布局)
_contextMenuPopup.Exclusive = false; // 立即取消独占，让点击穿透
```

**注意**：
- `Popup()` 必须在 `Exclusive = false` 之前调用，因为 `Popup()` 内部会设置 `Exclusive = true`
- 需要 `Popup()` 而不是 `Show()`，因为 `PopupMenu` 依赖 `Popup()` 来正确计算尺寸和布局
- 设置 `Exclusive = false` 后，`PopupHide` 信号仍然会触发（因为 `Popup()` 设置了 `is_popup` 标志）

#### 2. `CefGlueControl.Input.cs` — `_GuiInput()`

在 `_GuiInput` 顶部添加右键检测：

```csharp
public override void _GuiInput(InputEvent @event)
{
    if (_browserHost == null || _renderMode == RenderMode.EmbeddedWindow) return;

    if (@event is InputEventMouseButton mb && mb.Pressed)
    {
        // 右键 + 菜单正在显示 → 关闭旧菜单，转发右键给 CEF
        if (mb.ButtonIndex == MouseButton.Right && _contextMenuPopup != null)
        {
            CloseContextMenuPopup();
            SendMouseButtonEvent(mb);  // 发给 CEF，CEF 会在新位置创建新菜单
            return;
        }

        // 左键 + 菜单正在显示 → 关闭菜单（左键仍传给 CEF，让页面获焦等）
        if (mb.ButtonIndex == MouseButton.Left && _contextMenuPopup != null)
        {
            CloseContextMenuPopup();
            // 不 return，左键继续走 SendMouseButtonEvent
        }
    }

    switch (@event)
    {
        case InputEventMouseMotion m: SendMouseMoveEvent(m); break;
        case InputEventMouseButton b: SendMouseButtonEvent(b); break;
        // ...
    }
}
```

**注意**：
- 右键的 `SendMouseButtonEvent(mb)` 传的是 `CefMouseButtonType.Right`，不是 Left，所以**不会触发左键的 GrabFocus()**。输入框聚焦是 CEF 自身行为（右键点击输入框时浏览器自动聚焦），和 Chrome 一致
- 左键的 case 不包含 `return`，让左键继续进入 `SendMouseButtonEvent`，这样页面可以正常获焦
- 右键的 case 有 `return`，避免重复进入 `SendMouseButtonEvent`

#### 3. `CefGlueControl.Input.cs` — ESC 处理

在 `_GuiInput` 的 key 分支中添加 ESC 关闭菜单：

```csharp
case InputEventKey k:
    if (k.Pressed && !k.Echo && k.Keycode == Key.Escape && _contextMenuPopup != null)
    {
        CloseContextMenuPopup();
        GetViewport()?.SetInputAsHandled();
        return;
    }
    SendKeyEvent(k);
    // ...
```

**注意**：`Exclusive = false` 后，菜单不会自动响应 ESC 关闭，需要手动处理。

#### 4. `CefGlueControl.ContextMenu.cs` — 可选：WindowInput 兜底

订阅 PopupMenu 的 `WindowInput` 信号，处理右键**直接点在菜单上**的情况：

```csharp
_contextMenuPopup.WindowInput += OnPopupMenuWindowInput;

private void OnPopupMenuWindowInput(InputEvent @event)
{
    if (@event is InputEventMouseButton b && b.Pressed && b.ButtonIndex == MouseButton.Right)
    {
        CloseContextMenuPopup();
        if (_browserHost != null)
        {
            var localPos = GetLocalMousePosition();
            var mouseEvent = new CefMouseEvent { ... };
            _browserHost.SendMouseClickEvent(mouseEvent, CefMouseButtonType.Right, false, 1);
            _browserHost.SendMouseClickEvent(mouseEvent, CefMouseButtonType.Right, true, 1);
        }
        GetViewport()?.SetInputAsHandled();
    }
}
```

**注意**：`CloseContextMenuPopup` 中需要断开 `WindowInput` 信号，否则 PopupMenu 被 QueueFree 时可能报错。

### 需要修改的文件

| 文件 | 修改 |
|------|------|
| `plugin/addons/GCefGlue/CefGlueControl.ContextMenu.cs` | `NotifyRunContextMenu`: `Popup()` + `Exclusive = false`<br>添加 `WindowInput` 事件订阅/取消 |
| `plugin/addons/GCefGlue/CefGlueControl.Input.cs` | `_GuiInput`: 右键/左键/ESC 检测 + 关闭菜单 |

### 注意事项

1. **`Popup()` 必须在 `Exclusive = false` 之前调用**：`Popup()` 内部会设置 `Exclusive = true`，必须在其后覆盖
2. **`PopupHide` 信号仍然有效**：`Popup()` 设置了 `is_popup` 标志，所以 `PopupHide` 信号仍会触发。`DeferredContextMenuPopupHide` 的 generation 检查逻辑继续有效
3. **右键事件不会触发左键动作**：`SendMouseButtonEvent` 传的是 `CefMouseButtonType.Right`，不会走 `GrabFocus()` 分支。输入框聚焦是 CEF 原生行为
4. **`CloseContextMenuPopup` 需要断开所有信号**：包括 `WindowInput`、`IdPressed`、`PopupHide`
5. **`_contextMenuGeneration` 递增在 `OnRunContextMenu` 中**：已在 CEF 线程中递增，`PopupHide` 的 generation 检查可以防止 stale callback 被误 cancel
6. **仅修复 plugin 版本**：extension 版本未修改，如需同步，参考相同方案
7. **`Exclusive = false` 可能导致菜单不自动关闭**：需要在 `_GuiInput` 中手动处理关闭（上述代码已覆盖）