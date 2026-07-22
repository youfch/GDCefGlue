using System;
using System.Collections.Generic;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

// ══════════════════════════════════════════════════════════════
//  右键上下文菜单（OSR 模式）
//  ──────────────────────────────────────────────────────────────
//  线程安全模型:
//    CEF UI 线程 → OnXxx (dispose guard) → CallDeferred → Godot 主线程
// ══════════════════════════════════════════════════════════════
public partial class CefGlueControl
{
    // ── 运行时状态 ──
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
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Context menu parameters snapshot. Safe to keep references past the
    /// event handler (unlike <see cref="CefContextMenuParams"/>).
    /// </summary>
    public sealed class ContextMenuParams
    {
        public int X { get; }
        public int Y { get; }
        public CefContextMenuTypeFlags ContextMenuType { get; }
        public string LinkUrl { get; }
        public string UnfilteredLinkUrl { get; }
        public string SourceUrl { get; }
        public bool HasImageContents { get; }
        public string TitleText { get; }
        public string PageUrl { get; }
        public string FrameUrl { get; }
        public string FrameCharset { get; }
        public CefContextMenuMediaType MediaType { get; }
        public CefContextMenuMediaStateFlags MediaState { get; }
        public string SelectionText { get; }
        public bool IsEditable { get; }
        public bool IsSpellCheckEnabled { get; }
        public CefContextMenuEditStateFlags EditState { get; }
        public string MisspelledWord { get; }
        public string[] DictionarySuggestions { get; }
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
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// A thin wrapper around the live <see cref="CefMenuModel"/>. Use this
    /// in <see cref="BeforeContextMenu"/> to customize the menu before display.
    /// </summary>
    /// <remarks>
    /// ⚠️ Only valid during the <see cref="BeforeContextMenu"/> event handler.
    /// </remarks>
    public sealed class ContextMenuModel
    {
        private readonly CefMenuModel _model;
        internal ContextMenuModel(CefMenuModel model) { _model = model; }

