using System;
using System.Collections.Generic;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  右键上下文菜单（OSR 模式）
    //  ──────────────────────────────────────────────────────────────
    //  线程安全模型:
    //    CEF UI 线程 → OnXxx (dispose guard) → CallDeferred → Godot 主线程
    //    OnBeforeContextMenu: 同步事件（CEF 线程），用户可直接修改 model
    //    RunContextMenu: 异步显示（CallDeferred 到 Godot 主线程构建 PopupMenu）
    //    OnContextMenuCommand: 异步通知（CallDeferred）
    //    OnContextMenuDismissed: 异步通知（CallDeferred）
    //
    //  跨线程数据传递:
    //    不使用 Variant 序列化（自定义 DTO 非 Variant 兼容）。
    //    改用字段存储（_pendingContextMenuItems / _pendingContextMenuParams 等），
    //    CEF UI 线程写入字段后调用 CallDeferred，Godot 主线程读取字段。
    // ══════════════════════════════════════════════════════════════
    public partial class CefGlueControl
    {
        // ── 运行时状态 ──
        // _pendingContextMenuCallback 可被 CEF UI 线程与 Godot 主线程访问（单次写入-单次读取，无并发）
        // _pendingContextMenuItems / _pendingContextMenuParams 同理：CEF 线程写入 → CallDeferred → Godot 线程读取
        private PopupMenu _contextMenuPopup;
        private CefRunContextMenuCallback _pendingContextMenuCallback;
        private List<ContextMenuItem> _pendingContextMenuItems;
        private ContextMenuParams _pendingContextMenuParams;
        private int _pendingContextMenuX;
        private int _pendingContextMenuY;
        private bool _contextMenuPopupConnected;

        // 用于 OnContextMenuCommand 的延迟数据
        private int _pendingCommandId;
        private ContextMenuParams _pendingCommandParams;
        private CefEventFlags _pendingCommandEventFlags;

        // ══════════════════════════════════════════════════════════════
        //  DTO: ContextMenuParams — CefContextMenuParams 的安全快照
        //  可安全跨线程传递和长期持有（不像 CefContextMenuParams 会被 CEF 释放）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Context menu parameters snapshot. Safe to keep references past the
        /// event handler (unlike <see cref="CefContextMenuParams"/>).
        /// </summary>
        public sealed class ContextMenuParams
        {
            /// <summary>Mouse X coordinate relative to the render view origin.</summary>
            public int X { get; }
            /// <summary>Mouse Y coordinate relative to the render view origin.</summary>
            public int Y { get; }
            /// <summary>Flags describing what was right-clicked (page/link/media/editable/etc.).</summary>
            public CefContextMenuTypeFlags ContextMenuType { get; }
            /// <summary>URL of the link enclosing the clicked node, if any.</summary>
            public string LinkUrl { get; }
            /// <summary>Raw link URL (for "copy link address"), if any.</summary>
            public string UnfilteredLinkUrl { get; }
            /// <summary>Source URL for img/audio/video, if any.</summary>
            public string SourceUrl { get; }
            /// <summary>True if the clicked image has non-empty contents.</summary>
            public bool HasImageContents { get; }
            /// <summary>Title or alt text (typically for images).</summary>
            public string TitleText { get; }
            /// <summary>Top-level page URL.</summary>
            public string PageUrl { get; }
            /// <summary>Subframe URL, if right-clicked in a subframe.</summary>
            public string FrameUrl { get; }
            /// <summary>Character encoding of the subframe.</summary>
            public string FrameCharset { get; }
            /// <summary>Media element type (None/Image/Video/Audio/Canvas/File/Plugin).</summary>
            public CefContextMenuMediaType MediaType { get; }
            /// <summary>Media state flags (Paused/Muted/Loop/etc.).</summary>
            public CefContextMenuMediaStateFlags MediaState { get; }
            /// <summary>Currently selected text, if any.</summary>
            public string SelectionText { get; }
            /// <summary>True if invoked on an editable node.</summary>
            public bool IsEditable { get; }
            /// <summary>True if spell-check is enabled on the editable node.</summary>
            public bool IsSpellCheckEnabled { get; }
            /// <summary>Editable capability flags (CanUndo/CanRedo/CanCut/etc.).</summary>
            public CefContextMenuEditStateFlags EditState { get; }
            /// <summary>The misspelled word, if spell-check found one.</summary>
            public string MisspelledWord { get; }
            /// <summary>Spell-check dictionary suggestions for the misspelled word.</summary>
            public string[] DictionarySuggestions { get; }
            /// <summary>True if the menu contains renderer-process items.</summary>
            public bool IsCustomMenu { get; }

            internal ContextMenuParams(CefContextMenuParams p)
            {
                X = p.X;
                Y = p.Y;
                ContextMenuType = p.ContextMenuType;
                LinkUrl = p.LinkUrl ?? string.Empty;
                UnfilteredLinkUrl = p.UnfilteredLinkUrl ?? string.Empty;
                SourceUrl = p.SourceUrl ?? string.Empty;
                HasImageContents = p.HasImageContents;
                TitleText = p.TitleText ?? string.Empty;
                PageUrl = p.PageUrl ?? string.Empty;
                FrameUrl = p.FrameUrl ?? string.Empty;
                FrameCharset = p.FrameCharset ?? string.Empty;
                MediaType = p.MediaType;
                MediaState = p.MediaState;
                SelectionText = p.SelectionText ?? string.Empty;
                IsEditable = p.IsEditable;
                IsSpellCheckEnabled = p.IsSpellCheckEnabled;
                EditState = p.EditState;
                MisspelledWord = p.GetMisspelledWord() ?? string.Empty;
                DictionarySuggestions = p.GetDictionarySuggestions() ?? Array.Empty<string>();
                IsCustomMenu = p.IsCustomMenu;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  DTO: ContextMenuModel — CefMenuModel 的安全包装器
        //  ⚠️ 仅在 BeforeContextMenu 事件期间有效；事件返回后 CEF 会释放底层对象。
        //     不要将此对象存储到事件之外。
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// A thin wrapper around the live <see cref="CefMenuModel"/>. Use this
        /// in <see cref="BeforeContextMenu"/> to customize the menu before display.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>Thread safety:</b> Only valid during the <see cref="BeforeContextMenu"/>
        /// event handler. The underlying CefMenuModel is disposed by CEF after
        /// the handler returns. Do NOT store this object for later use.
        /// </remarks>
        public sealed class ContextMenuModel
        {
            private readonly CefMenuModel _model;
            internal ContextMenuModel(CefMenuModel model) { _model = model; }

            /// <summary>Number of items in the menu.</summary>
            public int Count => (int)_model.Count;

            /// <summary>True if this model is a submenu.</summary>
            public bool IsSubMenu => _model.IsSubMenu;

            /// <summary>Remove all items from the menu.</summary>
            public void Clear() => _model.Clear();

            /// <summary>Add a command item with the given command ID and label.</summary>
            public void AddItem(int commandId, string label) => _model.AddItem(commandId, label);

            /// <summary>Add a checkable item (checkbox) with the given command ID and label.</summary>
            public void AddCheckItem(int commandId, string label) => _model.AddCheckItem(commandId, label);

            /// <summary>Add a radio item with the given command ID, label, and group ID.</summary>
            public void AddRadioItem(int commandId, string label, int groupId) => _model.AddRadioItem(commandId, label, groupId);

            /// <summary>Add a separator.</summary>
            public void AddSeparator() => _model.AddSeparator();

            /// <summary>Add a submenu and return the submenu's model for further customization.</summary>
            public ContextMenuModel AddSubMenu(int commandId, string label)
            {
                var sub = _model.AddSubMenu(commandId, label);
                return sub != null ? new ContextMenuModel(sub) : null;
            }

            /// <summary>Remove the item with the given command ID.</summary>
            public bool Remove(int commandId) => _model.Remove(commandId);

            /// <summary>Set the label of the item with the given command ID.</summary>
            public bool SetLabel(int commandId, string label) => _model.SetLabel(commandId, label);

            /// <summary>Enable or disable the item with the given command ID.</summary>
            public bool SetEnabled(int commandId, bool enabled) => _model.SetEnabled(commandId, enabled);

            /// <summary>Show or hide the item with the given command ID.</summary>
            public bool SetVisible(int commandId, bool visible) => _model.SetVisible(commandId, visible);

            /// <summary>Set the checked state of a check/radio item.</summary>
            public bool SetChecked(int commandId, bool @checked) => _model.SetChecked(commandId, @checked);

            /// <summary>Get the label of the item at the given index.</summary>
            public string GetLabelAt(int index) => _model.GetLabelAt((nuint)index);

            /// <summary>Get the command ID at the given index (-1 for separators).</summary>
            public int GetCommandIdAt(int index) => _model.GetCommandIdAt((nuint)index);

            /// <summary>Get the item type at the given index.</summary>
            public CefMenuItemType GetItemTypeAt(int index) => _model.GetItemTypeAt((nuint)index);

            /// <summary>Check if the item at the given index is checked.</summary>
            public bool IsCheckedAt(int index) => _model.IsCheckedAt((nuint)index);

            /// <summary>Check if the item at the given index is enabled.</summary>
            public bool IsEnabledAt(int index) => _model.IsEnabledAt((nuint)index);

            /// <summary>Get the submenu model at the given index (null if not a submenu).</summary>
            public ContextMenuModel GetSubMenuAt(int index)
            {
                var sub = _model.GetSubMenuAt((nuint)index);
                return sub != null ? new ContextMenuModel(sub) : null;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  DTO: ContextMenuItem — 菜单项的不可变快照（用于构建 PopupMenu）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Immutable snapshot of a single menu item (or separator). Used internally
        /// to build a Godot <see cref="PopupMenu"/> from a CefMenuModel.
        /// </summary>
        private sealed class ContextMenuItem
        {
            public bool IsSeparator { get; init; }
            public string Label { get; init; }
            public int CommandId { get; init; }
            public bool IsEnabled { get; init; }
            public bool? IsChecked { get; init; } // null = not checkable
            public List<ContextMenuItem> Children { get; init; } // submenu items, null = leaf
        }

        /// <summary>
        /// Recursively snapshot a CefMenuModel into an immutable tree of ContextMenuItem.
        /// Must be called on the CEF thread while the model is still alive.
        /// </summary>
        private static List<ContextMenuItem> SnapshotMenuModel(CefMenuModel model)
        {
            if (model == null) return null;
            var count = (int)model.Count;
            if (count == 0) return null;

            var items = new List<ContextMenuItem>(count);
            for (int i = 0; i < count; i++)
            {
                var itemType = model.GetItemTypeAt((nuint)i);
                var commandId = model.GetCommandIdAt((nuint)i);
                var label = model.GetLabelAt((nuint)i) ?? string.Empty;
                var isEnabled = model.IsEnabledAt((nuint)i);

                switch (itemType)
                {
                    case CefMenuItemType.Separator:
                        items.Add(new ContextMenuItem { IsSeparator = true });
                        break;

                    case CefMenuItemType.Command:
                        items.Add(new ContextMenuItem
                        {
                            Label = label,
                            CommandId = commandId,
                            IsEnabled = isEnabled,
                            IsChecked = null,
                            Children = null,
                        });
                        break;

                    case CefMenuItemType.Check:
                    case CefMenuItemType.Radio:
                        // Godot PopupMenu has no native radio items; treat as check
                        items.Add(new ContextMenuItem
                        {
                            Label = label,
                            CommandId = commandId,
                            IsEnabled = isEnabled,
                            IsChecked = model.IsCheckedAt((nuint)i),
                            Children = null,
                        });
                        break;

                    case CefMenuItemType.SubMenu:
                        var subModel = model.GetSubMenuAt((nuint)i);
                        items.Add(new ContextMenuItem
                        {
                            Label = label,
                            CommandId = commandId,
                            IsEnabled = isEnabled,
                            IsChecked = null,
                            Children = SnapshotMenuModel(subModel),
                        });
                        break;
                }
            }
            return items;
        }

        // ══════════════════════════════════════════════════════════════
        //  CEF 回调入口（CEF UI 线程）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Called from <see cref="GodotContextMenuHandler.OnBeforeContextMenu"/>.
        /// Runs on the CEF UI thread. Always fires the event (when ContextMenuEnabled
        /// and subscribers exist) so the user can customize the menu. When no
        /// subscribers, the menu stays as CEF default (which we render via
        /// PopupMenu in <see cref="OnRunContextMenu"/>).
        /// </summary>
        internal void OnBeforeContextMenu(
            CefBrowser browser,
            CefFrame frame,
            CefContextMenuParams state,
            CefMenuModel model)
        {
            if (_disposed) return;
            if (!ContextMenuEnabled) return;
            if (!HasBeforeContextMenuSubscribers) return;

            // 同步触发事件 — 用户在 CEF 线程上直接修改 model。
            // model 仅在此调用期间有效，用户不可存储引用。
            var modelWrapper = new ContextMenuModel(model);
            var paramsSnapshot = new ContextMenuParams(state);
            try
            {
                RaiseBeforeContextMenu(modelWrapper, paramsSnapshot);
                GD.Print($"[CM] OnBeforeContextMenu: model.Count={modelWrapper.Count}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"CefGlueControl: BeforeContextMenu handler threw: {ex}");
            }
        }

        /// <summary>
        /// Called from <see cref="GodotContextMenuHandler.RunContextMenu"/>.
        /// Runs on the CEF UI thread. Snapshots the model into an immutable
        /// tree and marshals to the Godot main thread for PopupMenu display.
        /// Returns true to indicate we handle the menu rendering (or cancel).
        /// </summary>
        /// <remarks>
        /// Always returns true in OSR mode. When <see cref="ContextMenuEnabled"/>
        /// is false, cancels the callback silently to preserve the prior
        /// "no context menu" behavior — and critically, to prevent CEF's
        /// default menu runner from running (it would log
        /// "Window handle is required for default OSR context menu" because
        /// OSR mode has no HWND).
        /// </remarks>
        internal bool OnRunContextMenu(
            CefBrowser browser,
            CefFrame frame,
            CefContextMenuParams parameters,
            CefMenuModel model,
            CefRunContextMenuCallback callback)
        {
            // 关闭路径：静默取消，不显示任何菜单。
            // 必须返回 true 阻止 CEF 默认菜单 runner（OSR 无 HWND 会报错）。
            if (_disposed || !ContextMenuEnabled)
            {
                try { callback?.Cancel(); } catch { /* disposed/dismissed */ }
                return true;
            }

            // 快照 model 和 params — CEF 会在本次调用返回后释放它们。
            // callback 不会被释放，可安全跨线程持有直到 Continue/Cancel 被调用。
            // 将快照存入字段，由 Godot 主线程在 NotifyRunContextMenu 中读取。
            _pendingContextMenuItems = SnapshotMenuModel(model);
            _pendingContextMenuParams = new ContextMenuParams(parameters);
            _pendingContextMenuX = parameters.X;
            _pendingContextMenuY = parameters.Y;
            _pendingContextMenuCallback = callback;

            GD.Print($"[CM] OnRunContextMenu: items={_pendingContextMenuItems?.Count ?? 0}, x={_pendingContextMenuX}, y={_pendingContextMenuY}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");

            // Marshal 到 Godot 主线程构建并显示 PopupMenu
            // 不通过 Variant 传递自定义 DTO（非 Variant 克试类型），改用字段存储
            CallDeferred(nameof(NotifyRunContextMenu));

            return true; // 我们处理菜单显示，阻止 CEF 默认菜单
        }

        /// <summary>
        /// Called from <see cref="GodotContextMenuHandler.OnContextMenuCommand"/>.
        /// Runs on the CEF UI thread. Marshals the command to the Godot main
        /// thread for the <see cref="ContextMenuCommand"/> event.
        /// </summary>
        /// <returns>
        /// True if the command ID is in the user-defined range (26500-28500),
        /// false for built-in IDs so CEF applies default behavior.
        /// </returns>
        internal bool OnContextMenuCommand(
            CefBrowser browser,
            CefFrame frame,
            CefContextMenuParams state,
            int commandId,
            CefEventFlags eventFlags)
        {
            if (_disposed) return false;

            GD.Print($"[CM] OnContextMenuCommand: id={commandId}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");

            // 快照 params（state 在本次调用返回后会被 CEF 释放）
            // 存入字段，由 Godot 主线程在 NotifyContextMenuCommand 中读取
            _pendingCommandId = commandId;
            _pendingCommandParams = new ContextMenuParams(state);
            _pendingCommandEventFlags = eventFlags;

            // Marshal 到 Godot 主线程触发事件
            CallDeferred(nameof(NotifyContextMenuCommand));

            // 内置 ID (100-250): 返回 false → CEF 应用默认行为（后退/复制/粘贴等）
            if (commandId >= (int)CefMenuId.Back && commandId <= (int)CefMenuId.AddToDictionary)
                return false;

            // 用户自定义 ID (26500-28500): 返回 true → 已由用户处理
            if (commandId >= (int)CefMenuId.UserFirst && commandId <= (int)CefMenuId.UserLast)
                return true;

            // 未知范围: 让 CEF 处理
            return false;
        }

        /// <summary>
        /// Called from <see cref="GodotContextMenuHandler.OnContextMenuDismissed"/>.
        /// Runs on the CEF UI thread. Marshals to Godot main thread for cleanup.
        /// </summary>
        internal void OnContextMenuDismissed(CefBrowser browser, CefFrame frame)
        {
            if (_disposed) return;
            CallDeferred(nameof(NotifyContextMenuDismissed));
        }

        // ══════════════════════════════════════════════════════════════
        //  Godot 主线程回调
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Runs on the Godot main thread. Builds a PopupMenu from the snapshot
        /// (stored in fields by <see cref="OnRunContextMenu"/>) and displays it
        /// at the right coordinates.
        /// </summary>
        private void NotifyRunContextMenu()
        {
            if (_disposed) return;

            var callback = _pendingContextMenuCallback;
            var items = _pendingContextMenuItems;
            var x = _pendingContextMenuX;
            var y = _pendingContextMenuY;

            // 清理字段（一次性消费，注意: _pendingContextMenuParams 保留供事件使用，不在此清理）
            _pendingContextMenuCallback = null;
            _pendingContextMenuItems = null;
            _pendingContextMenuX = 0;
            _pendingContextMenuY = 0;

            if (callback == null)
            {
                GD.PrintErr("[CM] NotifyRunContextMenu called but no pending callback");
                return;
            }

            GD.Print($"[CM] NotifyRunContextMenu: items={items?.Count ?? 0}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");

            // 清理旧 PopupMenu（理论上不应该存在，但防御性处理）
            CloseContextMenuPopup();

            // 构建 PopupMenu
            _contextMenuPopup = new PopupMenu();
            _contextMenuPopup.HideOnItemSelection = true;

            BuildPopupMenuItems(_contextMenuPopup, items);

            GD.Print($"[CM] PopupMenu built with {_contextMenuPopup.ItemCount} items");

            // 连接信号
            _contextMenuPopup.IdPressed += OnContextMenuPopupIdPressed;
            _contextMenuPopup.PopupHide += OnContextMenuPopupHide;
            _contextMenuPopupConnected = true;

            AddChild(_contextMenuPopup);

            // 坐标变换: CEF 的 (x,y) 是相对于渲染视图原点的坐标
            // 需要转换为 Godot 屏幕坐标
            var globalPos = GetGlobalRect().Position;
            var screenPos = new Vector2I(
                (int)(globalPos.X + x),
                (int)(globalPos.Y + y));

            GD.Print($"[CM] Popup at screen ({screenPos.X}, {screenPos.Y}) [global={globalPos}, cef=({x},{y})]");

            // 显示菜单
            _contextMenuPopup.Position = screenPos;
            _contextMenuPopup.Popup();

            // 重新放回 callback — PopupMenu 显示后，IdPressed/PopupHide 会消费它
            _pendingContextMenuCallback = callback;
        }

        /// <summary>Runs on Godot main thread. Fires the ContextMenuCommand event.</summary>
        private void NotifyContextMenuCommand()
        {
            if (_disposed) return;

            var commandId = _pendingCommandId;
            var parameters = _pendingCommandParams;
            var eventFlags = _pendingCommandEventFlags;

            // 清理字段
            _pendingCommandId = 0;
            _pendingCommandParams = null;
            _pendingCommandEventFlags = CefEventFlags.None;

            try
            {
                RaiseContextMenuCommand(commandId, parameters, eventFlags);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"CefGlueControl: ContextMenuCommand handler threw: {ex}");
            }
        }

        /// <summary>Runs on Godot main thread. Cleans up the PopupMenu.</summary>
        private void NotifyContextMenuDismissed()
        {
            CloseContextMenuPopup();
        }

        // ══════════════════════════════════════════════════════════════
        //  PopupMenu 构建与信号处理
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Recursively build PopupMenu items from the snapshot tree.
        /// Runs on the Godot main thread.
        /// </summary>
        private void BuildPopupMenuItems(PopupMenu menu, List<ContextMenuItem> items)
        {
            if (items == null) return;

            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    menu.AddSeparator();
                    continue;
                }

                // Godot PopupMenu 将 "&" 视为助记符前缀，需转义
                var label = item.Label?.Replace("&", "&&") ?? string.Empty;

                if (item.Children != null && item.Children.Count > 0)
                {
                    // 子菜单: 添加子 PopupMenu 节点
                    // PopupMenu 添加为父菜单的子节点，并通过 add_submenu_node_item 关联
                    var subMenu = new PopupMenu();
                    BuildPopupMenuItems(subMenu, item.Children);
                    menu.AddChild(subMenu);
                    menu.AddSubmenuNodeItem(label, subMenu, item.CommandId);
                    var idx = menu.ItemCount - 1;
                    if (!item.IsEnabled)
                        menu.SetItemDisabled(idx, true);
                }
                else
                {
                    // 叶子节点
                    menu.AddItem(label, item.CommandId);
                    var idx = menu.ItemCount - 1;
                    if (!item.IsEnabled)
                        menu.SetItemDisabled(idx, true);
                    if (item.IsChecked.HasValue)
                    {
                        menu.SetItemAsCheckable(idx, true);
                        menu.SetItemChecked(idx, item.IsChecked.Value);
                    }
                }
            }
        }

        /// <summary>PopupMenu.IdPressed signal handler. Called on Godot main thread.</summary>
        private void OnContextMenuPopupIdPressed(long id)
        {
            GD.Print($"[CM] IdPressed: id={id}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");
            var callback = _pendingContextMenuCallback;
            _pendingContextMenuCallback = null;

            if (callback == null)
            {
                GD.PrintErr("[CM] IdPressed but no pending callback");
                return;
            }

            try
            {
                callback.Continue((int)id, CefEventFlags.None);
                GD.Print($"[CM] callback.Continue({id}) returned OK");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CM] callback.Continue threw: {ex}");
                try { callback.Cancel(); } catch { /* already cancelled */ }
            }
        }

        /// <summary>
        /// PopupMenu.PopupHide signal handler. Called on Godot main thread.
        /// ⚠️ Godot fires PopupHide BEFORE IdPressed when HideOnItemSelection=true.
        /// Defer the Cancel to let IdPressed consume the callback first.
        /// </summary>
        private void OnContextMenuPopupHide()
        {
            GD.Print($"[CM] PopupHide, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");
            // 延迟到帧末 — 如果 IdPressed 也会触发（用户点击了菜单项），
            // 它会在 deferred 调用之前同步消费 callback；deferred 中看到 null 直接跳过。
            // 如果用户未点击菜单项（Esc/点外部关闭），deferred 中 callback 仍在 → Cancel。
            CallDeferred(nameof(DeferredContextMenuPopupHide));
        }

        /// <summary>
        /// Deferred handler for PopupHide. Runs after IdPressed if it fired.
        /// </summary>
        private void DeferredContextMenuPopupHide()
        {
            var callback = _pendingContextMenuCallback;
            _pendingContextMenuCallback = null;

            if (callback == null)
            {
                // IdPressed 已消费 callback — 菜单项被选中，无需 Cancel
                GD.Print("[CM] DeferredPopupHide: callback already consumed by IdPressed");
                return;
            }

            // 用户未选中任何菜单项就关闭了弹窗（Esc / 点击外部）→ 通知 CEF 取消
            GD.Print("[CM] DeferredPopupHide: dismissing menu (no selection) → callback.Cancel()");
            try
            {
                callback.Cancel();
                GD.Print("[CM] callback.Cancel returned OK");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CM] callback.Cancel threw: {ex.Message}");
            }
        }

        /// <summary>
        /// Close and clean up the active PopupMenu. Safe to call from Godot main thread.
        /// </summary>
        private void CloseContextMenuPopup()
        {
            if (_contextMenuPopup != null)
            {
                if (_contextMenuPopupConnected)
                {
                    _contextMenuPopup.IdPressed -= OnContextMenuPopupIdPressed;
                    _contextMenuPopup.PopupHide -= OnContextMenuPopupHide;
                    _contextMenuPopupConnected = false;
                }
                if (_contextMenuPopup.IsInsideTree())
                    RemoveChild(_contextMenuPopup);

                _contextMenuPopup.QueueFree();
                _contextMenuPopup = null;
            }

            // 如果有未完成的 callback，取消它
            var callback = _pendingContextMenuCallback;
            _pendingContextMenuCallback = null;
            if (callback != null)
            {
                try { callback.Cancel(); }
                catch { /* 静默: 可能已被调用过 */ }
            }

            // 清理其他 pending 字段
            _pendingContextMenuItems = null;
            _pendingContextMenuParams = null;
        }
    }
}
