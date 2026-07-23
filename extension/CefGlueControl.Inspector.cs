using System.Collections.Generic;
using Godot;
using Godot.Bridge;

namespace GDCefGlueExtension;

// ── Inspector 属性动态显隐 ──────────────────────────────────
// 通过 _GetPropertyList + _Set/_Get 实现运行时可切换的 Inspector 属性。
// SyncCursor 只在 OSR 模式下显示，ForwardInputEvents 只在 EmbeddedWindow 下显示。
public partial class CefGlueControl
{
    protected override void _GetPropertyList(IList<PropertyInfo> properties)
    {
        // SyncCursor: 仅 OSR 模式
        if (_mode != RenderMode.EmbeddedWindow)
        {
            properties.Add(new PropertyInfo(new StringName("sync_cursor"), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.Default,
            });
        }

        // ContextMenuEnabled: 仅 OSR 模式
        if (_mode != RenderMode.EmbeddedWindow)
        {
            properties.Add(new PropertyInfo(new StringName("context_menu_enabled"), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.Default,
            });
        }

        // Embedded Mode 分组 + ForwardInputEvents: 仅 EmbeddedWindow 模式
        if (_mode == RenderMode.EmbeddedWindow)
        {
            // 分组头
            properties.Add(new PropertyInfo(new StringName("Embedded Mode"), VariantType.Nil)
            {
                Usage = PropertyUsageFlags.Group,
            });
            // 分组属性
            properties.Add(new PropertyInfo(new StringName("forward_input_events"), VariantType.Bool)
            {
                Usage = PropertyUsageFlags.Default,
            });
        }
    }

    protected override bool _Set(StringName property, Variant value)
    {
        if (property == new StringName("sync_cursor"))
        {
            SyncCursor = value.AsBool();
            return true;
        }
        if (property == new StringName("context_menu_enabled"))
        {
            ContextMenuEnabled = value.AsBool();
            return true;
        }
        if (property == new StringName("forward_input_events"))
        {
            ForwardInputEvents = value.AsBool();
            return true;
        }
        return false;
    }

    protected override bool _Get(StringName property, out Variant value)
    {
        if (property == new StringName("sync_cursor"))
        {
            value = Variant.CreateFrom(SyncCursor);
            return true;
        }
        if (property == new StringName("context_menu_enabled"))
        {
            value = Variant.CreateFrom(ContextMenuEnabled);
            return true;
        }
        if (property == new StringName("forward_input_events"))
        {
            value = Variant.CreateFrom(ForwardInputEvents);
            return true;
        }
        value = default;
        return false;
    }
}