        public int Count => (int)_model.Count;
        public bool IsSubMenu => _model.IsSubMenu;
        public void Clear() => _model.Clear();
        public void AddItem(int commandId, string label) => _model.AddItem(commandId, label);
        public void AddCheckItem(int commandId, string label) => _model.AddCheckItem(commandId, label);
        public void AddRadioItem(int commandId, string label, int groupId) => _model.AddRadioItem(commandId, label, groupId);
        public void AddSeparator() => _model.AddSeparator();
        public ContextMenuModel AddSubMenu(int commandId, string label)
        {
            var sub = _model.AddSubMenu(commandId, label);
            return sub != null ? new ContextMenuModel(sub) : null;
        }
        public bool Remove(int commandId) => _model.Remove(commandId);
        public bool SetLabel(int commandId, string label) => _model.SetLabel(commandId, label);
        public bool SetEnabled(int commandId, bool enabled) => _model.SetEnabled(commandId, enabled);
        public bool SetVisible(int commandId, bool visible) => _model.SetVisible(commandId, visible);
        public bool SetChecked(int commandId, bool @checked) => _model.SetChecked(commandId, @checked);
        public string GetLabelAt(int index) => _model.GetLabelAt((nuint)index);
        public int GetCommandIdAt(int index) => _model.GetCommandIdAt((nuint)index);
        public CefMenuItemType GetItemTypeAt(int index) => _model.GetItemTypeAt((nuint)index);
        public bool IsCheckedAt(int index) => _model.IsCheckedAt((nuint)index);
        public bool IsEnabledAt(int index) => _model.IsEnabledAt((nuint)index);
        public ContextMenuModel GetSubMenuAt(int index)
        {
            var sub = _model.GetSubMenuAt((nuint)index);
            return sub != null ? new ContextMenuModel(sub) : null;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  DTO: ContextMenuItem — 菜单项的不可变快照
    // ══════════════════════════════════════════════════════════════

    private sealed class ContextMenuItem
    {
        public bool IsSeparator { get; init; }
        public string Label { get; init; }
        public int CommandId { get; init; }
        public bool IsEnabled { get; init; }
        public bool? IsChecked { get; init; }
        public List<ContextMenuItem> Children { get; init; }
    }

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

    internal void OnBeforeContextMenu(
        CefBrowser browser,
        CefFrame frame,
        CefContextMenuParams state,
        CefMenuModel model)
    {
        if (_disposed) return;
        if (!ContextMenuEnabled) return;
        if (!HasBeforeContextMenuSubscribers) return;

        ContextMenuModel modelWrapper = null;
        try { modelWrapper = new ContextMenuModel(model); }
        catch { /* model may be disposed */ }
        if (modelWrapper == null) return;

        var paramsSnapshot = new ContextMenuParams(state);
        try
        {
            RaiseBeforeContextMenu(modelWrapper, paramsSnapshot);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CefGlueControl: BeforeContextMenu handler threw: {ex}");
        }
    }

    internal bool OnRunContextMenu(
        CefBrowser browser,
        CefFrame frame,
        CefContextMenuParams parameters,
        CefMenuModel model,
        CefRunContextMenuCallback callback)
    {
        if (_disposed || !ContextMenuEnabled)
        {
            try { callback?.Cancel(); } catch { }
            return true;
        }

        _pendingContextMenuItems = SnapshotMenuModel(model);
        _pendingContextMenuParams = new ContextMenuParams(parameters);
        _pendingContextMenuX = parameters.X;
        _pendingContextMenuY = parameters.Y;
        _pendingContextMenuCallback = callback;

        CallDeferred("_notify_run_context_menu");

        return true;
    }

    internal bool OnContextMenuCommand(
        CefBrowser browser,
        CefFrame frame,
        CefContextMenuParams state,
        int commandId,
        CefEventFlags eventFlags)
    {
        if (_disposed) return false;

        _pendingCommandId = commandId;
        _pendingCommandParams = new ContextMenuParams(state);
        _pendingCommandEventFlags = eventFlags;

        CallDeferred("_notify_context_menu_command");

        if (commandId >= (int)CefMenuId.Back && commandId <= (int)CefMenuId.AddToDictionary)
            return false;

        if (commandId >= (int)CefMenuId.UserFirst && commandId <= (int)CefMenuId.UserLast)
            return true;

        return false;
    }

    internal void OnContextMenuDismissed(CefBrowser browser, CefFrame frame)
    {
        if (_disposed) return;
        CallDeferred("_notify_context_menu_dismissed");
    }

    // ══════════════════════════════════════════════════════════════
    //  Godot 主线程回调
    // ══════════════════════════════════════════════════════════════

    private void _notify_run_context_menu()
    {
        if (_disposed) return;

        var callback = _pendingContextMenuCallback;
        var items = _pendingContextMenuItems;
        var x = _pendingContextMenuX;
        var y = _pendingContextMenuY;

        _pendingContextMenuCallback = null;
        _pendingContextMenuItems = null;
        _pendingContextMenuX = 0;
        _pendingContextMenuY = 0;

        if (callback == null)
        {
            GD.PrintErr("[CM] _notify_run_context_menu called but no pending callback");
            return;
        }

        CloseContextMenuPopup();

        _contextMenuPopup = new PopupMenu();
        _contextMenuPopup.HideOnItemSelection = true;

        BuildPopupMenuItems(_contextMenuPopup, items);

        _contextMenuPopup.IdPressed += OnContextMenuPopupIdPressed;
        _contextMenuPopup.PopupHide += OnContextMenuPopupHide;
        _contextMenuPopupConnected = true;

        AddChild(_contextMenuPopup);

        var globalPos = GetGlobalRect().Position;
        var screenPos = new Vector2I(
            (int)(globalPos.X + x),
            (int)(globalPos.Y + y));

        _contextMenuPopup.Position = screenPos;
        _contextMenuPopup.Popup();

        _pendingContextMenuCallback = callback;
    }

    private void _notify_context_menu_command()
    {
        if (_disposed) return;

        var commandId = _pendingCommandId;
        var parameters = _pendingCommandParams;
        var eventFlags = _pendingCommandEventFlags;

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

    private void _notify_context_menu_dismissed()
    {
        CloseContextMenuPopup();
    }

    // ══════════════════════════════════════════════════════════════
    //  PopupMenu 构建与信号处理
    // ══════════════════════════════════════════════════════════════

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

            var label = item.Label?.Replace("&", "&&") ?? string.Empty;

            if (item.Children != null && item.Children.Count > 0)
            {
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

    private void OnContextMenuPopupIdPressed(long id)
    {
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
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CM] callback.Continue threw: {ex}");
            try { callback.Cancel(); } catch { }
        }
    }

    private void OnContextMenuPopupHide()
    {
        CallDeferred("_deferred_context_menu_popup_hide");
    }

    private void _deferred_context_menu_popup_hide()
    {
        var callback = _pendingContextMenuCallback;
        _pendingContextMenuCallback = null;

        if (callback == null)
            return;

        try
        {
            callback.Cancel();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CM] callback.Cancel threw: {ex.Message}");
        }
    }

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

        var callback = _pendingContextMenuCallback;
        _pendingContextMenuCallback = null;
        if (callback != null)
        {
            try { callback.Cancel(); }
            catch { }
        }

        _pendingContextMenuItems = null;
        _pendingContextMenuParams = null;
    }
}